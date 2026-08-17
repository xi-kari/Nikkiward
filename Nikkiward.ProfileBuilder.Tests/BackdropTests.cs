using Nikkiward.Features.Background;

/// <summary>
/// Numeric tests for the adaptive backdrop. The palette analyser, blur baker and
/// analysis cache are pure logic, so they are verified here rather than by eye.
/// </summary>
internal static class BackdropTests
{
    /// <summary>Warm dark ink that labels every accent fill, both themes.</summary>
    private const byte InkOnAccentR = 0x2A;
    private const byte InkOnAccentG = 0x23;
    private const byte InkOnAccentB = 0x20;

    /// <summary>PaperBase per theme: the surface an accent fill sits on.</summary>
    private static readonly (byte R, byte G, byte B) PaperLight = (0xF6, 0xF1, 0xEA);
    private static readonly (byte R, byte G, byte B) PaperDark = (0x24, 0x1E, 0x1B);

    // OnArtPrimaryTextBrush and OnArtScrimBrush per artwork polarity, from
    // Themes\OnArt.xaml. Restated here so the test fails if either the dictionary
    // or the analyser's copy drifts.
    private static readonly (byte R, byte G, byte B) OnArtInkLight = (0x24, 0x1E, 0x1B);
    private static readonly (byte R, byte G, byte B) OnArtInkDark = (0xF7, 0xF2, 0xEA);
    private static readonly (byte R, byte G, byte B) OnArtScrimLight = (0xFD, 0xFA, 0xF5);
    private static readonly (byte R, byte G, byte B) OnArtScrimDark = (0x24, 0x1E, 0x1B);

    // The cache only accepts 64 lowercase hex characters, so tests use real-shaped
    // hashes instead of short labels.
    private const string SampleHash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string AltHash =
        "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    public static (string Name, Func<Task> Run)[] All =>
    [
        ("accent gate is satisfiable for the ink that labels it", TestAccentInkGateSatisfiable),
        ("accent stays distinguishable from its own theme surface", TestAccentSurfaceSeparation),
        ("theme accent selection retains both derived colours", TestThemeAccentSelection),
        ("action fill stays solid and tied to the derived accent", TestActionFill),
        ("action fill separates from raw and washed local backdrops", TestActionFillSeparation),
        ("a rejected publication queue faults without publishing", TestRejectedPublicationQueue),
        ("dominant hue survives a synthetic single-hue image", TestDominantHueRecovery),
        ("a derived accent that merges with the action region falls back", TestArtworkSeparationGate),
        ("a saturated detail below six percent falls back", TestLowWeightHueFallsBack),
        ("a saturated detail above six percent keeps its hue", TestHueAboveWeightFloor),
        ("desaturated art falls back rather than inventing an accent", TestGreyFallsBack),
        ("scrim rises with artwork luminance and stays clamped", TestScrimMonotonic),
        ("preferred theme follows artwork luminance", TestPreferredTheme),
        ("masthead notice action and pill luminance are sampled independently", TestRegionalLuminance),
        ("on-art ink holds AA at every artwork luminance", TestOnArtInkMeetsAa),
        ("blur bake is deterministic and correctly sized", TestBlurDeterminism),
        ("blur bake tolerates a degenerate one pixel source", TestBlurDegenerate),
        ("downsample preserves a flat colour exactly", TestDownsampleFlat),
        ("cache round trips an analysis", TestCacheRoundTrip),
        ("cache rejects a hash that escapes its directory", TestCachePathHardening),
        ("cache ignores a schema version it does not know", TestCacheSchemaMismatch),
    ];

    /// <summary>
    /// The gate must admit real hues. An accent is a fill labelled with
    /// InkOnAccent, so that pair is the adjacency that decides legibility.
    /// </summary>
    private static Task TestAccentInkGateSatisfiable()
    {
        var inkLuminance = ArtPaletteAnalyzer.RelativeLuminance(
            InkOnAccentR,
            InkOnAccentG,
            InkOnAccentB);

        var lightPasses = 0;
        var darkPasses = 0;
        var worstLight = double.MaxValue;
        var worstDark = double.MaxValue;

        for (var hue = 0; hue < 360; hue++)
        {
            var light = ArtPaletteAnalyzer.HslToArgb(hue, 0.45, 0.62);
            var dark = ArtPaletteAnalyzer.HslToArgb(hue, 0.45, 0.68);

            var lightRatio = ArtPaletteAnalyzer.ContrastRatio(
                LuminanceOf(light),
                inkLuminance);
            var darkRatio = ArtPaletteAnalyzer.ContrastRatio(
                LuminanceOf(dark),
                inkLuminance);

            worstLight = Math.Min(worstLight, lightRatio);
            worstDark = Math.Min(worstDark, darkRatio);
            if (lightRatio >= 3.0)
            {
                lightPasses++;
            }

            if (darkRatio >= 3.0)
            {
                darkPasses++;
            }
        }

        Console.WriteLine(
            $"      ink gate: light {lightPasses}/360 worst {worstLight:F2}:1, " +
            $"dark {darkPasses}/360 worst {worstDark:F2}:1");

        Assert(
            lightPasses == 360,
            $"every light accent must be legible under ink, worst was {worstLight:F2}:1");
        Assert(
            darkPasses == 360,
            $"every dark accent must be legible under ink, worst was {worstDark:F2}:1");
        return Task.CompletedTask;
    }

    /// <summary>
    /// A fill that matches its surface stops reading as a control. Dark theme is
    /// the case that matters: the accent keeps its light value there.
    /// </summary>
    private static Task TestAccentSurfaceSeparation()
    {
        var paperLight = ArtPaletteAnalyzer.RelativeLuminance(
            PaperLight.R,
            PaperLight.G,
            PaperLight.B);
        var paperDark = ArtPaletteAnalyzer.RelativeLuminance(
            PaperDark.R,
            PaperDark.G,
            PaperDark.B);

        var worstLight = double.MaxValue;
        var worstDark = double.MaxValue;
        for (var hue = 0; hue < 360; hue++)
        {
            worstLight = Math.Min(
                worstLight,
                ArtPaletteAnalyzer.ContrastRatio(
                    LuminanceOf(ArtPaletteAnalyzer.HslToArgb(hue, 0.45, 0.62)),
                    paperLight));
            worstDark = Math.Min(
                worstDark,
                ArtPaletteAnalyzer.ContrastRatio(
                    LuminanceOf(ArtPaletteAnalyzer.HslToArgb(hue, 0.45, 0.68)),
                    paperDark));
        }

        Console.WriteLine(
            $"      surface separation: light worst {worstLight:F2}:1, dark worst {worstDark:F2}:1");

        // 1.3:1 is a visibility floor, not a WCAG text threshold: the accent only
        // has to be seen as a distinct shape against paper.
        Assert(worstLight >= 1.3, $"light accent too close to paper: {worstLight:F2}:1");
        Assert(worstDark >= 1.3, $"dark accent too close to charcoal: {worstDark:F2}:1");
        return Task.CompletedTask;
    }

    private static Task TestThemeAccentSelection()
    {
        const uint light = 0xFFB85E7A;
        const uint dark = 0xFFE49AB0;

        AssertEqual(
            light,
            ArtThemeAccentSelector.Select(light, dark, ArtPreferredTheme.Light),
            "light theme accent");
        AssertEqual(
            dark,
            ArtThemeAccentSelector.Select(light, dark, ArtPreferredTheme.Dark),
            "dark theme accent");
        return Task.CompletedTask;
    }

    private static Task TestActionFill()
    {
        const uint accent = ArtPaletteAnalyzer.FallbackAccentArgb;
        var fill = ArtActionFill.ForBackdrops(accent, 0.05);

        AssertEqual(accent, fill, "a usable derived accent should remain the solid action fill");
        return Task.CompletedTask;
    }

    private static Task TestActionFillSeparation()
    {
        var inkLuminance = ArtPaletteAnalyzer.RelativeLuminance(
            InkOnAccentR,
            InkOnAccentG,
            InkOnAccentB);

        for (var hue = 0; hue < 360; hue += 15)
        {
            var accent = ArtPaletteAnalyzer.HslToArgb(hue, 0.45, 0.62);
            for (var step = 0; step <= 20; step++)
            {
                var rawBackdrop = step / 20.0;
                var preferredTheme = ArtPaletteAnalyzer.PreferredThemeForLuminance(rawBackdrop);
                var scrimOpacity = ArtPaletteAnalyzer.SolveScrimOpacity(rawBackdrop, preferredTheme);
                var baseLayerOpacity = 0.34 * scrimOpacity;
                var cornerOpacity = 1.0 -
                    ((1.0 - baseLayerOpacity) *
                     (1.0 - scrimOpacity) *
                     (1.0 - scrimOpacity));
                var baseWashed = ArtActionFill.CompositeWithScrim(
                    rawBackdrop,
                    preferredTheme,
                    baseLayerOpacity);
                var cornerWashed = ArtActionFill.CompositeWithScrim(
                    rawBackdrop,
                    preferredTheme,
                    cornerOpacity);
                var fill = ArtActionFill.ForBackdrops(
                    accent,
                    rawBackdrop,
                    baseWashed,
                    cornerWashed);
                var fillLuminance = LuminanceOf(fill);

                foreach (var backdropLuminance in new[] { rawBackdrop, baseWashed, cornerWashed })
                {
                    Assert(
                        ArtPaletteAnalyzer.ContrastRatio(fillLuminance, backdropLuminance) >=
                            ArtActionFill.MinimumShapeContrast,
                        $"action fill lost its shape at hue {hue}, backdrop {backdropLuminance:F2}");
                }

                Assert(
                    ArtPaletteAnalyzer.ContrastRatio(fillLuminance, inkLuminance) >=
                        ArtActionFill.MinimumInkContrast,
                    $"action fill lost label contrast at hue {hue}, backdrop {rawBackdrop:F2}");
            }
        }

        return Task.CompletedTask;
    }

    private static async Task TestRejectedPublicationQueue()
    {
        var published = false;
        var task = ArtPublicationDispatcher.EnqueueAsync(
            _ => false,
            () => published = true);

        try
        {
            await task;
            throw new InvalidOperationException("a rejected queue must not complete successfully");
        }
        catch (InvalidOperationException ex)
        {
            Assert(
                ex.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase),
                "the failure must identify the rejected UI publication");
        }

        Assert(!published, "the publication callback must not run after rejection");
    }

    private static Task TestDominantHueRecovery()
    {
        // A saturated teal field. Hue 180, well above the voting saturation floor.
        var buffer = FlatBuffer(16, 16, 0x20, 0xB0, 0xB0);
        PaintRegion(buffer, 9, 12, 7, 2, 0x08, 0x30, 0x30);
        var analysis = new ArtPaletteAnalyzer().Analyze(buffer, "teal");

        Assert(
            analysis.DominantHue >= 0.0,
            "a saturated field must produce a dominant hue");
        var delta = Math.Abs(analysis.DominantHue - 180.0);
        Console.WriteLine(
            $"      recovered hue {analysis.DominantHue:F1} (expected 180), " +
            $"weight {analysis.DominantHueWeight:F2}, fallback {analysis.AccentFromFallback}");
        Assert(delta <= 6.0, $"hue drifted {delta:F1} degrees from 180");
        Assert(
            Math.Abs(analysis.DominantHueWeight - 1.0) < 0.001,
            "a single-hue image should vote unanimously");
        Assert(
            !analysis.AccentFromFallback,
            "a clean saturated hue must not fall back to the brand blush");
        return Task.CompletedTask;
    }

    private static Task TestArtworkSeparationGate()
    {
        var analysis = new ArtPaletteAnalyzer().Analyze(
            FlatBuffer(16, 16, 0xE0, 0x88, 0xA4),
            "pink-on-pink");

        Assert(
            analysis.AccentFromFallback,
            "an accent derived from the action region must fall back when it merges with the artwork");
        return Task.CompletedTask;
    }

    private static Task TestGreyFallsBack()
    {
        var buffer = FlatBuffer(16, 16, 0x808080 >> 16 & 0xFF, 0x80, 0x80);
        var analysis = new ArtPaletteAnalyzer().Analyze(buffer, "grey");

        Assert(
            analysis.DominantHue < 0.0,
            "grey has no hue to derive, so none should be reported");
        Assert(
            analysis.AccentFromFallback,
            "grey artwork must fall back to the brand accent");
        return Task.CompletedTask;
    }

    private static Task TestLowWeightHueFallsBack()
    {
        var analysis = new ArtPaletteAnalyzer().Analyze(
            SparseSaturatedBuffer(15),
            "low-weight");

        Assert(
            Math.Abs(analysis.DominantHueWeight - 0.05859375) < 1e-9,
            $"15 of 256 pixels should weigh 0.05859375, actual {analysis.DominantHueWeight:R}");
        Assert(
            analysis.AccentFromFallback,
            "a hue covering less than six percent of the artwork must not choose the accent");
        return Task.CompletedTask;
    }

    private static Task TestHueAboveWeightFloor()
    {
        var buffer = SparseSaturatedBuffer(16);
        PaintRegion(buffer, 9, 12, 7, 3, 0x18, 0x18, 0x18);
        var analysis = new ArtPaletteAnalyzer().Analyze(buffer, "above-weight-floor");

        Assert(
            Math.Abs(analysis.DominantHueWeight - 0.0625) < 1e-9,
            $"16 of 256 pixels should weigh 0.0625, actual {analysis.DominantHueWeight:R}");
        Assert(
            !analysis.AccentFromFallback,
            "a usable hue covering more than six percent of the artwork should remain derived");
        return Task.CompletedTask;
    }

    private static Task TestScrimMonotonic()
    {
        var analyzer = new ArtPaletteAnalyzer();
        var dark = analyzer.Analyze(FlatBuffer(8, 8, 0x08, 0x08, 0x08), "dark").ScrimOpacity;
        var mid = analyzer.Analyze(FlatBuffer(8, 8, 0x80, 0x80, 0x80), "mid").ScrimOpacity;
        var bright = analyzer.Analyze(FlatBuffer(8, 8, 0xF8, 0xF8, 0xF8), "bright").ScrimOpacity;

        Console.WriteLine($"      scrim: dark {dark:F3}, mid {mid:F3}, bright {bright:F3}");

        // Non-decreasing, not strictly increasing: dark artwork needs no help, so
        // the low end is expected to sit flat on the clamp floor.
        Assert(dark <= mid && mid < bright, "brighter artwork needs a heavier scrim");
        Assert(dark >= 0.12 && bright <= 0.52, "scrim must stay inside its clamp");
        Assert(bright > dark, "the scrim must respond to luminance somewhere in range");
        return Task.CompletedTask;
    }

    private static Task TestPreferredTheme()
    {
        var analyzer = new ArtPaletteAnalyzer();
        AssertEqual(
            ArtPreferredTheme.Dark,
            analyzer.Analyze(FlatBuffer(8, 8, 0x10, 0x10, 0x10), SampleHash).PreferredTheme,
            "dark artwork suggests the dark theme");
        AssertEqual(
            ArtPreferredTheme.Light,
            analyzer.Analyze(FlatBuffer(8, 8, 0xF0, 0xF0, 0xF0), AltHash).PreferredTheme,
            "bright artwork suggests the light theme");
        return Task.CompletedTask;
    }

    private static Task TestRegionalLuminance()
    {
        var buffer = FlatBuffer(100, 100, 0x60, 0x60, 0x60);
        PaintRegion(buffer, 2, 10, 40, 22, 0xF0, 0xF0, 0xF0);
        PaintRegion(buffer, 2, 62, 40, 30, 0xD0, 0xD0, 0xD0);
        PaintRegion(buffer, 58, 72, 40, 18, 0x18, 0x18, 0x18);
        PaintRegion(buffer, 62, 2, 36, 12, 0xE8, 0xE8, 0xE8);

        var analysis = new ArtPaletteAnalyzer().Analyze(buffer, "regions");
        Assert(analysis.MastheadLuminance > 0.8, "masthead mean should track its bright region");
        Assert(analysis.MastheadP95Luminance > 0.8, "masthead P95 should track its bright region");
        Assert(analysis.CtaLuminance < 0.02, "CTA mean should track its dark region");
        Assert(analysis.CtaP95Luminance < 0.02, "CTA P95 should track its dark region");
        var notice = analysis.Regions.Single(region => region.RegionId == "notice");
        var pill = analysis.Regions.Single(region => region.RegionId == "pill");
        Assert(notice.MeanLuminance > 0.6, "notice mean should track its bright region");
        Assert(notice.P95Luminance > 0.6, "notice P95 should track its bright region");
        Assert(pill.MeanLuminance > 0.8, "pill mean should track its bright region");
        Assert(pill.P95Luminance > 0.8, "pill P95 should track its bright region");
        return Task.CompletedTask;
    }

    /// <summary>
    /// The readability contract the whole tier exists for: whatever the user picks
    /// as a wallpaper, OnArtPrimaryTextBrush over the scrim must clear WCAG AA.
    /// Sweeps every 8-bit grey, which crosses the polarity flip where the old
    /// luminance-ramp-only scrim was at its weakest.
    /// </summary>
    private static Task TestOnArtInkMeetsAa()
    {
        var analyzer = new ArtPaletteAnalyzer();
        var worstRatio = double.MaxValue;
        var worstChannel = -1;

        for (var channel = 0; channel <= 255; channel++)
        {
            var analysis = analyzer.Analyze(FlatBuffer(4, 4, channel, channel, channel), "sweep");
            var light = analysis.PreferredTheme == ArtPreferredTheme.Light;
            var ink = light ? OnArtInkLight : OnArtInkDark;
            var scrim = light ? OnArtScrimLight : OnArtScrimDark;

            var ratio = ArtPaletteAnalyzer.ContrastRatio(
                ArtPaletteAnalyzer.RelativeLuminance(ink.R, ink.G, ink.B),
                CompositedLuminance((byte)channel, scrim, analysis.ScrimOpacity));
            if (ratio < worstRatio)
            {
                worstRatio = ratio;
                worstChannel = channel;
            }
        }

        Console.WriteLine(
            $"      worst on-art contrast {worstRatio:F2}:1 at grey {worstChannel:X2}");
        Assert(worstRatio >= 4.5, $"on-art ink fell to {worstRatio:F2}:1 at grey {worstChannel:X2}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Relative luminance of a flat grey with an opaque scrim composited over it
    /// at <paramref name="alpha"/>. Independent of the analyser's own private
    /// blend so the assertion is a check rather than a tautology.
    /// </summary>
    private static double CompositedLuminance(
        byte artChannel,
        (byte R, byte G, byte B) scrim,
        double alpha)
    {
        var keep = 1.0 - alpha;
        return ArtPaletteAnalyzer.RelativeLuminance(
            Blend(artChannel, scrim.R),
            Blend(artChannel, scrim.G),
            Blend(artChannel, scrim.B));

        byte Blend(byte art, byte over) => (byte)Math.Clamp(
            Math.Round((art * keep) + (over * alpha), MidpointRounding.AwayFromZero),
            0.0,
            255.0);
    }

    private static Task TestBlurDeterminism()
    {
        var source = GradientBuffer(240, 135);
        var baker = new ArtBlurBaker();
        var first = baker.Bake(source);
        var second = baker.Bake(source);

        // The baker never upscales: a source narrower than the bake width is kept
        // at its own width rather than being blown up and re-blurred.
        AssertEqual(240, first.Width, "a narrow source must not be upscaled");
        Assert(first.Height > 0, "bake height must be positive");

        // A full-size decode must reach the baker already scaled down, otherwise the
        // box passes run over ~26x the pixels for a result drawn scaled up anyway.
        var expectedHeight = ArtBlurBaker.BakeWidth * 1080 / 1920;
        var wide = baker.Bake(
            GradientBuffer(1920, 1080).Downsample(ArtBlurBaker.BakeWidth, expectedHeight));
        AssertEqual(
            ArtBlurBaker.BakeWidth,
            wide.Width,
            "a large source bakes down to the bake width");
        AssertEqual(
            expectedHeight,
            wide.Height,
            "the bake must preserve the source aspect ratio");
        Assert(
            first.Pixels.Length == first.Width * first.Height * 4,
            "bake buffer length must match its dimensions");
        Assert(
            first.Pixels.AsSpan().SequenceEqual(second.Pixels),
            "the same source must bake to identical bytes, otherwise the cache lies");
        return Task.CompletedTask;
    }

    private static Task TestBlurDegenerate()
    {
        var baked = new ArtBlurBaker().Bake(FlatBuffer(1, 1, 0x40, 0x50, 0x60));
        Assert(baked.Width > 0 && baked.Height > 0, "a one pixel source must still bake");
        return Task.CompletedTask;
    }

    private static Task TestDownsampleFlat()
    {
        var flat = FlatBuffer(64, 64, 0x30, 0x60, 0x90);
        var small = flat.Downsample(16, 16);

        AssertEqual(16, small.Width, "downsample width");
        AssertEqual(16, small.Height, "downsample height");
        for (var i = 0; i < small.Pixels.Length; i += 4)
        {
            Assert(small.Pixels[i + 0] == 0x90, "blue channel must survive downsampling");
            Assert(small.Pixels[i + 1] == 0x60, "green channel must survive downsampling");
            Assert(small.Pixels[i + 2] == 0x30, "red channel must survive downsampling");
        }

        return Task.CompletedTask;
    }

    private static async Task TestCacheRoundTrip()
    {
        using var fixture = new CacheFixture();
        var cache = new ArtAnalysisCache(fixture.Root);
        var analysis = new ArtPaletteAnalyzer().Analyze(
            FlatBuffer(16, 16, 0xC0, 0x40, 0x60),
            SampleHash);
        await cache.SaveAsync(analysis);
        var loaded = await cache.LoadAsync(SampleHash);

        Assert(loaded is not null, "a saved analysis must load back");
        AssertEqual(3, analysis.SchemaVersion, "analysis schema");
        AssertEqual(3, loaded!.SchemaVersion, "loaded analysis schema");
        AssertEqual(analysis.ArtHash, loaded.ArtHash, "hash");
        AssertEqual(analysis.DerivedAccentLight, loaded.DerivedAccentLight, "light accent");
        AssertEqual(analysis.DerivedAccentDark, loaded.DerivedAccentDark, "dark accent");
        AssertEqual(analysis.PreferredTheme, loaded.PreferredTheme, "preferred theme");
        AssertEqual(analysis.SourceKind, loaded.SourceKind, "source kind");
        Assert(loaded.BlurredArtPath is null, "palette cache must not require a blur plate");
        Assert(
            Math.Abs(analysis.ScrimOpacity - loaded.ScrimOpacity) < 1e-9,
            "scrim opacity must survive the round trip");
        Assert(
            Math.Abs(analysis.MastheadP95Luminance - loaded.MastheadP95Luminance) < 1e-9,
            "masthead P95 must survive the round trip");
        Assert(
            Math.Abs(analysis.CtaP95Luminance - loaded.CtaP95Luminance) < 1e-9,
            "CTA P95 must survive the round trip");
        AssertEqual(analysis.Regions.Count, loaded.Regions.Count, "region count");
        foreach (var expected in analysis.Regions)
        {
            var actual = loaded.Regions.Single(region => region.RegionId == expected.RegionId);
            Assert(Math.Abs(expected.P95Luminance - actual.P95Luminance) < 1e-9,
                $"{expected.RegionId} P95 must survive the round trip");
        }
    }

    private static async Task TestCachePathHardening()
    {
        using var fixture = new CacheFixture();
        var cache = new ArtAnalysisCache(fixture.Root);

        // The cache rejects a malformed hash outright rather than quietly missing
        // on it, so traversal can never reach the filesystem layer at all.
        foreach (var hostile in new[]
        {
            @"..\..\escape",
            "../../escape",
            "with/slash",
            @"with\backslash",
            "с yrillic",
            string.Empty,
            SampleHash.ToUpperInvariant(),
            SampleHash[..63],
            SampleHash + "0",
        })
        {
            var rejected = false;
            try
            {
                await cache.LoadAsync(hostile);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            Assert(rejected, $"hostile hash '{hostile}' must be rejected, not resolved");
        }
    }

    private static async Task TestCacheSchemaMismatch()
    {
        using var fixture = new CacheFixture();
        var cache = new ArtAnalysisCache(fixture.Root);
        var analysis = new ArtPaletteAnalyzer().Analyze(
            FlatBuffer(16, 16, 0x30, 0xA0, 0x50),
            AltHash);
        await cache.SaveAsync(analysis);

        var path = Path.Combine(cache.RootPath, $"{AltHash}.json");
        Assert(File.Exists(path), "the analysis file should be named after its hash");
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(
            path,
            json.Replace("\"schemaVersion\": 3", "\"schemaVersion\": 99", StringComparison.Ordinal));

        var loaded = await cache.LoadAsync(AltHash);
        Assert(loaded is null, "an unknown schema version must be ignored, not misread");
    }

    private static double LuminanceOf(uint argb) => ArtPaletteAnalyzer.RelativeLuminance(
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    /// <summary>BGRA8 buffer filled with one colour.</summary>
    private static ArtPixelBuffer FlatBuffer(int width, int height, int r, int g, int b)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i + 0] = (byte)b;
            pixels[i + 1] = (byte)g;
            pixels[i + 2] = (byte)r;
            pixels[i + 3] = 0xFF;
        }

        return new ArtPixelBuffer(pixels, width, height);
    }

    private static void PaintRegion(
        ArtPixelBuffer buffer,
        int left,
        int top,
        int width,
        int height,
        byte r,
        byte g,
        byte b)
    {
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var offset = (y * buffer.Stride) + (x * 4);
                buffer.Pixels[offset + 0] = b;
                buffer.Pixels[offset + 1] = g;
                buffer.Pixels[offset + 2] = r;
                buffer.Pixels[offset + 3] = 0xFF;
            }
        }
    }

    private static ArtPixelBuffer GradientBuffer(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset + 0] = (byte)(x * 255 / Math.Max(1, width - 1));
                pixels[offset + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                pixels[offset + 2] = (byte)((x + y) % 256);
                pixels[offset + 3] = 0xFF;
            }
        }

        return new ArtPixelBuffer(pixels, width, height);
    }

    private static ArtPixelBuffer SparseSaturatedBuffer(int saturatedPixelCount)
    {
        const int width = 16;
        const int height = 16;
        var pixels = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            var offset = pixel * 4;
            var saturated = pixel < saturatedPixelCount;
            pixels[offset + 0] = saturated ? (byte)0xB0 : (byte)0x80;
            pixels[offset + 1] = saturated ? (byte)0xB0 : (byte)0x80;
            pixels[offset + 2] = saturated ? (byte)0x20 : (byte)0x80;
            pixels[offset + 3] = 0xFF;
        }

        return new ArtPixelBuffer(pixels, width, height);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected '{expected}', actual '{actual}'.");
        }
    }

    private sealed class CacheFixture : IDisposable
    {
        public CacheFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "NikkiwardBackdrop",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
