using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.Settings;
using Nikkiward.Models;
using Nikkiward.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private SettingsMaintenanceService? _settingsMaintenance;
    private readonly GameScreenshotService _gameScreenshotService = new();
    private SettingsCacheStatistics _settingsCacheStatistics = new(0, 0, 0, 0, 0);
    private bool _settingsMaintenanceBusy;

    private SettingsMaintenanceService SettingsMaintenance =>
        _settingsMaintenance ??= new SettingsMaintenanceService(
            ApplicationDataPaths.SettingsFilePath,
            JournalWebViewDataPath);

    private string ResolveScreenshotFolderPath() =>
        ViewModel.ScreenshotSettings.FolderPath ??
        Path.Combine(SettingsMaintenance.DataRoot, "Screenshots");

    private FileManagementSettingsViewState CreateFileManagementSettingsViewState() =>
        new()
        {
            DataFolderPath = ApplicationDataPaths.Root,
            CacheStatistics = _settingsCacheStatistics,
            LastBackupPath = ViewModel.FileManagementSettings.LastBackupPath,
            LastBackupAtUtc = ViewModel.FileManagementSettings.LastBackupAtUtc,
            ClearLauncherBackgroundFiles = ViewModel.FileManagementSettings.ClearLauncherBackgroundFiles,
            IsBusy = _settingsMaintenanceBusy,
            ProtectionMessage = App.Settings.ProtectionMessage,
        };

    private async Task SaveAndApplyGeneralAsync(GeneralSettings settings)
    {
        await ViewModel.SaveGeneralSettingsAsync(settings, _lifetimeCancellation?.Token ?? CancellationToken.None);
        UpdatePlayTimeTimer(fromSettings: true);
        if (_plannerTimer?.IsEnabled == true) RefreshPlanner();
        ApplicationLanguageRuntime.Apply(ViewModel.GeneralSettings.LanguageTag);
        App.MainWindow.ApplyCloseBehavior(settings.CloseWindowBehavior);
        SyncLauncherChrome();
    }

    private readonly SemaphoreSlim _hotkeySaveGate = new(1, 1);

    public async void CaptureGameScreenshotFromHotkey()
    {
        try
        {
            var result = await _gameScreenshotService.CaptureGameAsync(
                ViewModel.ScreenshotSettings,
                ResolveScreenshotFolderPath(),
                () => ViewModel.GalleryReadOnlyRoots,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            if (!result.Succeeded)
            {
                ViewModel.ReportUiError(result.Message);
                return;
            }

            await CopyScreenshotToClipboardAsync(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"游戏截图失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task CopyScreenshotToClipboardAsync(
        ScreenshotCaptureResult result)
    {
        if (!ViewModel.ScreenshotSettings.AutoCopyToClipboard ||
            string.IsNullOrWhiteSpace(result.ClipboardFilePath) ||
            !File.Exists(result.ClipboardFilePath))
        {
            return;
        }

        var file = await StorageFile.GetFileFromPathAsync(
            result.ClipboardFilePath);
        var package = new DataPackage();
        package.SetStorageItems([file]);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private static async Task OpenSettingsFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Folder path is empty.", nameof(path));
        }

        Directory.CreateDirectory(path);
        var folder = await StorageFolder.GetFolderFromPathAsync(path);
        if (!await Launcher.LaunchFolderAsync(folder))
        {
            throw new InvalidOperationException("The folder launcher returned false.");
        }
    }

    private Task<bool> ConfirmSettingsDeletionAsync(bool reset) => ConfirmSettingsActionAsync(
        reset ? "重置为默认设置" : "删除所有设置",
        reset ? "原设置文件将改名保存在数据文件夹中。应用将写入默认设置并关闭。" : "这会删除 Nikkiward 的 settings.json，应用关闭后下次启动将恢复默认设置。是否继续？",
        reset ? "重置并关闭" : "删除并关闭");

    private Task<bool> ConfirmDataFolderSwitchAsync(string folderPath) => ConfirmSettingsActionAsync(
        "切换数据文件夹",
        $"Nikkiward 不会自动移动现有数据。请确认已把当前数据复制到以下目录，且其中包含 settings.json：\n\n{folderPath}\n\n确认后应用将重启并从该目录读取设置、缓存、插件、手账和截图。",
        "切换并重启");
}
