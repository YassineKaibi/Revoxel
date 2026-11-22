namespace Revoxel.World
{
    // World-space pattern that makes a voxel type look like one continuous material (D-051).
    public enum SurfacePattern : byte
    {
        Flat = 0,
        Grain = 1,
        Mottle = 2,
        Patches = 3,
    }
}
