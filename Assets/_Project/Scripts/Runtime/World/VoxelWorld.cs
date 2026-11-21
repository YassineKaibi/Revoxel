using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Revoxel.World
{
    // All chunks, keyed by chunk coordinate (D-030). Owns generation at load, block edits,
    // the dirty queue and its per-frame remesh budget. Must sit at the world origin:
    // block coordinates are world coordinates.
    public sealed class VoxelWorld : MonoBehaviour
    {
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
        private BlockRegistry _registry;

        [SerializeField]
        private WorldGenSettings _genSettings;

        [SerializeField]
        private Material _material;

        [SerializeField]
        [Tooltip("Placeholder fixed seed of the demo island (PLAN-02).")]
        private int _seed = 42;

        [SerializeField]
        [Tooltip("Generate and mesh the island in Start.")]
        private bool _loadOnStart = true;

        [SerializeField]
        [Tooltip(
            "Dirty chunks nearest to this transform are remeshed first. Main camera if empty."
        )]
        private Transform _focus;

        private readonly Dictionary<ChunkCoord, Chunk> _chunks =
            new Dictionary<ChunkCoord, Chunk>();
        private readonly List<Chunk> _dirty = new List<Chunk>();
        private readonly List<Chunk> _selection = new List<Chunk>();
        private WorldBounds _bounds;
        private ChunkMeshBatch _batch;
        private NativeArray<byte> _zoneMap;
        private NativeArray<int> _heightMap;
        private JobHandle _generationHandle;
        private bool _isLoading;

        // (world position, old block, new block)
        public event Action<int3, BlockId, BlockId> BlockChanged;

        public event Action Loaded;

        public bool IsInitialized => _bounds != null;

        public bool IsLoaded { get; private set; }

        // 0 to 1 while loading, for the loading screen.
        public float LoadProgress { get; private set; }

        public WorldBounds Bounds => _bounds;

        public BlockRegistry Registry => _registry;

        public WorldSeed Seed => new WorldSeed(_seed);

        public int DirtyCount => _dirty.Count;

        public int ViewCount { get; private set; }

        public IReadOnlyDictionary<ChunkCoord, Chunk> Chunks => _chunks;

        // Zone id per column (Coords.ColumnIndex), valid once loaded.
        public NativeArray<byte> ZoneMap => _zoneMap;

        // First Air block above a Plage column at the shore, valid once loaded.
        public int3 SpawnPoint { get; private set; }

        public double GenerationMilliseconds { get; private set; }

        public double MeshingMilliseconds { get; private set; }

        public double CollidersMilliseconds { get; private set; }

        // Allocates every chunk inside the bounds, all Air and not dirty.
        public void Initialize(BlockRegistry registry, WorldBounds bounds)
        {
            if (IsInitialized)
            {
                ReleaseResources();
            }

            _registry = registry;
            _bounds = bounds;
            for (var y = bounds.Min.Value.y; y < bounds.Max.Value.y; y++)
            for (var z = bounds.Min.Value.z; z < bounds.Max.Value.z; z++)
            for (var x = bounds.Min.Value.x; x < bounds.Max.Value.x; x++)
            {
                var coord = new ChunkCoord(x, y, z);
                _chunks.Add(coord, new Chunk(coord));
            }
        }

        public bool TryGetChunk(ChunkCoord coord, out Chunk chunk) =>
            _chunks.TryGetValue(coord, out chunk);

        // Air when out of bounds or not loaded; never throws.
        public BlockId GetBlock(int3 world)
        {
            Coords.WorldToChunk(world, out var coord, out var local);
            return _chunks.TryGetValue(coord, out var chunk) ? chunk.Get(local) : BlockId.Air;
        }

        public ZoneId GetZone(int x, int z) =>
            _zoneMap.IsCreated && WorldGenerator.IsColumnInWorld(x, z)
                ? (ZoneId)_zoneMap[Coords.ColumnIndex(x, z)]
                : ZoneId.None;

        // False when out of bounds, not loaded, still loading or unchanged; never throws.
        public bool TrySetBlock(int3 world, BlockId id)
        {
            Coords.WorldToChunk(world, out var coord, out var local);
            if (!CanEdit() || !_chunks.TryGetValue(coord, out var chunk))
            {
                return false;
            }

            var old = chunk.Get(local);
            if (old == id)
            {
                return false;
            }

            chunk.Set(local, id);
            MarkDirty(chunk);
            for (var axis = 0; axis < 3; axis++)
            {
                if (local[axis] == 0)
                {
                    MarkDirty(coord.Offset(FaceNeighbors[axis * 2 + 1]));
                }
                else if (local[axis] == WorldConstants.ChunkMask)
                {
                    MarkDirty(coord.Offset(FaceNeighbors[axis * 2]));
                }
            }

            BlockChanged?.Invoke(world, old, id);
            return true;
        }

        // Generates and meshes the whole island over a few frames (PLAN-02 "Initial load").
        public IEnumerator Load()
        {
            if (_registry == null || _genSettings == null || _material == null)
            {
                Debug.LogError(
                    "[World] VoxelWorld needs a block registry, gen settings and a material.",
                    this
                );
                yield break;
            }

            if (!_genSettings.TryValidate(out var error))
            {
                Debug.LogError($"[World] Invalid gen settings: {error}", this);
                yield break;
            }

            Initialize(_registry, WorldBounds.Island());
            _isLoading = true;
            IsLoaded = false;
            LoadProgress = 0f;
            var watch = Stopwatch.StartNew();

            var p = WorldGenParams.Create(Seed, _genSettings);
            _zoneMap = new NativeArray<byte>(WorldConstants.ColumnCount, Allocator.Persistent);
            _heightMap = new NativeArray<int>(WorldConstants.ColumnCount, Allocator.Persistent);
            using (var handles = new NativeArray<JobHandle>(_chunks.Count + 1, Allocator.Temp))
            {
                var h = handles;
                h[0] = WorldGenerator.ScheduleColumns(p, _zoneMap, _heightMap);
                var i = 1;
                foreach (var chunk in _chunks.Values)
                {
                    h[i++] = WorldGenerator.ScheduleChunk(p, chunk.Coord, chunk.Blocks);
                }

                _generationHandle = JobHandle.CombineDependencies(h);
            }

            JobHandle.ScheduleBatchedJobs();
            while (!_generationHandle.IsCompleted)
            {
                yield return null;
            }

            _generationHandle.Complete();
            SpawnPoint = WorldGenerator.FindSpawnPoint(p);
            GenerationMilliseconds = watch.Elapsed.TotalMilliseconds;
            LoadProgress = 0.5f;

            watch.Restart();
            _batch = new ChunkMeshBatch(this, _registry, _material);
            _selection.Clear();
            _selection.AddRange(_chunks.Values);
            _batch.Schedule(_selection);
            _selection.Clear();
            while (!_batch.IsMeshingComplete)
            {
                yield return null;
            }

            _batch.ApplyMeshes();
            MeshingMilliseconds = watch.Elapsed.TotalMilliseconds;
            LoadProgress = 0.75f;

            watch.Restart();
            while (!_batch.IsBakingComplete)
            {
                yield return null;
            }

            _batch.FinishColliders();
            _batch.TrimPads(_genSettings.RemeshBudgetPerFrame);
            CollidersMilliseconds = watch.Elapsed.TotalMilliseconds;
            CountViews();

            _isLoading = false;
            IsLoaded = true;
            LoadProgress = 1f;
#if UNITY_EDITOR || REVOXEL_DEBUG
            Debug.Log(
                $"[World] Island loaded: seed {_seed}, {ViewCount} chunk views. "
                    + $"Generation {GenerationMilliseconds:F0} ms, meshing {MeshingMilliseconds:F0} ms, "
                    + $"colliders {CollidersMilliseconds:F0} ms."
            );
#endif
            Loaded?.Invoke();
        }

        public void ReleaseResources()
        {
            _generationHandle.Complete();
            _batch?.Dispose();
            _batch = null;
            foreach (var chunk in _chunks.Values)
            {
                DestroyChunkObjects(chunk);
                chunk.Dispose();
            }

            if (_zoneMap.IsCreated)
            {
                _zoneMap.Dispose();
            }

            if (_heightMap.IsCreated)
            {
                _heightMap.Dispose();
            }

            _chunks.Clear();
            _dirty.Clear();
            _selection.Clear();
            _bounds = null;
            _isLoading = false;
            IsLoaded = false;
            ViewCount = 0;
        }

        private void Awake()
        {
            VoxelShaderGlobals.SetDefaults();
        }

        private void Start()
        {
            if (_loadOnStart)
            {
                StartCoroutine(Load());
            }
        }

        // Schedules up to the budget of dirty chunks, nearest to the focus first.
        private void Update()
        {
            if (!IsLoaded || _batch.Current != ChunkMeshBatch.Stage.Idle || _dirty.Count == 0)
            {
                return;
            }

            var focus = FocusPosition();
            var budget = math.min(_genSettings.RemeshBudgetPerFrame, _dirty.Count);
            _selection.Clear();
            for (var n = 0; n < budget; n++)
            {
                var best = 0;
                var bestDistance = float.MaxValue;
                for (var i = 0; i < _dirty.Count; i++)
                {
                    var center =
                        (float3)Coords.ChunkOrigin(_dirty[i].Coord)
                        + WorldConstants.ChunkSize * 0.5f;
                    var distance = math.distancesq(center, focus);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }

                _selection.Add(_dirty[best]);
                _dirty[best] = _dirty[_dirty.Count - 1];
                _dirty.RemoveAt(_dirty.Count - 1);
            }

            _batch.Schedule(_selection);
            _selection.Clear();
        }

        // Edits show within the frame: the batch scheduled in Update completes here.
        private void LateUpdate()
        {
            if (!IsLoaded || _batch.Current != ChunkMeshBatch.Stage.Meshing)
            {
                return;
            }

            _batch.ApplyMeshes();
            _batch.FinishColliders();
            CountViews();
        }

        private void OnDestroy()
        {
            ReleaseResources();
        }

        private float3 FocusPosition()
        {
            if (_focus != null)
            {
                return _focus.position;
            }

            var main = Camera.main;
            return main != null ? (float3)main.transform.position : float3.zero;
        }

        private void CountViews()
        {
            var count = 0;
            foreach (var chunk in _chunks.Values)
            {
                if (chunk.View != null)
                {
                    count++;
                }
            }

            ViewCount = count;
        }

        private bool CanEdit() => IsInitialized && !_isLoading;

        private void MarkDirty(ChunkCoord coord)
        {
            if (_chunks.TryGetValue(coord, out var chunk))
            {
                MarkDirty(chunk);
            }
        }

        private void MarkDirty(Chunk chunk)
        {
            if (!chunk.IsDirty)
            {
                chunk.IsDirty = true;
                _dirty.Add(chunk);
            }
        }

        private static void DestroyChunkObjects(Chunk chunk)
        {
            DestroyUnityObject(chunk.View);
            DestroyUnityObject(chunk.RenderMesh);
            DestroyUnityObject(chunk.CollisionMesh);
            chunk.View = null;
            chunk.ViewFilter = null;
            chunk.ViewCollider = null;
            chunk.RenderMesh = null;
            chunk.CollisionMesh = null;
        }

        private static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}
