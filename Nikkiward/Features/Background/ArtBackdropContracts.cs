namespace Nikkiward.Features.Background;

/// <summary>
/// Which theme the artwork itself suggests. Only honoured when the user
/// selected "follow artwork"; never overrides an explicit choice.
/// </summary>
public enum ArtPreferredTheme
{
    Light,
    Dark,
}

public sealed record ArtBackdropDiagnosticState
{
    public bool IsReady { get; init; }

    public bool AccentFromFallback { get; init; }

    public double DominantHueWeight { get; init; }

    public ArtPreferredTheme PreferredTheme { get; init; }
}

public static class ArtThemeAccentSelector
{
    public static uint Select(
        uint derivedAccentLight,
        uint derivedAccentDark,
        ArtPreferredTheme theme) =>
        theme == ArtPreferredTheme.Dark
            ? derivedAccentDark
            : derivedAccentLight;
}

public static class ArtActionFill
{
    public const double MinimumShapeContrast = 1.3;
    public const double MinimumInkContrast = 4.6;

    private const double GateStep = 0.02;
    private const int GateStepCount = 50;

    private static readonly double InkLuminance = ArtPaletteAnalyzer.RelativeLuminance(
        0x2A,
        0x23,
        0x20);

    public static uint ForBackdrops(
        uint accentArgb,
        params double[] backdropLuminances)
    {
        var accent = accentArgb | 0xFF000000;
        var backdrops = NormalizeBackdrops(backdropLuminances);
        var best = accent;
        var bestShapeContrast = Evaluate(best, backdrops, out var bestInkContrast);
        if (bestInkContrast >= MinimumInkContrast &&
            bestShapeContrast >= MinimumShapeContrast)
        {
            return best;
        }

        if (bestInkContrast < MinimumInkContrast)
        {
            bestShapeContrast = -1.0;
        }

        for (var step = 1; step <= GateStepCount; step++)
        {
            var amount = step * GateStep;
            var lighter = Shift(accent, amount);
            var darker = Shift(accent, -amount);

            var lighterShape = Evaluate(lighter, backdrops, out var lighterInk);
            var darkerShape = Evaluate(darker, backdrops, out var darkerInk);
            var lighterPasses = lighterInk >= MinimumInkContrast;
            var darkerPasses = darkerInk >= MinimumInkContrast;

            if (lighterPasses && lighterShape > bestShapeContrast)
            {
                best = lighter;
                bestShapeContrast = lighterShape;
            }

            if (darkerPasses && darkerShape > bestShapeContrast)
            {
                best = darker;
                bestShapeContrast = darkerShape;
            }

            if (lighterPasses && lighterShape >= MinimumShapeContrast &&
                (!darkerPasses || darkerShape < MinimumShapeContrast || lighterShape >= darkerShape))
            {
                return lighter;
            }

            if (darkerPasses && darkerShape >= MinimumShapeContrast)
            {
                return darker;
            }
        }

        return best;
    }

    public static double CompositeWithScrim(
        double backdropLuminance,
        ArtPreferredTheme preferredTheme,
        double opacity)
    {
        var backdrop = EncodeChannel(Math.Clamp(backdropLuminance, 0.0, 1.0));
        var scrim = preferredTheme == ArtPreferredTheme.Light
            ? (R: (byte)0xFD, G: (byte)0xFA, B: (byte)0xF5)
            : (R: (byte)0x24, G: (byte)0x1E, B: (byte)0x1B);
        var alpha = Math.Clamp(opacity, 0.0, 1.0);
        return ArtPaletteAnalyzer.RelativeLuminance(
            Blend(backdrop, scrim.R, alpha),
            Blend(backdrop, scrim.G, alpha),
            Blend(backdrop, scrim.B, alpha));
    }

    private static double Evaluate(
        uint fill,
        IReadOnlyList<double> backdropLuminances,
        out double inkContrast)
    {
        var fillLuminance = LuminanceOf(fill);
        inkContrast = ArtPaletteAnalyzer.ContrastRatio(fillLuminance, InkLuminance);
        var shapeContrast = double.MaxValue;
        foreach (var backdropLuminance in backdropLuminances)
        {
            shapeContrast = Math.Min(
                shapeContrast,
                ArtPaletteAnalyzer.ContrastRatio(fillLuminance, backdropLuminance));
        }

        return shapeContrast;
    }

    private static double[] NormalizeBackdrops(double[]? backdropLuminances)
    {
        if (backdropLuminances is null || backdropLuminances.Length == 0)
        {
            return [0.5];
        }

        return backdropLuminances
            .Select(value => double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.5)
            .Distinct()
            .ToArray();
    }

    private static double LuminanceOf(uint argb) => ArtPaletteAnalyzer.RelativeLuminance(
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    private static uint Shift(uint argb, double amount)
    {
        static byte ShiftChannel(byte channel, double amount) =>
            (byte)Math.Clamp(
                Math.Round(
                    amount >= 0
                        ? channel + ((255 - channel) * amount)
                        : channel * (1 + amount)),
                0,
                255);

        var red = ShiftChannel((byte)((argb >> 16) & 0xFF), amount);
        var green = ShiftChannel((byte)((argb >> 8) & 0xFF), amount);
        var blue = ShiftChannel((byte)(argb & 0xFF), amount);
        return 0xFF000000 |
            ((uint)red << 16) |
            ((uint)green << 8) |
            blue;
    }

    private static byte EncodeChannel(double linear)
    {
        var encoded = linear <= 0.0031308
            ? linear * 12.92
            : (1.055 * Math.Pow(linear, 1.0 / 2.4)) - 0.055;
        return (byte)Math.Clamp(Math.Round(encoded * 255.0), 0, 255);
    }

    private static byte Blend(byte backdrop, byte scrim, double opacity) =>
        (byte)Math.Clamp(
            Math.Round((backdrop * (1.0 - opacity)) + (scrim * opacity)),
            0,
            255);
}

public static class ArtPublicationDispatcher
{
    public static Task EnqueueAsync(
        Func<Action, bool> tryEnqueue,
        Action publish)
    {
        ArgumentNullException.ThrowIfNull(tryEnqueue);
        ArgumentNullException.ThrowIfNull(publish);

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Execute()
        {
            try
            {
                publish();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        try
        {
            if (!tryEnqueue(Execute))
            {
                completion.TrySetException(new InvalidOperationException(
                    "The UI publication queue rejected the backdrop update."));
            }
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }

        return completion.Task;
    }
}

/// <summary>
/// A decoded, straight-alpha BGRA8 image held in managed memory.
/// Deliberately WinRT-free so every analysis stage stays a pure function
/// that can run on a background thread and be unit tested without a UI.
/// </summary>
public sealed class ArtPixelBuffer
{
    public ArtPixelBuffer(byte[] pixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "Art pixel buffer dimensions must be positive.");
        }

        var required = checked(width * height * 4);
        if (pixels.Length < required)
        {
            throw new ArgumentException(
                $"Expected at least {required} bytes for {width}x{height} BGRA8.",
                nameof(pixels));
        }

        Pixels = pixels;
        Width = width;
        Height = height;
    }

    /// <summary>BGRA8, row-major, stride == Width * 4.</summary>
    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    public int Stride => Width * 4;

    /// <summary>
    /// Box-averaging downsample. Used to derive the 16x16 palette sample from
    /// the same decode that feeds the blur bake, so the file is read once.
    /// </summary>
    public ArtPixelBuffer Downsample(int targetWidth, int targetHeight)
    {
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth));
        }

        if (targetWidth == Width && targetHeight == Height)
        {
            return this;
        }

        var destination = new byte[targetWidth * targetHeight * 4];
        for (var dy = 0; dy < targetHeight; dy++)
        {
            var sourceTop = dy * Height / targetHeight;
            var sourceBottom = Math.Max(sourceTop + 1, (dy + 1) * Height / targetHeight);
            for (var dx = 0; dx < targetWidth; dx++)
            {
                var sourceLeft = dx * Width / targetWidth;
                var sourceRight = Math.Max(sourceLeft + 1, (dx + 1) * Width / targetWidth);

                long b = 0, g = 0, r = 0, a = 0;
                var samples = 0;
                for (var sy = sourceTop; sy < sourceBottom; sy++)
                {
                    var rowOffset = sy * Stride;
                    for (var sx = sourceLeft; sx < sourceRight; sx++)
                    {
                        var i = rowOffset + (sx * 4);
                        b += Pixels[i];
                        g += Pixels[i + 1];
                        r += Pixels[i + 2];
                        a += Pixels[i + 3];
                        samples++;
                    }
                }

                var d = ((dy * targetWidth) + dx) * 4;
                destination[d] = (byte)(b / samples);
                destination[d + 1] = (byte)(g / samples);
                destination[d + 2] = (byte)(r / samples);
                destination[d + 3] = (byte)(a / samples);
            }
        }

        return new ArtPixelBuffer(destination, targetWidth, targetHeight);
    }
}

/// <summary>
/// Cached, serialisable result of analysing one artwork file. Persisted to
/// %LOCALAPPDATA%\Nikkiward\PaletteCache\&lt;sha256&gt;.json so the cost is paid
/// once per artwork, never on the first-frame path.
/// </summary>
public sealed class ArtAnalysis
{
    public int SchemaVersion { get; set; } = 3;

    /// <summary>SHA-256 of the source artwork bytes, lowercase hex.</summary>
    public string ArtHash { get; set; } = string.Empty;

    /// <summary>Mean WCAG relative luminance over the whole 16x16 sample, 0..1.</summary>
    public double MeanLuminance { get; set; }

    /// <summary>Mean relative luminance behind the launcher masthead, 0..1.</summary>
    public double MastheadLuminance { get; set; }

    /// <summary>95th-percentile relative luminance behind the launcher masthead, 0..1.</summary>
    public double MastheadP95Luminance { get; set; }

    /// <summary>Mean relative luminance behind the primary action, 0..1.</summary>
    public double CtaLuminance { get; set; }

    /// <summary>95th-percentile relative luminance behind the primary action, 0..1.</summary>
    public double CtaP95Luminance { get; set; }

    public IReadOnlyList<ArtRegionLuminance> Regions { get; set; } =
        Array.Empty<ArtRegionLuminance>();

    public BackgroundSourceKind SourceKind { get; set; } =
        BackgroundSourceKind.StillImage;

    /// <summary>Dominant hue in degrees 0..360, or -1 when no bucket qualified.</summary>
    public double DominantHue { get; set; } = -1;

    /// <summary>Share of sampled pixels backing the dominant hue, 0..1.</summary>
    public double DominantHueWeight { get; set; }

    /// <summary>Packed ARGB accent for the light theme.</summary>
    public uint DerivedAccentLight { get; set; }

    /// <summary>Packed ARGB accent for the dark theme.</summary>
    public uint DerivedAccentDark { get; set; }

    /// <summary>Adaptive text-protection scrim strength, 0.12..0.52.</summary>
    public double ScrimOpacity { get; set; }

    public ArtPreferredTheme PreferredTheme { get; set; } = ArtPreferredTheme.Light;

    /// <summary>True when the derived accent failed contrast and the brand blush was used.</summary>
    public bool AccentFromFallback { get; set; }

    /// <summary>Absolute path of the baked blur copy, when it exists.</summary>
    public string? BlurredArtPath { get; set; }
}

/// <summary>
/// Pure analysis of a small (16x16) artwork sample. No IO, no WinRT, no UI.
/// </summary>
public interface IArtPaletteAnalyzer
{
    /// <summary>
    /// Derives accent, scrim strength and preferred theme from the sample.
    /// <paramref name="sample"/> is expected to be 16x16 but any size is accepted.
    /// </summary>
    ArtAnalysis Analyze(ArtPixelBuffer sample, string artHash);
}

/// <summary>
/// Pure CPU pre-bake of the L1 depth layer. No IO, no WinRT, no UI.
/// </summary>
public interface IArtBlurBaker
{
    /// <summary>
    /// Blurs and desaturates a downsampled artwork copy. The caller has already
    /// scaled the source to roughly <c>BakeWidth</c> across.
    /// </summary>
    ArtPixelBuffer Bake(ArtPixelBuffer downsampled);
}

/// <summary>
/// Read/write cache for <see cref="ArtAnalysis"/> keyed by artwork hash.
/// </summary>
public interface IArtAnalysisCache
{
    string RootPath { get; }

    string BlurCachePath { get; }

    string GetBlurFilePath(string artHash);

    Task<ArtAnalysis?> LoadAsync(string artHash, CancellationToken cancellationToken = default);

    Task SaveAsync(ArtAnalysis analysis, CancellationToken cancellationToken = default);
}
