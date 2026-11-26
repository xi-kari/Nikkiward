using System.Globalization;
using Nikkiward.Models;

namespace Nikkiward.Features.Pages;

internal static class AppearanceSection
{
    internal static readonly string[] Actions = ["change", "choose", "restore"];

    internal static object State(AppearanceSettings s)
    {
        var b = s.Background;
        return new {
            skin = s.HomeSkin, theme = s.ThemeMode.ToString(), adaptive = s.ThemeMode == ThemeMode.FollowArtwork && s.AccentMode == AccentColorMode.Adaptive, accentMode = s.AccentMode.ToString(),
            fixedAccent = s.CustomAccentArgb.HasValue ? "custom" : s.FixedAccent.ToString(), customAccent = $"#{AppearanceAccentPalette.ResolveFixed(s) & 0xffffff:X6}",
            serif = s.UseSerifTitles, motion = s.Motion.ToString(), density = s.Density.ToString(),
            delay = s.AppreciationDelaySeconds, recent = s.ShowRecentPhotos,
            preset = AppearanceBackgroundPresets.All.FirstOrDefault(p => p.Source == (b.SelectedSource ?? AppearanceBackgroundPresets.Preset1Source)).Id,
            source = b.SelectedSource, carousel = b.CarouselEnabled, interval = b.CarouselIntervalMinutes,
            parallax = b.ParallaxEnabled, hologram = b.HolographicCardEnabled, video = b.MotionEnabled,
            videoAvailable = File.Exists(b.MotionSource), blur = b.UseLiveBlur, glass = b.GlassIntensity,
            pan = b.MotionPanEnabled, zoom = b.MotionZoom
        };
    }

    internal static AppearanceSettings Change(AppearanceSettings s, string field, string value)
    {
        var b = s.Background;
        return field switch
        {
            "skin" when value is "Cinema" or "Atelier" or "Stitch" or "Journal" or "WishStar" => s with { HomeSkin = value },
            "theme" => s with { ThemeMode = SettingsValues.Choice<ThemeMode>(value) },
            "adaptive" => SettingsValues.Toggle(value)
                ? s with { ThemeMode = ThemeMode.FollowArtwork, AccentMode = AccentColorMode.Adaptive }
                : s with { ThemeMode = s.ThemeMode == ThemeMode.FollowArtwork ? ThemeMode.WarmDark : s.ThemeMode, AccentMode = AccentColorMode.Fixed },
            "accentMode" => s with { AccentMode = SettingsValues.Choice<AccentColorMode>(value) },
            "fixedAccent" => s with { FixedAccent = SettingsValues.Choice<FixedAccentColor>(value), AccentMode = AccentColorMode.Fixed, CustomAccentArgb = null },
            "customAccent" when value.Length == 7 && value[0] == '#' && uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb)
                => s with { AccentMode = AccentColorMode.Fixed, CustomAccentArgb = rgb | 0xff000000 },
            "serif" => s with { UseSerifTitles = SettingsValues.Toggle(value) },
            "motion" => s with { Motion = SettingsValues.Choice<AppearanceMotionMode>(value) },
            "density" => s with { Density = SettingsValues.Choice<InterfaceDensity>(value) },
            "delay" => s with { AppreciationDelaySeconds = SettingsValues.Integer(value, 0, 3600) },
            "recent" => s with { ShowRecentPhotos = SettingsValues.Toggle(value) },
            "preset" when AppearanceBackgroundPresets.TryGet(value, out var preset) => s with { Background = new() { SelectedSource = preset.Source } },
            "carousel" => s with { Background = b with { CarouselEnabled = SettingsValues.Toggle(value), MotionEnabled = value == "false" && b.MotionEnabled } },
            "interval" => s with { Background = b with { CarouselIntervalMinutes = SettingsValues.Integer(value, 1, 1440) } },
            "parallax" => s with { Background = b with { ParallaxEnabled = SettingsValues.Toggle(value) } },
            "hologram" => s with { Background = b with { HolographicCardEnabled = SettingsValues.Toggle(value) } },
            "video" when value == "false" || File.Exists(b.MotionSource) => s with { Background = b with { MotionEnabled = SettingsValues.Toggle(value), CarouselEnabled = value == "false" && b.CarouselEnabled } },
            "blur" => s with { Background = b with { UseLiveBlur = SettingsValues.Toggle(value) } },
            "glass" => s with { Background = b with { GlassIntensity = SettingsValues.Number(value, 0, 1) } },
            "pan" => s with { Background = b with { MotionPanEnabled = SettingsValues.Toggle(value) } },
            "zoom" => s with { Background = b with { MotionZoom = SettingsValues.Number(value, 1, 2.8) } },
            _ => throw SettingsValues.Invalid()
        };
    }
}
