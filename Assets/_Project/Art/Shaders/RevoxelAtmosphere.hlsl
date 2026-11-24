// Revoxel sky functions (D-099, D-101), shared by Revoxel/Sky, Revoxel/Voxel and
// Revoxel/Water. Globals from AtmosphereShaderGlobals (Revoxel.Atmosphere); colors linear.
// While no atmosphere runs the globals are neutral: no clouds, no season, dry.
#ifndef REVOXEL_ATMOSPHERE_INCLUDED
#define REVOXEL_ATMOSPHERE_INCLUDED

float4 _RevoxelSunDirection;
float4 _RevoxelMoonDirection;
float4 _RevoxelSkyZenith;
float4 _RevoxelSkyHorizon;
float4 _RevoxelSunColor;
float4 _RevoxelMoonColor;
float4 _RevoxelClouds;
float4 _RevoxelCloudOffset;
float4x4 _RevoxelStarRotation;

float RevoxelHash21(float2 p)
{
    p = frac(p * float2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return frac((p.x + p.y) * p.x);
}

float RevoxelHash31(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.zyx + 31.32);
    return frac((p.x + p.y) * p.z);
}

float RevoxelNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 w = f * f * (3.0 - 2.0 * f);
    float a = RevoxelHash21(i);
    float b = RevoxelHash21(i + float2(1, 0));
    float c = RevoxelHash21(i + float2(0, 1));
    float d = RevoxelHash21(i + float2(1, 1));
    return lerp(lerp(a, b, w.x), lerp(c, d, w.x), w.y);
}

// Cloud density of the layer above a point (world x, z in meters), 0 to 1.
float RevoxelCloudDensity(float2 xz)
{
    float cover = _RevoxelClouds.x;
    if (cover <= 0.001)
    {
        return 0.0;
    }

    float2 p = (xz + _RevoxelCloudOffset.xy) * _RevoxelClouds.w;
    float n = 0.62 * RevoxelNoise2(p) + 0.28 * RevoxelNoise2(p * 2.7 + 5.3) + 0.10 * RevoxelNoise2(p * 7.3 + 1.7);
    return smoothstep(1.0 - cover - 0.05, 1.0 - cover + 0.3, n);
}

// How much of the main light reaches a point under the clouds, 0 to 1.
half RevoxelCloudShadow(float3 positionWS, float3 lightDirection)
{
    float height = _RevoxelClouds.z - positionWS.y;
    float2 xz = positionWS.xz + lightDirection.xz * (height / max(lightDirection.y, 0.08));
    return 1.0 - RevoxelCloudDensity(xz) * _RevoxelCloudOffset.z;
}

// Sky radiance in a direction: horizon to zenith, the glow around the sun, the cloud
// layer, and with discs the sun, the moon and the stars.
half3 RevoxelSkyRadiance(float3 dir, float3 cameraPosition, bool discs)
{
    float up = dir.y;
    float3 horizon = _RevoxelSkyHorizon.rgb;
    float3 zenith = _RevoxelSkyZenith.rgb;
    float3 sky = lerp(horizon, zenith, pow(saturate(up), 0.45));
    sky = up < 0.0 ? lerp(horizon, horizon * 0.35, saturate(-up * 6.0)) : sky;

    float3 sunDir = _RevoxelSunDirection.xyz;
    float mu = dot(dir, sunDir);
    float low = 1.0 - saturate(abs(up));
    float3 glow = _RevoxelSunColor.rgb * (0.08 * pow(saturate(mu), 6.0) * (0.4 + 0.6 * low) + 0.25 * pow(saturate(mu), 64.0));
    sky += glow * (1.0 - _RevoxelClouds.x * 0.7);

    if (discs)
    {
        float sun = smoothstep(0.99985, 0.99993, mu);
        sky += _RevoxelSunColor.rgb * sun * 40.0 * _RevoxelSunColor.w * step(-0.02, sunDir.y);

        float moonMu = dot(dir, _RevoxelMoonDirection.xyz);
        float moon = smoothstep(0.99982, 0.99990, moonMu);
        sky += _RevoxelMoonColor.rgb * moon * 2.0 * _RevoxelMoonDirection.w * (1.0 - _RevoxelClouds.x * 0.9);

        float starBrightness = _RevoxelCloudOffset.w;
        if (starBrightness > 0.001 && up > 0.0)
        {
            float3 s = mul((float3x3)_RevoxelStarRotation, dir) * 220.0;
            float3 cell = floor(s);
            float h = RevoxelHash31(cell);
            float3 center = cell + 0.5 + (float3(RevoxelHash31(cell + 7.1), RevoxelHash31(cell + 3.3), RevoxelHash31(cell + 5.9)) - 0.5) * 0.6;
            float star = step(0.9965, h) * smoothstep(0.35, 0.0, length(s - center));
            sky += star * starBrightness * (0.4 + 0.6 * frac(h * 97.0)) * saturate(up * 8.0);
        }
    }

    // The cloud layer, lit by the sun and the sky, fading toward the horizon.
    if (up > 0.005 && _RevoxelClouds.x > 0.001)
    {
        float t = (_RevoxelClouds.z - cameraPosition.y) / up;
        if (t > 0.0)
        {
            float2 xz = cameraPosition.xz + dir.xz * t;
            float density = RevoxelCloudDensity(xz) * saturate(1.0 - t / 40000.0);
            float3 lit = (zenith * 0.6 + horizon * 0.6) + _RevoxelSunColor.rgb * (0.35 + 0.4 * pow(saturate(mu), 4.0));
            lit *= 1.0 - 0.75 * _RevoxelClouds.y;
            sky = lerp(sky, lit, density * 0.95);
        }
    }

    return sky;
}

#endif
