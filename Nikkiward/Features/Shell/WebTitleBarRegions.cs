using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nikkiward.Features.Shell;

/// <summary>
/// The interactive parts of a web page's top strip, carved out of the window's drag area.
/// Until the page reports where its controls are, the whole strip stays clickable so no
/// button is swallowed by the title bar; afterwards only those controls are, and the rest
/// of the strip moves the window.
/// </summary>
internal sealed class WebTitleBarRegions
{
    private const double StripHeight = 44;
    private readonly Border _fallback = new() { Height = StripHeight, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly Border _first = Region(), _second = Region();
    private bool _measured;

    internal IReadOnlyList<FrameworkElement> Elements =>
        _measured ? new FrameworkElement[] { _first, _second }.Where(region => region.Width > 0).ToArray() : [_fallback];

    internal void AddTo(Panel panel)
    {
        panel.Children.Add(_fallback);
        panel.Children.Add(_first);
        panel.Children.Add(_second);
    }

    /// <summary>
    /// Takes the page's <c>window.mastheadBounds()</c> result: left and right edges of up to two
    /// control groups, in page pixels. Returns false and keeps the previous regions for anything else.
    /// </summary>
    internal bool Apply(string? json)
    {
        if (string.IsNullOrEmpty(json) || json == "null") return false;
        double[]? edges;
        try { edges = JsonSerializer.Deserialize<double[]>(json); }
        catch (JsonException) { return false; }
        if (edges is not { Length: 2 or 4 } || edges.Any(edge => !double.IsFinite(edge)) || edges[1] <= edges[0]) return false;
        Place(_first, edges[0], edges[1]);
        if (edges.Length == 4 && edges[3] > edges[2]) Place(_second, edges[2], edges[3]);
        else _second.Width = 0;
        _measured = true;
        return true;
    }

    private static void Place(Border region, double left, double right)
    {
        region.Margin = new Thickness(Math.Max(0, left), 0, 0, 0);
        region.Width = Math.Max(0, right - Math.Max(0, left));
    }

    private static Border Region() => new()
    {
        Height = StripHeight, Width = 0, HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
    };
}
