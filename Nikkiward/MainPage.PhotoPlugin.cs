using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Nikkiward.Features.Background;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.GamepadControl;
using Nikkiward.Features.Journal;
using Nikkiward.Features.Launcher;
using Nikkiward.Features.Settings;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private async Task ImportPhotoPluginAsync(bool confirmReplacement = false)
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

        if (confirmReplacement && _photoPluginInstallation is ({ IsInstalled: true } or { IsBroken: true }) &&
            !await ConfirmWebSettingsAsync("替换相册插件？", "将用所选程序替换已安装的插件和随附文件。", "替换"))
            return;

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
    }

    private async Task RefreshPhotoPluginStateAsync(
        CancellationToken cancellationToken)
    {
        _photoPluginInstallation = await _pluginCatalog.GetAsync(
            PhotoAlbumPluginId,
            cancellationToken);
        CurrentWebPage?.Refresh();
    }
}
