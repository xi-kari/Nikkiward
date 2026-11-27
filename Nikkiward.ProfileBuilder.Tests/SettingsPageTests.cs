using Nikkiward.Features.Settings;
using System.Text.Json;
using Nikkiward.Features.Pages;
using Nikkiward.Features.Gallery;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;

internal static class SettingsPageTests
{
    internal static (string, Func<Task>)[] All => [
        (nameof(RealSettingsSectionStatesSerialize), RealSettingsSectionStatesSerialize),
        (nameof(ScreenshotSectionRoundTrip), ScreenshotSectionRoundTrip),
        (nameof(HotkeysSectionRoundTrip), HotkeysSectionRoundTrip),
        (nameof(GamepadSectionRoundTrip), GamepadSectionRoundTrip),
        (nameof(GallerySectionRoundTrip), GallerySectionRoundTrip),
        (nameof(PluginSectionRoundTrip), PluginSectionRoundTrip),
        (nameof(AppearanceRoundTrip), AppearanceRoundTrip),
        (nameof(ChannelToolsFollowDeveloperMode), ChannelToolsFollowDeveloperMode),
        (nameof(DeveloperSettingsReflectCurrentInspection), DeveloperSettingsReflectCurrentInspection),
        (nameof(DownloadLimitsRoundTrip), DownloadLimitsRoundTrip),
        (nameof(SettingsNavigationFollowsDeveloperMode), SettingsNavigationFollowsDeveloperMode),
        (nameof(GeneralRoundTrip), GeneralRoundTrip),
        (nameof(CustomAccentReturnsToPreset), CustomAccentReturnsToPreset),
        (nameof(AboutChannelState), AboutChannelState),
        (nameof(SettingsActionAllowlist), SettingsActionAllowlist),
        (nameof(WebSettingsSectionRoutes), WebSettingsSectionRoutes),
        (nameof(InvalidSettingsPreserveValues), InvalidSettingsPreserveValues)
    ];

    private static Task RealSettingsSectionStatesSerialize()
    {
        var model = new MainPageViewModel(new JsonUserSettingsStore(Path.GetTempPath()),
            new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
        var about = AboutSection.CreateLazy();
        var snapshot = JsonSerializer.SerializeToElement(new {
            appearance = AppearanceSection.State(model.AppearanceSettings),
            general = GeneralSection.State(model.GeneralSettings),
            about = about.Value.State()
        });
        Require(snapshot.GetProperty("appearance").GetProperty("skin").GetString() == model.AppearanceSettings.HomeSkin);
        Require(snapshot.GetProperty("general").GetProperty("language").GetString() == model.GeneralSettings.LanguageTag);
        Require(snapshot.GetProperty("about").GetProperty("available").GetBoolean());
        return Task.CompletedTask;
    }

    private static async Task AppearanceRoundTrip()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new JsonUserSettingsStore(folder.FullName);
            var settings = await store.LoadAsync();
            var initial = settings.Appearance with { HomeSkin = "Journal" };
            await store.SaveAsync(settings with { Appearance = AppearanceSection.Change(initial, "delay", "30") });
            var saved = await store.LoadAsync();
            var state = JsonSerializer.SerializeToElement(AppearanceSection.State(saved.Appearance));
            Require(state.GetProperty("delay").GetInt32() == 30 && state.GetProperty("skin").GetString() == "Journal"
                && state.GetProperty("recent").GetBoolean() && saved.General == settings.General);
        }
        finally { folder.Delete(true); }
    }

    private static Task CustomAccentReturnsToPreset()
    {
        var initial = new AppearanceSettings();
        var custom = AppearanceSection.Change(initial, "customAccent", "#236585");
        Require(JsonSerializer.SerializeToElement(AppearanceSection.State(custom)).GetProperty("fixedAccent").GetString() == "custom");
        var preset = AppearanceSection.Change(custom, "fixedAccent", initial.FixedAccent.ToString());
        Require(preset.CustomAccentArgb == null && preset.FixedAccent == initial.FixedAccent);
        return Task.CompletedTask;
    }

    private static async Task GeneralRoundTrip()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new JsonUserSettingsStore(folder.FullName);
            var settings = await store.LoadAsync();
            var initial = settings.General with { MainWindowHotkey = "Ctrl+F9" };
            await store.SaveAsync(settings with { General = GeneralSection.Change(initial, "resetTime", "23:59") });
            var saved = await store.LoadAsync();
            var state = JsonSerializer.SerializeToElement(GeneralSection.State(saved.General));
            Require(state.GetProperty("resetTime").GetString() == "23:59" && saved.General.MainWindowHotkey == "Ctrl+F9"
                && saved.Appearance.HomeSkin == settings.Appearance.HomeSkin);
        }
        finally { folder.Delete(true); }
    }

    private static async Task ChannelToolsFollowDeveloperMode()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var model = new MainPageViewModel(new JsonUserSettingsStore(folder.FullName),
                new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
            Require(DeveloperSettingsState.ChannelTools(model) == null);
            await model.SaveDeveloperModeAsync(true, CancellationToken.None);
            var tools = JsonSerializer.SerializeToElement(DeveloperSettingsState.ChannelTools(model));
            Require(tools.GetProperty("candidates").GetArrayLength() == model.Profiles.Count &&
                tools.GetProperty("build").GetBoolean() == model.CanBuildChannelStore &&
                tools.GetProperty("activate").GetBoolean() == model.CanActivateSelectedChannel &&
                tools.GetProperty("rollback").GetBoolean() == model.CanRollbackChannelActivation);
        }
        finally { folder.Delete(true); }
    }

    private static Task DeveloperSettingsReflectCurrentInspection()
    {
        var model = new MainPageViewModel(new JsonUserSettingsStore(Path.GetTempPath()),
            new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
        var diagnostics = JsonSerializer.SerializeToElement(DeveloperSettingsState.Diagnostics(model));
        var contract = DeveloperSettingsState.Contract(model);
        Require(diagnostics.GetProperty("components").GetArrayLength() == model.Components.Count
            && diagnostics.GetProperty("canExport").GetBoolean() == model.CanExport
            && diagnostics.GetProperty("exportStatus").GetString() == model.ExportStatusText);
        Require(contract["Steam 应用编号"].Contains("3164330") && contract["清单编号"] == "未读取");
        return Task.CompletedTask;
    }

    private static async Task DownloadLimitsRoundTrip()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new JsonUserSettingsStore(folder.FullName);
            var download = DownloadSection.Change(new DownloadSettings { DefaultGameInstallPath = folder.FullName }, "hardLinks", "false");
            foreach (var limit in new[] { 10_000_000, 0 })
            {
                download = DownloadSection.Change(download, "limit", limit.ToString());
                await store.SaveAsync(new UserSettings { Download = download });
                var state = JsonSerializer.SerializeToElement(DownloadSection.State((await store.LoadAsync()).Download));
                Require(state.GetProperty("limit").GetInt32() == limit && !state.GetProperty("hardLinks").GetBoolean()
                    && state.GetProperty("path").GetString() == folder.FullName);
            }
            Reject(() => DownloadSection.Change(download, "limit", "-1"));
            Reject(() => DownloadSection.Change(download, "limit", "10000001"));
            Reject(() => DownloadSection.Change(download, "hardLinks", "yes"));
        }
        finally { folder.Delete(true); }
    }

    private static Task SettingsNavigationFollowsDeveloperMode()
    {
        Require(SettingsSections.All.Where(s => !s.Developer).Select(s => s.Id).SequenceEqual(
            ["appearance", "general", "Download", "FileManagement", "Screenshot", "Gallery", "Plugins", "Hotkeys", "Gamepad", "Advanced", "about"]));
        var developer = SettingsSections.All.SkipWhile(s => s.Id != "Advanced").Skip(1).SkipLast(1);
        Require(developer.Select(s => s.Id).SequenceEqual(["Journal", "Files", "Status", "Components", "Diagnostics", "Contract"]));
        foreach (var entry in developer)
        {
            Require(SettingsSections.Resolve(entry.Id, true) == entry);
            Reject(() => SettingsSections.Resolve(entry.Id, false));
        }
        return Task.CompletedTask;
    }

    private static Task AboutChannelState()
    {
        var section = new AboutSection();
        var before = JsonSerializer.SerializeToElement(section.State());
        section.SetChannel("Preview");
        var after = JsonSerializer.SerializeToElement(section.State());
        Require(after.GetProperty("channel").GetString() == "Preview"
            && before.GetProperty("version").GetString() == after.GetProperty("version").GetString());
        Reject(() => section.SetChannel("1"));
        Require(section.Channel.ToString() == "Preview");
        return Task.CompletedTask;
    }

    private static Task SettingsActionAllowlist()
    {
        var allowed = GeneralSection.Actions;
        Require(allowed.Contains("change") && !allowed.Contains("check"));
        Require(AboutSection.Actions.Contains("check") && AppearanceSection.Actions.Contains("choose"));
        Reject(() => SettingsSections.Resolve("about/check", true));
        return Task.CompletedTask;
    }

    private static Task WebSettingsSectionRoutes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Nikkiward", "Nikkiward.csproj")))
            directory = directory.Parent;
        var sections = Path.Combine(directory!.FullName, "Nikkiward", "Web", "pages", "settings", "sections");
        Require(SettingsSections.All.Select(s => s.Id).ToHashSet(StringComparer.Ordinal)
            .SetEquals(Directory.EnumerateFiles(sections, "*.js").Select(path => Path.GetFileNameWithoutExtension(path))));

        Reject(() => SettingsSections.Resolve("Overview", false));
        Reject(() => SettingsSections.Resolve("Diagnostics", false));
        Require(SettingsSections.Resolve("Plugins", true).Id == "Plugins");
        return Task.CompletedTask;
    }

    private static Task InvalidSettingsPreserveValues()
    {
        var general = new GeneralSettings();
        var appearance = new AppearanceSettings();
        Reject(() => general = GeneralSection.Change(general, "language", "unknown"));
        Reject(() => general = GeneralSection.Change(general, "resetTime", "24:01"));
        Reject(() => appearance = AppearanceSection.Change(appearance, "delay", "-1"));
        Reject(() => appearance = AppearanceSection.Change(appearance, "skin", "../other"));
        Reject(() => appearance = AppearanceSection.Change(appearance, "glass", "NaN"));
        Require(general.LanguageTag == "zh-CN" && appearance.AppreciationDelaySeconds == 8 && appearance.HomeSkin == "Stitch");
        return Task.CompletedTask;
    }

    private static async Task ScreenshotSectionRoundTrip()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new JsonUserSettingsStore(folder.FullName);
            var original = await store.LoadAsync();
            var next = ScreenshotSection.Change(original.Screenshot, "format", "Avif");
            await store.SaveAsync(original with { Screenshot = next });
            var saved = await store.LoadAsync();
            var state = JsonSerializer.SerializeToElement(ScreenshotSection.State(saved.Screenshot, folder.FullName));
            Require(state.GetProperty("format").GetString() == "Avif" && state.GetProperty("canOpen").GetBoolean());
            Require(saved.Screenshot == (original.Screenshot with { Format = ScreenshotImageFormat.Avif }));
            Reject(() => next = ScreenshotSection.Change(next, "quality", "12"));
            Require(next == saved.Screenshot && saved.General == original.General);
            Require(ScreenshotSection.Actions.ToHashSet().SetEquals(["change", "choose", "open", "test", "clear"]));
        }
        finally { folder.Delete(true); }
    }

    private static async Task HotkeysSectionRoundTrip()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new JsonUserSettingsStore(folder.FullName);
            var original = await store.LoadAsync();
            var next = HotkeysSection.Change(original.General, original.Screenshot, "main", "Ctrl+F9");
            var model = new MainPageViewModel(store, new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
            Task<HotkeyRegistrationResult> Apply(string main, string shot) => HotkeySettingsChange.ApplyAsync(
                () => NativeWindowRuntime.ParseHotkeys(main, shot, out _, out _),
                () => model.SaveHotkeySettingsAsync(main, shot), () => throw new InvalidOperationException());
            Require((await Apply(next.Main, next.Screenshot)).Succeeded);
            Require(!(await Apply("Ctrl+F9", "Control+F9")).Succeeded && !(await Apply("Ctrl", next.Screenshot)).Succeeded);
            var saved = await store.LoadAsync();
            var state = JsonSerializer.SerializeToElement(HotkeysSection.State(saved.General, saved.Screenshot));
            Require(state.GetProperty("main").GetString() == next.Main && saved.Screenshot == original.Screenshot);
            Require(HotkeysSection.Actions.ToHashSet().SetEquals(["record"]));
        }
        finally { folder.Delete(true); }
    }

    private static async Task GamepadSectionRoundTrip()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new JsonUserSettingsStore(folder.FullName);
            var original = await store.LoadAsync();
            var next = GamepadSection.Change(original.Gamepad, "guideAction", "MapKeys", k => k);
            await store.SaveAsync(original with { Gamepad = next });
            var saved = await store.LoadAsync();
            var state = JsonSerializer.SerializeToElement(GamepadSection.State(saved.Gamepad, "已启用", true, false));
            Require(state.GetProperty("guideAction").GetString() == "MapKeys");
            Reject(() => next = GamepadSection.Change(next, "longPress", "600", k => k));
            Require(next == (original.Gamepad with { GuideAction = GamepadButtonAction.MapKeys }) && saved.Gamepad == next);
            Require(GamepadSection.Actions.ToHashSet().SetEquals(["change", "runtime"]));
        }
        finally { folder.Delete(true); }
    }

    private static async Task GallerySectionRoundTrip()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var protection = new GalleryFavoriteProtectionService(folder.FullName, folder.FullName);
            var original = await protection.GetPreferencesAsync();
            await protection.SetEnabledAsync(GallerySection.ProtectionValue("protection", "false"));
            Reject(() => GallerySection.ProtectionValue("protection", "yes"));
            var saved = await protection.GetPreferencesAsync();
            var view = new GallerySettingsViewState("官服", folder.FullName, "自定义目录", true, true, true,
                saved.IsEnabled, "已关闭", saved.ActiveRootPath, true, true, false, false, "0 张", folder.FullName,
                true, false, "尚未关联", "尚未关联", true, false, false, false);
            var state = JsonSerializer.SerializeToElement(GallerySection.State(view, folder.FullName, true, false, "官服"));
            Require(!state.GetProperty("enabled").GetBoolean() && saved.ActiveRootPath == original.ActiveRootPath);
            Require(GallerySection.Actions.ToHashSet().SetEquals(["refresh", "choose", "reset", "open", "exports",
                "change", "chooseProtection", "openProtection", "verify", "clean", "clearCache", "associate", "openTool", "disconnect"]));
            Require(GallerySection.Allowed("choose", view) && !GallerySection.Allowed("clearCache", view) &&
                !GallerySection.Allowed("delete", view) && !GallerySection.Allowed("choose", view with { IsBusy = true }));
        }
        finally { folder.Delete(true); }
    }

    private static async Task PluginSectionRoundTrip()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var catalog = new LocalPluginCatalog(folder.FullName);
            var path = Path.Combine(folder.FullName, "album.exe");
            await File.WriteAllTextAsync(path, "test fixture");
            var original = await catalog.GetAsync("album");
            Require(!PluginsSection.Allowed("open", original, false));
            await catalog.ImportAsync(new("album", "相册插件", "1.0", path, []));
            var saved = await catalog.GetAsync("album");
            var state = JsonSerializer.SerializeToElement(PluginsSection.State(saved, false));
            Require(state.GetProperty("canOpen").GetBoolean() && state.GetProperty("version").GetString() == "1.0");
            Require(!PluginsSection.Allowed("uninstall", saved, true) && !PluginsSection.Allowed("delete", saved, false));
            Require(PluginsSection.Actions.ToHashSet().SetEquals(["import", "open", "uninstall"]));
            await catalog.UninstallAsync("album");
            Require(!(await catalog.GetAsync("album")).IsInstalled && File.Exists(path));
        }
        finally { folder.Delete(true); }
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("设置分区断言失败。");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("无效的设置请求未被拒绝。");
    }
}
