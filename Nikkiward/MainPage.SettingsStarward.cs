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
            StatusText = _settingsMaintenanceBusy ? "正在处理本地数据" : App.Settings.ProtectionMessage,
        };

    private async Task RefreshFileManagementSettingsStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            _settingsCacheStatistics = await SettingsMaintenance
                .GetCacheStatisticsAsync(cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"文件缓存统计失败：{ex.GetType().Name}: {ex.Message}");
        }

        if (_hostedSettingsPage is { } page)
        {
            page.ApplyFileManagementState(CreateFileManagementSettingsViewState());
        }
    }

    private async void OnSettingsGeneralChanged(
        object? sender,
        GeneralSettingsChangedEventArgs e)
    {
        try
        {
            await SaveAndApplyGeneralAsync(e.Settings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _hostedSettingsPage?.ApplyGeneralSettings(ViewModel.GeneralSettings);
            ViewModel.ReportUiError($"通用设置保存失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task SaveAndApplyGeneralAsync(GeneralSettings settings)
    {
        await ViewModel.SaveGeneralSettingsAsync(settings, _lifetimeCancellation?.Token ?? CancellationToken.None);
        UpdatePlayTimeTimer(fromSettings: true);
        if (_plannerTimer?.IsEnabled == true) RefreshPlanner();
        ApplicationLanguageRuntime.Apply(ViewModel.GeneralSettings.LanguageTag);
        App.MainWindow.ApplyCloseBehavior(settings.CloseWindowBehavior);
        SyncLauncherChrome();
    }

    private async void OnSettingsVisualEffectsRequested(object? sender, EventArgs e)
    {
        try
        {
            await Launcher.LaunchUriAsync(new Uri("ms-settings:easeofaccess-visualeffects"));
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"系统视觉效果设置打开失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnSettingsDownloadChanged(
        object? sender,
        DownloadSettingsChangedEventArgs e)
    {
        try
        {
            await ViewModel.SaveDownloadSettingsAsync(
                e.Settings,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ViewModel.ReportUiError($"下载设置保存失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnSettingsDownloadPathRequested(
        object? sender,
        DownloadPathRequestedEventArgs e)
    {
        if (e.Action is DownloadPathAction.Open && !string.IsNullOrWhiteSpace(e.Path))
        {
            await OpenFolderAsync(e.Path, "默认游戏安装路径打开失败");
            return;
        }

        if (e.Action is DownloadPathAction.Clear)
        {
            await SaveDownloadPathAsync(null);
            return;
        }

        var folderPath = await PickFolderAsync("选择默认游戏安装路径", PickerLocationId.ComputerFolder);
        if (folderPath is not null)
        {
            await SaveDownloadPathAsync(folderPath);
        }
    }

    private async Task SaveDownloadPathAsync(string? path)
    {
        try
        {
            await ViewModel.SaveDownloadSettingsAsync(
                ViewModel.DownloadSettings with { DefaultGameInstallPath = path },
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            _hostedSettingsPage?.ApplyDownloadSettings(ViewModel.DownloadSettings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ViewModel.ReportUiError($"默认安装路径保存失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task RunNativeFileCommandAsync(string action, bool enabled = false)
    {
        try { await RunFileCommandAsync(action, enabled); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ViewModel.ReportUiError($"文件管理操作失败：{ex.Message}"); }
    }

    private async void OnSettingsUserDataFolderRequested(object? sender, UserDataFolderRequestedEventArgs e) =>
        await RunNativeFileCommandAsync(e.Action == UserDataFolderAction.Open ? "openFolder" : "chooseFolder");

    private async void OnSettingsFileBackupRequested(object? sender, EventArgs e) => await RunNativeFileCommandAsync("backup");
    private async void OnSettingsFileOpenBackupRequested(object? sender, EventArgs e) => await RunNativeFileCommandAsync("openBackup");
    private async void OnSettingsFileDeleteAllSettingsRequested(object? sender, EventArgs e) => await RunNativeFileCommandAsync("delete");
    private async void OnSettingsFileOpenLogsRequested(object? sender, EventArgs e) => await RunNativeFileCommandAsync("logs");
    private async void OnSettingsFileClearCacheRequested(object? sender, EventArgs e) => await RunNativeFileCommandAsync("clearCache");
    private async void OnSettingsFileClearLauncherBackgroundChanged(object? sender, bool enabled) => await RunNativeFileCommandAsync("change", enabled);

    private async void OnSettingsScreenshotSettingsChanged(
        object? sender,
        ScreenshotSettingsChangedEventArgs e)
    {
        var token = _lifetimeCancellation?.Token ?? CancellationToken.None;
        var previousSettings = ViewModel.ScreenshotSettings;
        try
        {
            await _hotkeySaveGate.WaitAsync(token);
            try
            {
                previousSettings = ViewModel.ScreenshotSettings;
                var hotkeyChanged = !string.Equals(
                    e.Settings.Hotkey,
                    previousSettings.Hotkey,
                    StringComparison.OrdinalIgnoreCase);
                if (!hotkeyChanged)
                {
                    await ViewModel.SaveScreenshotSettingsAsync(e.Settings, token);
                }
                else
                {
                    var mainWindowHotkey = ViewModel.GeneralSettings.MainWindowHotkey;
                    var registration = await HotkeySettingsChange.ApplyAsync(
                        () => App.MainWindow.ApplyHotkeys(mainWindowHotkey, e.Settings.Hotkey),
                        () => ViewModel.SaveScreenshotSettingsAsync(e.Settings, token),
                        () =>
                        {
                            var rollback = App.MainWindow.ApplyHotkeys(mainWindowHotkey, previousSettings.Hotkey);
                            _hostedSettingsPage?.ApplyHotkeyRegistrationStatus(rollback.Message);
                        });
                    _hostedSettingsPage?.ApplyHotkeyRegistrationStatus(registration.Message);
                    if (!registration.Succeeded)
                    {
                        _hostedSettingsPage?.ApplyScreenshotSettings(
                            ViewModel.ScreenshotSettings,
                            ResolveScreenshotFolderPath());
                        return;
                    }
                }

                _hostedSettingsPage?.ApplyHotkeySettings(
                    ViewModel.GeneralSettings,
                    ViewModel.ScreenshotSettings);
            }
            finally
            {
                _hotkeySaveGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _hostedSettingsPage?.ApplyScreenshotSettings(
                ViewModel.ScreenshotSettings,
                ResolveScreenshotFolderPath());
        }
        catch (Exception ex)
        {
            _hostedSettingsPage?.ApplyScreenshotSettings(
                previousSettings,
                ResolveScreenshotFolderPath());
            ViewModel.ReportUiError($"截图设置保存失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnSettingsScreenshotFolderRequested(
        object? sender,
        ScreenshotFolderRequestedEventArgs e)
    {
        if (e.Action is ScreenshotFolderAction.Open && !string.IsNullOrWhiteSpace(e.Path))
        {
            if (Directory.Exists(e.Path)) await OpenFolderAsync(e.Path, "截图文件夹打开失败");
            return;
        }

        var folderPath = await PickFolderAsync("选择截图文件夹", PickerLocationId.PicturesLibrary);
        if (folderPath is null)
        {
            return;
        }

        try
        {
            await ViewModel.SaveScreenshotSettingsAsync(
                ViewModel.ScreenshotSettings with { FolderPath = folderPath },
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            _hostedSettingsPage?.ApplyScreenshotSettings(
                ViewModel.ScreenshotSettings,
                ResolveScreenshotFolderPath());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ViewModel.ReportUiError($"截图文件夹保存失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnSettingsScreenshotTestCaptureRequested(object? sender, EventArgs e)
    {
        try
        {
            var result = await _gameScreenshotService.CaptureTestAsync(
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                ViewModel.ScreenshotSettings,
                ResolveScreenshotFolderPath(),
                () => ViewModel.GalleryReadOnlyRoots,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            _hostedSettingsPage?.ApplyScreenshotStatus(result.Message);
            if (!result.Succeeded)
            {
                ViewModel.ReportUiError(result.Message);
                return;
            }

            await CopyScreenshotToClipboardAsync(result);
            _hostedSettingsPage?.ApplyScreenshotSettings(
                ViewModel.ScreenshotSettings,
                ResolveScreenshotFolderPath());
            await TryShowDialogAsync("测试截图完成", result.Message);
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"测试截图失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnSettingsScreenshotClearThumbnailCacheRequested(object? sender, EventArgs e)
    {
        try
        {
            await ClearGalleryThumbnailsAsync(
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            await TryShowDialogAsync("缩略图缓存已清除", "相册缩略图缓存已删除。 ");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ViewModel.ReportUiError($"缩略图缓存清理失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private readonly SemaphoreSlim _hotkeySaveGate = new(1, 1);

    private async void OnSettingsHotkeySettingsChanged(
        object? sender,
        HotkeySettingsChangedEventArgs e)
    {
        var token = _lifetimeCancellation?.Token ?? CancellationToken.None;
        try
        {
            await _hotkeySaveGate.WaitAsync(token);
            try
            {
                var previousGeneral = ViewModel.GeneralSettings;
                var previousScreenshot = ViewModel.ScreenshotSettings;
                var registration = await HotkeySettingsChange.ApplyAsync(
                    () => App.MainWindow.ApplyHotkeys(e.MainWindowHotkey, e.ScreenshotHotkey),
                    () => ViewModel.SaveHotkeySettingsAsync(e.MainWindowHotkey, e.ScreenshotHotkey, token),
                    () =>
                    {
                        var rollback = App.MainWindow.ApplyHotkeys(previousGeneral.MainWindowHotkey, previousScreenshot.Hotkey);
                        _hostedSettingsPage?.ApplyHotkeyRegistrationStatus(rollback.Message);
                        _hostedSettingsPage?.ApplyHotkeySettings(previousGeneral, previousScreenshot);
                    });
                if (_hostedSettingsPage is { } page)
                {
                    page.ApplyHotkeyRegistrationStatus(registration.Message);
                    page.ApplyHotkeySettings(ViewModel.GeneralSettings, ViewModel.ScreenshotSettings);
                    if (registration.Succeeded)
                    {
                        page.ApplyGeneralSettings(ViewModel.GeneralSettings);
                        page.ApplyScreenshotSettings(ViewModel.ScreenshotSettings, ResolveScreenshotFolderPath());
                    }
                }
            }
            finally
            {
                _hotkeySaveGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            _hostedSettingsPage?.ApplyHotkeySettings(ViewModel.GeneralSettings, ViewModel.ScreenshotSettings);
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"快捷键设置保存失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    public async void CaptureGameScreenshotFromHotkey()
    {
        try
        {
            var result = await _gameScreenshotService.CaptureGameAsync(
                ViewModel.ScreenshotSettings,
                ResolveScreenshotFolderPath(),
                () => ViewModel.GalleryReadOnlyRoots,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            _hostedSettingsPage?.ApplyScreenshotStatus(result.Message);
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

    private async Task OpenFolderAsync(string path, string failurePrefix)
    {
        try
        {
            await OpenSettingsFolderAsync(path);
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"{failurePrefix}：{ex.GetType().Name}: {ex.Message}");
        }
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
