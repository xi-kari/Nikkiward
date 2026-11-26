using Microsoft.UI.Xaml.Controls;
using Nikkiward.Features.Pages;
using Nikkiward.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Nikkiward;

public sealed partial class MainPage
{
    private bool _webCacheStatisticsReady;

    private PageAdapter FileManagementPageSection() => new("FileManagement",
        ["refresh", "openFolder", "chooseFolder", "backup", "openBackup", "delete", "logs", "clearCache", "change"],
        () => {
            var s = CreateFileManagementSettingsViewState();
            return new {
                folder = s.DataFolderPath, backup = s.LastBackupPath, saved = s.LastBackupAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                canOpenBackup = File.Exists(s.LastBackupPath), protection = s.ProtectionMessage,
                busy = s.IsBusy, backgrounds = s.ClearLauncherBackgroundFiles,
                sizes = _webCacheStatisticsReady ? new[] { s.CacheStatistics.LogBytes, s.CacheStatistics.ImageBytes,
                    s.CacheStatistics.BrowserBytes, s.CacheStatistics.GameResourceBytes, s.CacheStatistics.LauncherBackgroundBytes } : null
            };
        }, (op, m) => {
            if (op == "change" && SettingsValues.Field(m, "field") != "backgrounds") throw SettingsValues.Invalid();
            return RunFileCommandAsync(op, op == "change" && SettingsValues.Toggle(SettingsValues.Field(m, "value")));
        });

    private async Task RunFileCommandAsync(string op, bool enabled)
    {
        var token = _lifetimeCancellation?.Token ?? CancellationToken.None;
        if (_settingsMaintenanceBusy) return;
        var protection = App.Settings.ProtectionMessage;
        if (protection != null && op is ("backup" or "change")) throw new InvalidDataException(protection);
        _settingsMaintenanceBusy = true;
        try
        {
            RefreshMaintenanceViews();
            switch (op)
            {
                case "refresh":
                    _settingsCacheStatistics = await SettingsMaintenance.GetCacheStatisticsAsync(token);
                    _webCacheStatisticsReady = true;
                    break;
                case "openFolder": await OpenSettingsFolderAsync(ApplicationDataPaths.Root); break;
                case "logs": await OpenSettingsFolderAsync(SettingsMaintenance.LogFolder); break;
                case "chooseFolder":
                    if (await PickFolderAsync("选择数据文件夹", PickerLocationId.ComputerFolder) is { } path)
                    {
                        var folder = ApplicationDataPaths.ValidateExistingRoot(path, requireSettings: true);
                        if (!string.Equals(folder, ApplicationDataPaths.Root, StringComparison.OrdinalIgnoreCase) && await ConfirmDataFolderSwitchAsync(folder))
                            await App.MainWindow.ExitApplicationAsync(() => ApplicationDataPaths.ConfigureRoot(folder, App.StartSuccessor));
                    }
                    break;
                case "backup":
                    var receipt = await SettingsMaintenance.CreateBackupAsync(token, App.Settings.Current);
                    await ViewModel.SaveFileManagementSettingsAsync(ViewModel.FileManagementSettings with {
                        LastBackupPath = receipt.FilePath, LastBackupAtUtc = receipt.CreatedAtUtc
                    }, token);
                    break;
                case "openBackup" when protection != null:
                    var picker = new FileOpenPicker { CommitButtonText = "选择备份" };
                    picker.FileTypeFilter.Add(".zip");
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
                    if (await picker.PickSingleFileAsync() is { } archive && await ConfirmSettingsActionAsync("从备份恢复设置", $"将使用 {archive.Name} 中的设置。原设置会保留在数据目录，恢复后应用将关闭。", "恢复并关闭"))
                    {
                        await App.Settings.RecoverUnreadableAsync(archive.Path, token);
                        await App.MainWindow.ExitApplicationAsync();
                    }
                    break;
                case "openBackup":
                    var backup = ViewModel.FileManagementSettings.LastBackupPath;
                    if (!File.Exists(backup)) throw new FileNotFoundException("最近的备份不存在。");
                    if (!await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(backup)))
                        await OpenSettingsFolderAsync(Path.GetDirectoryName(backup)!);
                    break;
                case "delete":
                    if (!await ConfirmSettingsDeletionAsync(protection != null)) break;
                    if (protection != null) await App.Settings.RecoverUnreadableAsync(null, token);
                    else
                    {
                        await App.Settings.DeleteAsync(SettingsMaintenance.DeleteSettingsAsync, token);
                        await TryShowDialogAsync("设置已删除", "应用将关闭，下次启动时会使用默认设置。");
                    }
                    await App.MainWindow.ExitApplicationAsync();
                    break;
                case "change":
                    await ViewModel.SaveFileManagementSettingsAsync(ViewModel.FileManagementSettings with { ClearLauncherBackgroundFiles = enabled }, token);
                    break;
                case "clearCache":
                    var backgrounds = ViewModel.FileManagementSettings.ClearLauncherBackgroundFiles;
                    var message = "将删除日志、图片、浏览器及游戏资源缓存。" + (backgrounds ? "启动器背景缓存也会删除。" : "");
                    if (!await ConfirmSettingsActionAsync("清理缓存", message, "清理")) break;
                    var failedCount = 0;
                    await GalleryProtection.ChangeGalleryDirectoriesAsync(async () =>
                    {
                        var cleared = await SettingsMaintenance.ClearCachesAsync(backgrounds, await ThumbnailProtectedRootsAsync(token), token);
                        failedCount = cleared.FailedPaths.Count;
                    }, token);
                    _settingsCacheStatistics = await SettingsMaintenance.GetCacheStatisticsAsync(token);
                    _webCacheStatisticsReady = true;
                    if (failedCount > 0) throw new IOException($"有 {failedCount} 项未能清理，目录可能受保护或仍在使用。");
                    break;
                default: throw SettingsValues.Invalid();
            }
        }
        finally
        {
            _settingsMaintenanceBusy = false;
            RefreshMaintenanceViews();
        }
    }

    private void RefreshMaintenanceViews()
    {
        _hostedSettingsPage?.ApplyFileManagementState(CreateFileManagementSettingsViewState());
        CurrentWebPage?.Refresh();
    }

    private async Task<bool> ConfirmSettingsActionAsync(string title, string message, string confirm)
    {
        await _dialogGate.WaitAsync();
        try
        {
            var dialog = new ContentDialog {
                XamlRoot = XamlRoot, Title = title, Content = message, PrimaryButtonText = confirm,
                CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally { _dialogGate.Release(); }
    }
}
