using Nikkiward.Models;

namespace Nikkiward.Features.Pages;

internal static class HotkeysSection
{
    internal static readonly string[] Actions = ["record"];
    internal static object State(GeneralSettings general, ScreenshotSettings screenshot) =>
        new { main = general.MainWindowHotkey, screenshot = screenshot.Hotkey };

    internal static (string Main, string Screenshot) Change(GeneralSettings general, ScreenshotSettings screenshot, string field, string value) =>
        field switch {
            "main" => (value, screenshot.Hotkey),
            "screenshot" => (general.MainWindowHotkey, value),
            _ => throw SettingsValues.Invalid()
        };
}
