using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Nikkiward.Models;
using Nikkiward.Services;
using Windows.Graphics;

namespace Nikkiward;

public sealed partial class MainWindow
{
    internal void RestorePlacement(WindowPlacementSettings? saved)
    {
        var initial = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var displays = new List<WindowDisplay> { ReadDisplay(initial) };
        // Index the result: enumerating DisplayArea.FindAll() with foreach throws
        // InvalidCastException (E_NOINTERFACE) in the C# projection.
        var areas = DisplayArea.FindAll();
        for (var i = 0; i < areas.Count; i++)
        {
            var area = areas[i];
            if (area.DisplayId.Value != initial.DisplayId.Value)
            {
                displays.Add(ReadDisplay(area));
            }
        }

        const int smCxMinTrack = 34, smCyMinTrack = 35;
        var minimum = new Size(GetSystemMetricsForDpi(smCxMinTrack, 96), GetSystemMetricsForDpi(smCyMinTrack, 96));
        var (bounds, maximized) = WindowPlacement.Resolve(
            displays, saved, new Size(DesignWidth, DesignHeight), minimum);
        // The window is created on whichever display Windows picks. Moving it onto a
        // display with another DPI makes Windows rescale it, so apply the rectangle a
        // second time once the window already sits on the target display.
        var rect = new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        AppWindow.MoveAndResize(rect);
        AppWindow.MoveAndResize(rect);
        if (maximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private Task SaveWindowPlacementAsync()
    {
        if (AppWindow.IsVisible)
        {
            _placementSave = SaveVisiblePlacementAsync();
        }

        return _placementSave;
    }

    private async Task SaveVisiblePlacementAsync()
    {
        try
        {
            if (AppWindow.Presenter is not OverlappedPresenter presenter)
            {
                return;
            }

            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
            var display = ReadDisplay(area);
            var position = AppWindow.Position;
            var size = AppWindow.Size;
            var bounds = new Rectangle(position.X, position.Y, size.Width, size.Height);
            var state = presenter.State;
            var maximized = state == OverlappedPresenterState.Maximized ||
                (state == OverlappedPresenterState.Minimized && _nativeWindowRuntime.LastNonMinimizedWasMaximized);
            if (state != OverlappedPresenterState.Restored)
            {
                var placement = new NativePlacement { Length = (uint)Marshal.SizeOf<NativePlacement>() };
                if (!GetWindowPlacement(WinRT.Interop.WindowNative.GetWindowHandle(this), ref placement))
                {
                    return;
                }

                // WINDOWPLACEMENT uses workspace coordinates for a normal top-level window.
                bounds = Rectangle.FromLTRB(placement.Left, placement.Top, placement.Right, placement.Bottom);
                bounds.Offset(display.WorkArea.X - area.OuterBounds.X, display.WorkArea.Y - area.OuterBounds.Y);
            }

            if (bounds.Width > 0 && bounds.Height > 0)
            {
                await App.Settings.SaveWindowPlacementAsync(new WindowPlacementSettings(
                    bounds.X, bounds.Y, bounds.Width, bounds.Height,
                    display.WorkArea.X, display.WorkArea.Y, display.Dpi, maximized));
            }
        }
        catch (Exception ex)
        {
            _mainPage?.ViewModel.ReportUiError($"窗口位置保存失败：{ex.Message}");
        }
    }

    private static WindowDisplay ReadDisplay(DisplayArea area)
    {
        // WorkArea is already in virtual-screen coordinates, like OuterBounds (the
        // API docs say otherwise; a monitor at (-3360,-15) reports WorkArea.X = -3360).
        var work = area.WorkArea;
        var monitor = Win32Interop.GetMonitorFromDisplayId(area.DisplayId);
        var dpi = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0 ? (int)dpiX : 96;
        return new WindowDisplay(new Rectangle(work.X, work.Y, work.Width, work.Height), dpi);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement
    {
        public uint Length, Flags, ShowCommand;
        public Point MinPosition, MaxPosition;
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr window, ref NativePlacement placement);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
