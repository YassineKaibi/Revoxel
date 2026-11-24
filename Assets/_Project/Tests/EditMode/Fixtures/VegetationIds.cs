using System.Collections.Generic;
using Revoxel.World;

namespace Revoxel.Tests.EditMode
{
    // Every voxel id trees and ground plants write (PLAN-06): terrain tests that compare
    // against the bare ground skip them.
    public static class VegetationIds
    {
        public static HashSet<ushort> Of(in TerrainParams p)
        {
            var ids = new HashSet<ushort>();
            var v = p.Vegetation;
            for (var i = 0; i < v.Species.Length; i++)
            {
                ids.Add(v.Species[i].Bark);
                ids.Add(v.Species[i].Wood);
                ids.Add(v.Species[i].Leaves);
            }

            for (var i = 0; i < v.Plants.Length; i++)
            {
                ids.Add(v.Plants[i].Stem);
                ids.Add(v.Plants[i].Head);
            }

            ids.Remove(VoxelId.Air.Value);
            return ids;
        }
    }
}
