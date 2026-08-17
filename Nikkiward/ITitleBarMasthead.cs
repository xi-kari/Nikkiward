using Microsoft.UI.Xaml;

namespace Nikkiward;

/// <summary>
/// Implemented by pages whose header row sits inside the window drag strip.
/// The shell carves the returned element out of the non-client area so its
/// controls stay clickable, which is what lets the chrome above page content
/// stay a single 48px row instead of a title bar stacked on a command bar.
/// </summary>
/// <remarks>
/// The element must keep its right edge clear of the caption buttons. The
/// reserve is <see cref="CaptionButtonReserve"/> logical pixels wide.
/// </remarks>
public interface ITitleBarMasthead
{
    /// <summary>
    /// Width reserved on the right of the drag strip for the minimize,
    /// maximize and close buttons. Page headers must not extend into it.
    /// </summary>
    public const double CaptionButtonReserve = 160d;

    /// <summary>
    /// The page's header row, or <see langword="null"/> when the page has not
    /// finished loading it.
    /// </summary>
    FrameworkElement? MastheadInteractionRegion { get; }

    /// <summary>
    /// Raised once the header row has a measured size, and again whenever that
    /// size changes.
    /// </summary>
    /// <remarks>
    /// The shell cannot know when a freshly navigated page finishes layout: a
    /// region that still measures zero registers nothing, and its controls are
    /// then swallowed by the drag strip while still looking correct on screen.
    /// The page owns that timing, so the page reports it.
    /// </remarks>
    event EventHandler? MastheadInteractionRegionChanged;
}
