using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Nikkiward.Features.Shell;

public static class AppearanceRuntimeValues
{
    public static TimeSpan ReadDuration(string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        return Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true &&
            value is Duration duration && duration.HasTimeSpan
                ? duration.TimeSpan
                : TimeSpan.Zero;
    }

    public static TimeSpan ReadMilliseconds(string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        return Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true &&
            value is double milliseconds &&
            milliseconds > 0
                ? TimeSpan.FromMilliseconds(milliseconds)
                : TimeSpan.Zero;
    }

    public static bool IsFullNavigationMotionEnabled() =>
        ReadDuration("MotionSurface") >= TimeSpan.FromMilliseconds(300);

    public static void RefreshTransitions(DependencyObject root)
    {
        if (root is UIElement element)
        {
            if (element.OpacityTransition is not null)
            {
                element.OpacityTransition.Duration = ReadMilliseconds("MotionStateDuration");
            }

            if (element.ScaleTransition is not null)
            {
                element.ScaleTransition.Duration = ReadDuration("MotionStandard");
            }

            if (element.TranslationTransition is not null)
            {
                element.TranslationTransition.Duration = ReadMilliseconds("MotionPanelOpen");
            }

        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            RefreshTransitions(VisualTreeHelper.GetChild(root, index));
        }
    }

    public static NavigationTransitionInfo CreateNavigationTransitionInfo() =>
        IsFullNavigationMotionEnabled()
            ? new EntranceNavigationTransitionInfo()
            : new SuppressNavigationTransitionInfo();
}
