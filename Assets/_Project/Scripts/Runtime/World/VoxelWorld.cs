using UnityEngine;

namespace Revoxel.World
{
    // Loaded chunks, edits and streaming (D-030, D-050). Must sit at the world origin:
    // voxel coordinates are world coordinates.
    public sealed class VoxelWorld : MonoBehaviour
    {
        public bool IsLoaded => true;

        // 0 to 1 while loading, for the loading screen.
        public float LoadProgress => 1f;

        public int DirtyCount => 0;

        public int ViewCount => 0;

        private void Awake()
        {
            VoxelShaderGlobals.SetDefaults();
        }
    }
}
