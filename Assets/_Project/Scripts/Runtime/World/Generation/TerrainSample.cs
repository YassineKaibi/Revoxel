namespace Revoxel.World
{
    // What the generator knows about one voxel column before filling it (D-062): the
    // continuous surface height, the climate fields, the column's biome and its water
    // (D-070).
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

        // Water fills the non-solid voxels below this height, voxels (D-070): sea level, or
        // the surface of the lake or river the column belongs to.
        public int WaterLevel;

        public WaterBody Body;

        // How much of a coral head stands on the column, 0 to 1 (D-072), and the reef biome
        // whose corals it carries.
        public float Reef;

        public BiomeId ReefBiome;
    }
}
