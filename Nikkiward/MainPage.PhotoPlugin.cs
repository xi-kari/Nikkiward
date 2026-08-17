using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Nikkiward.Features.Background;
using Nikkiward.Features.Diagnostics;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.GamepadControl;
using Nikkiward.Features.Journal;
using Nikkiward.Features.Launcher;
using Nikkiward.Features.Profile;
using Nikkiward.Features.Settings;
using Nikkiward.Features.Wish;
using Nikkiward.Models;
using Nikkiward.Pages;
using Nikkiward.Services;
using Nikkiward.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private async void OnPhotoPluginImportClicked(object sender, RoutedEventArgs e)
    {
        if (_photoPluginOperationInProgress)
        {
            return;
        }

        _photoPluginOperationInProgress = true;
        UpdatePhotoPluginControls();
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.Downloads,
                ViewMode = PickerViewMode.List,
            };
            picker.FileTypeFilter.Add(".exe");

            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            var file = await picker.PickSingleFileAsync();
            if (file is null || string.IsNullOrWhiteSpace(file.Path))
            {
                return;
            }

            var companions = new List<string>();
            var sourceDirectory = Path.GetDirectoryName(file.Path);
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
            {
                var configPath = Path.Combine(sourceDirectory, "nikki_config.ini");
                if (File.Exists(configPath))
                {
                    companions.Add(configPath);
                }
            }

            _photoPluginInstallation = await _pluginCatalog.ImportAsync(
                new LocalPluginImportRequest(
                    PhotoAlbumPluginId,
                    PhotoAlbumPluginDisplayName,
                    PhotoAlbumPluginVersion,
                    file.Path,
                    companions),
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            UpdatePhotoPluginControls();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _hostedSettingsPage?.ApplyPhotoPluginState(
                "导入失败",
                canImport: true,
                canOpen: false,
                canUninstall: false);
            ViewModel.ReportUiError(
                $"插件导入失败：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _photoPluginOperationInProgress = false;
            UpdatePhotoPluginControls();
        }
    }

    private async void OnPhotoPluginOpenClicked(object? sender, EventArgs e)
    {
        if (_photoPluginOperationInProgress ||
            _photoPluginInstallation?.IsInstalled is not true)
        {
            return;
        }

        _photoPluginOperationInProgress = true;
        UpdatePhotoPluginControls();
        try
        {
            var opened = await _pluginCatalog.LaunchAsync(
                PhotoAlbumPluginId,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            _hostedPhotoPluginPage?.UpdateStatus(opened ? "已打开" : "打开失败");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _hostedPhotoPluginPage?.UpdateStatus("打开失败");
            ViewModel.ReportUiError(
                $"插件打开失败：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _photoPluginOperationInProgress = false;
            await RefreshPhotoPluginStateAsync(
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
    }

    private async void OnPhotoPluginUninstallClicked(object sender, RoutedEventArgs e)
    {
        if (_photoPluginOperationInProgress)
        {
            return;
        }

        _photoPluginOperationInProgress = true;
        UpdatePhotoPluginControls();
        try
        {
            await _pluginCatalog.UninstallAsync(
                PhotoAlbumPluginId,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            await RefreshPhotoPluginStateAsync(
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            ShowSettingsPage(destination: SettingsDestination.Plugins);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _hostedSettingsPage?.ApplyPhotoPluginState(
                "卸载失败",
                canImport: true,
                canOpen: _photoPluginInstallation?.IsInstalled is true,
                canUninstall: true);
            ViewModel.ReportUiError(
                $"插件卸载失败：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _photoPluginOperationInProgress = false;
            UpdatePhotoPluginControls();
        }
    }

    private void OnPhotoPluginSettingsClicked(object? sender, EventArgs e)
    {
        ShowSettingsPage(destination: SettingsDestination.Plugins);
    }

    private async Task RefreshPhotoPluginStateAsync(
        CancellationToken cancellationToken)
    {
        _photoPluginInstallation = await _pluginCatalog.GetAsync(
            PhotoAlbumPluginId,
            cancellationToken);
        UpdatePhotoPluginControls();
    }

    private void UpdatePhotoPluginControls()
    {
        if (!_xamlInitialized)
        {
            return;
        }

        var installation = _photoPluginInstallation;
        var installed = installation?.IsInstalled is true;
        var canUninstall = installation is { IsInstalled: true } or { IsBroken: true };
        PhotoPluginNavigationItem.Visibility = Visibility.Collapsed;
        _hostedSettingsPage?.ApplyPhotoPluginState(
            installation?.StatusText ?? "尚未安装",
            !_photoPluginOperationInProgress,
            installed && !_photoPluginOperationInProgress,
            canUninstall && !_photoPluginOperationInProgress);
        if (ContentFrame.Content is PhotoPluginPage photoPluginPage)
        {
            UpdatePhotoPluginPageState(photoPluginPage);
        }

        if (!installed &&
            ContentFrame.Visibility == Visibility.Visible &&
            ContentFrame.Content is PhotoPluginPage)
        {
            ContentFrame.Visibility = Visibility.Collapsed;
            SyncLauncherChrome();
        }
    }

    private async void OnResonanceLoginRequested(object? sender, EventArgs e)
    {
        await OpenResonanceJournalAsync();
    }

    private void UpdatePhotoPluginPageState(PhotoPluginPage page)
    {
        var installation = _photoPluginInstallation;
        page.UpdateState(
            installation?.StatusText ?? "尚未安装",
            installation?.Version,
            installation?.IsInstalled is true && !_photoPluginOperationInProgress);
    }
}
