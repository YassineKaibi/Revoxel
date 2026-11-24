using UnityEngine;

namespace Revoxel.Atmosphere
{
    // Global inputs of the sky include RevoxelAtmosphere.hlsl (D-099, D-101), read by
    // Revoxel/Sky, Revoxel/Voxel, Revoxel/Water and Revoxel/Precipitation. Colors linear.
    public static class AtmosphereShaderGlobals
    {
        // xyz toward the sun; w its light share of the main light (1 by day, 0 at night).
        public static readonly int SunDirectionId = Shader.PropertyToID("_RevoxelSunDirection");

        // xyz toward the moon; w how much the moon shows (night).
        public static readonly int MoonDirectionId = Shader.PropertyToID("_RevoxelMoonDirection");
        public static readonly int SkyZenithId = Shader.PropertyToID("_RevoxelSkyZenith");
        public static readonly int SkyHorizonId = Shader.PropertyToID("_RevoxelSkyHorizon");

        // rgb the sun's color times its intensity, w the disc brightness.
        public static readonly int SunColorId = Shader.PropertyToID("_RevoxelSunColor");
        public static readonly int MoonColorId = Shader.PropertyToID("_RevoxelMoonColor");

        // (cover, darkness, height m, 1 / scale m).
        public static readonly int CloudsId = Shader.PropertyToID("_RevoxelClouds");

        // (offset x m, offset z m, shadow strength, star brightness).
        public static readonly int CloudOffsetId = Shader.PropertyToID("_RevoxelCloudOffset");
        public static readonly int StarRotationId = Shader.PropertyToID("_RevoxelStarRotation");

        // (spring, autumn, winter, dryness) weights, 0 to 1.
        public static readonly int SeasonId = Shader.PropertyToID("_RevoxelSeason");

        // (wetness, air temperature offset °C, snow start °C, snow full °C).
        public static readonly int WeatherId = Shader.PropertyToID("_RevoxelWeather");
        public static readonly int SpringLeafId = Shader.PropertyToID("_RevoxelSpringLeaf");
        public static readonly int AutumnLeafAId = Shader.PropertyToID("_RevoxelAutumnLeafA");
        public static readonly int AutumnLeafBId = Shader.PropertyToID("_RevoxelAutumnLeafB");
        public static readonly int AutumnLeafCId = Shader.PropertyToID("_RevoxelAutumnLeafC");
        public static readonly int WinterLeafId = Shader.PropertyToID("_RevoxelWinterLeaf");
        public static readonly int DryGrassId = Shader.PropertyToID("_RevoxelDryGrass");
        public static readonly int WinterGrassId = Shader.PropertyToID("_RevoxelWinterGrass");
        public static readonly int SnowColorId = Shader.PropertyToID("_RevoxelSnowColor");

        public static Vector4 Linear(Color c) => (Vector4)c.linear;

        // The season colors, set once.
        public static void SetSeasonColors(AtmosphereSettings s)
        {
            Shader.SetGlobalVector(SpringLeafId, Linear(s.SpringLeafTint));
            Shader.SetGlobalVector(AutumnLeafAId, Linear(s.AutumnLeafColors[0]));
            Shader.SetGlobalVector(AutumnLeafBId, Linear(s.AutumnLeafColors[1]));
            Shader.SetGlobalVector(AutumnLeafCId, Linear(s.AutumnLeafColors[2]));
            Shader.SetGlobalVector(WinterLeafId, Linear(s.WinterLeafColor));
            Shader.SetGlobalVector(DryGrassId, Linear(s.DryGrassColor));
            Shader.SetGlobalVector(WinterGrassId, Linear(s.WinterGrassColor));
            Shader.SetGlobalVector(SnowColorId, Linear(s.SnowColor));
            Shader.SetGlobalVector(MoonColorId, Linear(s.MoonColor));
        }

        // Neutral values while no atmosphere runs: no clouds, no season, dry.
        public static void SetNeutral()
        {
            Shader.SetGlobalVector(CloudsId, new Vector4(0f, 0f, 1800f, 1f / 2000f));
            Shader.SetGlobalVector(CloudOffsetId, Vector4.zero);
            Shader.SetGlobalVector(SeasonId, Vector4.zero);
            Shader.SetGlobalVector(WeatherId, new Vector4(0f, 0f, 1f, -2f));
        }
    }
}
