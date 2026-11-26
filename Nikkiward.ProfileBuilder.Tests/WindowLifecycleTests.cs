using System.Drawing;
using Nikkiward.Models;
using Nikkiward.Services;

internal static class WindowLifecycleTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        (nameof(FirstOpenFitsSmallHighDpiScreen), FirstOpenFitsSmallHighDpiScreen),
        (nameof(NegativeSecondaryKeepsNormalBounds), NegativeSecondaryKeepsNormalBounds),
        (nameof(RemovedDisplayFallsBackToFirstOpen), RemovedDisplayFallsBackToFirstOpen),
        (nameof(SmallerWorkAreaContainsRestoredWindow), SmallerWorkAreaContainsRestoredWindow),
        (nameof(DpiChangeScalesSizeAndMonitorRelativeOffset), DpiChangeScalesSizeAndMonitorRelativeOffset),
        (nameof(MaximizedRestoresItsNormalRectangle), MaximizedRestoresItsNormalRectangle),
        (nameof(MinimizeKeepsPreviousMaximizedState), MinimizeKeepsPreviousMaximizedState),
        (nameof(MinimumSizeTakesPrecedenceOverMargin), MinimumSizeTakesPrecedenceOverMargin),
        (nameof(TrayFailureStillActivatesWindow), TrayFailureStillActivatesWindow),
        (nameof(OnlyKeyOwnerOpensWindow), OnlyKeyOwnerOpensWindow),
        (nameof(RestartWaitsOnlyForItsPredecessor), RestartWaitsOnlyForItsPredecessor),
        (nameof(OnlyExactRestartArgumentsRequestHandoff), OnlyExactRestartArgumentsRequestHandoff),
    ];

    private static readonly WindowDisplay Primary = new(new Rectangle(0, 0, 1920, 1040), 96);
    private static readonly WindowDisplay Left = new(new Rectangle(-1920, 40, 1920, 1040), 96);
    private static readonly Size Design = new(1440, 810);

    private static Task TrayFailureStillActivatesWindow()
    {
        var activated = false;
        string? error = null;
        var tray = new WindowTray(_ => throw new InvalidOperationException("Explorer unavailable"), message => error = message);
        tray.Open(CloseWindowBehavior.MinimizeToTray, () => activated = true);
        if (!activated || tray.CloseBehavior != CloseWindowBehavior.Exit || error?.Contains("Explorer unavailable") != true ||
            tray.TrySetEnabled(true))
        {
            throw new InvalidOperationException("lost window, fallback behavior or tray error");
        }
        return Task.CompletedTask;
    }

    private static Task FirstOpenFitsSmallHighDpiScreen() =>
        Check([Primary with { Dpi = 144 }], null, new Rectangle(24, 24, 1872, 992));

    private static Task NegativeSecondaryKeepsNormalBounds() =>
        Check([Primary, Left], new(-1810, 80, 1000, 600, -1920, 40, 96, false),
            new Rectangle(-1810, 80, 1000, 600));

    private static Task RemovedDisplayFallsBackToFirstOpen() =>
        Check([Primary], new(-1810, 80, 1000, 600, -1920, 40, 96, true),
            new Rectangle(240, 115, 1440, 810));

    private static Task SmallerWorkAreaContainsRestoredWindow() =>
        Check([new(new Rectangle(0, 50, 1024, 668), 96)],
            new(400, 250, 1300, 750, 0, 0, 96, false), new Rectangle(0, 50, 1024, 668));

    private static Task DpiChangeScalesSizeAndMonitorRelativeOffset() =>
        Check([Primary, Left with { Dpi = 144 }],
            new(-1820, 100, 800, 500, -1920, 40, 96, false), new Rectangle(-1770, 130, 1200, 750));

    private static Task MaximizedRestoresItsNormalRectangle() =>
        Check([Primary], new(180, 70, 900, 660, 0, 0, 96, true),
            new Rectangle(180, 70, 900, 660), maximized: true);

    private static Task MinimizeKeepsPreviousMaximizedState()
    {
        const nuint restored = 0, minimized = 1, maximized = 2;
        var state = NativeWindowRuntime.TrackMaximizedState(false, maximized);
        state = NativeWindowRuntime.TrackMaximizedState(state, minimized);
        if (!state)
        {
            throw new InvalidOperationException("Minimizing a maximized window must retain its maximized flag.");
        }

        state = NativeWindowRuntime.TrackMaximizedState(state, restored);
        if (NativeWindowRuntime.TrackMaximizedState(state, minimized))
        {
            throw new InvalidOperationException("Minimizing a restored window must retain its normal state.");
        }

        return Task.CompletedTask;
    }

    private static Task MinimumSizeTakesPrecedenceOverMargin() =>
        Check([new(new Rectangle(0, 0, 810, 620), 96)], null, new Rectangle(5, 10, 800, 600),
            minimum: new Size(800, 600));

    private static Task OnlyKeyOwnerOpensWindow()
    {
        if (InstanceActivation.Decide(true, 20, null) != InstanceAction.OpenWindow ||
            InstanceActivation.Decide(false, 20, null) != InstanceAction.Redirect ||
            InstanceActivation.Decide(false, null, null) != InstanceAction.Retry)
        {
            throw new InvalidOperationException("Only the registered owner may open a window.");
        }

        return Task.CompletedTask;
    }

    private static Task RestartWaitsOnlyForItsPredecessor()
    {
        if (InstanceActivation.Decide(false, 20, 20) != InstanceAction.Retry ||
            InstanceActivation.Decide(false, 30, 20) != InstanceAction.Redirect ||
            InstanceActivation.Decide(true, 40, 20) != InstanceAction.OpenWindow)
        {
            throw new InvalidOperationException("A restart must wait for the outgoing owner, then use the elected owner.");
        }

        return Task.CompletedTask;
    }

    private static Task OnlyExactRestartArgumentsRequestHandoff()
    {
        if (InstanceActivation.GetPredecessor(["--restart-from", "123"]) != 123)
        {
            throw new InvalidOperationException("The restart child must recognize its predecessor.");
        }

        string[][] ordinary = [[], ["--launch"], ["--restart-from"], ["--restart-from", "0"],
            ["--restart-from", "-1"], ["--restart-from", "4294967296"], ["--restart-from", "123", "extra"]];
        if (ordinary.Any(args => InstanceActivation.GetPredecessor(args) is not null))
        {
            throw new InvalidOperationException("Other command lines must follow ordinary window activation.");
        }

        return Task.CompletedTask;
    }

    private static Task Check(WindowDisplay[] displays, WindowPlacementSettings? saved, Rectangle expected,
        bool maximized = false, Size minimum = default)
    {
        var actual = WindowPlacement.Resolve(displays, saved, Design, minimum);
        if (actual != (expected, maximized))
        {
            throw new InvalidOperationException($"Expected ({expected}, {maximized}), got {actual}.");
        }

        return Task.CompletedTask;
    }
}
