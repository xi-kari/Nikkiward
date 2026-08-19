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
    /// <summary>
    /// Subscribes to whichever page is now hosted so its header keeps its
    /// clicks. Hooking the frame once means a page added later is covered
    /// without remembering to wire it at every call site.
    /// </summary>
    private void OnContentFrameNavigated(
        object sender,
        Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (_hostedMasthead is { } previous)
        {
            previous.MastheadInteractionRegionChanged -= OnMastheadRegionChanged;
        }

        _hostedMasthead = ContentFrame.Content as ITitleBarMasthead;
        if (_hostedMasthead is { } current)
        {
            current.MastheadInteractionRegionChanged += OnMastheadRegionChanged;
        }

        if (_hostedLauncherPage is { } previousLauncherPage)
        {
            UnsubscribeLauncherPage(previousLauncherPage);
        }

        _hostedLauncherPage = ContentFrame.Content as LauncherPage;
        if (_hostedLauncherPage is { } currentLauncherPage)
        {
            SubscribeLauncherPage(currentLauncherPage);
            currentLauncherPage.ApplyJournalDuration(
                _journalDurationText,
                _journalDurationDetailText);
        }

        if (_hostedJournalPage is { } previousJournalPage)
        {
            previousJournalPage.OpenRequested -= OnJournalOpenRequested;
            previousJournalPage.SyncRequested -= OnJournalSyncRequested;
            previousJournalPage.ExternalOpenRequested -= OnJournalExternalOpenRequested;
            previousJournalPage.ClearCacheRequested -= OnJournalClearCacheRequested;
            previousJournalPage.BrowserClosed -= OnJournalBrowserClosed;
            previousJournalPage.NavigationFinished -= OnJournalNavigationFinished;
            previousJournalPage.RouteChanged -= OnJournalRouteChanged;
        }

        _hostedJournalPage = ContentFrame.Content as JournalPage;
        if (_hostedJournalPage is { } currentJournalPage)
        {
            currentJournalPage.OpenRequested += OnJournalOpenRequested;
            currentJournalPage.SyncRequested += OnJournalSyncRequested;
            currentJournalPage.ExternalOpenRequested += OnJournalExternalOpenRequested;
            currentJournalPage.ClearCacheRequested += OnJournalClearCacheRequested;
            currentJournalPage.BrowserClosed += OnJournalBrowserClosed;
            currentJournalPage.NavigationFinished += OnJournalNavigationFinished;
            currentJournalPage.RouteChanged += OnJournalRouteChanged;
            if (_journalSnapshot is { } journalSnapshot)
            {
                currentJournalPage.ApplySnapshot(
                    journalSnapshot,
                    _journalDurationText,
                    _journalDurationSourceText);
            }
            else
            {
                currentJournalPage.ResetState(_journalDurationSourceText);
            }
        }

        if (_hostedProfilePage is { } previousProfilePage)
        {
            previousProfilePage.DiscoverRequested -= OnProfileDiscoverRequested;
            previousProfilePage.ProfileSelected -= OnProfileSelected;
            previousProfilePage.DetailsRequested -= OnProfileDetailsRequested;
            previousProfilePage.CloseRequested -= OnProfileCloseRequested;
            previousProfilePage.ChooseGameRootRequested -= OnProfileChooseGameRootRequested;
            previousProfilePage.ChooseLauncherRootRequested -= OnProfileChooseLauncherRootRequested;
            previousProfilePage.ChooseChannelStoreRootRequested -= OnProfileChooseChannelStoreRootRequested;
            previousProfilePage.PlanChannelStoreRequested -= OnProfilePlanChannelStoreRequested;
            previousProfilePage.BuildChannelStoreRequested -= OnProfileBuildChannelStoreRequested;
            previousProfilePage.ActivateChannelRequested -= OnProfileActivateChannelRequested;
            previousProfilePage.RollbackActivationRequested -= OnProfileRollbackActivationRequested;
        }

        _hostedProfilePage = ContentFrame.Content as ProfilePage;
        if (_hostedProfilePage is { } currentProfilePage)
        {
            currentProfilePage.DiscoverRequested += OnProfileDiscoverRequested;
            currentProfilePage.ProfileSelected += OnProfileSelected;
            currentProfilePage.DetailsRequested += OnProfileDetailsRequested;
            currentProfilePage.CloseRequested += OnProfileCloseRequested;
            currentProfilePage.ChooseGameRootRequested += OnProfileChooseGameRootRequested;
            currentProfilePage.ChooseLauncherRootRequested += OnProfileChooseLauncherRootRequested;
            currentProfilePage.ChooseChannelStoreRootRequested += OnProfileChooseChannelStoreRootRequested;
            currentProfilePage.PlanChannelStoreRequested += OnProfilePlanChannelStoreRequested;
            currentProfilePage.BuildChannelStoreRequested += OnProfileBuildChannelStoreRequested;
            currentProfilePage.ActivateChannelRequested += OnProfileActivateChannelRequested;
            currentProfilePage.RollbackActivationRequested += OnProfileRollbackActivationRequested;
        }

        if (_hostedSettingsPage is { } previousSettingsPage)
        {
            UnsubscribeSettingsPage(previousSettingsPage);
        }

        _hostedSettingsPage = ContentFrame.Content as SettingsPage;
        if (_hostedSettingsPage is { } currentSettingsPage)
        {
            SubscribeSettingsPage(currentSettingsPage);
            ApplySettingsPageState(currentSettingsPage);
        }

        if (_hostedPhotoPluginPage is { } previousPhotoPluginPage)
        {
            previousPhotoPluginPage.OpenRequested -= OnPhotoPluginOpenClicked;
            previousPhotoPluginPage.SettingsRequested -= OnPhotoPluginSettingsClicked;
        }

        _hostedPhotoPluginPage = ContentFrame.Content as PhotoPluginPage;
        if (_hostedPhotoPluginPage is { } currentPhotoPluginPage)
        {
            currentPhotoPluginPage.OpenRequested += OnPhotoPluginOpenClicked;
            currentPhotoPluginPage.SettingsRequested += OnPhotoPluginSettingsClicked;
            UpdatePhotoPluginPageState(currentPhotoPluginPage);
        }

        if (_hostedWishPage is { } previousWishPage)
        {
            previousWishPage.LoginRequested -= OnResonanceLoginRequested;
        }

        _hostedWishPage = ContentFrame.Content as WishPage;
        if (_hostedWishPage is { } currentWishPage)
        {
            currentWishPage.LoginRequested += OnResonanceLoginRequested;
            currentWishPage.ResetState();
            if (_resonanceSnapshot is { } resonanceSnapshot)
            {
                ApplyResonanceHistory(resonanceSnapshot);
            }

            if (_wishHistoryProjection is { } wishProjection)
            {
                ApplyWishHistoryProjection(
                    wishProjection,
                    _wishHistoryCapturedAtUtc);
            }
        }

        SyncLauncherChrome();
    }

    private void SubscribeLauncherPage(LauncherPage page)
    {
        page.DownloadStatusRequested += OnLauncherDownloadStatusRequested;
        page.PlayTimeRequested += OnLauncherPlayTimeRequested;
        page.OfficialFlowRequested += OnLauncherOfficialFlowRequested;
        page.CloseGameRequested += OnLauncherCloseGameRequested;
        page.LaunchSettingsRequested += OnLauncherLaunchSettingsRequested;
        page.JournalRequested += OnLauncherJournalRequested;
        page.GalleryRequested += OnLauncherGalleryRequested;
        page.BackgroundResetRequested += OnLauncherBackgroundResetRequested;
        page.ProfileRequested += OnLauncherProfileRequested;
    }

    private void UnsubscribeLauncherPage(LauncherPage page)
    {
        page.DownloadStatusRequested -= OnLauncherDownloadStatusRequested;
        page.PlayTimeRequested -= OnLauncherPlayTimeRequested;
        page.OfficialFlowRequested -= OnLauncherOfficialFlowRequested;
        page.CloseGameRequested -= OnLauncherCloseGameRequested;
        page.LaunchSettingsRequested -= OnLauncherLaunchSettingsRequested;
        page.JournalRequested -= OnLauncherJournalRequested;
        page.GalleryRequested -= OnLauncherGalleryRequested;
        page.BackgroundResetRequested -= OnLauncherBackgroundResetRequested;
        page.ProfileRequested -= OnLauncherProfileRequested;
    }

    private void OnLauncherDownloadStatusRequested(object? sender, EventArgs e) =>
        OnDownloadStatusClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherPlayTimeRequested(object? sender, EventArgs e) =>
        OnPlayTimeClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherOfficialFlowRequested(object? sender, EventArgs e) =>
        OnOfficialFlowClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherCloseGameRequested(object? sender, EventArgs e) =>
        OnCloseGameClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherLaunchSettingsRequested(object? sender, EventArgs e) =>
        OnLauncherSettingsClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherJournalRequested(object? sender, EventArgs e) =>
        OnServiceShortcutClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherGalleryRequested(object? sender, EventArgs e) =>
        OnBackgroundGalleryClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherBackgroundResetRequested(object? sender, EventArgs e) =>
        OnResetBackgroundClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherProfileRequested(object? sender, EventArgs e) =>
        ShowProfileOverlay();

    private void SubscribeSettingsPage(SettingsPage page)
    {
        page.CloseRequested += OnSettingsCloseRequested;
        page.DestinationChanged += OnSettingsDestinationChanged;
        page.ExternalDestinationRequested += OnSettingsExternalDestinationRequested;
        page.PhotoPluginImportRequested += OnSettingsPhotoPluginImportRequested;
        page.PhotoPluginOpenRequested += OnSettingsPhotoPluginOpenRequested;
        page.PhotoPluginUninstallRequested += OnSettingsPhotoPluginUninstallRequested;
        page.GalleryRootChooseRequested += OnSettingsGalleryRootChooseRequested;
        page.GalleryRootResetRequested += OnSettingsGalleryRootResetRequested;
        page.GalleryOpenRequested += OnSettingsGalleryOpenRequested;
        page.GalleryProtectionEnabledChanged += OnSettingsGalleryProtectionEnabledChanged;
        page.GalleryProtectionRootChooseRequested += OnSettingsGalleryProtectionRootChooseRequested;
        page.GalleryProtectionRootOpenRequested += OnSettingsGalleryProtectionRootOpenRequested;
        page.GalleryProtectionVerifyRequested += OnSettingsGalleryProtectionVerifyRequested;
        page.GalleryProtectionCleanRequested += OnSettingsGalleryProtectionCleanRequested;
        page.GalleryCacheRefreshRequested += OnSettingsGalleryCacheRefreshRequested;
        page.GalleryCacheClearRequested += OnSettingsGalleryCacheClearRequested;
        page.NikkiGalleryRegisterRequested += OnSettingsNikkiGalleryRegisterRequested;
        page.NikkiGalleryOpenRequested += OnSettingsNikkiGalleryOpenRequested;
        page.NikkiGalleryDisconnectRequested += OnSettingsNikkiGalleryDisconnectRequested;
        page.JournalOpenRequested += OnSettingsJournalOpenRequested;
        page.JournalCacheClearRequested += OnSettingsJournalCacheClearRequested;
        page.AppearanceSettingsChanged += OnSettingsAppearanceChanged;
        page.GeneralSettingsChanged += OnSettingsGeneralChanged;
        page.VisualEffectsRequested += OnSettingsVisualEffectsRequested;
        page.DownloadSettingsChanged += OnSettingsDownloadChanged;
        page.DownloadPathRequested += OnSettingsDownloadPathRequested;
        page.UserDataFolderRequested += OnSettingsUserDataFolderRequested;
        page.FileBackupRequested += OnSettingsFileBackupRequested;
        page.FileOpenBackupRequested += OnSettingsFileOpenBackupRequested;
        page.FileDeleteAllSettingsRequested += OnSettingsFileDeleteAllSettingsRequested;
        page.FileOpenLogsRequested += OnSettingsFileOpenLogsRequested;
        page.FileClearCacheRequested += OnSettingsFileClearCacheRequested;
        page.FileClearLauncherBackgroundChanged += OnSettingsFileClearLauncherBackgroundChanged;
        page.ScreenshotSettingsChanged += OnSettingsScreenshotSettingsChanged;
        page.ScreenshotFolderRequested += OnSettingsScreenshotFolderRequested;
        page.ScreenshotTestCaptureRequested += OnSettingsScreenshotTestCaptureRequested;
        page.ScreenshotClearThumbnailCacheRequested += OnSettingsScreenshotClearThumbnailCacheRequested;
        page.HotkeySettingsChanged += OnSettingsHotkeySettingsChanged;
        page.BackgroundChooseRequested += OnSettingsBackgroundChooseRequested;
        page.BackgroundResetRequested += OnSettingsBackgroundResetRequested;
        page.GamepadSettingsChanged += OnSettingsGamepadChanged;
        page.GamepadRuntimeDownloadRequested += OnSettingsGamepadRuntimeDownloadRequested;
        page.DiagnosticsExportRequested += OnSettingsDiagnosticsExportRequested;
        page.ProviderValidationDetailsRequested += OnSettingsProviderValidationDetailsRequested;
        page.DeveloperModeChanged += OnSettingsDeveloperModeChanged;
    }

    private void UnsubscribeSettingsPage(SettingsPage page)
    {
        page.CloseRequested -= OnSettingsCloseRequested;
        page.DestinationChanged -= OnSettingsDestinationChanged;
        page.ExternalDestinationRequested -= OnSettingsExternalDestinationRequested;
        page.PhotoPluginImportRequested -= OnSettingsPhotoPluginImportRequested;
        page.PhotoPluginOpenRequested -= OnSettingsPhotoPluginOpenRequested;
        page.PhotoPluginUninstallRequested -= OnSettingsPhotoPluginUninstallRequested;
        page.GalleryRootChooseRequested -= OnSettingsGalleryRootChooseRequested;
        page.GalleryRootResetRequested -= OnSettingsGalleryRootResetRequested;
        page.GalleryOpenRequested -= OnSettingsGalleryOpenRequested;
        page.GalleryProtectionEnabledChanged -= OnSettingsGalleryProtectionEnabledChanged;
        page.GalleryProtectionRootChooseRequested -= OnSettingsGalleryProtectionRootChooseRequested;
        page.GalleryProtectionRootOpenRequested -= OnSettingsGalleryProtectionRootOpenRequested;
        page.GalleryProtectionVerifyRequested -= OnSettingsGalleryProtectionVerifyRequested;
        page.GalleryProtectionCleanRequested -= OnSettingsGalleryProtectionCleanRequested;
        page.GalleryCacheRefreshRequested -= OnSettingsGalleryCacheRefreshRequested;
        page.GalleryCacheClearRequested -= OnSettingsGalleryCacheClearRequested;
        page.NikkiGalleryRegisterRequested -= OnSettingsNikkiGalleryRegisterRequested;
        page.NikkiGalleryOpenRequested -= OnSettingsNikkiGalleryOpenRequested;
        page.NikkiGalleryDisconnectRequested -= OnSettingsNikkiGalleryDisconnectRequested;
        page.JournalOpenRequested -= OnSettingsJournalOpenRequested;
        page.JournalCacheClearRequested -= OnSettingsJournalCacheClearRequested;
        page.AppearanceSettingsChanged -= OnSettingsAppearanceChanged;
        page.GeneralSettingsChanged -= OnSettingsGeneralChanged;
        page.VisualEffectsRequested -= OnSettingsVisualEffectsRequested;
        page.DownloadSettingsChanged -= OnSettingsDownloadChanged;
        page.DownloadPathRequested -= OnSettingsDownloadPathRequested;
        page.UserDataFolderRequested -= OnSettingsUserDataFolderRequested;
        page.FileBackupRequested -= OnSettingsFileBackupRequested;
        page.FileOpenBackupRequested -= OnSettingsFileOpenBackupRequested;
        page.FileDeleteAllSettingsRequested -= OnSettingsFileDeleteAllSettingsRequested;
        page.FileOpenLogsRequested -= OnSettingsFileOpenLogsRequested;
        page.FileClearCacheRequested -= OnSettingsFileClearCacheRequested;
        page.FileClearLauncherBackgroundChanged -= OnSettingsFileClearLauncherBackgroundChanged;
        page.ScreenshotSettingsChanged -= OnSettingsScreenshotSettingsChanged;
        page.ScreenshotFolderRequested -= OnSettingsScreenshotFolderRequested;
        page.ScreenshotTestCaptureRequested -= OnSettingsScreenshotTestCaptureRequested;
        page.ScreenshotClearThumbnailCacheRequested -= OnSettingsScreenshotClearThumbnailCacheRequested;
        page.HotkeySettingsChanged -= OnSettingsHotkeySettingsChanged;
        page.BackgroundChooseRequested -= OnSettingsBackgroundChooseRequested;
        page.BackgroundResetRequested -= OnSettingsBackgroundResetRequested;
        page.GamepadSettingsChanged -= OnSettingsGamepadChanged;
        page.GamepadRuntimeDownloadRequested -= OnSettingsGamepadRuntimeDownloadRequested;
        page.DiagnosticsExportRequested -= OnSettingsDiagnosticsExportRequested;
        page.ProviderValidationDetailsRequested -= OnSettingsProviderValidationDetailsRequested;
        page.DeveloperModeChanged -= OnSettingsDeveloperModeChanged;
    }

    private void OnSettingsPhotoPluginImportRequested(object? sender, EventArgs e) =>
        OnPhotoPluginImportClicked(sender ?? this, new RoutedEventArgs());

    private void OnSettingsPhotoPluginOpenRequested(object? sender, EventArgs e) =>
        OnPhotoPluginOpenClicked(sender, e);

    private void OnSettingsPhotoPluginUninstallRequested(object? sender, EventArgs e) =>
        OnPhotoPluginUninstallClicked(sender ?? this, new RoutedEventArgs());

    private void OnSettingsJournalOpenRequested(object? sender, EventArgs e) =>
        OnJournalOpenClicked(sender ?? this, new RoutedEventArgs());

    private void OnSettingsJournalCacheClearRequested(object? sender, EventArgs e) =>
        OnJournalCacheClearClicked(sender ?? this, new RoutedEventArgs());

    private void OnSettingsBackgroundChooseRequested(object? sender, EventArgs e) =>
        OnChooseBackgroundClicked(sender ?? this, new RoutedEventArgs());

    private void OnSettingsBackgroundResetRequested(object? sender, EventArgs e) =>
        OnResetBackgroundClicked(sender ?? this, new RoutedEventArgs());

    private void OnSettingsGamepadRuntimeDownloadRequested(object? sender, EventArgs e) =>
        OnGamepadRedistDownloadClicked(sender ?? this, new RoutedEventArgs());

    private void OnSettingsDiagnosticsExportRequested(object? sender, EventArgs e) =>
        OnExportDiagnosticsClicked(sender ?? this, new RoutedEventArgs());

    private void OnSettingsProviderValidationDetailsRequested(object? sender, EventArgs e) =>
        OnProviderValidationDetailsClicked(sender ?? this, new RoutedEventArgs());

    private async void OnSettingsDeveloperModeChanged(
        object? sender,
        DeveloperModeChangedEventArgs e)
    {
        try
        {
            await ViewModel.SaveDeveloperModeAsync(
                e.Enabled,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            _hostedSettingsPage?.ApplyDeveloperMode(ViewModel.DeveloperModeEnabled);
        }
        catch (OperationCanceledException)
        {
            _hostedSettingsPage?.ApplyDeveloperMode(ViewModel.DeveloperModeEnabled);
        }
        catch (Exception ex)
        {
            _hostedSettingsPage?.ApplyDeveloperMode(ViewModel.DeveloperModeEnabled);
            ViewModel.ReportUiError(
                $"开发者模式保存失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnMastheadRegionChanged(object? sender, EventArgs e)
    {
        NotifyTitleBarPassthroughChanged();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ActualThemeChanged -= OnActualThemeChanged;
        ActualThemeChanged += OnActualThemeChanged;
        EnsureSystemMotionSubscription();
        LauncherBackground.MotionPlaybackFailed -= OnMotionPlaybackFailed;
        LauncherBackground.MotionPlaybackFailed += OnMotionPlaybackFailed;
        LauncherBackground.Attach(_backdrop, App.MainWindow);

        var currentCancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(
            ref _lifetimeCancellation,
            currentCancellation);
        previousCancellation?.Cancel();

        _launchStateTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5),
        };
        _launchStateTimer.Tick -= OnLaunchStateTimerTick;
        _launchStateTimer.Tick += OnLaunchStateTimerTick;
        _launchStateTimer.Start();

        RefreshOnArtSurfaceRegistration();

        try
        {
            await LoadJournalSnapshotAsync(currentCancellation.Token);
            await LoadResonanceHistoryAsync(currentCancellation.Token);
            await LoadWishHistoryAsync(currentCancellation.Token);
            await ViewModel.InitializeAsync(currentCancellation.Token);
            App.MainWindow.ApplyCloseBehavior(ViewModel.GeneralSettings.CloseWindowBehavior);
            var hotkeyRegistration = App.MainWindow.ApplyHotkeys(
                ViewModel.GeneralSettings.MainWindowHotkey,
                ViewModel.ScreenshotSettings.Hotkey);
            if (!hotkeyRegistration.Succeeded)
            {
                ViewModel.ReportUiError(hotkeyRegistration.Message);
            }
            SyncLauncherChrome();
            await RestoreAppearanceAsync(
                ViewModel.AppearanceSettings,
                currentCancellation.Token);
            InitializeThemeModeUi();
            await ViewModel.LoadProviderValidationReceiptAsync(currentCancellation.Token);
            await RefreshPhotoPluginStateAsync(currentCancellation.Token);

            // After ViewModel.InitializeAsync: the stored gamepad section is
            // only available once settings have been read.
            InitializeGamepad();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"页面初始化失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        LauncherBackground.MotionPlaybackFailed -= OnMotionPlaybackFailed;
        _lifetimeCancellation?.Cancel();
        _launchStateTimer?.Stop();
        var shellNavigationCancellation = Interlocked.Exchange(
            ref _shellNavigationDebounceCancellation,
            null);
        shellNavigationCancellation?.Cancel();
        shellNavigationCancellation?.Dispose();
        DetachAppearanceRuntime();
        ActualThemeChanged -= OnActualThemeChanged;
        _backdrop.DetachOnArtSurface();
    }

    private async void OnMotionPlaybackFailed(
        object? sender,
        MotionPlaybackFailedEventArgs e)
    {
        try
        {
            var settings = ViewModel.AppearanceSettings;
            if (!settings.Background.MotionEnabled ||
                !string.Equals(
                    settings.Background.MotionSource,
                    e.Source,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await RestoreStaticBackgroundAsync(
                settings,
                _lifetimeCancellation?.Token ?? CancellationToken.None,
                "视频播放失败，已回退到静态背景。");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"视频背景恢复失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task RestoreAppearanceAsync(
        AppearanceSettings settings,
        CancellationToken cancellationToken)
    {
        ApplyAppearanceSettings(settings);
        var projection = AppearanceProjector.ProjectBackground(
            settings.Background,
            settings.Background.CarouselSources.Where(File.Exists),
            DefaultBackgroundSource);
        SetCurrentBackgroundSource(projection.Source);
        _backgroundStatusText = projection.UsesFallback
            ? "使用 Nikkiward 内置背景；可随时选择本地图片覆盖。"
            : $"当前背景：{Path.GetFileName(projection.Source)}";

        if (settings.Background.WallpaperEnginePresentation ==
                WallpaperEnginePresentation.HolographicCard &&
            await TryActivateConfiguredWallpaperEngineAsync(
                settings,
                WallpaperEnginePresentation.HolographicCard,
                cancellationToken))
        {
            return;
        }

        if (settings.Background.MotionEnabled &&
            await TryActivateConfiguredMotionAsync(settings, cancellationToken))
        {
            return;
        }

        await RestoreStaticBackgroundAsync(
            settings,
            cancellationToken,
            settings.Background.MotionEnabled
                ? "保存的视频背景不可用，已回退到静态背景。"
                : null);
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.RefreshAsync(_lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"刷新失败：{ex.GetType().Name}: {ex.Message}");
        }
    }
}
