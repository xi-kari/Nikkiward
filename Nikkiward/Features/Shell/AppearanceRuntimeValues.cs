using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Nikkiward.Features.Shell;

public static class AppearanceRuntimeValues
{
    public const string GalleryPreviewAnimationKey = "gallery-photo-preview";

    public const string JournalDetailAnimationKey = "journal-module-detail";

    public static float ReadScale(string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        return Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true &&
            value is double scale
                ? (float)scale
                : 1f;
    }

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

    public static bool IsMotionEnabled(string resourceKey) =>
        ReadDuration(resourceKey) > TimeSpan.Zero;

    public static bool IsFullNavigationMotionEnabled() =>
        ReadDuration("MotionSurface") >= TimeSpan.FromMilliseconds(300);

    public static void ApplyOpacityTransition(
        UIElement element,
        string durationResourceKey = "MotionStateDuration")
    {
        element.OpacityTransition ??= new ScalarTransition();
        element.OpacityTransition.Duration = ReadMilliseconds(durationResourceKey);
    }

    public static void ApplyScaleTransition(UIElement element)
    {
        element.ScaleTransition ??= new Vector3Transition();
        element.ScaleTransition.Duration = ReadDuration("MotionStandard");
    }

    public static void ApplyTranslationTransition(UIElement element, string durationResourceKey)
    {
        element.TranslationTransition ??= new Vector3Transition();
        element.TranslationTransition.Duration = ReadMilliseconds(durationResourceKey);
    }

    public static CompositionEasingFunction CreateCubicBezierEasingFunction(
        Compositor compositor,
        string resourcePrefix)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);
        return compositor.CreateCubicBezierEasingFunction(
            new Vector2(
                (float)ReadDouble($"{resourcePrefix}X1", 0.16),
                (float)ReadDouble($"{resourcePrefix}Y1", 1.0)),
            new Vector2(
                (float)ReadDouble($"{resourcePrefix}X2", 0.3),
                (float)ReadDouble($"{resourcePrefix}Y2", 1.0)));
    }

    public static void StartOpacityAnimation(
        UIElement element,
        float from,
        float to,
        string durationResourceKey,
        string easingResourcePrefix)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        var duration = ReadMilliseconds(durationResourceKey);
        if (duration <= TimeSpan.Zero)
        {
            visual.Opacity = to;
            return;
        }

        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0f, from);
        animation.InsertKeyFrame(
            1f,
            to,
            CreateCubicBezierEasingFunction(visual.Compositor, easingResourcePrefix));
        animation.Duration = duration;
        visual.StartAnimation("Opacity", animation);
    }

    public static void ResetOpacityAnimation(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1f;
    }

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

    private static double ReadDouble(string resourceKey, double fallback) =>
        Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true &&
        value is double resolved
            ? resolved
            : fallback;
}
