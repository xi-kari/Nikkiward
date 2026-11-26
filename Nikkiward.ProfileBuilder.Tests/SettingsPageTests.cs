using System.Text.Json;
using Nikkiward.Features.Pages;
using Nikkiward.Features.Settings;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;

internal static class SettingsPageTests
{
    internal static (string, Func<Task>)[] All => [
        (nameof(RealSettingsSectionStatesSerialize), RealSettingsSectionStatesSerialize),
        (nameof(AppearanceRoundTrip), AppearanceRoundTrip),
        (nameof(DeveloperSettingsReflectCurrentInspection), DeveloperSettingsReflectCurrentInspection),
        (nameof(DownloadLimitsRoundTrip), DownloadLimitsRoundTrip),
        (nameof(SettingsNavigationFollowsDeveloperMode), SettingsNavigationFollowsDeveloperMode),
        (nameof(GeneralRoundTrip), GeneralRoundTrip),
        (nameof(CustomAccentReturnsToPreset), CustomAccentReturnsToPreset),
        (nameof(AboutChannelState), AboutChannelState),
        (nameof(SettingsActionAllowlist), SettingsActionAllowlist),
        (nameof(NativeSectionFallback), NativeSectionFallback),
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

    private static Task NativeSectionFallback()
    {
        var registered = new Dictionary<string, PageAdapter> {
            ["appearance"] = new("appearance", AppearanceSection.Actions, () => new {}, (_,_) => Task.CompletedTask)
        };
        Require(SettingsSections.All.Select(s => s.Destination).ToHashSet().SetEquals(Enum.GetValues<SettingsDestination>().Except([SettingsDestination.Overview])));
        Require(SettingsSections.NativeTarget(SettingsSections.Resolve("Gallery", false), registered) == SettingsDestination.Gallery);
        Reject(() => SettingsSections.NativeTarget(SettingsSections.Resolve("appearance", false), registered));
        Reject(() => SettingsSections.Resolve("Overview", false));
        Reject(() => SettingsSections.Resolve("Diagnostics", false));
        Require(SettingsSections.Resolve("Plugins", true).Destination == SettingsDestination.Plugins);
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
