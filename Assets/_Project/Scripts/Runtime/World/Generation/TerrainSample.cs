namespace Revoxel.World
{
    // What the generator knows about one voxel column before filling it (D-062): the
    // continuous surface height, the climate fields and the column's biome.
    public struct TerrainSample
    {
        // Continuous surface height, voxels; y below its floor is solid.
        public float Height;

        // Temperature at the surface, °C.
        public float TemperatureC;

        // Temperature at sea level, °C.
        public float SeaTemperatureC;

        // 0 dry to 1 wet.
        public float Moisture;

        // Below 0 at sea, 0 at the coast, about 1 at the island center.
        public float Inland;

        // Height of the island shape and the ridges above sea level, before biome shaping, m.
        public float AltitudeMeters;

        // Dump site field, 0 to 1.
        public float Waste;

        public BiomeId Biome;
    }
}
