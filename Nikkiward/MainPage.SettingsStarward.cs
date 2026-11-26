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

            await App.MainWindow.ExitApplicationAsync(
                () => ApplicationDataPaths.ConfigureRoot(normalized, App.StartSuccessor));
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
                var receipt = await SettingsMaintenance.CreateBackupAsync(cancellationToken, App.Settings.Current);
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
        if (App.Settings.ProtectionMessage is not null)
        {
            await RunSettingsMaintenanceOperationAsync(async token =>
            {
                var picker = new FileOpenPicker { CommitButtonText = "恢复设置并关闭" };
                picker.FileTypeFilter.Add(".zip");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
                if (await picker.PickSingleFileAsync() is { } file)
                {
                    await App.Settings.RecoverUnreadableAsync(file.Path, token);
                    await App.MainWindow.ExitApplicationAsync();
                }
            }, "从备份恢复设置失败");
            return;
        }

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
        var reset = App.Settings.ProtectionMessage is not null;
        var confirmed = await ConfirmSettingsDeletionAsync(reset);
        if (!confirmed)
        {
            return;
        }

        await RunSettingsMaintenanceOperationAsync(
            async cancellationToken =>
            {
                if (reset)
                {
                    await App.Settings.RecoverUnreadableAsync(null, cancellationToken);
                    await App.MainWindow.ExitApplicationAsync();
                    return;
                }
                await App.Settings.DeleteAsync(SettingsMaintenance.DeleteSettingsAsync, cancellationToken);
                await TryShowDialogAsync(
                    "设置已删除",
                    "设置文件已删除，应用将关闭。下次启动时会使用默认设置。 ");
                await App.MainWindow.ExitApplicationAsync();
            },
            reset ? "设置重置失败" : "设置删除失败");
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

    private async Task<bool> ConfirmSettingsDeletionAsync(bool reset)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = reset ? "重置为默认设置" : "删除所有设置",
            Content = reset ? "原设置文件将改名保存在数据文件夹中。应用将写入默认设置并关闭。" : "这会删除 Nikkiward 的 settings.json，应用关闭后下次启动将恢复默认设置。是否继续？",
            PrimaryButtonText = reset ? "重置并关闭" : "删除并关闭",
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
