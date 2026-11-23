namespace Revoxel.World
{
    // What the meshing jobs need to know about a voxel type, indexed by voxel id.
    public struct VoxelRenderData
    {
        // Solid voxels collide and are in the collision mesh.
        public bool IsSolid;

        // Opaque voxels hide the faces of their neighbors.
        public bool IsOpaque;

        // Liquid voxels go to the water submesh and get faces only toward Air (D-073).
        public bool IsLiquid;
    }
}
