using System.Runtime.InteropServices;
using Nikkiward.Models;
using Windows.System.Power;
using Windows.UI.ViewManagement;

namespace Nikkiward.Features.Shell;

public sealed class GlassCapabilities
{
    private const int SmRemoteSession = 0x1000;

    private bool _blurConstructionFailed;
    private bool _lowFrameRateMeasured;
    private bool _motionActive;
    private bool _windowOccluded;
    private AppearanceSettings _settings = new();

    private GlassCapabilities()
    {
        Refresh();
    }

    public static GlassCapabilities Current { get; } = new();

    public event EventHandler? TierChanged;

    public GlassTier Tier { get; private set; } = GlassTier.StillScrim;

    public bool AllowsLiveBlur => Tier == GlassTier.StillBlur;

    public double GlassIntensity => _settings.Background.GlassIntensity;

    public void Configure(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        Refresh();
    }

    public void RefreshPlatformState() => Refresh();

    public void SetMotionActive(bool active)
    {
        if (_motionActive == active)
        {
            return;
        }

        _motionActive = active;
        Refresh();
    }

    public void SetWindowOccluded(bool occluded)
    {
        if (_windowOccluded == occluded)
        {
            return;
        }

        _windowOccluded = occluded;
        Refresh();
    }

    public void ReportBlurFailure()
    {
        if (_blurConstructionFailed)
        {
            return;
        }

        _blurConstructionFailed = true;
        Refresh();
    }

    public void ReportLowFrameRate()
    {
        if (_lowFrameRateMeasured)
        {
            return;
        }

        _lowFrameRateMeasured = true;
        Refresh();
    }

    public GlassSignals ReadSignals()
    {
        var uiSettings = TryCreateUiSettings();
        return new GlassSignals(
            HighContrast: TryReadHighContrast(),
            UserWantsLiveBlur: _settings.Background.UseLiveBlur,
            AdvancedEffectsEnabled: TryReadAdvancedEffects(uiSettings),
            BlurConstructionFailed: _blurConstructionFailed,
            EnergySaverOn: TryReadEnergySaver(),
            RemoteSession: TryReadRemoteSession(),
            LowFrameRateMeasured: _lowFrameRateMeasured,
            UserWantsMotion: _motionActive && _settings.Background.MotionEnabled,
            Motion: _settings.Motion,
            AnimationsEnabled: TryReadAnimations(uiSettings),
            WindowOccluded: _windowOccluded,
            MotionBackdropSamplingSupported: false);
    }

    private void Refresh()
    {
        var resolved = GlassTierResolver.Resolve(ReadSignals());
        if (resolved == Tier)
        {
            return;
        }

        Tier = resolved;
        TierChanged?.Invoke(this, EventArgs.Empty);
    }

    private static UISettings? TryCreateUiSettings()
    {
        try
        {
            return new UISettings();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryReadAdvancedEffects(UISettings? settings)
    {
        try
        {
            return settings?.AdvancedEffectsEnabled ?? false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryReadAnimations(UISettings? settings)
    {
        try
        {
            return settings?.AnimationsEnabled ?? true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return true;
        }
    }

    private static bool TryReadHighContrast()
    {
        try
        {
            return new AccessibilitySettings().HighContrast;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryReadEnergySaver()
    {
        try
        {
            return PowerManager.EnergySaverStatus == EnergySaverStatus.On;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryReadRemoteSession()
    {
        try
        {
            return OperatingSystem.IsWindows() && GetSystemMetrics(SmRemoteSession) != 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
