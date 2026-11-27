using Nikkiward.Models;

namespace Nikkiward.Features.Pages;

internal static class ScreenshotSection
{
    internal static readonly string[] Actions = ["change", "choose", "open", "test", "clear"];
    internal static object State(ScreenshotSettings s, string folder) => new {
        folder, canOpen = Directory.Exists(folder), hotkey = s.Hotkey, format = s.Format.ToString(),
        quality = s.Quality.ToString(), color = s.EnableColorManagement,
        copy = s.AutoCopyToClipboard, sdr = s.AutoConvertHdrToSdr
    };

    internal static ScreenshotSettings Change(ScreenshotSettings s, string field, string value) => field switch
    {
        "format" => s with { Format = SettingsValues.Choice<ScreenshotImageFormat>(value) },
        "quality" when s.Format != ScreenshotImageFormat.Png => s with { Quality = SettingsValues.Choice<ScreenshotImageQuality>(value) },
        "color" => s with { EnableColorManagement = SettingsValues.Toggle(value) },
        "copy" => s with { AutoCopyToClipboard = SettingsValues.Toggle(value) },
        "sdr" => s with { AutoConvertHdrToSdr = SettingsValues.Toggle(value) },
        _ => throw SettingsValues.Invalid()
    };
}
