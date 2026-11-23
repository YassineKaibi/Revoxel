using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Revoxel.World
{
    // Coarse detail levels 1 to Levels around the full-resolution VoxelWorld (level 0)
    // (PLAN-03 Task 4, D-055 to D-057). DetailRings gives each level's square; a wanted
    // cell (level L displays its square minus the finer one) runs a height job, then a fill
    // job chained to a GreedyMeshJob for each chunk that can show something. Meshes go to
    // hidden views. The swap step shows a cell, or a full-resolution column, only once
    // what replaces it is ready, and hides the old cover in the same frame, so no area once
    // drawn goes empty and none is drawn twice. A coarse cell inside finer squares splits
    // into its four children one level at a time (creating transitional children the rings
    // do not display), so detail near the focus arrives within frames, not when the whole
    // coarse area is done.
    //
    // Frame order, one main-thread budget: Update (after VoxelWorld): rings, collect
    // finished jobs, schedule nearest first. LateUpdate (after VoxelWorld applied its
    // meshes): apply finished coarse meshes, then swaps.
    [DefaultExecutionOrder(100)]
    public sealed class DistanceDetail : MonoBehaviour
    {
        private static readonly ProfilerMarker RingsMarker = new ProfilerMarker(
            "Revoxel.World.DetailRings"
        );
        private static readonly ProfilerMarker CollectMarker = new ProfilerMarker(
            "Revoxel.World.DetailCollect"
        );
        private static readonly ProfilerMarker ScheduleMarker = new ProfilerMarker(
            "Revoxel.World.DetailSchedule"
        );
        private static readonly ProfilerMarker ApplyMarker = new ProfilerMarker(
            "Revoxel.World.DetailApply"
        );
        private static readonly ProfilerMarker SwapMarker = new ProfilerMarker(
            "Revoxel.World.DetailSwap"
        );

        private const MeshUpdateFlags ApplyFlags =
            MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        // Unity frees writable mesh data allocations after 4 frames, with a warning.
        private const int MaxResultAgeFrames = 3;

        // The camera far plane covers the corners of the view square.
        private const float FarPlaneFactor = 1.5f;

        [SerializeField]
        private VoxelWorld _world;

        [SerializeField]
        private DistanceDetailSettings _settings;

        private readonly List<DetailCell> _work = new List<DetailCell>();
        private readonly List<DetailCell> _readyUnshown = new List<DetailCell>();
        private readonly List<DetailCell> _releasePending = new List<DetailCell>();
        private readonly List<DetailCell> _scratch = new List<DetailCell>();
        private readonly List<int3> _retiring = new List<int3>();
        private readonly Stack<DetailCell> _cellPool = new Stack<DetailCell>();
        private readonly HashSet<int2> _shownColumns = new HashSet<int2>();
        private readonly List<int2> _scratchColumns = new List<int2>();
        private readonly Mesh[] _targets = new Mesh[2];
        private readonly Stopwatch _watch = new Stopwatch();

        private Dictionary<int2, DetailCell>[] _cells;
        private Dictionary<int2, int>[] _shownBelow;
        private DetailRings _rings;
        private DetailViewPool _views;
        private HeightSlot[] _heightSlots;
        private ChunkSlot[] _chunkSlots;
        private NativeArray<VoxelRenderData> _renderData;
        private NativeArray<VertexAttributeDescriptor> _renderLayout;
        private NativeArray<VertexAttributeDescriptor> _collisionLayout;
        private Mesh _scratchCollision;
        private TerrainParams _params;
        private bool _running;
        private bool _failed;
        private int _busySlots;
        private int _shownCells;
        private long _meshBytes;
        private long _budgetTicks;
        private long _updateTicks;
        private float _lastMainThreadMs;

        public bool IsSettled { get; private set; }

        public float ViewDistanceMeters => _rings != null ? _rings.ViewDistanceMeters : 0f;

        public DistanceDetailSettings Settings => _settings;

        public DetailStats Stats =>
            new DetailStats
            {
                Cells = CellCount(),
                ShownCells = _shownCells,
                Views = _views?.ActiveCount ?? 0,
                PendingJobs = _busySlots + _work.Count,
                MeshBytes = _meshBytes,
                LastMainThreadMs = _lastMainThreadMs,
            };

        // Shown coarse cells and shown full-resolution columns covering a column (tests).
        internal int CoverageAt(int2 column)
        {
            if (!_running)
            {
                return 0;
            }

            var count = _shownColumns.Contains(column) ? 1 : 0;
            for (var level = 1; level <= _rings.Levels; level++)
            {
                if (_cells[level].TryGetValue(column >> level, out var cell) && cell.Shown)
                {
                    count++;
                }
            }

            return count;
        }

        internal DetailRings Rings => _rings;

        private void Awake()
        {
            if (_world == null)
            {
                _world = GetComponent<VoxelWorld>();
            }

            if (_world != null)
            {
                _world.VisibilityManaged = true;
            }
        }

        private void Update()
        {
            if (!_running && !TryStart())
            {
                return;
            }

            _watch.Restart();
            _budgetTicks = (long)(_settings.MainThreadBudgetMs * Stopwatch.Frequency / 1000.0);
            using (RingsMarker.Auto())
            {
                if (_rings.Update(_world.FullDetailCenter))
                {
                    OnRingsMoved();
                }
            }

            using (CollectMarker.Auto())
            {
                CollectHeights();
            }

            using (ScheduleMarker.Auto())
            {
                Schedule();
            }

            JobHandle.ScheduleBatchedJobs();
            _updateTicks = _watch.ElapsedTicks;
        }

        private void LateUpdate()
        {
            if (!_running)
            {
                return;
            }

            _watch.Restart();
            var remaining = math.max(0L, _budgetTicks - _updateTicks);
            using (ApplyMarker.Auto())
            {
                ApplyFinished(remaining);
            }

            using (SwapMarker.Auto())
            {
                Swap();
                ReleasePending();
            }

            _lastMainThreadMs = (float)(
                (_updateTicks + _watch.ElapsedTicks) * 1000.0 / Stopwatch.Frequency
            );
            IsSettled =
                _world.IsSettled
                && _work.Count == 0
                && _busySlots == 0
                && _readyUnshown.Count == 0
                && _retiring.Count == 0
                && AllFullDetailShown();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        // Waits for the world to stream; validates once.
        private bool TryStart()
        {
            if (_failed || _world == null || !_world.IsStreaming || !_world.HasFullDetailCenter)
            {
                return false;
            }

            var error = "settings are required.";
            if (
                _settings == null
                || !_settings.TryValidateWith(_world.StreamingSettings, out error)
            )
            {
                Debug.LogError($"[World] DistanceDetail cannot run: {error}", this);
                _world.VisibilityManaged = false;
                _failed = true;
                enabled = false;
                return false;
            }

            var levels = _settings.Levels;
            _rings = new DetailRings(
                levels,
                _world.FullDetailHalfWidth,
                _settings.RingHalfWidthCells
            );
            _cells = new Dictionary<int2, DetailCell>[levels + 1];
            _shownBelow = new Dictionary<int2, int>[levels + 1];
            for (var level = 1; level <= levels; level++)
            {
                _cells[level] = new Dictionary<int2, DetailCell>();
                _shownBelow[level] = new Dictionary<int2, int>();
            }

            _params = _world.TerrainParameters;
            _renderData = _world.Registry.CreateRenderData(Allocator.Persistent);
            _renderLayout = ChunkMesher.CreateRenderLayout(Allocator.Persistent);
            _collisionLayout = ChunkMesher.CreateCollisionLayout(Allocator.Persistent);
            _scratchCollision = new Mesh { name = "DetailScratchCollision" };
            _views = new DetailViewPool(transform, _world.Materials);
            var slots = _settings.MaxJobsInFlight;
            _heightSlots = new HeightSlot[slots];
            _chunkSlots = new ChunkSlot[slots];
            for (var i = 0; i < slots; i++)
            {
                _heightSlots[i] = new HeightSlot
                {
                    Range = new NativeReference<ColumnRange>(Allocator.Persistent),
                };
                _chunkSlots[i] = new ChunkSlot
                {
                    Samples = new NativeArray<TerrainSample>(
                        TerrainGenerator.DetailSampleGridCount,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory
                    ),
                    Padded = new NativeArray<ushort>(
                        WorldConstants.PaddedVoxels,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory
                    ),
                };
            }

            SetFarPlane();
            _running = true;
            return true;
        }

        private void SetFarPlane()
        {
            var focus = _world.Focus;
            var camera = focus != null ? focus.GetComponent<Camera>() : null;
            if (camera == null)
            {
                camera = Camera.main;
            }

            if (camera != null)
            {
                camera.farClipPlane = math.max(
                    camera.farClipPlane,
                    _rings.ViewDistanceMeters * FarPlaneFactor
                );
            }
        }

        // Marks cells wanted or not after the squares moved, creates the new ones, and
        // rebuilds the work and retiring lists.
        private void OnRingsMoved()
        {
            IsSettled = false;
            var center = (float2)_rings.Center(0);
            _work.Clear();
            _readyUnshown.Clear();
            _retiring.Clear();
            for (var level = 1; level <= _rings.Levels; level++)
            {
                var cells = _cells[level];
                _scratch.Clear();
                foreach (var cell in cells.Values)
                {
                    _scratch.Add(cell);
                }

                foreach (var cell in _scratch)
                {
                    cell.Wanted = _rings.IsDisplayed(level, cell.Coord);
                    if (cell.Wanted)
                    {
                        continue;
                    }

                    if (cell.Shown)
                    {
                        _retiring.Add(new int3(cell.Coord, level));
                    }
                    else
                    {
                        QueueRelease(cell);
                    }
                }

                var min = _rings.Min(level);
                var max = _rings.Max(level);
                for (var z = min.y; z < max.y; z++)
                for (var x = min.x; x < max.x; x++)
                {
                    var coord = new int2(x, z);
                    if (!_rings.IsDisplayed(level, coord) || cells.ContainsKey(coord))
                    {
                        continue;
                    }

                    var cell = _cellPool.Count > 0 ? _cellPool.Pop() : new DetailCell();
                    cell.Reset(level, coord);
                    cells.Add(coord, cell);
                }

                foreach (var cell in cells.Values)
                {
                    if (!cell.Wanted)
                    {
                        continue;
                    }

                    var d = ((float2)cell.Coord + 0.5f) * (1 << level) - center;
                    cell.SortKey = math.lengthsq(d);
                    if (cell.State != DetailCellState.Ready)
                    {
                        _work.Add(cell);
                    }
                    else if (!cell.Shown)
                    {
                        _readyUnshown.Add(cell);
                    }
                }
            }

            _work.Sort(CompareByDistance);

            // Full-resolution columns shown but no longer in the full-detail square.
            foreach (var column in _shownColumns)
            {
                if (!_rings.InRegion(0, column))
                {
                    _retiring.Add(new int3(column, 0));
                }
            }
        }

        private static readonly Comparer<DetailCell> ByDistance = Comparer<DetailCell>.Create(
            CompareByDistance
        );

        private static int CompareByDistance(DetailCell a, DetailCell b) =>
            a.SortKey.CompareTo(b.SortKey);

        // Indices of every submesh: voxels and liquids (D-073).
        private static int IndexCount(Mesh.MeshData mesh)
        {
            var count = 0;
            for (var i = 0; i < mesh.subMeshCount; i++)
            {
                count += mesh.GetSubMesh(i).indexCount;
            }

            return count;
        }

        // Finished height jobs decide which chunks of their cell to mesh.
        private void CollectHeights()
        {
            foreach (var slot in _heightSlots)
            {
                if (!slot.Busy || !slot.Handle.IsCompleted)
                {
                    continue;
                }

                slot.Handle.Complete();
                slot.Busy = false;
                _busySlots--;
                var cell = slot.Cell;
                slot.Cell = null;
                if (cell.Version != slot.Version)
                {
                    continue;
                }

                var range = slot.Range.Value;
                var chunks = WorldConstants.ChunksY >> cell.Level;
                for (var cy = 0; cy < chunks; cy++)
                {
                    if (
                        TerrainGenerator.DetailChunkNeedsMesh(
                            _params,
                            range,
                            cy,
                            cell.Level,
                            _settings.WallDepthVoxels
                        )
                    )
                    {
                        cell.Queue.Add(cy);
                    }
                }

                cell.State = DetailCellState.Chunks;
                TryFinishCell(cell);
            }
        }

        // Nearest wanted cells first: a height job, then chunk jobs, within the job limit
        // and the budget.
        private void Schedule()
        {
            var write = 0;
            for (var i = 0; i < _work.Count; i++)
            {
                var cell = _work[i];
                if (!cell.Wanted || cell.State == DetailCellState.Ready)
                {
                    continue;
                }

                if (!OverBudget())
                {
                    if (cell.State == DetailCellState.New)
                    {
                        TryScheduleHeights(cell);
                    }

                    while (
                        cell.State == DetailCellState.Chunks
                        && cell.Queue.Count > 0
                        && TryScheduleChunk(cell)
                    ) { }
                }

                _work[write++] = cell;
            }

            _work.RemoveRange(write, _work.Count - write);
        }

        private bool OverBudget() => _watch.ElapsedTicks > _budgetTicks;

        private void TryScheduleHeights(DetailCell cell)
        {
            foreach (var slot in _heightSlots)
            {
                if (slot.Busy)
                {
                    continue;
                }

                slot.Busy = true;
                slot.Cell = cell;
                slot.Version = cell.Version;
                _busySlots++;
                cell.State = DetailCellState.Heights;
                slot.Handle = new DetailHeightJob
                {
                    Params = _params,
                    Level = cell.Level,
                    Cell = cell.Coord,
                    Range = slot.Range,
                }.Schedule();
                return;
            }
        }

        private bool TryScheduleChunk(DetailCell cell)
        {
            foreach (var slot in _chunkSlots)
            {
                if (slot.Busy)
                {
                    continue;
                }

                var last = cell.Queue.Count - 1;
                var cy = cell.Queue[last];
                cell.Queue.RemoveAt(last);
                cell.JobsInFlight++;
                _busySlots++;
                slot.Busy = true;
                slot.Cell = cell;
                slot.Version = cell.Version;
                slot.Coord = new ChunkCoord(cell.Coord.x, cy, cell.Coord.y);
                slot.ScheduledFrame = Time.frameCount;
                slot.Data = Mesh.AllocateWritableMeshData(2);
                slot.HasData = true;
                var fill = new DetailFillJob
                {
                    Params = _params,
                    Level = cell.Level,
                    Coord = slot.Coord,
                    SideWalls = true,
                    Samples = slot.Samples,
                    Padded = slot.Padded,
                }.Schedule();
                slot.Handle = new GreedyMeshJob
                {
                    Padded = slot.Padded,
                    RenderData = _renderData,
                    RenderLayout = _renderLayout,
                    CollisionLayout = _collisionLayout,
                    RenderMesh = slot.Data[0],
                    CollisionMesh = slot.Data[1],
                    SkipCollision = true,
                    SkipLiquidSides = true,
                }.Schedule(fill);
                return true;
            }

            return false;
        }

        // Applies finished chunk meshes into hidden views until the budget runs out, at
        // least one per frame; results 3 frames old are completed and applied regardless.
        private void ApplyFinished(long budgetTicks)
        {
            var appliedAny = false;
            var frame = Time.frameCount;
            foreach (var slot in _chunkSlots)
            {
                if (!slot.Busy)
                {
                    continue;
                }

                var expiring = frame - slot.ScheduledFrame >= MaxResultAgeFrames;
                if (
                    !expiring
                    && (
                        !slot.Handle.IsCompleted
                        || (appliedAny && _watch.ElapsedTicks > budgetTicks)
                    )
                )
                {
                    continue;
                }

                Finish(slot);
                appliedAny = true;
            }
        }

        private void Finish(ChunkSlot slot)
        {
            slot.Handle.Complete();
            slot.Busy = false;
            _busySlots--;
            var cell = slot.Cell;
            slot.Cell = null;
            var current = cell.Version == slot.Version;
            if (current)
            {
                cell.JobsInFlight--;
            }

            var render = slot.Data[0];
            if (!current || render.vertexCount == 0)
            {
                slot.Data.Dispose();
                slot.HasData = false;
            }
            else
            {
                var view = _views.Get(cell.Level, slot.Coord);
                var bytes =
                    (long)render.vertexCount * render.GetVertexBufferStride(0)
                    + (long)IndexCount(render) * (render.indexFormat == IndexFormat.UInt32 ? 4 : 2);
                _targets[0] = view.Mesh;
                _targets[1] = _scratchCollision;
                Mesh.ApplyAndDisposeWritableMeshData(slot.Data, _targets, ApplyFlags);
                slot.HasData = false;
                _targets[0] = null;
                _targets[1] = null;
                view.Mesh.bounds = ChunkMesher.ChunkLocalBounds;
                view.MeshBytes = bytes;
                view.Renderer.enabled = cell.Shown;
                cell.Views.Add(view);
                cell.MeshBytes += bytes;
                _meshBytes += bytes;
            }

            if (current)
            {
                TryFinishCell(cell);
            }
        }

        private void TryFinishCell(DetailCell cell)
        {
            if (
                cell.State != DetailCellState.Chunks
                || cell.Queue.Count > 0
                || cell.JobsInFlight > 0
            )
            {
                return;
            }

            cell.State = DetailCellState.Ready;
            if (cell.Wanted && !cell.Shown)
            {
                _readyUnshown.Add(cell);
            }
            else if (!cell.Wanted && !cell.Shown)
            {
                QueueRelease(cell);
            }
        }

        // The swap step: new full-resolution columns and ready cells are shown when nothing
        // shown overlaps them, or replace the finer cells under them; shown cells no longer
        // wanted are hidden once the finer cover is ready, or at once outside the view.
        private void Swap()
        {
            var min = _rings.Min(0);
            var max = _rings.Max(0);
            for (var z = min.y; z < max.y; z++)
            for (var x = min.x; x < max.x; x++)
            {
                var column = new int2(x, z);
                if (!_shownColumns.Contains(column) && _world.IsColumnReady(column))
                {
                    TryShowWanted(0, column);
                }
            }

            var write = 0;
            for (var i = 0; i < _readyUnshown.Count; i++)
            {
                var cell = _readyUnshown[i];
                if (!cell.Wanted || cell.Shown || cell.State != DetailCellState.Ready)
                {
                    continue;
                }

                if (!TryShowWanted(cell.Level, cell.Coord))
                {
                    _readyUnshown[write++] = cell;
                }
            }

            _readyUnshown.RemoveRange(write, _readyUnshown.Count - write);

            write = 0;
            for (var i = 0; i < _retiring.Count; i++)
            {
                var entry = _retiring[i];
                if (!TryRetire(entry.z, entry.xy))
                {
                    _retiring[write++] = entry;
                }
            }

            _retiring.RemoveRange(write, _retiring.Count - write);
        }

        // Shows a wanted ready cell (or column) unless a shown coarser cell covers it (that
        // cell's retirement shows it); shown finer cells under it are hidden. Returns true
        // when shown.
        private bool TryShowWanted(int level, int2 coord)
        {
            for (var up = level + 1; up <= _rings.Levels; up++)
            {
                var ancestor = coord >> (up - level);
                if (_cells[up].TryGetValue(ancestor, out var cell) && cell.Shown)
                {
                    return false;
                }
            }

            if (level > 0 && ShownBelow(level, coord) > 0)
            {
                HideBelow(level, coord);
            }

            Show(level, coord);
            return true;
        }

        // A shown cell (or column) no longer wanted. Returns true when it no longer needs
        // checking (hidden, or wanted again).
        private bool TryRetire(int level, int2 coord)
        {
            if (level == 0)
            {
                if (!_shownColumns.Contains(coord))
                {
                    return true;
                }

                if (_rings.InRegion(0, coord))
                {
                    return true;
                }
            }
            else
            {
                if (!_cells[level].TryGetValue(coord, out var cell) || !cell.Shown)
                {
                    return true;
                }

                if (cell.Wanted)
                {
                    return true;
                }
            }

            // A wanted coarser cell will replace it when ready.
            for (var up = level + 1; up <= _rings.Levels; up++)
            {
                if (_rings.IsDisplayed(up, coord >> (up - level)))
                {
                    return false;
                }
            }

            if (level > 0 && _rings.InRegion(level - 1, coord << 1))
            {
                // Inside the finer square: split into the four children one level finer as
                // soon as they are ready; children that are themselves inside the next finer
                // square are transitional and split again (D-057, amended 2026-11-22).
                if (!ChildrenReady(level, coord))
                {
                    return false;
                }

                ShowChildren(level, coord);
            }

            Hide(level, coord);
            return true;
        }

        // Whether the four children of a cell, one level finer, are ready; missing
        // children are created (transitional when the rings do not display them).
        private bool ChildrenReady(int level, int2 coord)
        {
            var below = level - 1;
            var ready = true;
            var child = coord << 1;
            for (var dz = 0; dz <= 1; dz++)
            for (var dx = 0; dx <= 1; dx++)
            {
                var c = child + new int2(dx, dz);
                if (below == 0)
                {
                    ready &= _world.IsColumnReady(c);
                }
                else
                {
                    ready &= NeedCell(below, c).State == DetailCellState.Ready;
                }
            }

            return ready;
        }

        // The cell at a level and coordinate, kept wanted; created and queued nearest first
        // when missing.
        private DetailCell NeedCell(int level, int2 coord)
        {
            var cells = _cells[level];
            if (!cells.TryGetValue(coord, out var cell))
            {
                cell = _cellPool.Count > 0 ? _cellPool.Pop() : new DetailCell();
                cell.Reset(level, coord);
                cells.Add(coord, cell);
            }
            else if (cell.Wanted)
            {
                return cell;
            }

            cell.Wanted = true;
            if (cell.State != DetailCellState.Ready)
            {
                var d = ((float2)coord + 0.5f) * (1 << level) - (float2)_rings.Center(0);
                cell.SortKey = math.lengthsq(d);
                var index = _work.BinarySearch(cell, ByDistance);
                _work.Insert(index < 0 ? ~index : index, cell);
            }

            return cell;
        }

        // Shows the four children; transitional ones (not displayed by the rings) are shown
        // as no longer wanted, so they retire and split in turn.
        private void ShowChildren(int level, int2 coord)
        {
            var below = level - 1;
            var child = coord << 1;
            for (var dz = 0; dz <= 1; dz++)
            for (var dx = 0; dx <= 1; dx++)
            {
                var c = child + new int2(dx, dz);
                if (!IsShown(below, c))
                {
                    Show(below, c);
                }

                if (below > 0 && !_rings.IsDisplayed(below, c))
                {
                    _cells[below][c].Wanted = false;
                    _retiring.Add(new int3(c, below));
                }
            }
        }

        private bool IsShown(int level, int2 coord) =>
            level == 0
                ? _shownColumns.Contains(coord)
                : _cells[level].TryGetValue(coord, out var cell) && cell.Shown;

        // Hides every shown cell and column strictly inside a cell's area.
        private void HideBelow(int level, int2 coord)
        {
            var child = coord << 1;
            for (var dz = 0; dz <= 1; dz++)
            for (var dx = 0; dx <= 1; dx++)
            {
                var c = child + new int2(dx, dz);
                var below = level - 1;
                if (IsShown(below, c))
                {
                    Hide(below, c);
                }

                if (below > 0 && ShownBelow(below, c) > 0)
                {
                    HideBelow(below, c);
                }
            }
        }

        private int ShownBelow(int level, int2 coord) =>
            _shownBelow[level].TryGetValue(coord, out var n) ? n : 0;

        private void Show(int level, int2 coord)
        {
            if (level == 0)
            {
                _shownColumns.Add(coord);
                _world.SetColumnShown(coord, true);
            }
            else
            {
                var cell = _cells[level][coord];
                cell.Shown = true;
                _shownCells++;
                foreach (var view in cell.Views)
                {
                    view.Renderer.enabled = true;
                }
            }

            CountAncestors(level, coord, 1);
        }

        private void Hide(int level, int2 coord)
        {
            if (level == 0)
            {
                _shownColumns.Remove(coord);
                _world.SetColumnShown(coord, false);
            }
            else
            {
                var cell = _cells[level][coord];
                cell.Shown = false;
                _shownCells--;
                foreach (var view in cell.Views)
                {
                    view.Renderer.enabled = false;
                }

                if (!cell.Wanted)
                {
                    QueueRelease(cell);
                }
            }

            CountAncestors(level, coord, -1);
        }

        private void CountAncestors(int level, int2 coord, int delta)
        {
            for (var up = level + 1; up <= _rings.Levels; up++)
            {
                var ancestor = coord >> (up - level);
                var counts = _shownBelow[up];
                var n = (counts.TryGetValue(ancestor, out var value) ? value : 0) + delta;
                if (n == 0)
                {
                    counts.Remove(ancestor);
                }
                else
                {
                    counts[ancestor] = n;
                }
            }
        }

        private void QueueRelease(DetailCell cell)
        {
            if (!_releasePending.Contains(cell))
            {
                _releasePending.Add(cell);
            }
        }

        // Unwanted cells that are not shown go back to the pool once their jobs are done.
        private void ReleasePending()
        {
            var write = 0;
            for (var i = 0; i < _releasePending.Count; i++)
            {
                var cell = _releasePending[i];
                if (cell.Wanted || cell.Shown)
                {
                    continue;
                }

                if (cell.HasJobsInFlight)
                {
                    _releasePending[write++] = cell;
                    continue;
                }

                foreach (var view in cell.Views)
                {
                    _views.Release(view);
                }

                _meshBytes -= cell.MeshBytes;
                _cells[cell.Level].Remove(cell.Coord);
                cell.Views.Clear();
                cell.Version++;
                _cellPool.Push(cell);
            }

            _releasePending.RemoveRange(write, _releasePending.Count - write);
        }

        private bool AllFullDetailShown()
        {
            var min = _rings.Min(0);
            var max = _rings.Max(0);
            for (var z = min.y; z < max.y; z++)
            for (var x = min.x; x < max.x; x++)
            {
                if (!_shownColumns.Contains(new int2(x, z)))
                {
                    return false;
                }
            }

            return true;
        }

        private int CellCount()
        {
            if (_cells == null)
            {
                return 0;
            }

            var count = 0;
            for (var level = 1; level < _cells.Length; level++)
            {
                count += _cells[level].Count;
            }

            return count;
        }

        private void ReleaseResources()
        {
            if (_heightSlots != null)
            {
                foreach (var slot in _heightSlots)
                {
                    slot.Handle.Complete();
                    if (slot.Range.IsCreated)
                    {
                        slot.Range.Dispose();
                    }
                }

                _heightSlots = null;
            }

            if (_chunkSlots != null)
            {
                foreach (var slot in _chunkSlots)
                {
                    slot.Handle.Complete();
                    if (slot.HasData)
                    {
                        slot.Data.Dispose();
                        slot.HasData = false;
                    }

                    if (slot.Samples.IsCreated)
                    {
                        slot.Samples.Dispose();
                    }

                    if (slot.Padded.IsCreated)
                    {
                        slot.Padded.Dispose();
                    }
                }

                _chunkSlots = null;
            }

            if (_renderData.IsCreated)
            {
                _renderData.Dispose();
            }

            if (_renderLayout.IsCreated)
            {
                _renderLayout.Dispose();
            }

            if (_collisionLayout.IsCreated)
            {
                _collisionLayout.Dispose();
            }

            _views?.Dispose();
            _views = null;
            if (_scratchCollision != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_scratchCollision);
                }
                else
                {
                    DestroyImmediate(_scratchCollision);
                }

                _scratchCollision = null;
            }

            if (_world != null && _shownColumns.Count > 0)
            {
                _scratchColumns.Clear();
                _scratchColumns.AddRange(_shownColumns);
                foreach (var column in _scratchColumns)
                {
                    _world.SetColumnShown(column, false);
                }
            }

            _shownColumns.Clear();
            _work.Clear();
            _readyUnshown.Clear();
            _releasePending.Clear();
            _retiring.Clear();
            _cells = null;
            _shownBelow = null;
            _busySlots = 0;
            _shownCells = 0;
            _meshBytes = 0;
            _running = false;
            IsSettled = false;
        }

        private sealed class HeightSlot
        {
            public NativeReference<ColumnRange> Range;
            public JobHandle Handle;
            public DetailCell Cell;
            public int Version;
            public bool Busy;
        }

        private sealed class ChunkSlot
        {
            public NativeArray<TerrainSample> Samples;
            public NativeArray<ushort> Padded;
            public Mesh.MeshDataArray Data;
            public bool HasData;
            public JobHandle Handle;
            public DetailCell Cell;
            public int Version;
            public ChunkCoord Coord;
            public int ScheduledFrame;
            public bool Busy;
        }
    }
}
