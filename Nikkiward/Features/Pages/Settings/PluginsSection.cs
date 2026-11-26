using Nikkiward.ViewModels;

namespace Nikkiward.Features.Pages;

internal static class PluginsSection
{
    internal static readonly string[] Actions = ["import", "open", "uninstall"];
    internal static bool Allowed(string op, LocalPluginInstallation? item, bool busy) => !busy && (op switch {
        "import" => true,
        "open" => item?.IsInstalled == true,
        "uninstall" => item is { IsInstalled: true } or { IsBroken: true },
        _ => false
    });
    internal static object State(LocalPluginInstallation? item, bool busy) => new {
        status = item?.StatusText ?? "尚未安装", version = item?.Version, canImport = Allowed("import", item, busy),
        canOpen = Allowed("open", item, busy), canUninstall = Allowed("uninstall", item, busy)
    };
}
