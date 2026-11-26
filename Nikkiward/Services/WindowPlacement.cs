using System.Drawing;
using Nikkiward.Models;

namespace Nikkiward.Services;

internal readonly record struct WindowDisplay(Rectangle WorkArea, int Dpi);

internal static class WindowPlacement
{
    private const int FirstOpenMarginDip = 16;

    internal static (Rectangle Bounds, bool IsMaximized) Resolve(
        IReadOnlyList<WindowDisplay> displays,
        WindowPlacementSettings? saved,
        Size designSize,
        Size minimumSize)
    {
        // The first display contains the newly created window.
        var display = displays[0];
        long largestOverlap = 0;
        if (saved is { Width: > 0, Height: > 0, Dpi: > 0 })
        {
            foreach (var candidate in displays)
            {
                var work = candidate.WorkArea;
                var width = Math.Min((long)saved.X + saved.Width, (long)work.X + work.Width)
                    - Math.Max(saved.X, work.X);
                var height = Math.Min((long)saved.Y + saved.Height, (long)work.Y + work.Height)
                    - Math.Max(saved.Y, work.Y);
                if (width > 0 && height > 0 && width * height > largestOverlap)
                {
                    display = candidate;
                    largestOverlap = width * height;
                }
            }
        }

        var area = display.WorkArea;
        var scale = display.Dpi / 96d;
        if (largestOverlap == 0 || saved is null)
        {
            var margin = (int)Math.Round(FirstOpenMarginDip * scale);
            var width = Fit(designSize.Width * scale, minimumSize.Width * scale, area.Width, margin);
            var height = Fit(designSize.Height * scale, minimumSize.Height * scale, area.Height, margin);
            return (new Rectangle(area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2, width, height), false);
        }

        var ratio = (double)display.Dpi / saved.Dpi;
        var restoredWidth = Fit(saved.Width * ratio, minimumSize.Width * scale, area.Width, 0);
        var restoredHeight = Fit(saved.Height * ratio, minimumSize.Height * scale, area.Height, 0);
        var x = Math.Round(((long)saved.X - saved.WorkAreaX) * ratio);
        var y = Math.Round(((long)saved.Y - saved.WorkAreaY) * ratio);
        return (new Rectangle(area.X + (int)Math.Clamp(x, 0, area.Width - restoredWidth),
            area.Y + (int)Math.Clamp(y, 0, area.Height - restoredHeight), restoredWidth, restoredHeight),
            saved.IsMaximized);
    }

    private static int Fit(double requested, double minimum, int available, int margin)
    {
        var lower = Math.Clamp(Math.Ceiling(minimum), 1, available);
        return (int)Math.Clamp(Math.Round(requested), lower, Math.Max(lower, available - 2 * margin));
    }
}
