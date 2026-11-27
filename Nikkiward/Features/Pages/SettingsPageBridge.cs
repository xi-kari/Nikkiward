using Nikkiward.Features.Pages;
using Nikkiward.Features.Launcher.Skins;

namespace Nikkiward;

public sealed partial class MainPage
{
    private readonly Lazy<AboutSection> _webAbout = AboutSection.CreateLazy();

    private PageAdapter SettingsPageAdapter()
    {
        PageAdapter[] adapters = [AppearancePageSection(), GeneralPageSection(), AboutPageSection(),
            DownloadPageSection(), FileManagementPageSection(), AdvancedPageSection(), JournalStoragePageSection(),
            LocalFilesPageSection(), RuntimeStatusPageSection(), ComponentsPageSection(), DiagnosticsPageSection(), ContractPageSection(),
            ScreenshotPageSection(), GalleryPageSection(), PluginsPageSection(), HotkeysPageSection(), GamepadPageSection()];
        var sections = adapters.ToDictionary(adapter => adapter.Module, StringComparer.Ordinal);
        return new("settings", ["section"], () => new {
            initial = _settingsInitial,
            gamepadConnectionChanged = _publishingGamepadConnection,
            sections = SettingsSections.All.Where(s => !s.Developer || ViewModel.DeveloperModeEnabled)
                .ToDictionary(s => s.Id, s => sections[s.Id].State()),
            navigation = SettingsSections.All.Where(s => !s.Developer || ViewModel.DeveloperModeEnabled).Select(s => new { id = s.Id, label = s.Label }),
            skins = HomeSkins.Available.Select(s => new { id = s.Id, label = s.Name }),
            canClearTime = _playTime?.Sessions.Count > 0, playTimeError = _playTimeError,
            mainKey = ViewModel.GeneralSettings.MainWindowHotkey, screenshotKey = ViewModel.ScreenshotSettings.Hotkey
        }, async (action, message) => {
            var id = SettingsValues.Field(message, "section");
            SettingsSections.Resolve(id, ViewModel.DeveloperModeEnabled);
            var op = SettingsValues.Field(message, "op");
            if (!sections.TryGetValue(id, out var section) || !section.Actions.Contains(op)) throw SettingsValues.Invalid();
            try { await section.Execute(op, message); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CurrentWebPage?.Refresh();
                throw new InvalidDataException($"设置操作失败：{ex.Message}", ex);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation?.IsCancellationRequested == true) { }
            CurrentWebPage?.Refresh();
        });
    }

    private static async Task OpenSettingsLinkAsync(Uri uri)
    {
        if (!await Windows.System.Launcher.LaunchUriAsync(uri)) throw new IOException($"未能打开链接：{uri.Host}");
    }
}
