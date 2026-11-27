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
    internal bool InvokeHomeCommand(string action, string? id)
    {
        switch (action)
        {
            case "select-profile": _ = SelectProfileAsync(id!); break;
            case "rediscover": _ = DiscoverProfilesAsync(); break;
            case "locate": if (ViewModel.CanDiscover) _ = ChooseGameRootAsync(); break;
            case "recheck": if (ViewModel.CanRefresh) OnRefreshClicked(this, new RoutedEventArgs()); break;
            case "tide": case "blossom":
                var background = new BackgroundArtSettings { SelectedSource = action == "tide" ? AppearanceBackgroundPresets.Preset1Source : AppearanceBackgroundPresets.Preset2Source };
                SelectHomeBackground(background);
                break;
            default: return false;
        }
        return true;
    }

    private async Task DiscoverProfilesAsync()
    {
        try
        {
            await ViewModel.DiscoverAsync(
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"安装发现失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task ChooseGameRootAsync()
    {
        try
        {
            var selected = await PickFolderAsync(
                "选择无限暖暖游戏目录",
                PickerLocationId.ComputerFolder);
            if (selected is null)
            {
                return;
            }

            _manualGameRootPath = selected;
            var outcome = await ViewModel.DiscoverFromManualRootAsync(
                selected,
                _manualLauncherRootPath,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            if (!outcome.Succeeded)
            {
                await TryShowDialogAsync(outcome.Title, outcome.Detail);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"游戏目录发现失败：{ex.GetType().Name}: {ex.Message}");
            await TryShowDialogAsync("游戏目录发现失败", ViewModel.LastErrorText);
        }
    }

    private async Task ChooseLauncherRootAsync()
    {
        try
        {
            var selected = await PickFolderAsync(
                "选择 InfinityNikki Launcher 根目录",
                PickerLocationId.ComputerFolder);
            if (selected is null)
            {
                return;
            }

            _manualLauncherRootPath = selected;
            var gameRoot = Directory.Exists(_manualGameRootPath)
                ? _manualGameRootPath
                : Directory.Exists(ViewModel.GameRootPath)
                    ? ViewModel.GameRootPath
                    : null;
            if (gameRoot is null)
            {
                await TryShowDialogAsync(
                    "还需要游戏目录",
                    "launcher root 已记录在本次会话中。请先选择游戏根；builder 会同时验证两个独立 root，且不会从 xstarter 所在目录反推 WorkingDirectory。");
                return;
            }

            var outcome = await ViewModel.DiscoverFromManualRootAsync(
                gameRoot,
                selected,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            if (!outcome.Succeeded)
            {
                await TryShowDialogAsync(outcome.Title, outcome.Detail);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"launcher 目录发现失败：{ex.GetType().Name}: {ex.Message}");
            await TryShowDialogAsync("launcher 目录发现失败", ViewModel.LastErrorText);
        }
    }

    private async Task SelectProfileAsync(string profileId)
    {
        try
        {
            var selectionChanged = await ViewModel.SelectProfileAsync(
                profileId,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            if (!selectionChanged)
            {
                return;
            }

            ShowLauncher();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"Profile 选择失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<string?> ChooseChannelStoreRootAsync()
    {
        var selected = await PickFolderAsync(
            "选择三渠道单本体存储的上级目录",
            PickerLocationId.ComputerFolder);
        if (selected is null)
        {
            return null;
        }

        _manualChannelStoreRootPath = string.Equals(
            Path.GetFileName(selected),
            "NikkiwardStore",
            StringComparison.OrdinalIgnoreCase)
            ? selected
            : Path.Combine(selected, "NikkiwardStore");
        ViewModel.SelectChannelStoreRoot(_manualChannelStoreRootPath);
        return _manualChannelStoreRootPath;
    }
}
