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
    private void ShowSettingsPage(
        bool restoreDestination = false,
        SettingsDestination destination = SettingsDestination.Overview)
    {
        CloseProfileOverlay();
        HideLibrary();
        HideGallery();
        HideResonance();
        HidePhotoPlugin();
        HideStatusDrawer();
        ContentFrame.Visibility = Visibility.Visible;
        SyncLauncherChrome();
        SetShellNavigationSelection(ShellNavigation.SettingsItem);
        var target = restoreDestination ? _activeSettingsDestination : destination;
        if (ContentFrame.Content is SettingsPage page)
        {
            page.NavigateTo(target);
            ApplySettingsPageState(page);
            return;
        }

        ContentFrame.Navigate(
            typeof(SettingsPage),
            CreateSettingsNavigationContext(target),
            AppearanceRuntimeValues.CreateNavigationTransitionInfo());
    }

    private SettingsNavigationContext CreateSettingsNavigationContext(
        SettingsDestination destination) =>
        new(
            ViewModel,
            destination,
            new SettingsStoragePaths(
                JournalWebViewDataPath,
                JournalSnapshotPath,
                JournalAssetsPath),
            ViewModel.DeveloperModeEnabled);

    private void ApplySettingsPageState(SettingsPage? page)
    {
        if (page is null)
        {
            return;
        }

        var installation = _photoPluginInstallation;
        var canUninstall = installation is { IsInstalled: true } or { IsBroken: true };
        page.ApplyPhotoPluginState(
            installation?.StatusText ?? "尚未安装",
            !_photoPluginOperationInProgress,
            installation?.IsInstalled is true && !_photoPluginOperationInProgress,
            canUninstall && !_photoPluginOperationInProgress);
        ApplyGallerySettingsPageState(page);
        _ = RefreshGallerySettingsStateAsync(
            _lifetimeCancellation?.Token ?? CancellationToken.None);
        page.ApplyAppearanceState(
            ViewModel.AppearanceSettings,
            _backgroundStatusText,
            _currentBackgroundSource);
        page.ApplyGeneralSettings(ViewModel.GeneralSettings);
        page.ApplyDownloadSettings(ViewModel.DownloadSettings);
        page.ApplyFileManagementState(CreateFileManagementSettingsViewState());
        page.ApplyScreenshotSettings(
            ViewModel.ScreenshotSettings,
            ResolveScreenshotFolderPath());
        page.ApplyHotkeySettings(ViewModel.GeneralSettings, ViewModel.ScreenshotSettings);
        page.ApplyGamepadState(
            ViewModel.GamepadSettings,
            CreateGamepadRuntimeState());
        page.ApplyDeveloperMode(ViewModel.DeveloperModeEnabled);
        _ = RefreshFileManagementSettingsStateAsync(
            _lifetimeCancellation?.Token ?? CancellationToken.None);
    }

    private GamepadRuntimeViewState CreateGamepadRuntimeState()
    {
        string status;
        if (!ViewModel.GamepadSettings.Enabled)
        {
            status = "已禁用；导航键与分享键交回系统处理。";
        }
        else if (!GamepadController.Initialized)
        {
            status = GamepadController.InitializationError is { Length: > 0 } error
                ? $"初始化失败：{error}"
                : "初始化失败；手柄增强未生效。";
        }
        else
        {
            status = GamepadController.GamepadConnected
                ? "已启用；检测到手柄。"
                : "已启用；未检测到手柄，连接后自动生效。";
        }

        return new GamepadRuntimeViewState(status, GamepadController.RuntimeMissing);
    }

    private void OnSettingsCloseRequested(object? sender, EventArgs e) => CloseDetails();

    private void OnSettingsDestinationChanged(
        object? sender,
        SettingsDestinationEventArgs e) =>
        _activeSettingsDestination = e.Destination;

    private void OnSettingsExternalDestinationRequested(
        object? sender,
        SettingsDestinationEventArgs e)
    {
        switch (e.Destination)
        {
            case SettingsDestination.Status:
                ShowStatusDrawer();
                break;
        }
    }

    private async void OnBackgroundGalleryClicked(object sender, RoutedEventArgs e)
    {
        var returnToSettings = ContentFrame.Content is SettingsPage;
        SetShellNavigationSelection(GalleryNavigationItem);
        await ShowGalleryAsync(returnToSettings);
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_launchSettingsOpen && e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseLaunchSettings();
            e.Handled = true;
        }
    }
}
