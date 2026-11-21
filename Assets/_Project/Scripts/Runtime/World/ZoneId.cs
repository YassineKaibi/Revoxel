namespace Revoxel.World
{
    // Zone of a world column (D-012), stored as a byte in VoxelWorld.ZoneMap.
    // None is open sea. The gameplay Zone (gauge, WP-06) is keyed by this id.
    public enum ZoneId : byte
    {
        None = 0,
        Plage = 1,
        Foret = 2,
        Decharge = 3,
    }
}
