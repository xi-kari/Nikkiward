using Nikkiward.Models;

namespace Nikkiward.Features.Pages;

internal static class GamepadSection
{
    internal static readonly string[] Actions = ["change", "runtime"];
    internal static object State(GamepadSettings s, string status, bool canEdit, bool runtimeMissing) => new {
        enabled = s.Enabled, longPress = s.GuideLongPressOpensMainWindow, guideAction = s.GuideAction.ToString(),
        shareAction = s.ShareAction.ToString(), guideKeys = s.GuideMapKeys, shareKeys = s.ShareMapKeys, status, canEdit, runtimeMissing
    };

    internal static GamepadSettings Change(GamepadSettings s, string field, string value, Func<string, string> parseKeys) => field switch
    {
        "enabled" => s with { Enabled = SettingsValues.Toggle(value) },
        "longPress" => s with { GuideLongPressOpensMainWindow = SettingsValues.Toggle(value) },
        "guideAction" => s with { GuideAction = SettingsValues.Choice<GamepadButtonAction>(value) },
        "shareAction" => s with { ShareAction = SettingsValues.Choice<GamepadButtonAction>(value) },
        "guideKeys" when s.GuideAction == GamepadButtonAction.MapKeys => s with { GuideMapKeys = parseKeys(value) },
        "shareKeys" when s.ShareAction == GamepadButtonAction.MapKeys => s with { ShareMapKeys = parseKeys(value) },
        _ => throw SettingsValues.Invalid()
    };
}
