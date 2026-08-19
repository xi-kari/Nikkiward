using Nikkiward.Features.Background;

internal static class HolographicBackdropProjectionTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("holographic layout fills compatible aspect ratios", CompatibleAspectUsesFill),
        ("holographic layout preserves a near-matching portrait", PortraitAspectUsesContain),
        ("square Wallpaper preview drives a square card", SquarePreviewDrivesSquareCard),
        ("holographic layout contains extreme aspect ratios", ExtremeAspectUsesContain),
        ("holographic layout handles missing source dimensions", MissingSourceUsesViewport),
        ("holographic pointer projection clamps and rejects invalid input", PointerProjectionIsBounded),
        ("holographic unload resets pointer and render state", UnloadContractIsComplete),
    ];

    private static Task CompatibleAspectUsesFill()
    {
        var layout = HolographicBackdropProjection.ProjectLayout(1600, 900, 800, 450);

        Assert(layout.IsValid, "compatible layout should be valid");
        AssertEqual(StillArtworkFitMode.Fill, layout.FitMode, "compatible fit mode");
        AssertNear(800, layout.Width, "compatible width");
        AssertNear(450, layout.Height, "compatible height");
        AssertNear(1, layout.SourceRetention, "compatible retention");
        return Task.CompletedTask;
    }

    private static Task ExtremeAspectUsesContain()
    {
        var layout = HolographicBackdropProjection.ProjectLayout(2400, 400, 800, 600);

        Assert(layout.IsValid, "contained layout should be valid");
        AssertEqual(StillArtworkFitMode.Contain, layout.FitMode, "extreme fit mode");
        AssertNear(800, layout.Width, "contained width");
        AssertNear(133.33333333333334d, layout.Height, "contained height");
        AssertNear(1, layout.SourceRetention, "contained retention");
        return Task.CompletedTask;
    }

    private static Task PortraitAspectUsesContain()
    {
        var layout = HolographicBackdropProjection.ProjectLayout(
            1440,
            2160,
            917,
            1215);

        Assert(layout.IsValid, "portrait layout should be valid");
        AssertEqual(StillArtworkFitMode.Contain, layout.FitMode, "portrait fit mode");
        AssertNear(810, layout.Width, "portrait width");
        AssertNear(1215, layout.Height, "portrait height");
        AssertNear(1, layout.SourceRetention, "portrait retention");
        return Task.CompletedTask;
    }

    private static Task SquarePreviewDrivesSquareCard()
    {
        var layout = HolographicBackdropProjection.ProjectLayout(
            1024,
            1024,
            2032,
            1143);

        Assert(layout.IsValid, "square preview layout should be valid");
        AssertEqual(StillArtworkFitMode.Contain, layout.FitMode, "square preview fit mode");
        AssertNear(1143, layout.Width, "square preview width");
        AssertNear(1143, layout.Height, "square preview height");
        AssertNear(1, layout.SourceRetention, "square preview retention");
        return Task.CompletedTask;
    }

    private static Task MissingSourceUsesViewport()
    {
        var layout = HolographicBackdropProjection.ProjectLayout(0, 0, 640, 360);

        Assert(layout.IsValid, "missing-source layout should be valid");
        AssertEqual(StillArtworkFitMode.Fill, layout.FitMode, "missing-source fit mode");
        AssertNear(640, layout.Width, "missing-source width");
        AssertNear(360, layout.Height, "missing-source height");
        return Task.CompletedTask;
    }

    private static Task PointerProjectionIsBounded()
    {
        var clamped = HolographicBackdropProjection.ProjectPointer(4, -3);
        Assert(clamped.IsValid, "clamped pointer should be valid");
        AssertNear(1, clamped.NormalizedX, "clamped x");
        AssertNear(-1, clamped.NormalizedY, "clamped y");
        Assert(
            clamped.GlarePosition is >= 0.12d and <= 0.88d,
            "glare position should remain bounded");

        var invalid = HolographicBackdropProjection.ProjectPointer(double.NaN, 0);
        Assert(!invalid.IsValid, "invalid pointer should be rejected");
        return Task.CompletedTask;
    }

    private static Task UnloadContractIsComplete()
    {
        var code = File.ReadAllText(Path.Combine(
            FindWorkspaceRoot(),
            "Nikkiward",
            "Controls",
            "HolographicBackdropOverlay.cs"));
        var backdrop = File.ReadAllText(Path.Combine(
            FindWorkspaceRoot(),
            "Nikkiward",
            "Features",
            "Background",
            "ArtBackdropView.xaml.cs"));
        var appearance = File.ReadAllText(Path.Combine(
            FindWorkspaceRoot(),
            "Nikkiward",
            "MainPage.Appearance.cs"));

        AssertContains(code, "ResetVisualState();", "unload reset owner");
        AssertContains(code, "_pointerActive = false;", "pointer reset");
        AssertContains(code, "_targetStrength = 0d;", "target render reset");
        AssertContains(code, "_renderStrength = 0d;", "render strength reset");
        AssertContains(code, "_introEnvelope = 0d;", "intro render reset");
        AssertContains(code, "Scale = Vector3.One;", "scale reset");
        AssertContains(code, "Translation = Vector3.Zero;", "translation reset");
        AssertContains(code, "_animationTimer.Tick -= OnAnimationTick;", "timer event release");
        AssertContains(
            backdrop,
            "ArtSharpHost.Projection = _stillArtProjection;",
            "shared card projection owner");
        Assert(
            !backdrop.Contains("ArtSharp.Projection = _stillArtProjection;", StringComparison.Ordinal),
            "static artwork must not own a projection that excludes the live Wallpaper Engine frame");
        AssertContains(
            backdrop,
            "CaptureStillSourceDimensions(ArtSharp.Source);",
            "preview dimension capture");
        AssertContains(
            backdrop,
            "var target = _wallpaperEnginePresentation == WallpaperEnginePresentation.HolographicCard",
            "card capture bounds target");
        AssertContains(
            appearance,
            "SetCurrentBackgroundSource(previewPath);",
            "Wallpaper preview source assignment");
        return Task.CompletedTask;
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nikkiward", "Nikkiward.csproj")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Nikkiward workspace root was not found.");
    }

    private static void AssertContains(string text, string expected, string message)
    {
        Assert(text.Contains(expected, StringComparison.Ordinal), $"{message}: {expected}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message}: expected '{expected}', actual '{actual}'.");
        }
    }

    private static void AssertNear(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.000001)
        {
            throw new InvalidOperationException(
                $"{message}: expected '{expected:R}', actual '{actual:R}'.");
        }
    }
}
