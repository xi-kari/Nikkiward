using System.Globalization;
using System.Numerics;
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
using Nikkiward.Features.Launcher.Skins;
using Nikkiward.Features.Settings;
using Nikkiward.Features.Shell;
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
    internal async Task<bool> InvokeOverlayActionAsync(HomeAction request)
    {
        switch (request.Action)
        {
            case "open-root":
                var root = ViewModel.GameRootPath;
                if (!Path.IsPathFullyQualified(root)) break;
                try
                {
                    var folder = await StorageFolder.GetFolderFromPathAsync(root);
                    if (!await Launcher.LaunchFolderAsync(folder))
                        ViewModel.ReportUiError("无法在资源管理器中打开游戏目录。");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                    or System.Runtime.InteropServices.COMException)
                {
                    ViewModel.ReportUiError($"游戏目录打开失败：{ex.Message}");
                }
                break;
            case "import-background":
                await ChooseWallpaperAsync(null);
                break;
            case "art-title":
                if (ViewModel.CanNameArtwork && request.ArtKey == ViewModel.HomeArtKey)
                {
                    try { await SaveAndApplyAppearanceAsync(settings => settings.WithArtworkTitle(request.ArtKey, request.Title!)); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { ViewModel.ReportUiError($"画名保存失败：{ex.Message}"); }
                }
                break;
            case "official-flow":
                if (ViewModel.HomeState == LaunchButtonState.Ready && ViewModel.CanAttemptOfficialAssistedLaunch)
                    OnOfficialFlowClicked(this, new RoutedEventArgs());
                break;
            case "export-diagnostics":
                if (ViewModel.CanExport) OnExportDiagnosticsClicked(this, new RoutedEventArgs());
                break;
            default: return false;
        }
        return true;
    }
    private void ShowStatusDrawer()
    {
        ShowLauncher();
        _hostedLauncherPage?.WebHome?.OpenOverlay("status");
    }
}
