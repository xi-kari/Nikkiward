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
        Require(SettingsSections.All.Select(s => s.Destination).ToHashSet().SetEquals(Enum.GetValues<SettingsDestination>()));
        Require(SettingsSections.NativeTarget(SettingsSections.Resolve("Gallery", false), registered) == SettingsDestination.Gallery);
        Reject(() => SettingsSections.NativeTarget(SettingsSections.Resolve("appearance", false), registered));
        Require(SettingsSections.Resolve("Overview", false).Destination == SettingsDestination.Overview);
        Reject(() => SettingsSections.Resolve("Plugins", false));
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
