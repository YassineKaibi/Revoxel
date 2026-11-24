using Unity.Mathematics;
using UnityEngine;

namespace Revoxel.Atmosphere
{
    // World time and what follows from it (D-098): time of day, season, the sun and moon
    // directions and the air temperature offsets. World time is game days since the world
    // began; world x is east, z north, y up. Pure functions, no state.
    public static class AtmosphereClock
    {
        public const int SeasonCount = 4;
        public const int Spring = 0;
        public const int Summer = 1;
        public const int Autumn = 2;
        public const int Winter = 3;

        // The spring equinox lies a quarter into spring, midsummer half into summer.
        private const double EquinoxYearFraction = 0.0625;
        private const double MidsummerYearFraction = 0.375;

        // Warmest hour of the day.
        private const double WarmestDayFraction = 15.0 / 24.0;

        public static double DayFraction(double days) => days - math.floor(days);

        public static int DayIndex(double days) => (int)math.floor(days);

        public static double YearFraction(double days, int daysPerSeason)
        {
            var year = days / (SeasonCount * (double)daysPerSeason);
            return year - math.floor(year);
        }

        public static int Season(double days, int daysPerSeason) =>
            math.min(SeasonCount - 1, (int)(YearFraction(days, daysPerSeason) * SeasonCount));

        // Sun declination, radians.
        public static double Declination(double days, int daysPerSeason, float tiltDegrees) =>
            math.radians(tiltDegrees)
            * math.sin(
                2.0 * math.PI_DBL * (YearFraction(days, daysPerSeason) - EquinoxYearFraction)
            );

        // Unit vector toward the sun.
        public static float3 SunDirection(
            double days,
            int daysPerSeason,
            float latitudeDegrees,
            float tiltDegrees
        )
        {
            var hour = 2.0 * math.PI_DBL * (DayFraction(days) - 0.5);
            var declination = Declination(days, daysPerSeason, tiltDegrees);
            var latitude = math.radians((double)latitudeDegrees);
            var east = -math.cos(declination) * math.sin(hour);
            var north =
                math.sin(declination) * math.cos(latitude)
                - math.cos(declination) * math.cos(hour) * math.sin(latitude);
            var up =
                math.sin(latitude) * math.sin(declination)
                + math.cos(latitude) * math.cos(declination) * math.cos(hour);
            return math.normalize(new float3((float)east, (float)up, (float)north));
        }

        // The moon stands opposite the sun.
        public static float3 MoonDirection(float3 sun) => -sun;

        public static float ElevationDegrees(float3 direction) =>
            math.degrees(math.asin(math.clamp(direction.y, -1f, 1f)));

        // Season offset of the air temperature, °C: winterC at midwinter, summerC at
        // midsummer, a cosine between.
        public static float SeasonOffsetC(
            double days,
            int daysPerSeason,
            float summerC,
            float winterC
        )
        {
            var phase =
                2.0 * math.PI_DBL * (YearFraction(days, daysPerSeason) - MidsummerYearFraction);
            return (float)(0.5 * (summerC + winterC) + 0.5 * (summerC - winterC) * math.cos(phase));
        }

        // Day offset of the air temperature, °C: +swing at 15:00, -swing at 03:00.
        public static float DayOffsetC(double days, float swingC) =>
            (float)(
                swingC * math.cos(2.0 * math.PI_DBL * (DayFraction(days) - WarmestDayFraction))
            );

        // How far into each season the year is: weights of spring freshness, autumn color,
        // winter and summer dryness, 0 to 1, continuous over the year (D-101).
        public static Vector4 SeasonWeights(double days, int daysPerSeason)
        {
            var s = (float)(YearFraction(days, daysPerSeason) * SeasonCount);
            var spring = Bump(s, 0.5f) + Bump(s, 4.5f);
            var autumn = Bump(s, 2.5f);
            var winter = Bump(s, 3.5f) + Bump(s, -0.5f);
            var dry = math.saturate(Bump(s, 1.8f) * 0.8f + Bump(s, 2.4f) * 0.6f);
            return new Vector4(spring, autumn, winter, dry);
        }

        // 1 at the center, falling smoothly to 0 one season away.
        private static float Bump(float s, float center)
        {
            var d = math.saturate(1f - math.abs(s - center));
            return d * d * (3f - 2f * d);
        }
    }
}
