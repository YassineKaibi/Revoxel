using Unity.Mathematics;

namespace Revoxel.Atmosphere
{
    // The blended look and effects of the weather at one moment (D-100).
    public struct WeatherState
    {
        public WeatherKind Kind;
        public WeatherKind Next;
        public float Blend;
        public float CloudCover;
        public float CloudDarkness;
        public float FogDensity;
        public float LightDim;
        public float ShadowStrength;
        public float WindMetersPerSecond;
        public float Precipitation;
        public float TemperatureOffsetC;

        public static WeatherState From(in WeatherProfile p, WeatherKind kind) =>
            new WeatherState
            {
                Kind = kind,
                Next = kind,
                CloudCover = p.CloudCover,
                CloudDarkness = p.CloudDarkness,
                FogDensity = p.FogDensity,
                LightDim = p.LightDim,
                ShadowStrength = p.ShadowStrength,
                WindMetersPerSecond = p.WindMetersPerSecond,
                Precipitation = p.Precipitation,
                TemperatureOffsetC = p.TemperatureOffsetC,
            };

        public static WeatherState Lerp(in WeatherState a, in WeatherState b, float t) =>
            new WeatherState
            {
                Kind = a.Kind,
                Next = b.Kind,
                Blend = t,
                CloudCover = math.lerp(a.CloudCover, b.CloudCover, t),
                CloudDarkness = math.lerp(a.CloudDarkness, b.CloudDarkness, t),
                FogDensity = math.lerp(a.FogDensity, b.FogDensity, t),
                LightDim = math.lerp(a.LightDim, b.LightDim, t),
                ShadowStrength = math.lerp(a.ShadowStrength, b.ShadowStrength, t),
                WindMetersPerSecond = math.lerp(a.WindMetersPerSecond, b.WindMetersPerSecond, t),
                Precipitation = math.lerp(a.Precipitation, b.Precipitation, t),
                TemperatureOffsetC = math.lerp(a.TemperatureOffsetC, b.TemperatureOffsetC, t),
            };
    }

    // The weather of each period from the seed and the season (D-100). Pure functions.
    public static class WeatherSchedule
    {
        public static long Period(double days, float periodHours) =>
            (long)math.floor(days * 24.0 / periodHours);

        // The weather of a period: a hash of the seed and the period against the weights of
        // the season the period starts in.
        public static WeatherKind KindOf(int seed, long period, AtmosphereSettings settings)
        {
            var startDays = period * settings.WeatherPeriodHours / 24.0;
            var season = AtmosphereClock.Season(startDays, settings.DaysPerSeason);
            var weathers = settings.Weathers;
            var total = 0f;
            for (var i = 0; i < weathers.Length; i++)
            {
                total += weathers[i].Weight(season);
            }

            var hash = math.hash(new int3(seed, (int)period, (int)(period >> 32) ^ 0x5EA7));
            var pick = (hash / (float)uint.MaxValue) * total;
            for (var i = 0; i < weathers.Length; i++)
            {
                pick -= weathers[i].Weight(season);
                if (pick < 0f)
                {
                    return (WeatherKind)i;
                }
            }

            return WeatherKind.Clear;
        }

        // The weather now: the period's weather, blending into the next one over the last
        // blend hours of the period.
        public static WeatherState At(int seed, double days, AtmosphereSettings settings)
        {
            var periodHours = settings.WeatherPeriodHours;
            var period = Period(days, periodHours);
            var kind = KindOf(seed, period, settings);
            var next = KindOf(seed, period + 1, settings);
            var hoursIn = (float)(days * 24.0 - period * (double)periodHours);
            var blendStart = periodHours - settings.WeatherBlendHours;
            var t = math.smoothstep(blendStart, periodHours, hoursIn);
            var current = WeatherState.From(settings.Weathers[(int)kind], kind);
            if (t <= 0f || next == kind)
            {
                return current;
            }

            var upcoming = WeatherState.From(settings.Weathers[(int)next], next);
            return WeatherState.Lerp(current, upcoming, t);
        }
    }
}
