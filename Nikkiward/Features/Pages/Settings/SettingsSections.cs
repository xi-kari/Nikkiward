namespace Nikkiward.Features.Pages;

internal sealed record SettingsSection(string Id, string Label, bool Developer);

internal static class SettingsSections
{
    internal static readonly SettingsSection[] All = [
        new("appearance", "外观", false),
        new("general", "通用", false),
        new("Download", "下载", false),
        new("FileManagement", "文件管理", false),
        new("Screenshot", "游戏截图", false),
        new("Gallery", "相册", false),
        new("Plugins", "插件", false),
        new("Hotkeys", "键盘快捷键", false),
        new("Gamepad", "手柄增强", false),
        new("Advanced", "高级", false),
        new("Journal", "奇想手账与缓存", true),
        new("Files", "本地资源", true),
        new("Status", "运行状态", true),
        new("Components", "组件验证", true),
        new("Diagnostics", "脱敏诊断", true),
        new("Contract", "隔离契约", true),
        new("about", "关于", false)
    ];

    internal static SettingsSection Resolve(string id, bool developer) =>
        All.FirstOrDefault(s => s.Id == id && (!s.Developer || developer)) ?? throw SettingsValues.Invalid();
}
