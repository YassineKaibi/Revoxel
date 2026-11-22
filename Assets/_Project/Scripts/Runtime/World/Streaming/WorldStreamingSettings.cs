using UnityEngine;

namespace Revoxel.World
{
    // Streaming radii, job limits and the main-thread budget of the world (PLAN-02 Task 8,
    // D-052). Every value is a placeholder (Q-12).
    [CreateAssetMenu(
        menuName = "Revoxel/World/World Streaming Settings",
        fileName = "Placeholder_WorldStreamingSettings"
    )]
    public sealed class WorldStreamingSettings : ScriptableObject
    {
        private const string Placeholder = "Placeholder value (Q-12).";

        [SerializeField]
        [Tooltip("Chunk columns within this distance of the focus are loaded. " + Placeholder)]
        private int _loadRadiusChunks = 12;

        [SerializeField]
        [Tooltip("Columns unload only beyond the load radius plus this. " + Placeholder)]
        private int _unloadHysteresisChunks = 2;

        [SerializeField]
        [Tooltip(
            "Chunks within this many columns of the focus (square) get colliders. " + Placeholder
        )]
        private int _colliderRadiusChunks = 2;

        [SerializeField]
        [Tooltip("Chunk generation jobs, and columns being generated, at once. " + Placeholder)]
        private int _maxGenerationJobsInFlight = 32;

        [SerializeField]
        [Tooltip("Chunk mesh jobs at once (edits may add a few). " + Placeholder)]
        private int _maxMeshJobsInFlight = 32;

        [SerializeField]
        [Tooltip("Main-thread milliseconds per frame for all world steps (D-052). " + Placeholder)]
        private float _mainThreadBudgetMs = 2f;

        [SerializeField]
        [Tooltip("Voxel storage the loaded world should stay under, MiB (D-052). " + Placeholder)]
        private int _voxelMemoryBudgetMiB = 300;

        public int LoadRadiusChunks => _loadRadiusChunks;

        public int UnloadHysteresisChunks => _unloadHysteresisChunks;

        public int ColliderRadiusChunks => _colliderRadiusChunks;

        public int MaxGenerationJobsInFlight => _maxGenerationJobsInFlight;

        public int MaxMeshJobsInFlight => _maxMeshJobsInFlight;

        public float MainThreadBudgetMs => _mainThreadBudgetMs;

        public int VoxelMemoryBudgetMiB => _voxelMemoryBudgetMiB;

        public bool TryValidate(out string error)
        {
            if (_loadRadiusChunks < 2 || _unloadHysteresisChunks < 0)
            {
                error = "Load radius must be at least 2 and hysteresis not negative.";
            }
            else if (_colliderRadiusChunks < 0 || _colliderRadiusChunks >= _loadRadiusChunks - 1)
            {
                // Colliders need meshed chunks, and the outer ring of columns is never meshed.
                error = "Collider radius must be 0 to load radius - 2.";
            }
            else if (_maxGenerationJobsInFlight < 1 || _maxMeshJobsInFlight < 1)
            {
                error = "Job limits must be at least 1.";
            }
            else if (!(_mainThreadBudgetMs > 0f) || _voxelMemoryBudgetMiB < 1)
            {
                error = "Budgets must be positive.";
            }
            else
            {
                error = string.Empty;
                return true;
            }

            return false;
        }

        private void OnValidate()
        {
            if (!TryValidate(out var error))
            {
                Debug.LogWarning($"[World] {name}: {error}", this);
            }
        }
    }
}
