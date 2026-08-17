using Nikkiward.Models;

namespace Nikkiward.Features.Shell;

public enum GlassTier
{
    MotionBlur,
    MotionScrim,
    StillBlur,
    StillScrim,
    Flat,
}

public readonly record struct GlassSignals(
    bool HighContrast,
    bool UserWantsLiveBlur,
    bool AdvancedEffectsEnabled,
    bool BlurConstructionFailed,
    bool EnergySaverOn,
    bool RemoteSession,
    bool LowFrameRateMeasured,
    bool UserWantsMotion,
    AppearanceMotionMode Motion,
    bool AnimationsEnabled,
    bool WindowOccluded,
    bool MotionBackdropSamplingSupported);

public static class GlassTierResolver
{
    public static GlassTier Resolve(GlassSignals signals)
    {
        if (signals.HighContrast)
        {
            return GlassTier.Flat;
        }

        var blurAllowed = signals.UserWantsLiveBlur &&
            signals.AdvancedEffectsEnabled &&
            !signals.BlurConstructionFailed &&
            !signals.EnergySaverOn &&
            !signals.RemoteSession &&
            !signals.LowFrameRateMeasured;
        var motionAllowed = signals.UserWantsMotion &&
            signals.Motion != AppearanceMotionMode.Off &&
            signals.AnimationsEnabled &&
            !signals.EnergySaverOn &&
            !signals.RemoteSession &&
            !signals.WindowOccluded;

        if (motionAllowed)
        {
            return GlassTier.MotionScrim;
        }

        return blurAllowed ? GlassTier.StillBlur : GlassTier.StillScrim;
    }
}
