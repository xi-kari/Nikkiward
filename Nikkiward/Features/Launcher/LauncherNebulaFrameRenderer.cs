using System.Numerics;
using Nikkiward.Models;

namespace Nikkiward.Features.Launcher;

internal static class LauncherNebulaFrameRenderer
{
    public static void RenderBgra(
        byte[] destination,
        int width,
        int height,
        float displayAspect,
        LauncherCapsuleStyle style,
        double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (width <= 0 || height <= 0 || destination.Length != width * height * 4)
        {
            throw new ArgumentException("The destination must contain one BGRA pixel per frame position.", nameof(destination));
        }

        var preset = ResolvePreset(style);
        var time = (float)((preset.Seed * 0.73) + (elapsedSeconds * preset.Speed));
        var aspect = float.IsFinite(displayAspect) && displayAspect > 0
            ? displayAspect
            : (float)width / height;

        for (var y = 0; y < height; y++)
        {
            var uvY = 1f - ((y + 0.5f) / height);
            for (var x = 0; x < width; x++)
            {
                var uvX = (x + 0.5f) / width;
                var pX = (uvX - 0.5f) * aspect;
                var pY = uvY - 0.5f;
                var color = RenderNebula(uvX, uvY, pX, pY, time, preset);

                var vignetteDistance = MathF.Sqrt(
                    Square(uvX - 0.5f) +
                    Square((uvY - 0.5f) * 1.35f));
                var vignette = SmoothStep(0.94f, 0.18f, vignetteDistance);
                color *= 0.70f + (vignette * 0.42f);
                color = new Vector3(
                    MathF.Pow(MathF.Max(color.X, 0), 0.88f),
                    MathF.Pow(MathF.Max(color.Y, 0), 0.88f),
                    MathF.Pow(MathF.Max(color.Z, 0), 0.88f));

                var offset = ((y * width) + x) * 4;
                destination[offset] = ToByte(color.Z);
                destination[offset + 1] = ToByte(color.Y);
                destination[offset + 2] = ToByte(color.X);
                destination[offset + 3] = byte.MaxValue;
            }
        }
    }

    private static Vector3 RenderNebula(
        float uvX,
        float uvY,
        float pX,
        float pY,
        float time,
        NebulaPreset preset)
    {
        var driftX = time * 0.22f;
        var driftY = -time * 0.13f;
        var qX = Fbm(
            (pX * 1.35f) + driftX + preset.Seed,
            (pY * 1.35f) + driftY + preset.Seed,
            preset.Seed);
        var qY = Fbm(
            (pX * 1.35f) + 5.2f - (driftX * 0.85f),
            (pY * 1.35f) + 1.3f - (driftY * 0.85f),
            preset.Seed);

        var rX = Fbm(
            (pX * 2f) + (3.6f * qX) + 1.7f + (time * 0.10f),
            (pY * 2f) + (3.6f * qY) + 9.2f + (time * 0.10f),
            preset.Seed);
        var rY = Fbm(
            (pX * 2f) + (3f * qX) + 8.3f - (time * 0.085f),
            (pY * 2f) + (3f * qY) + 2.8f - (time * 0.085f),
            preset.Seed);

        var cloud = Fbm(
            (pX * 1.7f) + (4.2f * rX),
            (pY * 1.7f) + (4.2f * rY),
            preset.Seed);
        var veins = Fbm(
            (pX * 4f) - (2f * qX) + (time * 0.065f),
            (pY * 4f) - (2f * qY) + (time * 0.065f),
            preset.Seed);
        var nebula = SmoothStep(0.18f, 0.91f, (cloud * 0.9f) + (veins * 0.22f));

        var color = Palette(nebula, preset);
        color += preset.ColorD * Square(MathF.Max(cloud - 0.63f, 0)) * 1.05f;
        color *= 0.78f + (0.34f * SmoothStep(0.15f, 0.9f, veins));

        var starGridX = MathF.Floor((uvX + (preset.Seed * 0.013f)) * 132f);
        var starGridY = MathF.Floor(uvY * 58f);
        var starCellX = Fract(uvX * 132f) - 0.5f;
        var starCellY = Fract(uvY * 58f) - 0.5f;
        var starRandom = Hash21(starGridX, starGridY, preset.Seed);
        var starShape = SmoothStep(
            0.075f,
            0,
            MathF.Sqrt(Square(starCellX) + Square(starCellY)));
        var starMask = Step(0.989f, starRandom) * starShape;
        var twinkle = 0.35f +
            (0.65f * MathF.Sin(
                (time * (1f + (starRandom * 2.4f))) +
                (starRandom * 40f)) * 0.5f) +
            0.5f;
        color += Vector3.Lerp(preset.ColorC, preset.ColorD, starRandom) *
            starMask *
            twinkle *
            1.05f;
        return color;
    }

    private static Vector3 Palette(float value, NebulaPreset preset)
    {
        var t = Math.Clamp(value, 0, 1);
        var shadow = Vector3.Lerp(
            preset.ColorA,
            preset.ColorB,
            SmoothStep(0.06f, 0.62f, t));
        var body = Vector3.Lerp(
            preset.ColorB,
            preset.ColorC,
            SmoothStep(0.30f, 0.82f, t));
        var highlight = Vector3.Lerp(
            preset.ColorC,
            preset.ColorD,
            SmoothStep(0.74f, 1f, t));
        var restrained = Vector3.Lerp(
            shadow,
            body,
            SmoothStep(0.26f, 0.72f, t));
        return Vector3.Lerp(
            restrained,
            highlight,
            SmoothStep(0.78f, 0.97f, t));
    }

    private static float Fbm(float x, float y, float seed)
    {
        var value = 0f;
        var amplitude = 0.52f;
        for (var octave = 0; octave < 6; octave++)
        {
            value += amplitude * Noise(x, y, seed);
            var nextX = ((0.80f * x) - (0.60f * y)) * 2.03f + 17.7f;
            var nextY = ((0.60f * x) + (0.80f * y)) * 2.03f + 17.7f;
            x = nextX;
            y = nextY;
            amplitude *= 0.5f;
        }

        return value;
    }

    private static float Noise(float x, float y, float seed)
    {
        var ix = MathF.Floor(x);
        var iy = MathF.Floor(y);
        var fx = Fract(x);
        var fy = Fract(y);
        fx = fx * fx * (3f - (2f * fx));
        fy = fy * fy * (3f - (2f * fy));

        var a = Hash21(ix, iy, seed);
        var b = Hash21(ix + 1f, iy, seed);
        var c = Hash21(ix, iy + 1f, seed);
        var d = Hash21(ix + 1f, iy + 1f, seed);
        return Mix(Mix(a, b, fx), Mix(c, d, fx), fy);
    }

    private static float Hash21(float x, float y, float seed)
    {
        x = Fract(x * 123.34f);
        y = Fract(y * 456.21f);
        var dot = (x * (x + 45.32f + seed)) +
            (y * (y + 45.32f + seed));
        x += dot;
        y += dot;
        return Fract(x * y);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        var t = Math.Clamp((value - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    private static float Mix(float from, float to, float amount) =>
        from + ((to - from) * amount);

    private static float Step(float edge, float value) => value < edge ? 0f : 1f;

    private static float Fract(float value) => value - MathF.Floor(value);

    private static float Square(float value) => value * value;

    private static byte ToByte(float value) =>
        (byte)Math.Clamp(MathF.Round(value * byte.MaxValue), 0, byte.MaxValue);

    internal static NebulaPreset ResolvePreset(LauncherCapsuleStyle style) => style switch
    {
        LauncherCapsuleStyle.Ocean => new(8.2f, 0.48f, "EAF6FF", "8FD0FF", "3B87F6", "6B58E9"),
        LauncherCapsuleStyle.Klein => new(14.1f, 0.49f, "EDF2FF", "2F58D5", "1B2040", "E07A43"),
        LauncherCapsuleStyle.Ultraviolet => new(23.4f, 0.47f, "F2EEFF", "B99AF1", "8F74DB", "D7D85C"),
        LauncherCapsuleStyle.Chrome => new(37.8f, 0.42f, "F5F6F8", "B9C0CC", "7F8793", "4A4F59"),
        LauncherCapsuleStyle.Plus => new(51.3f, 0.50f, "FFF0E6", "F6C26B", "F98A64", "E86D74"),
        _ => new(1.7f, 0.50f, "FFF3EA", "F5B27A", "F67BC6", "A978E8"),
    };

    internal sealed record NebulaPreset
    {
        public NebulaPreset(
            float seed,
            float speed,
            string colorA,
            string colorB,
            string colorC,
            string colorD)
        {
            Seed = seed;
            Speed = speed;
            ColorA = ParseColor(colorA);
            ColorB = ParseColor(colorB);
            ColorC = ParseColor(colorC);
            ColorD = ParseColor(colorD);
        }

        public float Seed { get; }

        public float Speed { get; }

        public Vector3 ColorA { get; }

        public Vector3 ColorB { get; }

        public Vector3 ColorC { get; }

        public Vector3 ColorD { get; }

        private static Vector3 ParseColor(string value) => new(
            Convert.ToByte(value[..2], 16) / 255f,
            Convert.ToByte(value.Substring(2, 2), 16) / 255f,
            Convert.ToByte(value.Substring(4, 2), 16) / 255f);
    }
}
