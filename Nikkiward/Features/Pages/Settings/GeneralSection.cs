using System.Globalization;
using Nikkiward.Models;

namespace Nikkiward.Features.Pages;

internal static class GeneralSection
{
    internal static readonly string[] Actions = ["change", "clearTime", "effects"];

    internal static object State(GeneralSettings s) => new {
        language = s.LanguageTag, close = s.CloseWindowBehavior.ToString(), afterLaunch = s.AfterLaunchBehavior.ToString(),
        resetTime = s.ChecklistReset.ToString("HH:mm", CultureInfo.InvariantCulture), playTime = s.RecordLocalPlayTime
    };

    internal static GeneralSettings Change(GeneralSettings s, string field, string value) => field switch
    {
        "language" when value is "system" or "zh-CN" => s with { LanguageTag = value },
        "close" => s with { CloseWindowBehavior = SettingsValues.Choice<CloseWindowBehavior>(value) },
        "afterLaunch" => s with { AfterLaunchBehavior = SettingsValues.Choice<AfterLaunchBehavior>(value) },
        "resetTime" when TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) => s with { ChecklistReset = t },
        "playTime" => s with { RecordLocalPlayTime = SettingsValues.Toggle(value) },
        _ => throw SettingsValues.Invalid()
    };
}
