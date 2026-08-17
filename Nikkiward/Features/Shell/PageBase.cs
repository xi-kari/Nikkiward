using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Nikkiward.Features.Shell;

/// <summary>
/// Common page contract for shell-hosted surfaces.
/// </summary>
public abstract class PageBase : Page, ITitleBarMasthead
{
    public virtual string PageTitle => GetType().Name;

    public virtual UIElement? CommandBarContent => null;

    public virtual FrameworkElement? MastheadInteractionRegion => null;

    public event EventHandler? MastheadInteractionRegionChanged;

    public event EventHandler? Entered;

    public event EventHandler? Exiting;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        OnEntering(e);
        Entered?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        OnExiting(e);
        Exiting?.Invoke(this, EventArgs.Empty);
        base.OnNavigatedFrom(e);
    }

    protected virtual void OnEntering(NavigationEventArgs e)
    {
    }

    protected virtual void OnExiting(NavigationEventArgs e)
    {
    }

    protected void NotifyMastheadInteractionRegionChanged() =>
        MastheadInteractionRegionChanged?.Invoke(this, EventArgs.Empty);

    protected static ConnectedAnimation? PrepareConnectedAnimation(
        string key,
        UIElement source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(source);
        if (!AppearanceRuntimeValues.IsMotionEnabled("MotionArt"))
        {
            return null;
        }

        return ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(key, source);
    }

    protected static bool TryReceiveConnectedAnimation(
        string key,
        UIElement destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(destination);
        if (!AppearanceRuntimeValues.IsMotionEnabled("MotionArt"))
        {
            return false;
        }

        return ConnectedAnimationService.GetForCurrentView().GetAnimation(key)
            ?.TryStart(destination) == true;
    }
}
