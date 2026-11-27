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
    private void OnContentFrameNavigated(
        object sender,
        Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (_hostedLauncherPage is { } previousLauncherPage)
        {
            UnsubscribeLauncherPage(previousLauncherPage);
        }

        _hostedLauncherPage = ContentFrame.Content as LauncherPage;
        if (_hostedLauncherPage is { } currentLauncherPage)
        {
            SubscribeLauncherPage(currentLauncherPage);
        }

        if (_hostedJournalPage is { } previousJournalPage)
        {
            previousJournalPage.SyncRequested -= OnJournalSyncRequested;
            previousJournalPage.ExternalOpenRequested -= OnJournalExternalOpenRequested;
            previousJournalPage.ClearCacheRequested -= OnJournalClearCacheRequested;
            previousJournalPage.NavigationFinished -= OnJournalNavigationFinished;
            previousJournalPage.RouteChanged -= OnJournalRouteChanged;
        }

        _hostedJournalPage = ContentFrame.Content as JournalPage;
        if (_hostedJournalPage is { } currentJournalPage)
        {
            RefreshPlayTimeViews();
            currentJournalPage.SyncRequested += OnJournalSyncRequested;
            currentJournalPage.ExternalOpenRequested += OnJournalExternalOpenRequested;
            currentJournalPage.ClearCacheRequested += OnJournalClearCacheRequested;
            currentJournalPage.NavigationFinished += OnJournalNavigationFinished;
            currentJournalPage.RouteChanged += OnJournalRouteChanged;
        }

        SyncLauncherChrome();
    }

    private void OnHomeSkinChanged(object? sender, EventArgs args) => SyncLauncherChrome();

    private void SubscribeLauncherPage(LauncherPage page)
    {
        page.SkinChanged += OnHomeSkinChanged;
        page.NavigationRequested += OnNavigationItemInvoked;
        page.PhotoRequested += OnRecentPhotoRequested;
        page.DownloadStatusRequested += OnLauncherDownloadStatusRequested;
        page.PlayTimeRequested += OnLauncherPlayTimeRequested;
        page.OfficialFlowRequested += OnLauncherOfficialFlowRequested;
        page.CloseGameRequested += OnLauncherCloseGameRequested;
        page.JournalRequested += OnLauncherJournalRequested;
        page.GalleryRequested += OnLauncherGalleryRequested;
        page.BackgroundResetRequested += OnLauncherBackgroundResetRequested;
    }

    private void UnsubscribeLauncherPage(LauncherPage page)
    {
        page.SkinChanged -= OnHomeSkinChanged;
        page.NavigationRequested -= OnNavigationItemInvoked;
        page.PhotoRequested -= OnRecentPhotoRequested;
        page.DownloadStatusRequested -= OnLauncherDownloadStatusRequested;
        page.PlayTimeRequested -= OnLauncherPlayTimeRequested;
        page.OfficialFlowRequested -= OnLauncherOfficialFlowRequested;
        page.CloseGameRequested -= OnLauncherCloseGameRequested;
        page.JournalRequested -= OnLauncherJournalRequested;
        page.GalleryRequested -= OnLauncherGalleryRequested;
        page.BackgroundResetRequested -= OnLauncherBackgroundResetRequested;
    }

    private void OnLauncherDownloadStatusRequested(object? sender, EventArgs e) =>
        OnDownloadStatusClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherPlayTimeRequested(object? sender, EventArgs e) =>
        OnPlayTimeClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherOfficialFlowRequested(object? sender, EventArgs e) =>
        OnOfficialFlowClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherCloseGameRequested(object? sender, EventArgs e) =>
        OnCloseGameClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherJournalRequested(object? sender, EventArgs e) =>
        OnServiceShortcutClicked(sender ?? this, new RoutedEventArgs());

    private void OnLauncherGalleryRequested(object? sender, EventArgs e) =>
        ShowWebPage("gallery");

    private void OnLauncherBackgroundResetRequested(object? sender, EventArgs e) =>
        OnResetBackgroundClicked(sender ?? this, new RoutedEventArgs());

    private void OnRecentPhotoRequested(object? sender, GalleryPhotoItemViewModel photo) =>
        OpenStudio(((LauncherPage)sender!).RecentPhotos, photo);

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
            try { PhotoDirectoryPolicy.ScreenshotFolder(ResolveScreenshotFolderPath(), ViewModel.GalleryReadOnlyRoots); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                ViewModel.ReportUiError($"截图目录不可用：{ex.Message}");
            }
            StartPlanner();
            await InitializePlayTimeAsync(currentCancellation.Token);
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
        _hostedLauncherPage?.ReleaseHome();
        LauncherBackground.MotionPlaybackFailed -= OnMotionPlaybackFailed;
        _lifetimeCancellation?.Cancel();
        _plannerTimer?.Stop();
        StopPlayTime();
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
                _lifetimeCancellation?.Token ?? CancellationToken.None);
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
#if DEBUG
        if (HomePreview.Art is { } previewArt)
        {
            settings = settings with { Background = new BackgroundArtSettings { SelectedSource = previewArt } };
        }
#endif
        ApplyAppearanceSettings(settings);
        var projection = AppearanceProjector.ProjectBackground(
            settings.Background,
            settings.Background.CarouselSources.Where(File.Exists),
            DefaultBackgroundSource);
        SetCurrentBackgroundSource(projection.Source);

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
            cancellationToken);
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
