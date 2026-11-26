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
using Nikkiward.Features.Shell;
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
    internal bool InvokeHomeCommand(string action, string? id)
    {
        switch (action)
        {
            case "select-profile": _ = SelectProfileAsync(id!); break;
            case "rediscover": _ = DiscoverProfilesAsync(); break;
            case "locate": if (ViewModel.CanDiscover) OnChooseGameRootClicked(this, new RoutedEventArgs()); break;
            case "recheck": if (ViewModel.CanRefresh) OnRefreshClicked(this, new RoutedEventArgs()); break;
            case "tide": case "blossom":
                var background = new BackgroundArtSettings { SelectedSource = action == "tide" ? AppearanceBackgroundPresets.Preset1Source : AppearanceBackgroundPresets.Preset2Source };
                OnSettingsAppearanceChanged(this, new AppearanceSettingsChangedEventArgs(ViewModel.AppearanceSettings with { Background = background }, true));
                break;
            default: return false;
        }
        return true;
    }

    private async void OnDiscoverProfilesClicked(object sender, RoutedEventArgs e)
    {
        await DiscoverProfilesAsync();
    }

    private async void OnProfileDiscoverRequested(object? sender, EventArgs e)
    {
        await DiscoverProfilesAsync();
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

    private async void OnChooseGameRootClicked(object sender, RoutedEventArgs e)
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

    private async void OnChooseLauncherRootClicked(object sender, RoutedEventArgs e)
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

    private async void OnProfileSelected(object? sender, ProfileSelectedEventArgs e)
    {
        await SelectProfileAsync(e.ProfileId);
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

    private Flyout? _profileFlyout;

    private void OnProfileDetailsRequested(object? sender, EventArgs e)
    {
        CloseProfileOverlay();
        ContentFrame.Navigate(typeof(ProfilePage), new ProfileNavigationContext(ViewModel),
            AppearanceRuntimeValues.CreateNavigationTransitionInfo());
        _hostedProfilePage?.ShowDetails();
        SyncLauncherChrome();
    }

    private void OnProfileChooseGameRootRequested(object? sender, EventArgs e) =>
        OnChooseGameRootClicked(sender ?? this, new RoutedEventArgs());

    private void OnProfileChooseLauncherRootRequested(object? sender, EventArgs e) =>
        OnChooseLauncherRootClicked(sender ?? this, new RoutedEventArgs());

    private async void OnProfileChooseChannelStoreRootRequested(object? sender, EventArgs e)
    {
        try
        {
            await ChooseChannelStoreRootAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"单本体路径选择失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnProfilePlanChannelStoreRequested(object? sender, EventArgs e)
    {
        try
        {
            var root = _manualChannelStoreRootPath;
            if (string.IsNullOrWhiteSpace(root) &&
                !string.Equals(ViewModel.ChannelStoreRootPath, "尚未选择", StringComparison.Ordinal))
            {
                root = ViewModel.ChannelStoreRootPath;
            }

            if (string.IsNullOrWhiteSpace(root))
            {
                root = await ChooseChannelStoreRootAsync();
                if (string.IsNullOrWhiteSpace(root))
                {
                    return;
                }
            }

            await ViewModel.PlanChannelStoreAsync(
                root,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"单本体 dry-run 失败：{ex.GetType().Name}: {ex.Message}");
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

    private async void OnProfileBuildChannelStoreRequested(object? sender, EventArgs e)
    {
        try
        {
            await ViewModel.BuildChannelStoreAsync(
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"单本体创建失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnProfileActivateChannelRequested(object? sender, EventArgs e)
    {
        try
        {
            await ViewModel.ActivateSelectedChannelAsync(
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"渠道激活失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnProfileRollbackActivationRequested(object? sender, EventArgs e)
    {
        try
        {
            await ViewModel.RollbackLastChannelActivationAsync(
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"渠道激活回滚失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ShowProfileOverlay(FrameworkElement anchor)
    {
        if (!HomeInteractive) return;
        if (_profileFlyout is null)
        {
            var picker = new ProfilePickerView(ViewModel);
            picker.DiscoverRequested += OnProfileDiscoverRequested;
            picker.ProfileSelected += OnProfileSelected;
            picker.DetailsRequested += OnProfileDetailsRequested;
            picker.CloseRequested += (_, _) => CloseProfileOverlay();
            _profileFlyout = new Flyout { Content = picker };
            _profileFlyout.Opened += (_, _) => SyncLauncherChrome();
            _profileFlyout.Closed += (_, _) => SyncLauncherChrome();
        }
        _profileFlyout.ShowAt(anchor);
    }

    private void CloseProfileOverlay() => _profileFlyout?.Hide();
}
