using Unity.Collections;
using Unity.Mathematics;

namespace Revoxel.World
{
    // One Biome converted for Burst (D-062): climate ranges, shape in voxels, voxel ids.
    public struct BiomeParams
    {
        public float2 TemperatureC;
        public float2 Moisture;
        public float2 Inland;
        public float2 AltitudeMeters;
        public float2 Waste;
        public float Bias;
        public float BaseInfluence;
        public float Offset;
        public float HillAmplitude;
        public float Terrace;
        public ushort Surface;
        public ushort AlternateSurface;
        public ushort Shore;
        public ushort Soil;
        public float AlternateShare;
        public float SoilMin;
        public float SoilMax;
        public float StrataThickness;
        public float PocketChance;
        public FixedList32Bytes<ushort> Strata;

        // Run of river valley walls below their shore (D-076).
        public float RiverWallRun;

        // Coral heads (D-072), voxels; ReefHeight 0 means none.
        public float ReefHeight;
        public float ReefFrequency;
        public float ReefCover;
        public int ReefClearance;
        public float ReefCoralMin;
        public ushort ReefRock;
        public FixedList32Bytes<ushort> ReefCorals;

        public static BiomeParams Create(Biome biome)
        {
            const float v = WorldConstants.VoxelsPerMeter;
            var strata = new FixedList32Bytes<ushort>();
            foreach (var type in biome.Strata)
            {
                strata.Add(type.Id.Value);
            }

            var corals = new FixedList32Bytes<ushort>();
            if (biome.HasReef)
            {
                foreach (var type in biome.ReefCorals)
                {
                    corals.Add(type.Id.Value);
                }
            }

            return new BiomeParams
            {
                TemperatureC = biome.TemperatureC,
                Moisture = biome.Moisture,
                Inland = biome.Inland,
                AltitudeMeters = biome.AltitudeMeters,
                Waste = biome.Waste,
                Bias = biome.Bias,
                BaseInfluence = biome.BaseInfluence,
                Offset = biome.OffsetMeters * v,
                HillAmplitude = biome.HillAmplitudeMeters * v,
                Terrace = biome.Terrace,
                Surface = biome.Surface.Id.Value,
                AlternateSurface =
                    biome.AlternateSurface != null
                        ? biome.AlternateSurface.Id.Value
                        : biome.Surface.Id.Value,
                Shore = biome.Shore.Id.Value,
                Soil = biome.Soil.Id.Value,
                AlternateShare = biome.AlternateShare,
                SoilMin = biome.SoilDepthMeters.x * v,
                SoilMax = biome.SoilDepthMeters.y * v,
                StrataThickness = biome.StrataThicknessMeters * v,
                PocketChance = biome.WastePocketChance,
                Strata = strata,
                RiverWallRun = biome.RiverWallRun,
                ReefHeight = biome.ReefHeightMeters * v,
                ReefFrequency = biome.HasReef ? 1f / (biome.ReefScaleMeters * v) : 0f,
                ReefCover = biome.ReefCover,
                ReefClearance = (int)math.round(biome.ReefClearanceMeters * v),
                ReefCoralMin = biome.ReefCoralMin,
                ReefRock = biome.HasReef ? biome.ReefRock.Id.Value : (ushort)0,
                ReefCorals = corals,
            };
        }
    }
}
