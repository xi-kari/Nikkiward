using Nikkiward.Controls;

internal static class CardBorderGlowProjectionTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("border glow projection is quiet at card center", CenterIsQuiet),
        ("border glow projection follows each cardinal edge", CardinalEdgesSetDirection),
        ("border glow projection preserves the sensitivity threshold", SensitivityThresholdIsStable),
        ("border glow projection clamps outside and invalid input", InputBoundsAreStable),
        ("border glow unload resets pointer depth and render state", UnloadContractIsComplete),
    ];

    private static Task CenterIsQuiet()
    {
        var state = CardBorderGlowProjection.Project(200, 100, 100, 50, 30);
        AssertNear(0, state.EdgeProximity, "center proximity");
        AssertNear(0, state.AngleDegrees, "center angle");
        AssertNear(0, state.GlowOpacity, "center glow");
        AssertNear(0, state.ColorOpacity, "center color");
        return Task.CompletedTask;
    }

    private static Task CardinalEdgesSetDirection()
    {
        var top = CardBorderGlowProjection.Project(200, 100, 100, 0, 30);
        var right = CardBorderGlowProjection.Project(200, 100, 200, 50, 30);
        var bottom = CardBorderGlowProjection.Project(200, 100, 100, 100, 30);
        var left = CardBorderGlowProjection.Project(200, 100, 0, 50, 30);

        AssertNear(0, top.AngleDegrees, "top angle");
        AssertNear(90, right.AngleDegrees, "right angle");
        AssertNear(180, bottom.AngleDegrees, "bottom angle");
        AssertNear(270, left.AngleDegrees, "left angle");
        foreach (var state in new[] { top, right, bottom, left })
        {
            AssertNear(1, state.EdgeProximity, "edge proximity");
            AssertNear(1, state.GlowOpacity, "edge glow");
            AssertNear(1, state.ColorOpacity, "edge color");
        }

        return Task.CompletedTask;
    }

    private static Task SensitivityThresholdIsStable()
    {
        var threshold = CardBorderGlowProjection.Project(200, 100, 130, 50, 30);
        var nearEdge = CardBorderGlowProjection.Project(200, 100, 180, 50, 30);

        AssertNear(0.3, threshold.EdgeProximity, "threshold proximity");
        AssertNear(0, threshold.GlowOpacity, "threshold glow");
        AssertNear(0, threshold.ColorOpacity, "threshold color");
        AssertNear(5d / 7d, nearEdge.GlowOpacity, "near-edge glow");
        AssertNear(0.6, nearEdge.ColorOpacity, "near-edge color");
        return Task.CompletedTask;
    }

    private static Task InputBoundsAreStable()
    {
        var outside = CardBorderGlowProjection.Project(200, 100, 400, 50, 30);
        var invalid = CardBorderGlowProjection.Project(0, 100, 0, 0, 30);

        AssertNear(1, outside.EdgeProximity, "outside proximity");
        AssertNear(1, outside.GlowOpacity, "outside glow");
        AssertNear(0, invalid.EdgeProximity, "invalid proximity");
        AssertNear(0, invalid.GlowOpacity, "invalid glow");
        return Task.CompletedTask;
    }

    private static Task UnloadContractIsComplete()
    {
        var code = File.ReadAllText(Path.Combine(
            FindWorkspaceRoot(),
            "Nikkiward",
            "Controls",
            "CardBorderGlow.cs"));

        AssertContains(code, "ResetVisualState();", "unload reset owner");
        AssertContains(code, "_pointerState = default;", "pointer projection reset");
        AssertContains(code, "Scale = Vector3.One;", "scale reset");
        AssertContains(code, "Translation = Vector3.Zero;", "translation reset");
        AssertContains(code, "DepthTarget.Translation = Vector3.Zero;", "depth target reset");
        AssertContains(code, "_renderGlowOpacity = 0d;", "glow render reset");
        AssertContains(code, "_animationTimer.Tick -= OnAnimationTick;", "timer event release");
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
        if (!text.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message}: {expected}");
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
