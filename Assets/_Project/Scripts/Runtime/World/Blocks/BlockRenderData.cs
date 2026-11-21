using UnityEngine;

namespace Revoxel.World
{
    // Per block id data read by the meshing job. Colors are linear: vertex colors are not
    // converted from sRGB in a Linear-space project, so the conversion happens here.
    public struct BlockRenderData
    {
        public bool IsSolid;
        public bool IsOpaque;
        public Color32 Corrupted;
        public Color32 Restored;

        public static BlockRenderData FromSrgb(
            bool isSolid,
            bool isOpaque,
            Color corruptedSrgb,
            Color restoredSrgb
        ) =>
            new BlockRenderData
            {
                IsSolid = isSolid,
                IsOpaque = isOpaque,
                Corrupted = corruptedSrgb.linear,
                Restored = restoredSrgb.linear,
            };
    }
}
