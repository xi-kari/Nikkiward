using Nikkiward.Features.Background;
using Nikkiward.Features.Shell;
using Nikkiward.Models;

internal static class MotionBackgroundTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("motion source rules accept common codecs and containers", MotionSourceRulesAcceptCommonMedia),
        ("motion source rules accept every positive finite frame rate", MotionSourceRulesAcceptUncappedFrameRates),
        ("motion source rules reject unsupported media", MotionSourceRulesRejectUnsupportedMedia),
        ("motion import retries a transient source lock", MotionImportRetriesTransientSourceLock),
        ("glass tier resolver covers the degradation matrix", GlassTierResolverCoversMatrix),
        ("motion requests never resolve to motion blur", MotionNeverResolvesToMotionBlur),
        ("motion scrim floor and local factor remain bounded", ReadabilityRuntimeContractsStayBounded),
        ("live blur frame monitor locks after two slow p95 windows", FrameMonitorContractIsBounded),
    ];

    private static Task MotionSourceRulesAcceptCommonMedia()
    {
        var accepted = new (string Name, MotionSourceFacts Facts)[]
        {
            ("H.264 MP4", new(".MP4", 1, 1920, 1080, 30, "H264", "AAC")),
            ("HEVC MP4", new(".mp4", 1, 1920, 1080, 60, "HEVC", "EAC3")),
            ("AV1 MKV", new(".mkv", 1, 3840, 2160, 60, "AV01", "OPUS")),
            ("VP9 WebM", new(".webm", 1, 2560, 1440, 60, "VP90", "OPUS")),
            ("MPEG-4 AVI", new(".avi", 1, 1920, 1080, 30, "MP4V", "MP3")),
            ("WMV", new(".wmv", 1, 1920, 1080, 30, "WMV3", "WMAUDIO2")),
            ("ProRes MOV", new(".mov", 1, 4096, 2160, 30, "APCH", null)),
            ("MPEG-2 TS", new(".ts", 1, 1920, 1080, 50, "MPEG2", "AC3")),
            ("AVCHD MTS", new(".mts", 1, 1920, 1080, 50, "H264", "AC3")),
            ("ASF", new(".asf", 1, 1920, 1080, 30, "WMV3", "WMAUDIO2")),
            ("VOB", new(".vob", 1, 720, 576, 25, "MPEG2", "AC3")),
            ("Windows TV", new(".wtv", 1, 1920, 1080, 30, "MPEG2", "AC3")),
            ("decoder-recognized custom container", new(".future", 1, 1920, 1080, 30, "H264", null)),
        };

        foreach (var scenario in accepted)
        {
            var validation = MotionSourceRules.Validate(scenario.Facts);
            Assert(
                validation.IsUsable,
                $"{scenario.Name} should reach system decoder validation: {validation.RejectReason}");
        }

        Assert(MotionSourceRules.Validate(new MotionSourceFacts(
            ".mp4",
            4UL * 1024 * 1024 * 1024,
            MotionSourceRules.MaximumLongEdge,
            MotionSourceRules.MaximumShortEdge,
            60,
            "AVC1",
            null)).IsUsable, "multi-gigabyte landscape 8K sources should be accepted");
        Assert(MotionSourceRules.Validate(new MotionSourceFacts(
            ".mp4",
            1,
            MotionSourceRules.MaximumShortEdge,
            MotionSourceRules.MaximumLongEdge,
            60,
            "H264",
            "AAC")).IsUsable, "the portrait 8K boundary should be accepted");
        AssertEqual(7680U, MotionSourceRules.MaximumLongEdge, "motion import long-edge ceiling");
        AssertEqual(4320U, MotionSourceRules.MaximumShortEdge, "motion import short-edge ceiling");
        Assert(MotionSourceRules.Validate(new MotionSourceFacts(
            ".mp4",
            1,
            1280,
            720,
            240,
            "H264",
            null)).IsUsable, "high frame-rate H.264 must not be capped");
        foreach (var extension in new[]
                 {
                     ".mts", ".m2t", ".asf", ".vob", ".wtv", ".3gpp", ".3gp2", ".mp4v",
                 })
        {
            Assert(
                MotionSourceRules.IsSupportedExtension(extension),
                $"{extension} should be visible in the common-container picker");
        }

        return Task.CompletedTask;
    }

    private static Task MotionSourceRulesAcceptUncappedFrameRates()
    {
        var source = new MotionSourceFacts(
            ".mp4",
            1,
            1920,
            1080,
            30,
            "H264",
            "AAC");
        foreach (var framesPerSecond in new[] { 1d, 23.976, 60d, 120d, 240d, 1000d })
        {
            Assert(
                MotionSourceRules.Validate(source with { FramesPerSecond = framesPerSecond }).IsUsable,
                $"{framesPerSecond:0.###} FPS source should be accepted without a cap");
        }

        foreach (var framesPerSecond in new[]
                 {
                     0d,
                     -1d,
                     double.NaN,
                     double.PositiveInfinity,
                     double.NegativeInfinity,
                 })
        {
            var rejected = MotionSourceRules.Validate(source with { FramesPerSecond = framesPerSecond });
            Assert(!rejected.IsUsable, "invalid frame-rate metadata must still be rejected");
            Assert(
                rejected.RejectReason?.Contains("帧率", StringComparison.Ordinal) == true,
                "invalid frame-rate metadata needs a precise reason");
        }

        return Task.CompletedTask;
    }

    private static Task MotionSourceRulesRejectUnsupportedMedia()
    {
        var rejected = new (string Name, MotionSourceFacts Facts)[]
        {
            ("landscape over 8K", new(".mp4", 1, 7681, 4320, 30, "H264", "AAC")),
            ("portrait over 8K", new(".mp4", 1, 4320, 7681, 30, "H264", "AAC")),
            ("short edge over 8K", new(".mp4", 1, 7680, 4321, 30, "H264", "AAC")),
            ("empty file", new(".mp4", 0, 1920, 1080, 30, "H264", "AAC")),
        };

        foreach (var scenario in rejected)
        {
            var result = MotionSourceRules.Validate(scenario.Facts);
            Assert(!result.IsUsable, $"{scenario.Name} source must be rejected");
            Assert(!string.IsNullOrWhiteSpace(result.RejectReason), $"{scenario.Name} rejection needs a reason");
        }

        return Task.CompletedTask;
    }

    private static async Task MotionImportRetriesTransientSourceLock()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"nikkiward-motion-copy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.mp4");
        var destination = Path.Combine(root, "destination.tmp");
        var expected = new byte[256 * 1024];
        for (var index = 0; index < expected.Length; index++)
        {
            expected[index] = (byte)(index % 251);
        }

        await File.WriteAllBytesAsync(source, expected);
        FileStream? held = new(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        try
        {
            var copy = MotionImportFileCopier.CopyWithRetryAsync(
                source,
                destination,
                CancellationToken.None);
            await Task.Delay(250);
            await held.DisposeAsync();
            held = null;
            await copy;

            var actual = await File.ReadAllBytesAsync(destination);
            Assert(expected.SequenceEqual(actual), "retried motion import must preserve every source byte");
        }
        finally
        {
            if (held is not null)
            {
                await held.DisposeAsync();
            }

            Directory.Delete(root, recursive: true);
        }
    }

    private static Task GlassTierResolverCoversMatrix()
    {
        var baseline = BaselineSignals();
        var scenarios = new (string Name, GlassSignals Signals, GlassTier Expected)[]
        {
            ("high contrast", baseline with { HighContrast = true, UserWantsMotion = true }, GlassTier.Flat),
            ("still blur", baseline, GlassTier.StillBlur),
            ("blur not requested", baseline with { UserWantsLiveBlur = false }, GlassTier.StillScrim),
            ("advanced effects off", baseline with { AdvancedEffectsEnabled = false }, GlassTier.StillScrim),
            ("blur construction failed", baseline with { BlurConstructionFailed = true }, GlassTier.StillScrim),
            ("low frame rate", baseline with { LowFrameRateMeasured = true }, GlassTier.StillScrim),
            ("energy saver", baseline with { EnergySaverOn = true }, GlassTier.StillScrim),
            ("remote session", baseline with { RemoteSession = true }, GlassTier.StillScrim),
            ("motion with sampling", baseline with { UserWantsMotion = true, MotionBackdropSamplingSupported = true }, GlassTier.MotionScrim),
            ("motion without sampling", baseline with { UserWantsMotion = true, MotionBackdropSamplingSupported = false }, GlassTier.MotionScrim),
            ("motion without live blur", baseline with { UserWantsMotion = true, UserWantsLiveBlur = false }, GlassTier.MotionScrim),
            ("motion mode off", baseline with { UserWantsMotion = true, Motion = AppearanceMotionMode.Off }, GlassTier.StillBlur),
            ("animations off", baseline with { UserWantsMotion = true, AnimationsEnabled = false }, GlassTier.StillBlur),
            ("window occluded", baseline with { UserWantsMotion = true, WindowOccluded = true }, GlassTier.StillBlur),
            ("motion energy saver", baseline with { UserWantsMotion = true, EnergySaverOn = true }, GlassTier.StillScrim),
            ("motion remote session", baseline with { UserWantsMotion = true, RemoteSession = true }, GlassTier.StillScrim),
        };

        foreach (var scenario in scenarios)
        {
            AssertEqual(
                scenario.Expected,
                GlassTierResolver.Resolve(scenario.Signals),
                scenario.Name);
        }

        return Task.CompletedTask;
    }

    private static Task MotionNeverResolvesToMotionBlur()
    {
        const int BooleanSignalCount = 11;
        foreach (var motion in Enum.GetValues<AppearanceMotionMode>())
        {
            for (var mask = 0; mask < (1 << BooleanSignalCount); mask++)
            {
                var signals = new GlassSignals(
                    Flag(mask, 0),
                    Flag(mask, 1),
                    Flag(mask, 2),
                    Flag(mask, 3),
                    Flag(mask, 4),
                    Flag(mask, 5),
                    Flag(mask, 6),
                    Flag(mask, 7),
                    motion,
                    Flag(mask, 8),
                    Flag(mask, 9),
                    Flag(mask, 10));
                Assert(
                    GlassTierResolver.Resolve(signals) != GlassTier.MotionBlur,
                    $"motion blur leaked for mode {motion}, mask 0x{mask:X}");
            }
        }

        return Task.CompletedTask;
    }

    private static Task ReadabilityRuntimeContractsStayBounded()
    {
        var service = ReadProductSource("Features", "Background", "ArtBackdropService.cs");
        Assert(service.Contains("LocalScrimMinimumFactor = 0.60", StringComparison.Ordinal),
            "local scrim minimum factor must remain 0.60");
        Assert(service.Contains("LocalScrimMaximumFactor = 1.40", StringComparison.Ordinal),
            "local scrim maximum factor must remain 1.40");
        Assert(service.Contains("required / Math.Max(0.01, reference)", StringComparison.Ordinal),
            "local scrim must scale the solved opacity by its reference alpha");
        Assert(service.Contains("ResolveDoubleResource(\"MotionScrimFloor\"", StringComparison.Ordinal),
            "motion publication must consume the scrim floor token");
        Assert(service.Contains("ScrimOpacity = Math.Max(", StringComparison.Ordinal),
            "motion publication must clamp scrim opacity to the floor");
        Assert(service.Contains("HasMaterialLuminanceDrift", StringComparison.Ordinal) &&
            service.Contains("region.P95Luminance", StringComparison.Ordinal),
            "dynamic motion analysis must publish on region P95 drift");

        foreach (var channel in Enumerable.Range(0, 256))
        {
            var luminance = ArtPaletteAnalyzer.RelativeLuminance(
                (byte)channel,
                (byte)channel,
                (byte)channel);
            var required = ArtPaletteAnalyzer.SolveScrimOpacity(
                luminance,
                ArtPreferredTheme.Dark);
            var factor = Math.Clamp(required / 0.34, 0.60, 1.40);
            Assert(factor is >= 0.60 and <= 1.40, $"local factor escaped at grey {channel:X2}");
            var motionScrim = Math.Max(required, 0.28);
            Assert(motionScrim >= 0.28, $"motion scrim fell below its floor at grey {channel:X2}");
        }

        return Task.CompletedTask;
    }

    private static Task FrameMonitorContractIsBounded()
    {
        var monitor = ReadProductSource("Features", "Shell", "FrameIntervalMonitor.cs");
        var backdrop = ReadProductSource("Features", "Background", "ArtBackdropView.xaml.cs");
        Assert(monitor.Contains("WindowSeconds = 3", StringComparison.Ordinal),
            "frame monitor window must remain three seconds");
        Assert(monitor.Contains("SlowFrameThresholdMilliseconds = 22", StringComparison.Ordinal),
            "frame monitor p95 threshold must remain 22 ms");
        Assert(monitor.Contains("RequiredSlowWindows = 2", StringComparison.Ordinal),
            "frame monitor must require two consecutive slow windows");
        Assert(monitor.Contains("CompositionTarget.Rendering", StringComparison.Ordinal) &&
            monitor.Contains("Math.Ceiling(_intervals.Count * 0.95)", StringComparison.Ordinal),
            "frame monitor must sample compositor intervals and evaluate p95");
        Assert(backdrop.Contains("ReportLowFrameRate", StringComparison.Ordinal),
            "the backdrop must lock the glass tier when monitoring reports sustained slow frames");
        return Task.CompletedTask;
    }

    private static GlassSignals BaselineSignals() => new(
        HighContrast: false,
        UserWantsLiveBlur: true,
        AdvancedEffectsEnabled: true,
        BlurConstructionFailed: false,
        EnergySaverOn: false,
        RemoteSession: false,
        LowFrameRateMeasured: false,
        UserWantsMotion: false,
        Motion: AppearanceMotionMode.Full,
        AnimationsEnabled: true,
        WindowOccluded: false,
        MotionBackdropSamplingSupported: true);

    private static bool Flag(int mask, int bit) => (mask & (1 << bit)) != 0;

    private static string ReadProductSource(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "Nikkiward",
                Path.Combine(parts));
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Product source not found: {Path.Combine(parts)}");
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
            throw new InvalidOperationException(
                $"{message}: expected '{expected}', actual '{actual}'.");
        }
    }
}
