using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Revoxel.World
{
    // Render vertex: COLOR is the restored color, TEXCOORD0 the corrupted color (both linear).
    [StructLayout(LayoutKind.Sequential)]
    public struct VoxelVertex
    {
        public float3 Position;
        public float3 Normal;
        public Color32 Restored;
        public Color32 Corrupted;
    }
}
