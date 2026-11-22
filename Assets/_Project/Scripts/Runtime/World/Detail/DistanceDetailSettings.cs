using UnityEngine;

namespace Revoxel.World
{
    // Levels, ring size, job limit and budgets of the distance detail (PLAN-03 Task 4,
    // D-052, D-055). Every value is a placeholder (Q-12).
    [CreateAssetMenu(
        menuName = "Revoxel/World/Distance Detail Settings",
        fileName = "Placeholder_DistanceDetailSettings"
    )]
    public sealed class DistanceDetailSettings : ScriptableObject
    {
        private const string Placeholder = "Placeholder value (Q-12).";

        [SerializeField]
        [Tooltip("Coarse levels beyond full resolution, 1/2 to 1/2^n (D-055). " + Placeholder)]
        private int _levels = 6;

        [SerializeField]
        [Tooltip(
            "Half-width of each coarse level's square, in that level's cells (even). " + Placeholder
        )]
        private int _ringHalfWidthCells = 8;

        [SerializeField]
        [Tooltip(
            "Coarse voxels below the lowest surface still meshed for their side walls. "
                + Placeholder
        )]
        private int _wallDepthVoxels = 8;

        [SerializeField]
        [Tooltip("Height jobs, and chunk fill and mesh jobs, at once. " + Placeholder)]
        private int _maxJobsInFlight = 16;

        [SerializeField]
        [Tooltip(
            "Main-thread milliseconds per frame for the distance detail (D-052). " + Placeholder
        )]
        private float _mainThreadBudgetMs = 1f;

        [SerializeField]
        [Tooltip("Coarse mesh memory the view should stay under, MiB (D-052). " + Placeholder)]
        private int _meshMemoryBudgetMiB = 384;

        public int Levels => _levels;

        public int RingHalfWidthCells => _ringHalfWidthCells;

        public int WallDepthVoxels => _wallDepthVoxels;

        public int MaxJobsInFlight => _maxJobsInFlight;

        public float MainThreadBudgetMs => _mainThreadBudgetMs;

        public int MeshMemoryBudgetMiB => _meshMemoryBudgetMiB;

        public bool TryValidate(out string error)
        {
            if (_levels < 1 || _levels > WorldConstants.MaxDetailLevels)
            {
                error = $"Levels must be 1 to {WorldConstants.MaxDetailLevels}.";
            }
            else if (_ringHalfWidthCells < 4 || (_ringHalfWidthCells & 1) != 0)
            {
                error = "Ring half-width must be even and at least 4.";
            }
            else if (_wallDepthVoxels < 1)
            {
                error = "Wall depth must be at least 1.";
            }
            else if (_maxJobsInFlight < 1)
            {
                error = "Job limit must be at least 1.";
            }
            else if (!(_mainThreadBudgetMs > 0f) || _meshMemoryBudgetMiB < 1)
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

        // With the streaming settings the rings are built from.
        public bool TryValidateWith(WorldStreamingSettings streaming, out string error)
        {
            if (!TryValidate(out error))
            {
                return false;
            }

            if (streaming == null)
            {
                error = "Streaming settings are required.";
                return false;
            }

            return streaming.TryValidate(out error);
        }

        // Half-width of the outermost square, meters, for a full-detail half-width.
        public float ViewDistanceMeters(int fullHalfWidth) =>
            new DetailRings(_levels, fullHalfWidth, _ringHalfWidthCells).ViewDistanceMeters;

        private void OnValidate()
        {
            if (!TryValidate(out var error))
            {
                Debug.LogWarning($"[World] {name}: {error}", this);
            }
        }
    }
}
