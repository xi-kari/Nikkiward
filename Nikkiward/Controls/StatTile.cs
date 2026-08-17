using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using Nikkiward.Features.Shell;

namespace Nikkiward.Controls;

/// <summary>
/// Presents one labeled statistic with the shared editorial number treatment.
/// </summary>
public sealed class StatTile : Control
{
    private const string NoDataText = "暂无数据";
    private const int EntranceSequenceLength = 5;
    private static long s_entranceSequence;
    private bool _hasPresentedValue;

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(string),
            typeof(StatTile),
            new PropertyMetadata(NoDataText, OnValueChanged));

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label),
            typeof(string),
            typeof(StatTile),
            new PropertyMetadata(string.Empty));

    public StatTile()
    {
        DefaultStyleKey = typeof(StatTile);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, NormalizeValue(value));
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value ?? string.Empty);
    }

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var tile = (StatTile)sender;
        var normalized = NormalizeValue(args.NewValue as string);
        if (!string.Equals(normalized, args.NewValue as string, StringComparison.Ordinal))
        {
            tile.SetValue(ValueProperty, normalized);
            return;
        }

        tile.AnimateValueChange(normalized);
    }

    private static string NormalizeValue(string? value) =>
        string.IsNullOrWhiteSpace(value) || value is "—" or "-"
            ? NoDataText
            : value.Trim();

    private void AnimateValueChange(string value)
    {
        if (!IsLoaded || value == NoDataText)
        {
            _hasPresentedValue |= value != NoDataText;
            return;
        }

        var duration = AppearanceRuntimeValues.ReadDuration("MotionStandard");
        var visual = ElementCompositionPreview.GetElementVisual(this);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Translation");
        if (duration <= TimeSpan.Zero)
        {
            Opacity = 1;
            Translation = System.Numerics.Vector3.Zero;
            _hasPresentedValue = true;
            return;
        }

        var delay = TimeSpan.Zero;
        if (!_hasPresentedValue)
        {
            var sequenceIndex = (int)(Interlocked.Increment(ref s_entranceSequence) - 1) %
                EntranceSequenceLength;
            delay = TimeSpan.FromTicks(duration.Ticks * sequenceIndex / EntranceSequenceLength);
        }

        _hasPresentedValue = true;
        var compositor = visual.Compositor;
        var easing = AppearanceRuntimeValues.CreateCubicBezierEasingFunction(
            compositor,
            "MotionDecelerate");
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0f, 0f);
        opacity.InsertKeyFrame(1f, 1f, easing);
        opacity.Duration = duration;
        opacity.DelayTime = delay;
        visual.StartAnimation("Opacity", opacity);

        var translation = compositor.CreateVector3KeyFrameAnimation();
        translation.InsertKeyFrame(0f, new System.Numerics.Vector3(0f, 8f, 0f));
        translation.InsertKeyFrame(1f, System.Numerics.Vector3.Zero, easing);
        translation.Duration = duration;
        translation.DelayTime = delay;
        visual.StartAnimation("Translation", translation);
    }
}
