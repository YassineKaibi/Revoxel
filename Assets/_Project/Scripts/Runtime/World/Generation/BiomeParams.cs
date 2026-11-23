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

        public static BiomeParams Create(Biome biome)
        {
            const float v = WorldConstants.VoxelsPerMeter;
            var strata = new FixedList32Bytes<ushort>();
            foreach (var type in biome.Strata)
            {
                strata.Add(type.Id.Value);
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
            };
        }
    }
}
