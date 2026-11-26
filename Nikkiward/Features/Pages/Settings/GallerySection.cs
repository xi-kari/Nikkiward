using Nikkiward.Features.Settings;

namespace Nikkiward.Features.Pages;

internal static class GallerySection
{
    internal static readonly string[] Actions = ["refresh", "choose", "reset", "open", "exports", "change", "chooseProtection",
        "openProtection", "verify", "clean", "clearCache", "associate", "openTool", "disconnect"];

    internal static bool Allowed(string op, GallerySettingsViewState s) => !s.IsBusy && (op switch {
        "refresh" => s.CanRefreshCache,
        "choose" => s.CanChooseRoot,
        "reset" => s.CanChooseRoot && s.CanResetRoot,
        "open" or "exports" => s.CanOpenGallery,
        "change" or "chooseProtection" => s.CanChangeProtection,
        "openProtection" => s.CanOpenProtectionRoot,
        "verify" => s.CanVerifyProtection,
        "clean" => s.CanCleanProtection,
        "clearCache" => s.CanClearCache,
        "associate" => s.CanRegisterNikkiGallery,
        "openTool" => s.CanOpenNikkiGallery,
        "disconnect" => s.CanDisconnectNikkiGallery,
        _ => false
    });

    internal static bool ProtectionValue(string field, string value) => field == "protection"
        ? SettingsValues.Toggle(value) : throw SettingsValues.Invalid();

    internal static object State(GallerySettingsViewState s, string exportRoot, bool loaded, bool busy, string profile) => new {
        root = s.GalleryRootText, customRoot = s.CanResetRoot, profile, exportRoot, enabled = s.ProtectionEnabled, protection = s.ProtectionPathText,
        protectionStatus = s.ProtectionStatusText, cache = s.CacheStatusText, cachePath = s.CachePathText,
        tool = s.NikkiGalleryPathText, toolStatus = s.NikkiGalleryStatusText, loaded,
        actions = Actions.Where(op => !busy && (loaded || op == "refresh") && Allowed(op, s)).ToArray()
    };
}
