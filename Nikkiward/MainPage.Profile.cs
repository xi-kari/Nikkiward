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
            await ViewModel.DiscoverFromManualRootAsync(
                selected,
                _manualLauncherRootPath,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"游戏目录发现失败：{ex.GetType().Name}: {ex.Message}");
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

            await ViewModel.DiscoverFromManualRootAsync(
                gameRoot,
                selected,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"launcher 目录发现失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void OnProfileCandidateClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string profileId })
        {
            return;
        }

        await SelectProfileAsync(profileId);
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

            SetProfileQuickSwitchRailVisibility(false);
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

    private void OnProfileButtonClicked(object sender, RoutedEventArgs e)
    {
        SetProfileQuickSwitchRailVisibility(false);

        if (ContentFrame.Visibility == Visibility.Visible &&
            ContentFrame.Content is ProfilePage)
        {
            ShowLauncher();
            return;
        }

        ShowProfileOverlay();
    }

    private void OnProfileQuickSwitchHostPointerEntered(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (ProfileOverlayScrim.Visibility == Visibility.Visible)
        {
            SetProfileQuickSwitchRailVisibility(false);
            return;
        }

        SetProfileQuickSwitchRailVisibility(true);
    }

    private void OnProfileQuickSwitchHostPointerExited(
        object sender,
        PointerRoutedEventArgs e)
    {
        if (ProfileOverlayScrim.Visibility == Visibility.Visible)
        {
            return;
        }

        SetProfileQuickSwitchRailVisibility(false);
    }

    private void OnProfileQuickSwitchHostSizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        NotifyTitleBarPassthroughChanged();
    }

    private void SetProfileQuickSwitchRailVisibility(bool isVisible)
    {
        ProfileQuickSwitchRail.Visibility = isVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProfileQuickSwitchRail.IsHitTestVisible = isVisible;
        ProfileQuickSwitchRail.Opacity = isVisible ? 1 : 0;
    }

    private void OnProfileOverlayMaskTapped(object sender, TappedRoutedEventArgs e)
    {
        ShowLauncher();
    }

    private void OnProfileDetailsRequested(object? sender, EventArgs e)
    {
        SetShellNavigationSelection(LauncherNavigationItem);
        _hostedProfilePage?.ShowDetails();
        SetProfileQuickSwitchRailVisibility(false);
        ProfileOverlayScrim.Visibility = Visibility.Collapsed;
        ProfileOverlayMask.Visibility = Visibility.Collapsed;
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

    private void OnProfileCloseRequested(object? sender, EventArgs e) =>
        ShowLauncher();

    private void ShowProfileOverlay()
    {
        HideLibrary();
        HideGallery();
        HideResonance();
        HidePhotoPlugin();
        HideStatusDrawer();
        SetProfileQuickSwitchRailVisibility(false);
        ProfileOverlayScrim.Visibility = Visibility.Visible;
        ProfileOverlayMask.Visibility = Visibility.Visible;
        ContentFrame.Visibility = Visibility.Visible;
        SyncLauncherChrome();
        if (ContentFrame.Content is ProfilePage)
        {
            _hostedProfilePage?.ShowPicker();
            return;
        }

        ContentFrame.Navigate(
            typeof(ProfilePage),
            new ProfileNavigationContext(ViewModel),
            AppearanceRuntimeValues.CreateNavigationTransitionInfo());
    }

    private void CloseProfileOverlay()
    {
        if (ContentFrame.Content is ProfilePage)
        {
            ContentFrame.Visibility = Visibility.Collapsed;
        }
        SyncLauncherChrome();
        SetProfileQuickSwitchRailVisibility(false);
        ProfileOverlayScrim.Visibility = Visibility.Collapsed;
        ProfileOverlayMask.Visibility = Visibility.Collapsed;
    }
}
