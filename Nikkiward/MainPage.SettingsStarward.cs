using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
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
            StatusText = _settingsMaintenanceBusy ? "正在处理本地数据" : null,
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

    private async Task RunSettingsMaintenanceOperationAsync(
        Func<CancellationToken, Task> operation,
        string failurePrefix)
    {
        if (_settingsMaintenanceBusy)
        {
            return;
        }

        _settingsMaintenanceBusy = true;
        _hostedSettingsPage?.ApplyFileManagementState(CreateFileManagementSettingsViewState());
        var cancellationToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
        try
        {
            await operation(cancellationToken);
            await RefreshFileManagementSettingsStateAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"{failurePrefix}：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _settingsMaintenanceBusy = false;
            _hostedSettingsPage?.ApplyFileManagementState(CreateFileManagementSettingsViewState());
        }
    }

    private async void OnSettingsGeneralChanged(
        object? sender,
        GeneralSettingsChangedEventArgs e)
    {
        try
        {
            await ViewModel.SaveGeneralSettingsAsync(
                e.Settings,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            ApplicationLanguageRuntime.Apply(ViewModel.GeneralSettings.LanguageTag);
            App.MainWindow.ApplyCloseBehavior(e.Settings.CloseWindowBehavior);
            SyncLauncherChrome();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ViewModel.ReportUiError($"通用设置保存失败：{ex.GetType().Name}: {ex.Message}");
        }
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

    private async void OnSettingsUserDataFolderRequested(
        object? sender,
        UserDataFolderRequestedEventArgs e)
    {
        if (e.Action is UserDataFolderAction.Open)
        {
            await OpenFolderAsync(e.Path, "数据文件夹打开失败");
            return;
        }

        var folderPath = await PickFolderAsync("选择数据文件夹", PickerLocationId.ComputerFolder);
        if (folderPath is null)
        {
            return;
        }

        try
        {
            var normalized = ApplicationDataPaths.ValidateExistingRoot(
                folderPath,
                requireSettings: true);
            if (string.Equals(
                    normalized,
                    ApplicationDataPaths.Root,
                    StringComparison.OrdinalIgnoreCase))
            {
                _hostedSettingsPage?.ApplyFileManagementState(
                    CreateFileManagementSettingsViewState());
                return;
            }

            if (!await ConfirmDataFolderSwitchAsync(normalized))
            {
                return;
            }

            ApplicationDataPaths.ConfigureRoot(normalized);
            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The Nikkiward executable path is unavailable.");
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
            });
            App.MainWindow.ExitApplication();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ViewModel.ReportUiError($"数据文件夹保存失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnSettingsFileBackupRequested(object? sender, EventArgs e)
    {
        await RunSettingsMaintenanceOperationAsync(
            async cancellationToken =>
            {
                var receipt = await SettingsMaintenance.CreateBackupAsync(cancellationToken);
                await ViewModel.SaveFileManagementSettingsAsync(
                    ViewModel.FileManagementSettings with
                    {
                        LastBackupPath = receipt.FilePath,
                        LastBackupAtUtc = receipt.CreatedAtUtc,
                    },
                    cancellationToken);
            },
            "数据备份失败");
    }

    private async void OnSettingsFileOpenBackupRequested(object? sender, EventArgs e)
    {
        var path = ViewModel.FileManagementSettings.LastBackupPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                if (!await Launcher.LaunchFileAsync(file))
                {
                    await OpenFolderAsync(Path.GetDirectoryName(path)!, "备份目录打开失败");
                }
            }
            catch (Exception ex)
            {
                ViewModel.ReportUiError($"最近备份打开失败：{ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private async void OnSettingsFileDeleteAllSettingsRequested(object? sender, EventArgs e)
    {
        var confirmed = await ConfirmSettingsDeletionAsync();
        if (!confirmed)
        {
            return;
        }

        await RunSettingsMaintenanceOperationAsync(
            async cancellationToken =>
            {
                await SettingsMaintenance.DeleteSettingsAsync(cancellationToken);
                await TryShowDialogAsync(
                    "设置已删除",
                    "设置文件已删除，应用将关闭。下次启动时会使用默认设置。 ");
                App.MainWindow.ExitApplication();
            },
            "设置删除失败");
    }

    private async void OnSettingsFileOpenLogsRequested(object? sender, EventArgs e) =>
        await OpenFolderAsync(SettingsMaintenance.LogFolder, "日志文件夹打开失败");

    private async void OnSettingsFileClearCacheRequested(object? sender, EventArgs e)
    {
        await RunSettingsMaintenanceOperationAsync(
            cancellationToken => SettingsMaintenance.ClearCachesAsync(
                ViewModel.FileManagementSettings.ClearLauncherBackgroundFiles,
                cancellationToken),
            "缓存清理失败");
    }

    private async void OnSettingsFileClearLauncherBackgroundChanged(object? sender, bool enabled)
    {
        try
        {
            await ViewModel.SaveFileManagementSettingsAsync(
                ViewModel.FileManagementSettings with
                {
                    ClearLauncherBackgroundFiles = enabled,
                },
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ViewModel.ReportUiError($"缓存清理选项保存失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnSettingsScreenshotSettingsChanged(
        object? sender,
        ScreenshotSettingsChangedEventArgs e)
    {
        var previousSettings = ViewModel.ScreenshotSettings;
        var hotkeyChanged = !string.Equals(
            e.Settings.Hotkey,
            previousSettings.Hotkey,
            StringComparison.OrdinalIgnoreCase);
        var persisted = false;
        try
        {
            if (hotkeyChanged)
            {
                var registration = App.MainWindow.ApplyHotkeys(
                    ViewModel.GeneralSettings.MainWindowHotkey,
                    e.Settings.Hotkey);
                _hostedSettingsPage?.ApplyHotkeyRegistrationStatus(
                    registration.Message);
                if (!registration.Succeeded)
                {
                    _hostedSettingsPage?.ApplyScreenshotSettings(
                        ViewModel.ScreenshotSettings,
                        ResolveScreenshotFolderPath());
                    return;
                }
            }

            await ViewModel.SaveScreenshotSettingsAsync(
                e.Settings,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            persisted = true;
            _hostedSettingsPage?.ApplyHotkeySettings(
                ViewModel.GeneralSettings,
                ViewModel.ScreenshotSettings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (hotkeyChanged && !persisted)
            {
                var rollback = App.MainWindow.ApplyHotkeys(
                    ViewModel.GeneralSettings.MainWindowHotkey,
                    previousSettings.Hotkey);
                _hostedSettingsPage?.ApplyHotkeyRegistrationStatus(rollback.Message);
            }

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
            await OpenFolderAsync(e.Path, "截图文件夹打开失败");
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
            _ = await GalleryThumbnailCache.ClearAsync(
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            await TryShowDialogAsync("缩略图缓存已清除", "相册缩略图缓存已删除。 ");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ViewModel.ReportUiError($"缩略图缓存清理失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnSettingsHotkeySettingsChanged(
        object? sender,
        HotkeySettingsChangedEventArgs e)
    {
        var previousGeneral = ViewModel.GeneralSettings;
        var previousScreenshot = ViewModel.ScreenshotSettings;
        var persisted = false;
        try
        {
            var registration = App.MainWindow.ApplyHotkeys(
                e.MainWindowHotkey,
                e.ScreenshotHotkey);
            _hostedSettingsPage?.ApplyHotkeyRegistrationStatus(
                registration.Message);
            if (!registration.Succeeded)
            {
                _hostedSettingsPage?.ApplyHotkeySettings(
                    ViewModel.GeneralSettings,
                    ViewModel.ScreenshotSettings);
                return;
            }

            await ViewModel.SaveHotkeySettingsAsync(
                e.MainWindowHotkey,
                e.ScreenshotHotkey,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            persisted = true;
            if (_hostedSettingsPage is { } page)
            {
                page.ApplyGeneralSettings(ViewModel.GeneralSettings);
                page.ApplyScreenshotSettings(
                    ViewModel.ScreenshotSettings,
                    ResolveScreenshotFolderPath());
                page.ApplyHotkeySettings(
                    ViewModel.GeneralSettings,
                    ViewModel.ScreenshotSettings);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!persisted)
            {
                var rollback = App.MainWindow.ApplyHotkeys(
                    previousGeneral.MainWindowHotkey,
                    previousScreenshot.Hotkey);
                _hostedSettingsPage?.ApplyHotkeyRegistrationStatus(rollback.Message);
                _hostedSettingsPage?.ApplyHotkeySettings(previousGeneral, previousScreenshot);
            }
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
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            _hostedSettingsPage?.ApplyScreenshotStatus(result.Message);
            if (!result.Succeeded)
            {
                ViewModel.ReportUiError(result.Message);
                return;
            }

            await CopyScreenshotToClipboardAsync(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
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
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"{failurePrefix}：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<bool> ConfirmSettingsDeletionAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除所有设置",
            Content = "这会删除 Nikkiward 的 settings.json，应用关闭后下次启动将恢复默认设置。是否继续？",
            PrimaryButtonText = "删除并关闭",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() is ContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmDataFolderSwitchAsync(string folderPath)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "切换数据文件夹",
            Content = $"Nikkiward 不会自动移动现有数据。请确认已把当前数据复制到以下目录，且其中包含 settings.json：\n\n{folderPath}\n\n确认后应用将重启并从该目录读取设置、缓存、插件、手账和截图。",
            PrimaryButtonText = "切换并重启",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() is ContentDialogResult.Primary;
    }
}
