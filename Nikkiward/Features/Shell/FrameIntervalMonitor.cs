using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace Nikkiward.Features.Shell;

internal sealed class FrameIntervalMonitor : IDisposable
{
    private const double WindowSeconds = 3;
    private const double SlowFrameThresholdMilliseconds = 22;
    private const int RequiredSlowWindows = 2;
    private const int MinimumSamplesPerWindow = 8;

    private readonly Action _onSustainedSlowFrames;
    private readonly List<double> _intervals = [];
    private long _previousTimestamp;
    private long _windowStartTimestamp;
    private int _consecutiveSlowWindows;
    private bool _enabled;
    private bool _reported;

    public FrameIntervalMonitor(Action onSustainedSlowFrames)
    {
        _onSustainedSlowFrames = onSustainedSlowFrames ??
            throw new ArgumentNullException(nameof(onSustainedSlowFrames));
    }

    public void SetEnabled(bool enabled)
    {
        if (_reported)
        {
            enabled = false;
        }

        if (_enabled == enabled)
        {
            return;
        }

        _enabled = enabled;
        if (enabled)
        {
            ResetWindow();
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            CompositionTarget.Rendering -= OnRendering;
            ResetWindow();
            _consecutiveSlowWindows = 0;
        }
    }

    public void Dispose() => SetEnabled(false);

    private void OnRendering(object? sender, object args)
    {
        var now = Stopwatch.GetTimestamp();
        if (_previousTimestamp != 0)
        {
            _intervals.Add(Stopwatch.GetElapsedTime(_previousTimestamp, now).TotalMilliseconds);
        }

        _previousTimestamp = now;
        if (_windowStartTimestamp == 0)
        {
            _windowStartTimestamp = now;
            return;
        }

        if (Stopwatch.GetElapsedTime(_windowStartTimestamp, now).TotalSeconds < WindowSeconds)
        {
            return;
        }

        EvaluateWindow();
        _windowStartTimestamp = now;
        _intervals.Clear();
    }

    private void EvaluateWindow()
    {
        if (_intervals.Count < MinimumSamplesPerWindow)
        {
            _consecutiveSlowWindows = 0;
            return;
        }

        _intervals.Sort();
        var index = Math.Clamp(
            (int)Math.Ceiling(_intervals.Count * 0.95) - 1,
            0,
            _intervals.Count - 1);
        var p95 = _intervals[index];
        _consecutiveSlowWindows = p95 > SlowFrameThresholdMilliseconds
            ? _consecutiveSlowWindows + 1
            : 0;
        if (_consecutiveSlowWindows < RequiredSlowWindows)
        {
            return;
        }

        _reported = true;
        SetEnabled(false);
        _onSustainedSlowFrames();
    }

    private void ResetWindow()
    {
        _previousTimestamp = 0;
        _windowStartTimestamp = 0;
        _intervals.Clear();
    }
}
