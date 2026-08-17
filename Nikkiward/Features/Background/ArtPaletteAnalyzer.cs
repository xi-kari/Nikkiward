namespace Nikkiward.Features.Background;

/// <summary>
/// Derives the adaptive accent, scrim strength and preferred theme from a small
/// artwork sample. Pure math: no IO, no WinRT, no UI, callable from any thread.
/// </summary>
public sealed class ArtPaletteAnalyzer : IArtPaletteAnalyzer
{
    /// <summary>Brand AccentBlush, used whenever a derived accent fails the contrast gate.</summary>
    public const uint FallbackAccentArgb = 0xFFE8A0B4;

    private const int HueBucketCount = 12;
    private const double HueBucketWidth = 360.0 / HueBucketCount;
    private const double MinimumVotingSaturation = 0.25;

    // The hue vote is decided on relative bucket count, which has no notion of how
    // much of the artwork actually backs the winner. Without a floor a saturated
    // detail covering a few percent of a mostly desaturated photograph decides the
    // product's identity colour. Measured against the sample denominator (all
    // pixels, not just voting ones), so 0.06 means "at least 6% of the artwork".
    private const double MinimumDominantHueWeight = 0.06;
    private const double AccentSaturation = 0.45;
    private const double AccentLightnessLight = 0.62;
    private const double AccentLightnessDark = 0.68;
    private const double MinimumAccentContrast = 3.0;

    // Measured worst case across all 360 hues is 1.53:1 in light and 5.15:1 in
    // dark, so 1.3 rejects a fill that vanishes into paper without discarding any
    // hue the artwork can actually produce.
    private const double MinimumSurfaceSeparation = 1.3;
    private const double MinimumArtworkSeparation = 2.0;
    private const double ScrimBase = 0.18;
    private const double ScrimPivot = 0.35;
    private const double ScrimSlope = 0.55;
    private const double ScrimMinimum = 0.12;
    private const double ScrimMaximum = 0.52;
    private const double LightThemeThreshold = 0.55;
    private const double DegreesPerRadian = 180.0 / Math.PI;

    // WCAG AA for body text (4.5) plus a margin for the compositor rounding each
    // blended channel to 8 bits: the solve below is continuous, and without the
    // margin a quantised composite lands just under AA around grey 0xBA.
    // On-art chrome includes body-sized labels, so the large-text 3.0 allowance
    // is not enough.
    private const double TargetOnArtContrast = 4.6;
    private const int ScrimSolveIterations = 20;

    // OnArtPrimaryTextBrush and OnArtScrimBrush per polarity. These four values
    // MUST track Themes\OnArt.xaml: the scrim strength solved below is only
    // correct for the ink and wash actually painted.
    private static readonly (byte R, byte G, byte B) InkLightPolarity = (0x24, 0x1E, 0x1B);
    private static readonly (byte R, byte G, byte B) InkDarkPolarity = (0xF7, 0xF2, 0xEA);
    private static readonly (byte R, byte G, byte B) ScrimLightPolarity = (0xFD, 0xFA, 0xF5);
    private static readonly (byte R, byte G, byte B) ScrimDarkPolarity = (0x24, 0x1E, 0x1B);

    // A derived accent is a fill, and the mark on that fill is InkOnAccentBrush,
    // which is the same warm dark ink in both themes. So the accent is gated
    // against that ink, not against InkPrimary: InkPrimary is body text on paper
    // and never lands on an accent. Gating against InkPrimary is unsatisfiable in
    // dark, where it is near white and every accent at L 0.68 fails, which
    // silently forced every artwork onto the fallback blush.
    private static readonly double InkOnAccentLuminance = RelativeLuminance(0x2A, 0x23, 0x20);

    // PaperBase per theme. An accent also has to stay distinguishable from the
    // surface it sits on, or the control loses its shape.
    private static readonly double PaperBaseLightLuminance = RelativeLuminance(0xF6, 0xF1, 0xEA);
    private static readonly double PaperBaseDarkLuminance = RelativeLuminance(0x24, 0x1E, 0x1B);

    /// <inheritdoc />
    public ArtAnalysis Analyze(ArtPixelBuffer sample, string artHash)
    {
        ArgumentNullException.ThrowIfNull(sample);

        var width = sample.Width;
        var height = sample.Height;
        var stride = sample.Stride;
        var pixels = sample.Pixels;
        var totalPixels = width * height;

        var luminanceSum = 0.0;
        var allLuminances = new List<double>(totalPixels);
        var mastheadLuminances = new List<double>();
        var noticeLuminances = new List<double>();
        var ctaLuminances = new List<double>();
        var pillLuminances = new List<double>();

        var bucketVotes = new int[HueBucketCount];
        var bucketSin = new double[HueBucketCount];
        var bucketCos = new double[HueBucketCount];

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < width; x++)
            {
                var i = rowOffset + (x * 4);
                var b = pixels[i];
                var g = pixels[i + 1];
                var r = pixels[i + 2];

                var luminance = RelativeLuminance(r, g, b);
                luminanceSum += luminance;
                allLuminances.Add(luminance);
                var normalizedX = (x + 0.5) / width;
                var normalizedY = (y + 0.5) / height;
                if (normalizedX >= 0.02 && normalizedX <= 0.42 &&
                    normalizedY >= 0.10 && normalizedY <= 0.32)
                {
                    mastheadLuminances.Add(luminance);
                }

                if (normalizedX >= 0.58 && normalizedX <= 0.98 &&
                    normalizedY >= 0.72 && normalizedY <= 0.90)
                {
                    ctaLuminances.Add(luminance);
                }

                if (normalizedX >= 0.02 && normalizedX <= 0.42 &&
                    normalizedY >= 0.62 && normalizedY <= 0.92)
                {
                    noticeLuminances.Add(luminance);
                }

                if (normalizedX >= 0.62 && normalizedX <= 0.98 &&
                    normalizedY >= 0.02 && normalizedY <= 0.14)
                {
                    pillLuminances.Add(luminance);
                }

                var (hue, saturation) = ToHueSaturation(r, g, b);
                if (saturation <= MinimumVotingSaturation)
                {
                    continue;
                }

                var bucket = (int)(hue / HueBucketWidth);
                if (bucket >= HueBucketCount)
                {
                    bucket = HueBucketCount - 1;
                }

                var radians = hue / DegreesPerRadian;
                bucketVotes[bucket]++;
                bucketSin[bucket] += Math.Sin(radians) * saturation;
                bucketCos[bucket] += Math.Cos(radians) * saturation;
            }
        }

        var meanLuminance = luminanceSum / totalPixels;
        var mastheadLuminance = MeanOrFallback(mastheadLuminances, meanLuminance);
        var mastheadP95Luminance = Percentile95OrFallback(
            mastheadLuminances,
            meanLuminance);
        var ctaLuminance = MeanOrFallback(ctaLuminances, meanLuminance);
        var ctaP95Luminance = Percentile95OrFallback(ctaLuminances, meanLuminance);
        var globalP95Luminance = Percentile95OrFallback(allLuminances, meanLuminance);
        var noticeLuminance = MeanOrFallback(noticeLuminances, meanLuminance);
        var noticeP95Luminance = Percentile95OrFallback(noticeLuminances, meanLuminance);
        var pillLuminance = MeanOrFallback(pillLuminances, meanLuminance);
        var pillP95Luminance = Percentile95OrFallback(pillLuminances, meanLuminance);

        var winner = -1;
        for (var bucket = 0; bucket < HueBucketCount; bucket++)
        {
            if (bucketVotes[bucket] > 0 && (winner < 0 || bucketVotes[bucket] > bucketVotes[winner]))
            {
                winner = bucket;
            }
        }

        var dominantHue = -1.0;
        var dominantHueWeight = 0.0;
        if (winner >= 0)
        {
            dominantHue = CircularMeanHue(
                bucketSin[winner],
                bucketCos[winner],
                (winner + 0.5) * HueBucketWidth);
            dominantHueWeight = (double)bucketVotes[winner] / totalPixels;
        }

        var preferredTheme = PreferredThemeForLuminance(meanLuminance);

        var accentLight = HslToArgb(dominantHue, AccentSaturation, AccentLightnessLight);
        var accentDark = HslToArgb(dominantHue, AccentSaturation, AccentLightnessDark);

        // Both themes fall back together: a half-derived pair would make them
        // disagree about the product's identity colour.
        var accentFromFallback = dominantHue < 0.0
            || dominantHueWeight < MinimumDominantHueWeight
            || !IsAccentUsable(accentLight, PaperBaseLightLuminance, ctaP95Luminance)
            || !IsAccentUsable(accentDark, PaperBaseDarkLuminance, ctaP95Luminance);
        if (accentFromFallback)
        {
            accentLight = FallbackAccentArgb;
            accentDark = FallbackAccentArgb;
        }

        return new ArtAnalysis
        {
            SchemaVersion = 3,
            ArtHash = artHash,
            MeanLuminance = meanLuminance,
            MastheadLuminance = mastheadLuminance,
            MastheadP95Luminance = mastheadP95Luminance,
            CtaLuminance = ctaLuminance,
            CtaP95Luminance = ctaP95Luminance,
            Regions =
            [
                new("global", meanLuminance, globalP95Luminance),
                new("masthead", mastheadLuminance, mastheadP95Luminance),
                new("notice", noticeLuminance, noticeP95Luminance),
                new("cta", ctaLuminance, ctaP95Luminance),
                new("pill", pillLuminance, pillP95Luminance),
            ],
            DominantHue = dominantHue,
            DominantHueWeight = dominantHueWeight,
            DerivedAccentLight = accentLight,
            DerivedAccentDark = accentDark,
            ScrimOpacity = SolveScrimOpacity(meanLuminance, preferredTheme),
            PreferredTheme = preferredTheme,
            AccentFromFallback = accentFromFallback,
            BlurredArtPath = null,
        };
    }

    /// <summary>
    /// Scrim strength for <paramref name="meanLuminance"/> under
    /// <paramref name="preferredTheme"/>, as an alpha inside the clamp range.
    /// </summary>
    /// <remarks>
    /// The luminance ramp alone leaves a readability hole: polarity flips at
    /// <see cref="LightThemeThreshold"/>, so artwork just below it takes off-white
    /// ink over a near-black wash the ramp has barely opened, and a plain 50% grey
    /// wallpaper reaches only ~4.2:1. So the ramp is treated as a floor for the
    /// designed look and raised to whatever alpha the ink actually needs to hold
    /// <see cref="TargetOnArtContrast"/>. Never lowered: dropping below the ramp
    /// would thin the separation the shell was tuned against.
    ///
    /// The artwork is modelled as a flat grey of the sampled mean luminance,
    /// because the mean is the only luminance statistic carried this far. A
    /// wallpaper whose mean hides a bright patch under the text can still fall
    /// short locally; per-region sampling is the fix for that, not a heavier
    /// global wash.
    /// </remarks>
    public static double SolveScrimOpacity(double meanLuminance, ArtPreferredTheme preferredTheme)
    {
        var ramp = Math.Clamp(
            ScrimBase + ((meanLuminance - ScrimPivot) * ScrimSlope),
            ScrimMinimum,
            ScrimMaximum);

        var light = preferredTheme == ArtPreferredTheme.Light;
        var ink = light ? InkLightPolarity : InkDarkPolarity;
        var scrim = light ? ScrimLightPolarity : ScrimDarkPolarity;
        var inkLuminance = RelativeLuminance(ink.R, ink.G, ink.B);
        var art = EncodeChannel(meanLuminance);

        if (MeetsTarget(ramp))
        {
            return ramp;
        }

        // Composite luminance is monotonic in alpha for either polarity, so a
        // fixed-iteration bisection converges without needing a closed form for
        // the nonlinear sRGB transfer.
        var low = ramp;
        var high = ScrimMaximum;
        if (!MeetsTarget(high))
        {
            return high;
        }

        for (var i = 0; i < ScrimSolveIterations; i++)
        {
            var mid = (low + high) / 2.0;
            if (MeetsTarget(mid))
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        return high;

        bool MeetsTarget(double alpha) =>
            ContrastRatio(inkLuminance, CompositeLuminance(art, scrim, alpha))
                >= TargetOnArtContrast;
    }

    public static ArtPreferredTheme PreferredThemeForLuminance(double luminance) =>
        Math.Clamp(luminance, 0.0, 1.0) > LightThemeThreshold
            ? ArtPreferredTheme.Light
            : ArtPreferredTheme.Dark;

    /// <summary>WCAG relative luminance of an sRGB triple, 0..1.</summary>
    public static double RelativeLuminance(byte r, byte g, byte b) =>
        (0.2126 * Linearise(r)) + (0.7152 * Linearise(g)) + (0.0722 * Linearise(b));

    /// <summary>
    /// WCAG contrast ratio between two relative luminances, 1..21. Order-independent.
    /// </summary>
    public static double ContrastRatio(double luminanceA, double luminanceB) =>
        (Math.Max(luminanceA, luminanceB) + 0.05) / (Math.Min(luminanceA, luminanceB) + 0.05);

    /// <summary>Packs channels into a 0xAARRGGBB value.</summary>
    public static uint PackArgb(byte a, byte r, byte g, byte b) =>
        ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    private static double MeanOrFallback(IReadOnlyCollection<double> values, double fallback) =>
        values.Count == 0 ? fallback : values.Average();

    private static double Percentile95OrFallback(List<double> values, double fallback)
    {
        if (values.Count == 0)
        {
            return fallback;
        }

        values.Sort();
        var index = Math.Clamp((int)Math.Ceiling(values.Count * 0.95) - 1, 0, values.Count - 1);
        return values[index];
    }

    /// <summary>
    /// Standard HSL to opaque packed ARGB. <paramref name="hueDegrees"/> wraps, so a
    /// negative or out-of-range hue is normalised rather than rejected.
    /// </summary>
    public static uint HslToArgb(double hueDegrees, double saturation, double lightness)
    {
        var s = Math.Clamp(saturation, 0.0, 1.0);
        var l = Math.Clamp(lightness, 0.0, 1.0);
        if (s <= 0.0)
        {
            var grey = ToChannel(l);
            return PackArgb(0xFF, grey, grey, grey);
        }

        var h = NormaliseHue(hueDegrees) / 360.0;
        var q = l < 0.5 ? l * (1.0 + s) : l + s - (l * s);
        var p = (2.0 * l) - q;

        return PackArgb(
            0xFF,
            ToChannel(HueToChannel(p, q, h + (1.0 / 3.0))),
            ToChannel(HueToChannel(p, q, h)),
            ToChannel(HueToChannel(p, q, h - (1.0 / 3.0))));
    }

    private static double Linearise(byte channel) => Linearise(channel / 255.0);

    private static double Linearise(double channel)
    {
        var c = Math.Clamp(channel, 0.0, 1.0);
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Inverse of <see cref="Linearise(double)"/>: the sRGB-encoded 0..1 channel
    /// whose relative luminance contribution is <paramref name="linear"/>.
    /// </summary>
    private static double EncodeChannel(double linear)
    {
        var l = Math.Clamp(linear, 0.0, 1.0);
        return l <= 0.03928 / 12.92
            ? l * 12.92
            : (1.055 * Math.Pow(l, 1.0 / 2.4)) - 0.055;
    }

    /// <summary>
    /// Relative luminance of a flat grey artwork channel with a scrim composited
    /// over it at <paramref name="alpha"/>. Source-over in encoded space, which is
    /// what the compositor does for a non-linear-blended XAML layer.
    /// </summary>
    private static double CompositeLuminance(
        double artChannel,
        (byte R, byte G, byte B) scrim,
        double alpha)
    {
        var a = Math.Clamp(alpha, 0.0, 1.0);
        var keep = 1.0 - a;
        var r = (artChannel * keep) + ((scrim.R / 255.0) * a);
        var g = (artChannel * keep) + ((scrim.G / 255.0) * a);
        var b = (artChannel * keep) + ((scrim.B / 255.0) * a);
        return (0.2126 * Linearise(r)) + (0.7152 * Linearise(g)) + (0.0722 * Linearise(b));
    }

    /// <summary>
    /// An accent is usable when its own label stays readable on it and it stays
    /// visible against the surface under it. The ink threshold is the WCAG large
    /// text ratio; the surface threshold is only a shape-visibility floor, since
    /// a fill next to a fill is not a reading task.
    /// </summary>
    private static bool IsAccentUsable(
        uint accentArgb,
        double paperLuminance,
        double artworkLuminance)
    {
        var accentLuminance = RelativeLuminance(
            (byte)((accentArgb >> 16) & 0xFF),
            (byte)((accentArgb >> 8) & 0xFF),
            (byte)(accentArgb & 0xFF));
        return ContrastRatio(accentLuminance, InkOnAccentLuminance) >= MinimumAccentContrast
            && ContrastRatio(accentLuminance, paperLuminance) >= MinimumSurfaceSeparation
            && ContrastRatio(accentLuminance, artworkLuminance) >= MinimumArtworkSeparation;
    }

    /// <summary>
    /// Hue and HSL saturation of an sRGB triple. Achromatic input reports saturation 0
    /// and hue 0, which the caller treats as a non-vote.
    /// </summary>
    private static (double Hue, double Saturation) ToHueSaturation(byte r, byte g, byte b)
    {
        var red = r / 255.0;
        var green = g / 255.0;
        var blue = b / 255.0;

        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        if (delta <= 0.0)
        {
            return (0.0, 0.0);
        }

        // delta > 0 rules out both zero denominators (sum == 0 and sum == 2).
        var sum = max + min;
        var saturation = sum > 1.0 ? delta / (2.0 - sum) : delta / sum;

        double hue;
        if (max == red)
        {
            hue = 60.0 * ((green - blue) / delta);
        }
        else if (max == green)
        {
            hue = 60.0 * (((blue - red) / delta) + 2.0);
        }
        else
        {
            hue = 60.0 * (((red - green) / delta) + 4.0);
        }

        return (NormaliseHue(hue), saturation);
    }

    /// <summary>
    /// Saturation-weighted circular mean, so hues sitting either side of a bucket
    /// edge average correctly instead of snapping to the bucket centre.
    /// </summary>
    private static double CircularMeanHue(double sinSum, double cosSum, double fallbackDegrees)
    {
        if (sinSum == 0.0 && cosSum == 0.0)
        {
            return NormaliseHue(fallbackDegrees);
        }

        return NormaliseHue(Math.Atan2(sinSum, cosSum) * DegreesPerRadian);
    }

    private static double NormaliseHue(double degrees)
    {
        var hue = degrees % 360.0;
        return hue < 0.0 ? hue + 360.0 : hue;
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0.0)
        {
            t += 1.0;
        }
        else if (t > 1.0)
        {
            t -= 1.0;
        }

        if (t < 1.0 / 6.0)
        {
            return p + ((q - p) * 6.0 * t);
        }

        if (t < 1.0 / 2.0)
        {
            return q;
        }

        if (t < 2.0 / 3.0)
        {
            return p + ((q - p) * ((2.0 / 3.0) - t) * 6.0);
        }

        return p;
    }

    private static byte ToChannel(double value) =>
        (byte)Math.Clamp(Math.Round(value * 255.0, MidpointRounding.AwayFromZero), 0.0, 255.0);
}
