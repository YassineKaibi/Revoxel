using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Revoxel.World
{
    // Loaded chunks, keyed by chunk coordinate (D-030, D-050), voxel edits, and chunk-column
    // streaming around a focus (PLAN-02 Task 8). Must sit at the world origin: voxel
    // coordinates are world coordinates. The streamed area is the full-detail square
    // (D-057, PLAN-03 Task 1) plus one margin column; under DistanceDetail (level 0 of the
    // detail levels) a column's chunks are drawn only once the detail swaps them in.
    //
    // Frame order. Update, sharing one main-thread budget (D-052): collect finished
    // generation and insert complete columns; unload columns beyond the radius plus
    // hysteresis; schedule generation nearest first (a column height job, then a chunk job
    // only for chunks the heights do not settle, D-053); pad and schedule meshing of dirty
    // chunks nearest first, only once every neighbor is loaded or outside the island.
    // LateUpdate: mesh chunks dirtied by edits near the focus at once, apply finished
    // meshes, and keep colliders on the chunks near the focus only.
    public sealed class VoxelWorld : MonoBehaviour
    {
        private static readonly ProfilerMarker CollectMarker = new ProfilerMarker(
            "Revoxel.World.CollectGeneration"
        );
        private static readonly ProfilerMarker UnloadMarker = new ProfilerMarker(
            "Revoxel.World.Unload"
        );
        private static readonly ProfilerMarker ScheduleGenerationMarker = new ProfilerMarker(
            "Revoxel.World.ScheduleGeneration"
        );
        private static readonly ProfilerMarker ScheduleMeshingMarker = new ProfilerMarker(
            "Revoxel.World.ScheduleMeshing"
        );
        private static readonly ProfilerMarker ApplyMarker = new ProfilerMarker(
            "Revoxel.World.ApplyMeshes"
        );
        private static readonly ProfilerMarker CollidersMarker = new ProfilerMarker(
            "Revoxel.World.Colliders"
        );

        // Water queries look this far up for the surface: deeper than any generated water.
        private const int WaterScanVoxels = 1024;

        private static readonly int3[] FaceNeighbors =
        {
            new int3(1, 0, 0),
            new int3(-1, 0, 0),
            new int3(0, 1, 0),
            new int3(0, -1, 0),
            new int3(0, 0, 1),
            new int3(0, 0, -1),
        };

        [SerializeField]
        private VoxelRegistry _registry;

        [SerializeField]
        private TerrainSettings _terrainSettings;

        [SerializeField]
        private WorldStreamingSettings _streamingSettings;

        [SerializeField]
        private WorldRenderSettings _renderSettings;

        [SerializeField]
        private Material _material;

        [SerializeField]
        [Tooltip("Material of the liquid submesh (Revoxel/Water, D-073).")]
        private Material _waterMaterial;

        [SerializeField]
        [Tooltip("Placeholder fixed seed (PLAN-02).")]
        private int _seed = 42;

        [SerializeField]
        [Tooltip("Columns stream around this transform. Main camera if empty.")]
        private Transform _focus;

        private readonly Dictionary<ChunkCoord, Chunk> _chunks =
            new Dictionary<ChunkCoord, Chunk>();
        private readonly Dictionary<int2, StreamColumn> _columns =
            new Dictionary<int2, StreamColumn>();
        private readonly Stack<StreamColumn> _columnPool = new Stack<StreamColumn>();
        private readonly Stack<Chunk> _chunkPool = new Stack<Chunk>();
        private readonly ChunkStreamer _streamer = new ChunkStreamer();
        private readonly List<int2> _wanted = new List<int2>();
        private readonly List<StreamColumn> _toRemove = new List<StreamColumn>();
        private readonly List<Chunk> _urgent = new List<Chunk>();
        private readonly List<Chunk> _applied = new List<Chunk>();
        private readonly List<Chunk> _toBake = new List<Chunk>();
        private readonly List<Chunk> _scratchChunks = new List<Chunk>();
        private readonly HashSet<Chunk> _colliderChunks = new HashSet<Chunk>();
        private readonly Stopwatch _watch = new Stopwatch();

        private VoxelRegistry _activeRegistry;
        private WorldBounds _bounds;
        private TerrainParams _params;
        private bool[] _opaque;
        private bool[] _liquid;
        private TerrainParams _queryParams;
        private bool _hasQueryParams;
        private bool _underwater;
        private ChunkMeshPipeline _pipeline;
        private ChunkViewPool _views;
        private HeightSlot[] _heightSlots;
        private GenSlot[] _genSlots;
        private NativeArray<EntityId> _bakeIds;
        private Texture2D _materialTable;
        private ChunkVoxels _air;
        private bool _streaming;
        private int2 _focusColumn;
        private bool _hasFocusColumn;
        private int2 _fullCenter;
        private bool _hasFullCenter;
        private bool _focusMoved;
        private int _columnsInProgress;
        private int _genBusy;
        private int _dirtyCount;
        private int _uniformChunks;
        private long _voxelMemory;
        private long _budgetTicks;
        private long _updateTicks;
        private bool _budgetHit;
        private bool _pendingReady;
        private int _wantedInBounds;
        private int _wantedLoaded;
        private int _meshedWithMissingNeighbors;
        private float _lastMainThreadMs;
        private bool _firstSettledRaised;

        // Inclusive min and max voxel of a change. Raised once per edit call.
        public event Action<int3, int3> VoxelsChanged;

        // Raised once, the first time nothing wanted is pending.
        public event Action FirstSettled;

        public bool IsInitialized => _bounds != null;

        // Nothing wanted is pending: every wanted column is loaded and every meshable dirty
        // chunk is meshed.
        public bool IsSettled { get; private set; }

        public bool IsLoaded => _firstSettledRaised;

        // Share of the wanted columns that are loaded, 0 to 1, for the loading screen.
        public float LoadProgress =>
            _wantedInBounds == 0 ? 0f : (float)_wantedLoaded / _wantedInBounds;

        public int DirtyCount => _dirtyCount;

        public int ViewCount => _views?.ActiveCount ?? 0;

        public WorldBounds Bounds => _bounds;

        public VoxelRegistry Registry => _activeRegistry;

        public WorldStreamingSettings StreamingSettings => _streamingSettings;

        public Transform Focus
        {
            get => _focus;
            set => _focus = value;
        }

        // Center (even) and half-width, in columns, of the full-detail square (D-057).
        public int2 FullDetailCenter => _fullCenter;

        public int FullDetailHalfWidth => _streamingSettings.LoadRadiusChunks;

        public bool HasFullDetailCenter => _hasFullCenter;

        public bool IsStreaming => _streaming;

        // When true (set by DistanceDetail), a column's chunks render only while the column
        // is shown, and a shown column is never unloaded.
        internal bool VisibilityManaged { get; set; }

        internal TerrainParams TerrainParameters => _params;

        internal Material Material => _material;

        internal Material WaterMaterial => _waterMaterial;

        // The chunk materials by submesh: voxels, then liquids (D-073).
        internal Material[] Materials { get; private set; }

        // True while the main camera is inside a liquid voxel (D-073).
        public bool IsCameraUnderwater => _underwater;

        public bool IsInFullDetail(int2 column) =>
            _hasFullCenter
            && math.all(column >= _fullCenter - FullDetailHalfWidth)
            && math.all(column < _fullCenter + FullDetailHalfWidth);

        // Outside the island: true (nothing to draw). Else loaded, nothing dirty and no mesh
        // job in flight for the column.
        internal bool IsColumnReady(int2 column)
        {
            if (!ColumnInBounds(column))
            {
                return true;
            }

            return _columns.TryGetValue(column, out var c)
                && c.Stage == ColumnStage.Loaded
                && c.Dirty.Count == 0
                && (_pipeline == null || !_pipeline.HasJobForColumn(column));
        }

        internal bool IsColumnShown(int2 column) =>
            _columns.TryGetValue(column, out var c) && c.Shown;

        // Shows or hides the renderers of a loaded column's chunks. No-op when not loaded.
        internal void SetColumnShown(int2 column, bool shown)
        {
            if (!_columns.TryGetValue(column, out var c) || c.Stage != ColumnStage.Loaded)
            {
                return;
            }

            c.Shown = shown;
            foreach (var chunk in c.Chunks)
            {
                if (chunk != null && chunk.View != null)
                {
                    chunk.View.Renderer.enabled = shown;
                }
            }
        }

        public WorldStats Stats =>
            new WorldStats
            {
                LoadedChunks = _chunks.Count,
                UniformChunks = _uniformChunks,
                ViewCount = ViewCount,
                PendingGeneration = _columnsInProgress,
                PendingMeshing = _dirtyCount + (_pipeline?.Busy ?? 0),
                VoxelMemoryBytes = _voxelMemory,
                LastMainThreadMs = _lastMainThreadMs,
                MeshedWithMissingNeighbors = _meshedWithMissingNeighbors,
            };

        // No chunk is allocated: chunks arrive through streaming or InsertChunk.
        public void Initialize(VoxelRegistry registry, WorldBounds bounds)
        {
            if (IsInitialized)
            {
                ReleaseResources();
            }

            _activeRegistry = registry;
            _bounds = bounds;
            _liquid = new bool[WorldConstants.MaxVoxelTypes];
            using var renderData = registry.CreateRenderData(Allocator.Temp);
            for (var i = 0; i < renderData.Length; i++)
            {
                _liquid[i] = renderData[i].IsLiquid;
            }
        }

        // Takes ownership of the voxels. Replaces (and disposes) a chunk already there.
        internal void InsertChunk(ChunkCoord coord, ChunkVoxels voxels)
        {
            var columnCoord = Coords.ColumnOf(coord);
            if (!_columns.TryGetValue(columnCoord, out var column))
            {
                column = RentColumn(columnCoord, ColumnStage.Loaded);
            }

            if (_chunks.TryGetValue(coord, out var old))
            {
                RemoveChunk(old);
            }

            AddChunk(column, coord, voxels);
        }

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk) =>
            _chunks.TryGetValue(coord, out chunk);

        // Generated surface height at a voxel column, meters (edits not included). False
        // when the terrain settings are missing.
        public bool TryGetSurfaceHeightMeters(int x, int z, out float meters)
        {
            if (_terrainSettings == null)
            {
                meters = 0f;
                return false;
            }

            meters = TerrainGenerator.SurfaceHeight(QueryParams(), x, z) * WorldConstants.VoxelSize;
            return true;
        }

        // Generated biome and climate of the column under a position, meters (D-062; edits
        // not included). False when the terrain settings are missing.
        public bool TrySampleBiome(float3 positionMeters, out TerrainSample sample)
        {
            if (_terrainSettings == null || _terrainSettings.Biomes == null)
            {
                sample = default;
                return false;
            }

            var voxel = Coords.WorldPositionToVoxel(positionMeters);
            sample = BiomeMap.Sample(QueryParams(), voxel.x, voxel.z);
            return true;
        }

        // The water at a position, meters (D-074): from the loaded voxels, so edits count.
        // In water when the voxel there is a liquid; the surface is the top of the liquid
        // above it. False when the chunk there is not loaded.
        public bool TrySampleWater(float3 positionMeters, out WaterSample sample)
        {
            sample = default;
            var voxel = Coords.WorldPositionToVoxel(positionMeters);
            Coords.WorldToChunk(voxel, out var coord, out _);
            if (_liquid == null || !_chunks.ContainsKey(coord))
            {
                return false;
            }

            if (!_liquid[GetVoxel(voxel).Value])
            {
                return true;
            }

            var top = voxel.y;
            var limit = math.min(WorldConstants.WorldHeightVoxels - 1, voxel.y + WaterScanVoxels);
            while (top < limit && _liquid[GetVoxel(new int3(voxel.x, top + 1, voxel.z)).Value])
            {
                top++;
            }

            var surface = (top + 1) * WorldConstants.VoxelSize;
            sample = new WaterSample
            {
                InWater = true,
                SurfaceMeters = surface,
                DepthMeters = surface - positionMeters.y,
                Body =
                    _terrainSettings != null && _terrainSettings.WaterSettings != null
                        ? TerrainGenerator.Sample(QueryParams(), voxel.x, voxel.z).Body
                        : WaterBody.Sea,
            };
            return true;
        }

        // Generated water of a voxel column (D-074; edits not included): level and body.
        // False when the terrain settings are missing.
        public bool TryGetWaterColumn(int x, int z, out WaterColumn column)
        {
            if (_terrainSettings == null || _terrainSettings.WaterSettings == null)
            {
                column = default;
                return false;
            }

            column = WaterMap.Column(QueryParams(), x, z);
            return true;
        }

        // The streaming parameters, or ones built once for queries before streaming starts.
        private TerrainParams QueryParams()
        {
            if (_streaming)
            {
                return _params;
            }

            if (!_hasQueryParams)
            {
                _queryParams = TerrainParams.Create(new WorldSeed(_seed), _terrainSettings);
                _hasQueryParams = true;
            }

            return _queryParams;
        }

        public bool TryGetBiome(BiomeId id, out Biome biome)
        {
            biome = null;
            return _terrainSettings != null
                && _terrainSettings.Biomes != null
                && _terrainSettings.Biomes.TryGet(id, out biome);
        }

        public bool IsColumnLoaded(int2 column) =>
            _columns.TryGetValue(column, out var c) && c.Stage == ColumnStage.Loaded;

        // Every loaded column. Allocates; for tests and tools, not for hot paths.
        public IEnumerable<int2> LoadedColumns()
        {
            foreach (var pair in _columns)
            {
                if (pair.Value.Stage == ColumnStage.Loaded)
                {
                    yield return pair.Key;
                }
            }
        }

        // Air when out of bounds or not loaded; never throws.
        public VoxelId GetVoxel(int3 voxel)
        {
            Coords.WorldToChunk(voxel, out var coord, out var local);
            return _chunks.TryGetValue(coord, out var chunk)
                ? chunk.Voxels.Get(Coords.LocalIndex(local))
                : VoxelId.Air;
        }

        // False when out of bounds, not loaded, unregistered id or unchanged; never throws.
        public bool TrySetVoxel(int3 voxel, VoxelId id)
        {
            if (!IsInitialized || !_activeRegistry.IsRegistered(id))
            {
                return false;
            }

            Coords.WorldToChunk(voxel, out var coord, out var local);
            if (!_chunks.TryGetValue(coord, out var chunk))
            {
                return false;
            }

            var index = Coords.LocalIndex(local);
            if (chunk.Voxels.Get(index) == id)
            {
                return false;
            }

            var memory = chunk.Voxels.MemoryBytes;
            var wasUniform = chunk.Voxels.IsUniform;
            chunk.Voxels.Set(index, id);
            TrackStorageChange(chunk, memory, wasUniform);
            MarkDirtyAround(chunk, local, local);
            VoxelsChanged?.Invoke(voxel, voxel);
            return true;
        }

        // Sets every loaded voxel whose center is within the radius and differs from fill.
        // Returns the number changed; raises one event when it is not zero.
        public int CarveSphere(float3 centerMeters, float radiusMeters, VoxelId fill)
        {
            if (!IsInitialized || !_activeRegistry.IsRegistered(fill) || !(radiusMeters > 0f))
            {
                return 0;
            }

            var center = centerMeters * WorldConstants.VoxelsPerMeter;
            var radius = radiusMeters * WorldConstants.VoxelsPerMeter;
            var radiusSq = radius * radius;
            var min = (int3)math.floor(center - radius);
            var max = (int3)math.floor(center + radius);
            var minChunk = Coords.ChunkOf(min).Value;
            var maxChunk = Coords.ChunkOf(max).Value;
            var changedMin = new int3(int.MaxValue);
            var changedMax = new int3(int.MinValue);
            var count = 0;

            for (var cy = minChunk.y; cy <= maxChunk.y; cy++)
            for (var cz = minChunk.z; cz <= maxChunk.z; cz++)
            for (var cx = minChunk.x; cx <= maxChunk.x; cx++)
            {
                if (!_chunks.TryGetValue(new ChunkCoord(cx, cy, cz), out var chunk))
                {
                    continue;
                }

                var origin = Coords.ChunkOrigin(chunk.Coord);
                var localMin = math.max(min - origin, 0);
                var localMax = math.min(max - origin, WorldConstants.ChunkMask);
                var chunkMin = new int3(int.MaxValue);
                var chunkMax = new int3(int.MinValue);
                var memory = chunk.Voxels.MemoryBytes;
                var wasUniform = chunk.Voxels.IsUniform;
                ref var voxels = ref chunk.Voxels;
                for (var y = localMin.y; y <= localMax.y; y++)
                for (var z = localMin.z; z <= localMax.z; z++)
                for (var x = localMin.x; x <= localMax.x; x++)
                {
                    var local = new int3(x, y, z);
                    if (math.distancesq((float3)(origin + local) + 0.5f, center) > radiusSq)
                    {
                        continue;
                    }

                    var index = Coords.LocalIndex(local);
                    if (voxels.Get(index) == fill)
                    {
                        continue;
                    }

                    voxels.Set(index, fill);
                    chunkMin = math.min(chunkMin, local);
                    chunkMax = math.max(chunkMax, local);
                    count++;
                }

                if (chunkMin.x != int.MaxValue)
                {
                    TrackStorageChange(chunk, memory, wasUniform);
                    MarkDirtyAround(chunk, chunkMin, chunkMax);
                    changedMin = math.min(changedMin, origin + chunkMin);
                    changedMax = math.max(changedMax, origin + chunkMax);
                }
            }

            if (count > 0)
            {
                VoxelsChanged?.Invoke(changedMin, changedMax);
            }

            return count;
        }

        public void ReleaseResources()
        {
            CompleteGenerationJobs();
            _pipeline?.Dispose();
            _pipeline = null;

            foreach (var column in _columns.Values)
            {
                column.DisposePending();
            }

            foreach (var chunk in _chunks.Values)
            {
                chunk.Dispose();
            }

            _views?.Dispose();
            _views = null;
            DisposeSlots();
            if (_bakeIds.IsCreated)
            {
                _bakeIds.Dispose();
            }

            if (_air.IsCreated)
            {
                _air.Dispose();
            }

            if (_materialTable != null)
            {
                DestroyUnityObject(_materialTable);
                _materialTable = null;
            }

            if (_underwater)
            {
                _underwater = false;
                VoxelShaderGlobals.SetUnderwater(false);
            }

            _chunks.Clear();
            _columns.Clear();
            _urgent.Clear();
            _applied.Clear();
            _toBake.Clear();
            _colliderChunks.Clear();
            _wanted.Clear();
            _bounds = null;
            _streaming = false;
            _hasFocusColumn = false;
            _hasFullCenter = false;
            _columnsInProgress = 0;
            _genBusy = 0;
            _dirtyCount = 0;
            _uniformChunks = 0;
            _voxelMemory = 0;
            IsSettled = false;
        }

        private void Start()
        {
            if (!TryStartStreaming(out var error))
            {
                Debug.LogError($"[World] VoxelWorld cannot stream: {error}", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (!_streaming)
            {
                return;
            }

            _watch.Restart();
            _budgetTicks = (long)(
                _streamingSettings.MainThreadBudgetMs * Stopwatch.Frequency / 1000.0
            );
            _budgetHit = false;
            UpdateFocus();
            using (CollectMarker.Auto())
            {
                CollectGeneration();
            }

            using (UnloadMarker.Auto())
            {
                UnloadColumns();
            }

            using (ScheduleGenerationMarker.Auto())
            {
                ScheduleGeneration();
            }

            using (ScheduleMeshingMarker.Auto())
            {
                ScheduleMeshing();
            }

            JobHandle.ScheduleBatchedJobs();
            _updateTicks = _watch.ElapsedTicks;
        }

        private void LateUpdate()
        {
            if (!_streaming)
            {
                return;
            }

            // LateUpdate gets what Update left of the budget.
            _watch.Restart();
            var remaining = math.max(0L, _budgetTicks - _updateTicks);
            _applied.Clear();
            using (ApplyMarker.Auto())
            {
                ScheduleUrgent();
                _pipeline.ApplyFinished(_views, _applied, _watch, remaining, Time.frameCount);
                foreach (var chunk in _applied)
                {
                    if (chunk.View != null)
                    {
                        chunk.View.Renderer.enabled = !VisibilityManaged || chunk.Column.Shown;
                    }
                }
            }

            using (CollidersMarker.Auto())
            {
                UpdateColliders();
            }

            UpdateUnderwater();

            _lastMainThreadMs = (float)(
                (_updateTicks + _watch.ElapsedTicks) * 1000.0 / Stopwatch.Frequency
            );
            UpdateSettled();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        // The shaders fog the view under water while the main camera is in a liquid.
        private void UpdateUnderwater()
        {
            var main = Camera.main;
            var underwater =
                main != null
                && TrySampleWater(main.transform.position, out var water)
                && water.InWater;
            if (underwater != _underwater)
            {
                _underwater = underwater;
                VoxelShaderGlobals.SetUnderwater(underwater);
            }
        }

        private bool TryStartStreaming(out string error)
        {
            if (
                _registry == null
                || _terrainSettings == null
                || _streamingSettings == null
                || _renderSettings == null
                || _material == null
                || _waterMaterial == null
            )
            {
                error =
                    "registry, terrain, streaming and render settings and both materials are required.";
                return false;
            }

            if (
                !_registry.TryValidate(out error)
                || !_terrainSettings.TryValidate(out error)
                || !_streamingSettings.TryValidate(out error)
                || !_renderSettings.TryValidate(out error)
            )
            {
                return false;
            }

            Initialize(_registry, WorldBounds.Island());
            _params = TerrainParams.Create(new WorldSeed(_seed), _terrainSettings);
            _hasQueryParams = false;
            _opaque = new bool[WorldConstants.MaxVoxelTypes];
            using (var renderData = _registry.CreateRenderData(Allocator.Temp))
            {
                for (var i = 0; i < renderData.Length; i++)
                {
                    _opaque[i] = renderData[i].IsOpaque;
                }
            }

            _materialTable = _registry.CreateMaterialTable();
            VoxelShaderGlobals.SetMaterialTable(_materialTable);
            VoxelShaderGlobals.SetDefaults(_renderSettings);
            _pipeline = new ChunkMeshPipeline(_registry, _streamingSettings.MaxMeshJobsInFlight);
            Materials = new[] { _material, _waterMaterial };
            _views = new ChunkViewPool(transform, Materials);
            _air = ChunkVoxels.CreateUniform(VoxelId.Air, Allocator.Persistent);
            var slots = _streamingSettings.MaxGenerationJobsInFlight;
            _heightSlots = new HeightSlot[slots];
            _genSlots = new GenSlot[slots];
            for (var i = 0; i < slots; i++)
            {
                _heightSlots[i] = new HeightSlot
                {
                    Range = new NativeReference<ColumnRange>(Allocator.Persistent),
                };
                _genSlots[i] = new GenSlot
                {
                    Scratch = new NativeArray<ushort>(
                        WorldConstants.VoxelsPerChunk,
                        Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory
                    ),
                    Result = new NativeReference<ChunkVoxels>(Allocator.Persistent),
                };
            }

            _bakeIds = new NativeArray<EntityId>(64, Allocator.Persistent);
            _streaming = true;
            error = string.Empty;
            return true;
        }

        private void UpdateFocus()
        {
            var focus = _focus;
            if (focus == null)
            {
                var main = Camera.main;
                focus = main != null ? main.transform : null;
            }

            var position = focus != null ? (float3)focus.position : float3.zero;
            var column = Coords.ColumnOf(Coords.ChunkOf(Coords.WorldPositionToVoxel(position)));
            _focusMoved = !_hasFocusColumn || !column.Equals(_focusColumn);
            if (_focusMoved)
            {
                // Stale until LateUpdate recomputes it.
                IsSettled = false;
                _focusColumn = column;
                _hasFocusColumn = true;
            }

            if (ChunkStreamer.RecenterFullDetail(ref _fullCenter, column, _hasFullCenter))
            {
                _hasFullCenter = true;
                _streamer.ComputeWanted(_fullCenter, LoadedHalfWidth, _wanted);
            }
        }

        private bool OverBudget()
        {
            if (_watch.ElapsedTicks <= _budgetTicks)
            {
                return false;
            }

            _budgetHit = true;
            return true;
        }

        // Step 1: finished height jobs classify their column's chunks; finished chunk jobs
        // fill them; complete columns are inserted.
        private void CollectGeneration()
        {
            for (var i = 0; i < _heightSlots.Length; i++)
            {
                var slot = _heightSlots[i];
                if (!slot.Busy || !slot.Handle.IsCompleted)
                {
                    continue;
                }

                slot.Handle.Complete();
                slot.Busy = false;
                var column = slot.Column;
                slot.Column = null;
                column.HeightSlot = -1;
                var range = slot.Range.Value;
                for (var cy = 0; cy < WorldConstants.ChunksY; cy++)
                {
                    var kind = TerrainGenerator.Classify(_params, range, cy, out var id);
                    if (kind == ChunkKind.Mixed)
                    {
                        column.MixedQueue.Add(cy);
                    }
                    else
                    {
                        column.Pending[cy] = ChunkVoxels.CreateUniform(id, Allocator.Persistent);
                    }
                }

                column.Stage = ColumnStage.Chunks;
                TryFinishColumn(column);
            }

            for (var i = 0; i < _genSlots.Length; i++)
            {
                var slot = _genSlots[i];
                if (!slot.Busy || !slot.Handle.IsCompleted)
                {
                    continue;
                }

                if (OverBudget())
                {
                    return;
                }

                slot.Handle.Complete();
                slot.Busy = false;
                _genBusy--;
                var column = slot.Column;
                slot.Column = null;
                column.Pending[slot.Cy] = slot.Result.Value;
                column.MixedInFlight--;
                TryFinishColumn(column);
            }
        }

        // Inserts a column whose chunks are all generated, or drops it if no longer wanted.
        private void TryFinishColumn(StreamColumn column)
        {
            if (column.Stage != ColumnStage.Chunks || column.HasJobsInFlight)
            {
                return;
            }

            if (ShouldUnload(column.Coord))
            {
                DropColumn(column);
                return;
            }

            if (column.MixedQueue.Count > 0)
            {
                return;
            }

            for (var cy = 0; cy < WorldConstants.ChunksY; cy++)
            {
                var coord = new ChunkCoord(column.Coord.x, cy, column.Coord.y);
                AddChunk(column, coord, column.Pending[cy]);
                column.Pending[cy] = default;
            }

            column.Stage = ColumnStage.Loaded;
            _columnsInProgress--;

            // The new chunks, and every neighbor whose padded volume reaches into them.
            foreach (var chunk in column.Chunks)
            {
                if (!chunk.IsUniformAir)
                {
                    MarkDirty(chunk, false);
                }
            }

            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (
                    (dx != 0 || dz != 0)
                    && _columns.TryGetValue(column.Coord + new int2(dx, dz), out var neighbor)
                    && neighbor.Stage == ColumnStage.Loaded
                )
                {
                    foreach (var chunk in neighbor.Chunks)
                    {
                        if (chunk != null && !chunk.IsUniformAir)
                        {
                            MarkDirty(chunk, false);
                        }
                    }
                }
            }
        }

        // Step 2: loaded columns beyond the radius plus hysteresis, and generating columns
        // no longer wanted that have no job in flight.
        private void UnloadColumns()
        {
            _toRemove.Clear();
            foreach (var column in _columns.Values)
            {
                if (
                    ShouldUnload(column.Coord)
                    && !column.HasJobsInFlight
                    && !(VisibilityManaged && column.Shown)
                )
                {
                    _toRemove.Add(column);
                }
            }

            foreach (var column in _toRemove)
            {
                if (OverBudget())
                {
                    break;
                }

                if (column.Stage == ColumnStage.Loaded)
                {
                    for (var cy = 0; cy < WorldConstants.ChunksY; cy++)
                    {
                        if (column.Chunks[cy] != null)
                        {
                            RemoveChunk(column.Chunks[cy]);
                        }
                    }

                    _columns.Remove(column.Coord);
                    _columnPool.Push(column);
                }
                else
                {
                    DropColumn(column);
                }
            }

            _toRemove.Clear();
        }

        // Step 3: new columns nearest first (a height job each), then chunk jobs for the
        // chunks their heights did not settle.
        private void ScheduleGeneration()
        {
            _wantedInBounds = 0;
            _wantedLoaded = 0;
            var maxColumns = _streamingSettings.MaxGenerationJobsInFlight;
            foreach (var coord in _wanted)
            {
                if (!ColumnInBounds(coord))
                {
                    continue;
                }

                _wantedInBounds++;
                if (!_columns.TryGetValue(coord, out var column))
                {
                    if (_columnsInProgress < maxColumns && !OverBudget())
                    {
                        StartColumn(coord);
                    }

                    continue;
                }

                if (column.Stage == ColumnStage.Loaded)
                {
                    _wantedLoaded++;
                    continue;
                }

                while (
                    column.Stage == ColumnStage.Chunks
                    && column.MixedQueue.Count > 0
                    && _genBusy < _genSlots.Length
                    && !OverBudget()
                )
                {
                    var last = column.MixedQueue.Count - 1;
                    var cy = column.MixedQueue[last];
                    column.MixedQueue.RemoveAt(last);
                    ScheduleChunk(column, cy);
                }
            }
        }

        private void StartColumn(int2 coord)
        {
            var slotIndex = -1;
            for (var i = 0; i < _heightSlots.Length; i++)
            {
                if (!_heightSlots[i].Busy)
                {
                    slotIndex = i;
                    break;
                }
            }

            if (slotIndex < 0)
            {
                return;
            }

            var column = RentColumn(coord, ColumnStage.Heights);
            _columnsInProgress++;
            var slot = _heightSlots[slotIndex];
            slot.Busy = true;
            slot.Column = column;
            column.HeightSlot = slotIndex;
            slot.Handle = new ColumnHeightJob
            {
                Params = _params,
                Column = coord,
                Range = slot.Range,
            }.Schedule();
        }

        private void ScheduleChunk(StreamColumn column, int cy)
        {
            foreach (var slot in _genSlots)
            {
                if (slot.Busy)
                {
                    continue;
                }

                slot.Busy = true;
                slot.Column = column;
                slot.Cy = cy;
                _genBusy++;
                column.MixedInFlight++;
                slot.Handle = new ChunkGenJob
                {
                    Params = _params,
                    Coord = new ChunkCoord(column.Coord.x, cy, column.Coord.y),
                    Scratch = slot.Scratch,
                    Result = slot.Result,
                }.Schedule();
                return;
            }
        }

        // Step 4: dirty chunks of wanted columns, nearest column first, once all eight
        // neighbor columns are loaded (or outside the island).
        private void ScheduleMeshing()
        {
            _pendingReady = false;
            var max = _streamingSettings.MaxMeshJobsInFlight;
            foreach (var coord in _wanted)
            {
                if (
                    !_columns.TryGetValue(coord, out var column)
                    || column.Stage != ColumnStage.Loaded
                    || column.Dirty.Count == 0
                    || !NeighborColumnsLoaded(coord)
                )
                {
                    continue;
                }

                for (var i = column.Dirty.Count - 1; i >= 0; i--)
                {
                    var chunk = column.Dirty[i];
                    if (CannotHaveFaces(chunk))
                    {
                        ClearDirtyAt(column, i);
                        ReleaseView(chunk);
                        continue;
                    }

                    if (_pipeline.Busy >= max || OverBudget())
                    {
                        _pendingReady = true;
                        return;
                    }

                    if (!AllNeighborsPresent(chunk))
                    {
                        _meshedWithMissingNeighbors++;
                        continue;
                    }

                    ClearDirtyAt(column, i);
                    FillNeighborhood(chunk);
                    _pipeline.Schedule(chunk, false, Time.frameCount);
                }
            }
        }

        // Edits near the focus are meshed at once and completed in this LateUpdate.
        private void ScheduleUrgent()
        {
            foreach (var chunk in _urgent)
            {
                if (
                    !chunk.IsDirty
                    || !_chunks.TryGetValue(chunk.Coord, out var current)
                    || current != chunk
                    || !AllNeighborsPresent(chunk)
                )
                {
                    continue;
                }

                var column = chunk.Column;
                column.Dirty.Remove(chunk);
                chunk.IsDirty = false;
                _dirtyCount--;
                if (CannotHaveFaces(chunk))
                {
                    ReleaseView(chunk);
                    continue;
                }

                FillNeighborhood(chunk);
                _pipeline.Schedule(chunk, true, Time.frameCount);
            }

            _urgent.Clear();
            JobHandle.ScheduleBatchedJobs();
        }

        // Colliders exist only on chunks within the collider radius (square) of the focus.
        private void UpdateColliders()
        {
            var radius = _streamingSettings.ColliderRadiusChunks;
            _toBake.Clear();
            if (_focusMoved)
            {
                _scratchChunks.Clear();
                foreach (var chunk in _colliderChunks)
                {
                    if (!InColliderRadius(chunk.Coord, radius))
                    {
                        _scratchChunks.Add(chunk);
                    }
                }

                foreach (var chunk in _scratchChunks)
                {
                    DropCollider(chunk);
                }

                for (var dz = -radius; dz <= radius; dz++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (!_columns.TryGetValue(_focusColumn + new int2(dx, dz), out var column))
                    {
                        continue;
                    }

                    foreach (var chunk in column.Chunks)
                    {
                        if (chunk != null && !chunk.HasCollider && NeedsCollider(chunk))
                        {
                            _toBake.Add(chunk);
                        }
                    }
                }
            }

            foreach (var chunk in _applied)
            {
                _colliderChunks.Remove(chunk);
                if (
                    InColliderRadius(chunk.Coord, radius)
                    && NeedsCollider(chunk)
                    && !_toBake.Contains(chunk)
                )
                {
                    _toBake.Add(chunk);
                }
            }

            if (_toBake.Count == 0)
            {
                return;
            }

            if (_bakeIds.Length < _toBake.Count)
            {
                _bakeIds.Dispose();
                _bakeIds = new NativeArray<EntityId>(_toBake.Count * 2, Allocator.Persistent);
            }

            for (var i = 0; i < _toBake.Count; i++)
            {
                _bakeIds[i] = _toBake[i].View.CollisionMesh.GetEntityId();
            }

            new ColliderBakeJob { MeshIds = _bakeIds.GetSubArray(0, _toBake.Count) }
                .Schedule(_toBake.Count, 1)
                .Complete();
            foreach (var chunk in _toBake)
            {
                chunk.View.Collider.sharedMesh = chunk.View.CollisionMesh;
                chunk.HasCollider = true;
                _colliderChunks.Add(chunk);
            }

            _toBake.Clear();
        }

        private void UpdateSettled()
        {
            var settled =
                !_budgetHit
                && !_pendingReady
                && _columnsInProgress == 0
                && _pipeline.Busy == 0
                && _urgent.Count == 0
                && _wantedLoaded == _wantedInBounds;
            IsSettled = settled;
            if (settled && !_firstSettledRaised)
            {
                _firstSettledRaised = true;
                FirstSettled?.Invoke();
            }
        }

        private static bool NeedsCollider(Chunk chunk) =>
            chunk.View != null && chunk.View.CollisionMesh.vertexCount > 0;

        private bool InColliderRadius(ChunkCoord coord, int radius)
        {
            var d = math.abs(Coords.ColumnOf(coord) - _focusColumn);
            return math.cmax(d) <= radius;
        }

        private void DropCollider(Chunk chunk)
        {
            if (chunk.View != null)
            {
                chunk.View.Collider.sharedMesh = null;
            }

            chunk.HasCollider = false;
            _colliderChunks.Remove(chunk);
        }

        private void ReleaseView(Chunk chunk)
        {
            if (chunk.View == null)
            {
                return;
            }

            DropCollider(chunk);
            _views.Release(chunk.View);
            chunk.View = null;
        }

        // The full-detail square plus the margin column its border chunks mesh against.
        private int LoadedHalfWidth => _streamingSettings.LoadRadiusChunks + 1;

        private bool ShouldUnload(int2 column) =>
            _hasFullCenter
            && _streamer.ShouldUnload(
                column,
                _fullCenter,
                LoadedHalfWidth,
                _streamingSettings.UnloadHysteresisChunks
            );

        private bool ColumnInBounds(int2 column) =>
            _bounds.Contains(new ChunkCoord(column.x, 0, column.y));

        private bool NeighborColumnsLoaded(int2 coord)
        {
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var n = coord + new int2(dx, dz);
                if (!ColumnInBounds(n))
                {
                    continue;
                }

                if (!_columns.TryGetValue(n, out var column) || column.Stage != ColumnStage.Loaded)
                {
                    return false;
                }
            }

            return true;
        }

        private bool AllNeighborsPresent(Chunk chunk)
        {
            for (var dy = -1; dy <= 1; dy++)
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var coord = chunk.Coord.Offset(new int3(dx, dy, dz));
                if (_bounds.Contains(coord) && !_chunks.ContainsKey(coord))
                {
                    return false;
                }
            }

            return true;
        }

        // A uniform chunk has no faces when it is Air, or when each face neighbor is uniform
        // and either opaque or of the same id (face rule: A not Air, B not opaque, A != B).
        private bool CannotHaveFaces(Chunk chunk)
        {
            ref var voxels = ref chunk.Voxels;
            if (!voxels.IsUniform)
            {
                return false;
            }

            var id = voxels.UniformId;
            if (id.IsAir)
            {
                return true;
            }

            foreach (var offset in FaceNeighbors)
            {
                if (!_chunks.TryGetValue(chunk.Coord.Offset(offset), out var neighbor))
                {
                    return false;
                }

                ref var n = ref neighbor.Voxels;
                if (!n.IsUniform || (n.UniformId != id && !_opaque[n.UniformId.Value]))
                {
                    return false;
                }
            }

            return true;
        }

        private void FillNeighborhood(Chunk chunk)
        {
            var neighborhood = _pipeline.Neighborhood;
            for (var dy = -1; dy <= 1; dy++)
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var coord = chunk.Coord.Offset(new int3(dx, dy, dz));
                neighborhood[(dx + 1) + 3 * ((dz + 1) + 3 * (dy + 1))] = _chunks.TryGetValue(
                    coord,
                    out var neighbor
                )
                    ? neighbor.Voxels
                    : _air;
            }
        }

        private StreamColumn RentColumn(int2 coord, ColumnStage stage)
        {
            var column = _columnPool.Count > 0 ? _columnPool.Pop() : new StreamColumn();
            column.Reset(coord, stage);
            _columns.Add(coord, column);
            return column;
        }

        // Frees a generating column that is no longer wanted. Its jobs must be done.
        private void DropColumn(StreamColumn column)
        {
            column.DisposePending();
            _columns.Remove(column.Coord);
            _columnsInProgress--;
            _columnPool.Push(column);
        }

        private void AddChunk(StreamColumn column, ChunkCoord coord, ChunkVoxels voxels)
        {
            var chunk = _chunkPool.Count > 0 ? _chunkPool.Pop() : new Chunk();
            chunk.Reset(coord, voxels, column);
            column.Chunks[coord.Value.y] = chunk;
            _chunks[coord] = chunk;
            _voxelMemory += voxels.MemoryBytes;
            _uniformChunks += voxels.IsUniform ? 1 : 0;
        }

        private void RemoveChunk(Chunk chunk)
        {
            _chunks.Remove(chunk.Coord);
            if (chunk.IsDirty)
            {
                chunk.Column.Dirty.Remove(chunk);
                chunk.IsDirty = false;
                _dirtyCount--;
            }

            if (_views != null)
            {
                ReleaseView(chunk);
            }

            _voxelMemory -= chunk.Voxels.MemoryBytes;
            _uniformChunks -= chunk.Voxels.IsUniform ? 1 : 0;
            if (chunk.Column.Chunks[chunk.Coord.Value.y] == chunk)
            {
                chunk.Column.Chunks[chunk.Coord.Value.y] = null;
            }

            chunk.Dispose();
            chunk.Column = null;
            _chunkPool.Push(chunk);
        }

        private void TrackStorageChange(Chunk chunk, long memoryBefore, bool wasUniform)
        {
            _voxelMemory += chunk.Voxels.MemoryBytes - memoryBefore;
            if (wasUniform && !chunk.Voxels.IsUniform)
            {
                _uniformChunks--;
            }
        }

        // Marks the chunk and every loaded chunk whose padded volume contains a changed
        // voxel: per axis offset 0, plus -1 when the change touches local 0, plus +1 when it
        // touches local 31 (1, 2, 4 or 8 chunks).
        private void MarkDirtyAround(Chunk chunk, int3 localMin, int3 localMax)
        {
            var low = math.select(int3.zero, new int3(-1), localMin == 0);
            var high = math.select(int3.zero, new int3(1), localMax == WorldConstants.ChunkMask);
            for (var dy = low.y; dy <= high.y; dy++)
            for (var dz = low.z; dz <= high.z; dz++)
            for (var dx = low.x; dx <= high.x; dx++)
            {
                if (dx == 0 && dy == 0 && dz == 0)
                {
                    MarkDirty(chunk, true);
                }
                else if (_chunks.TryGetValue(chunk.Coord.Offset(new int3(dx, dy, dz)), out var n))
                {
                    MarkDirty(n, true);
                }
            }
        }

        // Bumps the version so a mesh job in flight for the old voxels is dropped.
        private void MarkDirty(Chunk chunk, bool fromEdit)
        {
            chunk.Version++;
            IsSettled = false;
            if (!chunk.IsDirty)
            {
                chunk.IsDirty = true;
                chunk.Column.Dirty.Add(chunk);
                _dirtyCount++;
            }

            if (
                fromEdit
                && _streaming
                && InColliderRadius(chunk.Coord, _streamingSettings.ColliderRadiusChunks)
                && !_urgent.Contains(chunk)
            )
            {
                _urgent.Add(chunk);
            }
        }

        private void ClearDirtyAt(StreamColumn column, int index)
        {
            var chunk = column.Dirty[index];
            var last = column.Dirty.Count - 1;
            column.Dirty[index] = column.Dirty[last];
            column.Dirty.RemoveAt(last);
            chunk.IsDirty = false;
            _dirtyCount--;
        }

        private void CompleteGenerationJobs()
        {
            if (_heightSlots != null)
            {
                foreach (var slot in _heightSlots)
                {
                    slot.Handle.Complete();
                    slot.Busy = false;
                    slot.Column = null;
                }
            }

            if (_genSlots != null)
            {
                foreach (var slot in _genSlots)
                {
                    slot.Handle.Complete();
                    if (slot.Busy && slot.Result.Value.IsCreated)
                    {
                        slot.Result.Value.Dispose();
                    }

                    slot.Busy = false;
                    slot.Column = null;
                }
            }
        }

        private void DisposeSlots()
        {
            if (_heightSlots != null)
            {
                foreach (var slot in _heightSlots)
                {
                    if (slot.Range.IsCreated)
                    {
                        slot.Range.Dispose();
                    }
                }

                _heightSlots = null;
            }

            if (_genSlots != null)
            {
                foreach (var slot in _genSlots)
                {
                    if (slot.Scratch.IsCreated)
                    {
                        slot.Scratch.Dispose();
                    }

                    if (slot.Result.IsCreated)
                    {
                        slot.Result.Dispose();
                    }
                }

                _genSlots = null;
            }
        }

        private static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }

        private sealed class HeightSlot
        {
            public NativeReference<ColumnRange> Range;
            public JobHandle Handle;
            public StreamColumn Column;
            public bool Busy;
        }

        private sealed class GenSlot
        {
            public NativeArray<ushort> Scratch;
            public NativeReference<ChunkVoxels> Result;
            public JobHandle Handle;
            public StreamColumn Column;
            public int Cy;
            public bool Busy;
        }
    }
}
