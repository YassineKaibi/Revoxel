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
        [Tooltip(
            "Half-width, in columns, of the full-detail square around the focus (even; one "
                + "margin column is loaded around it, D-057). "
                + Placeholder
        )]
        private int _loadRadiusChunks = 24;

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
            if (
                _loadRadiusChunks < 4
                || (_loadRadiusChunks & 1) != 0
                || _unloadHysteresisChunks < 0
            )
            {
                error = "Load radius must be even and at least 4, hysteresis not negative.";
            }
            else if (
                _colliderRadiusChunks < 0
                || _colliderRadiusChunks >= _loadRadiusChunks - ChunkStreamer.RecenterDistance
            )
            {
                // Colliders need meshed chunks inside the full-detail square, whose center
                // may be RecenterDistance columns from the focus.
                error = "Collider radius must be 0 to load radius - 3.";
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
