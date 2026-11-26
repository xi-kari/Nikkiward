using Nikkiward.Features.Settings;

namespace Nikkiward.Features.Pages;

internal sealed record SettingsSection(string Id, string Label, SettingsDestination Destination, bool Developer);

internal static class SettingsSections
{
    internal static readonly SettingsSection[] All = [
        new("appearance", "外观", SettingsDestination.General, false),
        new("general", "通用", SettingsDestination.Common, false),
        new("Download", "下载", SettingsDestination.Download, false),
        new("FileManagement", "文件管理", SettingsDestination.FileManagement, false),
        new("Screenshot", "游戏截图", SettingsDestination.Screenshot, false),
        new("Gallery", "相册", SettingsDestination.Gallery, false),
        new("Plugins", "插件", SettingsDestination.Plugins, false),
        new("Hotkeys", "键盘快捷键", SettingsDestination.Hotkeys, false),
        new("Gamepad", "手柄增强", SettingsDestination.Gamepad, false),
        new("Advanced", "高级", SettingsDestination.Advanced, false),
        new("Journal", "奇想手账与缓存", SettingsDestination.Journal, true),
        new("Files", "本地资源", SettingsDestination.Files, true),
        new("Status", "运行状态", SettingsDestination.Status, true),
        new("Components", "组件验证", SettingsDestination.Components, true),
        new("Diagnostics", "脱敏诊断", SettingsDestination.Diagnostics, true),
        new("Contract", "隔离契约", SettingsDestination.Contract, true),
        new("about", "关于", SettingsDestination.About, false)
    ];

    internal static SettingsDestination NativeTarget(SettingsSection section, IReadOnlyDictionary<string, PageAdapter> web) =>
        web.ContainsKey(section.Id) ? throw SettingsValues.Invalid() : section.Destination;

    internal static SettingsSection Resolve(string id, bool developer) =>
        All.FirstOrDefault(s => s.Id == id && (!s.Developer || developer)) ?? throw SettingsValues.Invalid();
}
