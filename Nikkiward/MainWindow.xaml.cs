using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Nikkiward.Features.Background;
using Nikkiward.Models;
using Nikkiward.Services;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace Nikkiward;

public sealed partial class MainWindow : Window
{
    private const int DesignWidth = 1440;
    private const int DesignHeight = 810;

    private const double TitleBarStripHeight = 48;
    private const double NavigationRailWidth = 56;
    private const double CaptionButtonsWidth = 160;

    private InputNonClientPointerSource? _nonClientPointerSource;
    private readonly NativeWindowRuntime _nativeWindowRuntime;
    private MainPage? _mainPage;
    private CloseWindowBehavior _closeWindowBehavior = CloseWindowBehavior.Exit;
    private bool _allowClose;

    public event EventHandler<NativeWindowActivationChangedEventArgs>? ForegroundActivationChanged
    {
        add => _nativeWindowRuntime.ActivationChanged += value;
        remove => _nativeWindowRuntime.ActivationChanged -= value;
    }

    public bool IsForegroundActive => _nativeWindowRuntime.IsApplicationActive;

    public MainWindow()
    {
        InitializeComponent();

        Title = "Nikkiward";
        ExtendsContentIntoTitleBar = true;

        // Deliberately not SetTitleBar(AppTitleBar): a XAML drag element wins
        // over Passthrough regions, so page headers drawn in the same strip
        // become undraggable-but-unclickable. The Caption region is declared
        // from geometry below instead, carved around those headers.

        AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.IconShowOptions = IconShowOptions.ShowIconAndSystemMenu;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.TitleBar.BackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.InactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonPressedBackgroundColor = Colors.Transparent;
        ApplyCaptionButtonPolarity(ArtPreferredTheme.Dark);
        AppWindow.Closing += OnAppWindowClosing;

        try
        {
            _nonClientPointerSource =
                InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
        }
        catch
        {
            _nonClientPointerSource = null;
        }

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "NikkiwardIcon.ico"));
        CenterAtDesignSize();

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _nativeWindowRuntime = new NativeWindowRuntime(windowHandle);
        _nativeWindowRuntime.ShowWindowRequested += OnNativeShowWindowRequested;
        _nativeWindowRuntime.ExitRequested += OnNativeExitRequested;
        _nativeWindowRuntime.ScreenshotRequested += OnNativeScreenshotRequested;
        Closed += OnWindowClosed;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = true;
            presenter.IsResizable = true;
        }

        RootFrame.Navigate(typeof(MainPage));
        if (RootFrame.Content is MainPage mainPage)
        {
            _mainPage = mainPage;
            mainPage.TitleBarPassthroughChanged += OnTitleBarPassthroughChanged;
            RootFrame.SizeChanged += OnRootFrameSizeChanged;
        }
    }

    public void ApplyCaptionButtonPolarity(ArtPreferredTheme artworkTheme)
    {
        var foreground = artworkTheme == ArtPreferredTheme.Dark
            ? Windows.UI.Color.FromArgb(0xFF, 0xF7, 0xF2, 0xEA)
            : Windows.UI.Color.FromArgb(0xFF, 0x24, 0x1E, 0x1B);
        try
        {
            if (new AccessibilitySettings().HighContrast)
            {
                foreground = new UISettings().GetColorValue(UIColorType.Foreground);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
        }

        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = foreground;
        AppWindow.TitleBar.ButtonHoverForegroundColor = foreground;
        AppWindow.TitleBar.ButtonPressedForegroundColor = foreground;
    }

    public void ApplyCloseBehavior(CloseWindowBehavior behavior)
    {
        _closeWindowBehavior = behavior;
        _nativeWindowRuntime.SetTrayEnabled(
            behavior is CloseWindowBehavior.MinimizeToTray);
    }

    public HotkeyRegistrationResult ApplyHotkeys(
        string showWindowHotkey,
        string screenshotHotkey) =>
        _nativeWindowRuntime.ApplyHotkeys(showWindowHotkey, screenshotHotkey);

    public void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || _closeWindowBehavior is CloseWindowBehavior.Exit)
        {
            return;
        }

        args.Cancel = true;
        _nativeWindowRuntime.SetTrayEnabled(true);
        AppWindow.Hide();
    }

    private void OnNativeShowWindowRequested(object? sender, EventArgs e) =>
        ShowWindowCore();

    private void OnNativeExitRequested(object? sender, EventArgs e) =>
        ExitApplication();

    private void OnNativeScreenshotRequested(object? sender, EventArgs e) =>
        _mainPage?.CaptureGameScreenshotFromHotkey();

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _nativeWindowRuntime.ShowWindowRequested -= OnNativeShowWindowRequested;
        _nativeWindowRuntime.ExitRequested -= OnNativeExitRequested;
        _nativeWindowRuntime.ScreenshotRequested -= OnNativeScreenshotRequested;
        _nativeWindowRuntime.Dispose();
    }

    private void OnTitleBarPassthroughChanged(object? sender, EventArgs e)
    {
        UpdateTitleBarPassthroughRegion();
    }

    private void OnRootFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTitleBarPassthroughRegion();
    }

    /// <summary>
    /// Carves every interactive element the active page places inside the drag
    /// strip out of the non-client area. Without this the title bar swallows
    /// their clicks; the page header lives in that strip so the chrome above
    /// content stays a single 48px row instead of a title bar plus a command bar.
    /// </summary>
    private void UpdateTitleBarPassthroughRegion()
    {
        if (_nonClientPointerSource is null || _mainPage is null)
        {
            return;
        }

        var rects = new List<RectInt32>();
        foreach (var region in _mainPage.TitleBarPassthroughRegions)
        {
            if (region.XamlRoot is null ||
                region.Visibility != Visibility.Visible ||
                region.ActualWidth <= 0 ||
                region.ActualHeight <= 0)
            {
                continue;
            }

            var scale = region.XamlRoot.RasterizationScale;
            var bounds = region.TransformToVisual(null).TransformBounds(
                new Windows.Foundation.Rect(
                    0,
                    0,
                    region.ActualWidth,
                    region.ActualHeight));
            var left = checked((int)Math.Floor(bounds.X * scale));
            var top = checked((int)Math.Floor(bounds.Y * scale));
            var right = checked((int)Math.Ceiling((bounds.X + bounds.Width) * scale));
            var bottom = checked((int)Math.Ceiling((bounds.Y + bounds.Height) * scale));
            if (right <= left || bottom <= top)
            {
                continue;
            }

            rects.Add(new RectInt32(left, top, right - left, bottom - top));
        }

        _nonClientPointerSource.SetRegionRects(
            NonClientRegionKind.Passthrough,
            rects.Count == 0 ? null : [.. rects]);

        UpdateCaptionRegion(rects);
    }

    /// <summary>
    /// Declares the draggable strip around the navigation rail, caption
    /// buttons, and page-owned interactive regions.
    /// </summary>
    private void UpdateCaptionRegion(List<RectInt32> passthrough)
    {
        if (_nonClientPointerSource is null || _mainPage?.XamlRoot is null)
        {
            return;
        }

        var scale = _mainPage.XamlRoot.RasterizationScale;
        var stripHeight = checked((int)Math.Ceiling(TitleBarStripHeight * scale));
        var leftReserve = checked((int)Math.Ceiling(NavigationRailWidth * scale));
        var windowWidth = AppWindow.ClientSize.Width;
        var captionWidth = checked((int)Math.Ceiling(CaptionButtonsWidth * scale));
        var right = Math.Max(leftReserve, windowWidth - captionWidth);
        if (right <= leftReserve)
        {
            _nonClientPointerSource.SetRegionRects(NonClientRegionKind.Caption, null);
            return;
        }

        // Walk left to right, emitting the gaps between interactive regions.
        var blockers = passthrough
            .Where(rect => rect.Y < stripHeight && rect.Y + rect.Height > 0)
            .OrderBy(rect => rect.X)
            .ToList();

        var caption = new List<RectInt32>();
        var cursor = leftReserve;
        foreach (var blocker in blockers)
        {
            var blockerLeft = Math.Clamp(blocker.X, leftReserve, right);
            if (blockerLeft > cursor)
            {
                caption.Add(new RectInt32(cursor, 0, blockerLeft - cursor, stripHeight));
            }

            cursor = Math.Max(cursor, Math.Clamp(blocker.X + blocker.Width, leftReserve, right));
        }

        if (cursor < right)
        {
            caption.Add(new RectInt32(cursor, 0, right - cursor, stripHeight));
        }

        _nonClientPointerSource.SetRegionRects(
            NonClientRegionKind.Caption,
            caption.Count == 0 ? null : [.. caption]);
    }

    private void CenterAtDesignSize()
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = Math.Max(1d, GetDpiForWindow(windowHandle) / 96d);
        var width = checked((int)Math.Round(DesignWidth * scale));
        var height = checked((int)Math.Round(DesignHeight * scale));
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var x = workArea.X + (workArea.Width - width) / 2;
        var y = workArea.Y + (workArea.Height - height) / 2;

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    /// <summary>
    /// Brings the shell forward in response to a gamepad gesture. Called on the
    /// UI thread from the gamepad controller.
    /// </summary>
    public void ShowByGamepad()
    {
        ShowWindowCore();
    }

    private void ShowWindowCore()
    {
        try
        {
            AppWindow.Show();
            if (AppWindow.Presenter is OverlappedPresenter presenter
                && presenter.State is OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }

            Activate();

            // Activate alone does not steal focus from a full-screen game.
            SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }
        catch
        {
            // A foreground-lock refusal is the expected failure here and leaves
            // the window shown but behind the game; nothing to recover.
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
