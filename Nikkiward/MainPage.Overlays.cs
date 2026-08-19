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
    private void OnShowDetailsClicked(object sender, RoutedEventArgs e)
    {
        SetShellNavigationSelection(LauncherNavigationItem);
        if (StatusDrawer.Visibility == Visibility.Visible)
        {
            CloseDetails();
            return;
        }

        ShowStatusDrawer();
    }

    private void ShowStatusDrawer()
    {
        CloseProfileOverlay();
        HideLibrary();
        HideGallery();
        HideResonance();
        HidePhotoPlugin();
        OpenStatusDrawerSurface();
        if (StatusFrame.Content is not StatusPage)
        {
            StatusFrame.Navigate(
                typeof(StatusPage),
                new StatusNavigationContext(ViewModel),
                AppearanceRuntimeValues.CreateNavigationTransitionInfo());
        }
        HookStatusPage();
        if (StatusFrame.Content is StatusPage statusPage)
        {
            statusPage.ResetView();
        }
        SyncLauncherChrome();
    }

    private void HookStatusPage()
    {
        if (StatusFrame.Content is not StatusPage page || ReferenceEquals(_hostedStatusPage, page))
        {
            return;
        }

        if (_hostedStatusPage is { } previous)
        {
            previous.CloseRequested -= OnStatusCloseRequested;
            previous.RefreshRequested -= OnStatusRefreshRequested;
            previous.OfficialFlowRequested -= OnStatusOfficialFlowRequested;
            previous.ExportRequested -= OnStatusExportRequested;
        }
        _hostedStatusPage = page;
        page.CloseRequested += OnStatusCloseRequested;
        page.RefreshRequested += OnStatusRefreshRequested;
        page.OfficialFlowRequested += OnStatusOfficialFlowRequested;
        page.ExportRequested += OnStatusExportRequested;
    }

    private void OnStatusCloseRequested(object? sender, EventArgs e) => CloseDetails();
    private void OnStatusRefreshRequested(object? sender, EventArgs e) => OnRefreshClicked(sender ?? this, new RoutedEventArgs());
    private void OnStatusOfficialFlowRequested(object? sender, EventArgs e) => OnOfficialFlowClicked(sender ?? this, new RoutedEventArgs());
    private void OnStatusExportRequested(object? sender, EventArgs e) => OnExportDiagnosticsClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (_launchSettingsOpen)
        {
            CloseLaunchSettings();
            return;
        }

        ShowLaunchSettings();
    }

    private void OnLaunchSettingsMaskTapped(object sender, TappedRoutedEventArgs e)
    {
        CloseLaunchSettings();
    }

    private void OnCloseLaunchSettingsClicked(object sender, RoutedEventArgs e)
    {
        CloseLaunchSettings();
    }

    private void ShowLaunchSettings()
    {
        CloseProfileOverlay();
        HideLibrary();
        HideGallery();
        HideResonance();
        HidePhotoPlugin();
        CloseDetails();

        _launchSettingsOpen = true;
        OpenLaunchSettingsSurface();
        if (LaunchSettingsFrame.Content is LaunchSettingsPage page)
        {
            page.ApplyBackgroundPreview(LauncherBackground.Source);
            page.ApplyAppearanceSettings(ViewModel.AppearanceSettings);
            page.ResetToBasic();
        }
        else
        {
            LaunchSettingsFrame.Navigate(
                typeof(LaunchSettingsPage),
                new LaunchSettingsNavigationContext(
                    ViewModel,
                    LauncherBackground.Source),
                AppearanceRuntimeValues.CreateNavigationTransitionInfo());
        }
        SyncLauncherChrome();
    }

    private void CloseLaunchSettings()
    {
        _launchSettingsOpen = false;
        _hostedLaunchSettingsPage?.ResetToBasic();
        HideLaunchSettingsSurface();
        SyncLauncherChrome();
    }

    private void OnLaunchSettingsFrameNavigated(
        object sender,
        Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (_hostedLaunchSettingsPage is { } previous)
        {
            previous.CloseRequested -= OnLaunchSettingsCloseRequested;
            previous.ChooseGameRootRequested -= OnLaunchSettingsChooseGameRootRequested;
            previous.WallpaperImportRequested -= OnLaunchSettingsWallpaperImportRequested;
            previous.BackgroundResetRequested -= OnLaunchSettingsBackgroundResetRequested;
            previous.MastheadSubtitleSaveRequested -= OnLaunchSettingsMastheadSubtitleSaveRequested;
            previous.MastheadInteractionRegionChanged -= OnMastheadRegionChanged;
        }

        _hostedLaunchSettingsPage = LaunchSettingsFrame.Content as LaunchSettingsPage;
        if (_hostedLaunchSettingsPage is { } current)
        {
            current.CloseRequested += OnLaunchSettingsCloseRequested;
            current.ChooseGameRootRequested += OnLaunchSettingsChooseGameRootRequested;
            current.WallpaperImportRequested += OnLaunchSettingsWallpaperImportRequested;
            current.BackgroundResetRequested += OnLaunchSettingsBackgroundResetRequested;
            current.MastheadSubtitleSaveRequested += OnLaunchSettingsMastheadSubtitleSaveRequested;
            current.MastheadInteractionRegionChanged += OnMastheadRegionChanged;
        }

        RefreshOnArtSurfaceRegistration();
        NotifyTitleBarPassthroughChanged();
    }

    private void OnLaunchSettingsCloseRequested(object? sender, EventArgs e) =>
        CloseLaunchSettings();

    private void OnLaunchSettingsChooseGameRootRequested(object? sender, EventArgs e) =>
        OnChooseGameRootClicked(sender ?? this, new RoutedEventArgs());

    private async void OnLaunchSettingsWallpaperImportRequested(
        object? sender,
        WallpaperImportRequestedEventArgs e) =>
        await ChooseWallpaperAsync(e.Mode);

    private async void OnLaunchSettingsMastheadSubtitleSaveRequested(
        object? sender,
        MastheadSubtitleChangedEventArgs e)
    {
        try
        {
            await SaveAndApplyAppearanceAsync(
                ViewModel.AppearanceSettings with
                {
                    LauncherMastheadSubtitle = e.Subtitle,
                });
            _hostedLaunchSettingsPage?.ApplyAppearanceSettings(
                ViewModel.AppearanceSettings);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"主页文案保存失败：{ex.GetType().Name}: {ex.Message}");
            _hostedLaunchSettingsPage?.ApplyAppearanceSettings(
                ViewModel.AppearanceSettings);
        }
    }

    private void OnLaunchSettingsBackgroundResetRequested(object? sender, EventArgs e) =>
        OnResetBackgroundClicked(sender ?? this, new RoutedEventArgs());

    private void OpenStatusDrawerSurface()
    {
        var animationVersion = ++_statusDrawerAnimationVersion;
        var duration = AppearanceRuntimeValues.ReadMilliseconds("MotionPanelOpen");
        DrawerMask.IsHitTestVisible = true;
        StatusDrawer.IsHitTestVisible = true;
        DrawerMask.OpacityTransition = null;
        StatusDrawer.OpacityTransition = null;
        StatusDrawer.TranslationTransition = null;
        DrawerMask.Opacity = duration > TimeSpan.Zero ? 0d : 1d;
        StatusDrawer.Opacity = duration > TimeSpan.Zero ? 0d : 1d;
        StatusDrawer.Translation = duration > TimeSpan.Zero
            ? new Vector3(24f, 0f, 32f)
            : new Vector3(0f, 0f, 32f);
        DrawerMask.Visibility = Visibility.Visible;
        StatusDrawer.Visibility = Visibility.Visible;
        AppearanceRuntimeValues.ApplyOpacityTransition(
            DrawerMask,
            "MotionPanelOpen");
        AppearanceRuntimeValues.ApplyOpacityTransition(
            StatusDrawer,
            "MotionPanelOpen");
        AppearanceRuntimeValues.ApplyTranslationTransition(
            StatusDrawer,
            "MotionPanelOpen");
        if (duration > TimeSpan.Zero)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (animationVersion == _statusDrawerAnimationVersion)
                {
                    DrawerMask.Opacity = 1d;
                    StatusDrawer.Opacity = 1d;
                    StatusDrawer.Translation = new Vector3(0f, 0f, 32f);
                }
            });
        }
    }

    private async void HideStatusDrawer()
    {
        var animationVersion = ++_statusDrawerAnimationVersion;
        if (StatusDrawer.Visibility != Visibility.Visible &&
            DrawerMask.Visibility != Visibility.Visible)
        {
            return;
        }

        var duration = AppearanceRuntimeValues.ReadMilliseconds("MotionPanelClose");
        DrawerMask.IsHitTestVisible = false;
        StatusDrawer.IsHitTestVisible = false;
        AppearanceRuntimeValues.ApplyOpacityTransition(
            DrawerMask,
            "MotionPanelClose");
        AppearanceRuntimeValues.ApplyOpacityTransition(
            StatusDrawer,
            "MotionPanelClose");
        AppearanceRuntimeValues.ApplyTranslationTransition(
            StatusDrawer,
            "MotionPanelClose");
        DrawerMask.Opacity = 0d;
        StatusDrawer.Opacity = 0d;
        StatusDrawer.Translation = new Vector3(24f, 0f, 32f);
        if (duration > TimeSpan.Zero)
        {
            await Task.Delay(duration);
        }

        if (animationVersion != _statusDrawerAnimationVersion)
        {
            return;
        }

        StatusDrawer.Visibility = Visibility.Collapsed;
        DrawerMask.Visibility = Visibility.Collapsed;
        DrawerMask.OpacityTransition = null;
        StatusDrawer.OpacityTransition = null;
        StatusDrawer.TranslationTransition = null;
        DrawerMask.Opacity = 1d;
        StatusDrawer.Opacity = 1d;
        StatusDrawer.Translation = new Vector3(0f, 0f, 32f);
        SyncLauncherChrome();
    }

    private void OpenLaunchSettingsSurface()
    {
        var animationVersion = ++_launchSettingsAnimationVersion;
        var duration = AppearanceRuntimeValues.ReadMilliseconds("MotionPanelOpen");
        LaunchSettingsMask.IsHitTestVisible = true;
        LaunchSettingsFrame.IsHitTestVisible = true;
        LaunchSettingsMask.OpacityTransition = null;
        LaunchSettingsFrame.OpacityTransition = null;
        LaunchSettingsFrame.TranslationTransition = null;
        LaunchSettingsMask.Opacity = duration > TimeSpan.Zero ? 0d : 1d;
        LaunchSettingsFrame.Opacity = duration > TimeSpan.Zero ? 0d : 1d;
        LaunchSettingsFrame.Translation = duration > TimeSpan.Zero
            ? new Vector3(0f, 12f, 0f)
            : Vector3.Zero;
        LaunchSettingsMask.Visibility = Visibility.Visible;
        LaunchSettingsFrame.Visibility = Visibility.Visible;
        AppearanceRuntimeValues.ApplyOpacityTransition(
            LaunchSettingsMask,
            "MotionPanelOpen");
        AppearanceRuntimeValues.ApplyOpacityTransition(
            LaunchSettingsFrame,
            "MotionPanelOpen");
        AppearanceRuntimeValues.ApplyTranslationTransition(
            LaunchSettingsFrame,
            "MotionPanelOpen");
        if (duration > TimeSpan.Zero)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (animationVersion == _launchSettingsAnimationVersion)
                {
                    LaunchSettingsMask.Opacity = 1d;
                    LaunchSettingsFrame.Opacity = 1d;
                    LaunchSettingsFrame.Translation = Vector3.Zero;
                }
            });
        }
    }

    private async void HideLaunchSettingsSurface()
    {
        var animationVersion = ++_launchSettingsAnimationVersion;
        if (LaunchSettingsFrame.Visibility != Visibility.Visible &&
            LaunchSettingsMask.Visibility != Visibility.Visible)
        {
            return;
        }

        var duration = AppearanceRuntimeValues.ReadMilliseconds("MotionPanelClose");
        LaunchSettingsMask.IsHitTestVisible = false;
        LaunchSettingsFrame.IsHitTestVisible = false;
        AppearanceRuntimeValues.ApplyOpacityTransition(
            LaunchSettingsMask,
            "MotionPanelClose");
        AppearanceRuntimeValues.ApplyOpacityTransition(
            LaunchSettingsFrame,
            "MotionPanelClose");
        AppearanceRuntimeValues.ApplyTranslationTransition(
            LaunchSettingsFrame,
            "MotionPanelClose");
        LaunchSettingsMask.Opacity = 0d;
        LaunchSettingsFrame.Opacity = 0d;
        LaunchSettingsFrame.Translation = new Vector3(0f, 12f, 0f);
        if (duration > TimeSpan.Zero)
        {
            await Task.Delay(duration);
        }

        if (animationVersion != _launchSettingsAnimationVersion)
        {
            return;
        }

        LaunchSettingsFrame.Visibility = Visibility.Collapsed;
        LaunchSettingsMask.Visibility = Visibility.Collapsed;
        LaunchSettingsMask.OpacityTransition = null;
        LaunchSettingsFrame.OpacityTransition = null;
        LaunchSettingsFrame.TranslationTransition = null;
        LaunchSettingsMask.Opacity = 1d;
        LaunchSettingsFrame.Opacity = 1d;
        LaunchSettingsFrame.Translation = Vector3.Zero;
        SyncLauncherChrome();
    }

    private void OnDrawerMaskTapped(object sender, TappedRoutedEventArgs e)
    {
        CloseDetails();
    }
}
