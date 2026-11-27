using Nikkiward.Models;

namespace Nikkiward.Features.Pages;

internal static class DownloadSection
{
    internal static object State(DownloadSettings s) => new { path = s.DefaultGameInstallPath, hardLinks = s.EnableHardLinks, limit = s.SpeedLimitKbps };

    internal static DownloadSettings Change(DownloadSettings s, string field, string value) => field switch {
        "hardLinks" => s with { EnableHardLinks = SettingsValues.Toggle(value) },
        "limit" => s with { SpeedLimitKbps = SettingsValues.Integer(value, 0, 10_000_000) },
        _ => throw SettingsValues.Invalid()
    };
}
