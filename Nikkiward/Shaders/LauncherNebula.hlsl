#define D2D_INPUT_COUNT 0
#define D2D_REQUIRES_SCENE_POSITION

#include "d2d1effecthelpers.hlsli"

float2 resolution;
float time;
float seed;
float motion;
float2 pointer;
float3 colorA;
float3 colorB;
float3 colorC;
float3 colorD;

float hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32 + seed);
    return frac(p.x * p.y);
}

float noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + float2(1.0, 0.0));
    float c = hash21(i + float2(0.0, 1.0));
    float d = hash21(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float fbm(float2 p)
{
    float value = 0.0;
    float amplitude = 0.52;
    [unroll]
    for (int i = 0; i < 6; i++)
    {
        value += amplitude * noise(p);
        p = float2(
            0.80 * p.x - 0.60 * p.y,
            0.60 * p.x + 0.80 * p.y) * 2.03 + 17.7;
        amplitude *= 0.5;
    }
    return value;
}

float3 palette(float value)
{
    float t = saturate(value);
    float3 shadow = lerp(colorA, colorB, smoothstep(0.06, 0.62, t));
    float3 body = lerp(colorB, colorC, smoothstep(0.30, 0.82, t));
    float3 highlight = lerp(colorC, colorD, smoothstep(0.74, 1.0, t));
    float3 restrained = lerp(shadow, body, smoothstep(0.26, 0.72, t));
    return lerp(restrained, highlight, smoothstep(0.78, 0.97, t));
}

float3 renderNebula(
    float2 uv,
    float2 p,
    float2 pointerPosition,
    float distanceToPointer,
    float t)
{
    float2 delta = p - pointerPosition;
    float influence = exp(-distanceToPointer * 4.6) * motion;
    float angle = influence * 1.7;
    float cosine = cos(angle);
    float sine = sin(angle);
    float2 swirled = float2(
        cosine * delta.x + sine * delta.y,
        -sine * delta.x + cosine * delta.y);
    p = pointerPosition + swirled;
    p += normalize(delta + 0.0001) * influence * 0.08;

    float2 drift = float2(t * 0.22, -t * 0.13);
    float2 q = float2(
        fbm(p * 1.35 + drift + seed),
        fbm(p * 1.35 + float2(5.2, 1.3) - drift * 0.85));
    float2 r = float2(
        fbm(p * 2.0 + 3.6 * q + float2(1.7, 9.2) + t * 0.10),
        fbm(p * 2.0 + 3.0 * q + float2(8.3, 2.8) - t * 0.085));

    float cloud = fbm(p * 1.7 + 4.2 * r);
    float veins = fbm(p * 4.0 - 2.0 * q + t * 0.065);
    float nebula = smoothstep(0.18, 0.91, cloud * 0.9 + veins * 0.22);

    float3 color = palette(nebula);
    color += colorD * pow(max(cloud - 0.63, 0.0), 2.0) * 1.05;
    color *= 0.78 + 0.34 * smoothstep(0.15, 0.9, veins);

    float2 starGrid = floor((uv + float2(seed * 0.013, 0.0)) * float2(132.0, 58.0));
    float2 starCell = frac(uv * float2(132.0, 58.0)) - 0.5;
    float starRandom = hash21(starGrid);
    float starShape = smoothstep(0.075, 0.0, length(starCell));
    float starMask = step(0.989, starRandom) * starShape;
    float twinkle = 0.35 + 0.65 * sin(t * (1.0 + starRandom * 2.4) + starRandom * 40.0) * 0.5 + 0.5;
    color += starMask * twinkle * lerp(colorC, colorD, starRandom) * 1.05;

    float pointerGlow = exp(-distanceToPointer * 7.0) * motion;
    color += colorD * pointerGlow * 0.28;
    return color;
}

D2D_PS_ENTRY(main)
{
    float2 scenePosition = D2DGetScenePosition().xy;
    float2 uv = float2(
        scenePosition.x / max(resolution.x, 1.0),
        1.0 - scenePosition.y / max(resolution.y, 1.0));
    float2 p = uv - 0.5;
    p.x *= resolution.x / max(resolution.y, 1.0);

    float2 pointerPosition = pointer - 0.5;
    pointerPosition.x *= resolution.x / max(resolution.y, 1.0);
    float distanceToPointer = length(p - pointerPosition);

    float3 color = renderNebula(uv, p, pointerPosition, distanceToPointer, time);
    float vignette = smoothstep(0.94, 0.18, length((uv - 0.5) * float2(1.0, 1.35)));
    color *= 0.70 + vignette * 0.42;
    color = pow(max(color, 0.0), 0.88);
    return float4(saturate(color), 1.0);
}
