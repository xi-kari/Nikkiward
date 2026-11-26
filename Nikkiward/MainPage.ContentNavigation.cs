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
using Nikkiward.Features.Launcher.Skins;
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
using Windows.UI.ViewManagement;

namespace Nikkiward;

public sealed partial class MainPage
{
    private readonly UISettings _motionUiSettings = new();
    private bool _motionUiSettingsSubscribed;

    private void EnsureSystemMotionSubscription()
    {
        if (_motionUiSettingsSubscribed ||
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            return;
        }

        try
        {
            _motionUiSettings.AnimationsEnabledChanged += OnSystemAnimationsEnabledChanged;
            _motionUiSettingsSubscribed = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
        }
    }

    private void OnSystemAnimationsEnabledChanged(UISettings sender, object args)
    {
        if (DispatcherQueue is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyMotionPreference(ViewModel.AppearanceSettings.Motion);
            LauncherBackground.ConfigureAppearance(ViewModel.AppearanceSettings);
            HomeInteractionChanged?.Invoke(this, EventArgs.Empty);
            SyncLauncherChrome();
        });
    }

    private async Task ShowGalleryAsync(
        bool returnToSettings = false,
        GalleryViewMode viewMode = GalleryViewMode.All, GalleryPhotoItemViewModel? photo = null)
    {
        CloseProfileOverlay();
        HideLibrary();
        HideResonance();
        HidePhotoPlugin();
        _returnToSettingsAfterGallery = returnToSettings;
        HideStatusDrawer();
        ContentFrame.Visibility = Visibility.Visible;
        SyncLauncherChrome();

        if (photo == null && TryShowWebPage(viewMode == GalleryViewMode.Favorites ? "favorites" : "gallery")) return;
        var cancellationToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
        await ViewModel.InitializeAsync(cancellationToken);
        if (ContentFrame.Content is GalleryPage galleryPage)
        {
            galleryPage.ApplyAppearanceSettings(ViewModel.AppearanceSettings);
            galleryPage.SetSurfaceActive(true);
            await galleryPage.LoadForProfileAsync(
                ViewModel.SelectedProfileId,
                ViewModel.GameRootPath,
                ViewModel.GalleryRootPath,
                cancellationToken,
                viewMode,
                forceReload: photo != null, photo: photo);
            return;
        }

        ContentFrame.Navigate(
            typeof(GalleryPage),
            new GalleryNavigationContext(
                ViewModel.SelectedProfileId,
                ViewModel.GameRootPath,
                ViewModel.GalleryRootPath,
                GalleryProtection,
                cancellationToken,
                viewMode,
                ViewModel.AppearanceSettings, photo),
            AppearanceRuntimeValues.CreateNavigationTransitionInfo());
    }

    private void ShowLibrary(bool officialBrowser = false)
    {
        CloseProfileOverlay();
        HideGallery();
        HideResonance();
        HidePhotoPlugin();
        HideStatusDrawer();
        ContentFrame.Visibility = Visibility.Visible;
        SyncLauncherChrome();
        if (!officialBrowser && TryShowWebPage("journal")) return;
        if (ContentFrame.Content is JournalPage journalPage)
        {
            journalPage.ResetScroll();
            return;
        }

        ContentFrame.Navigate(
            typeof(JournalPage),
            null,
            AppearanceRuntimeValues.CreateNavigationTransitionInfo());
    }

    private void ShowResonance()
    {
        CloseProfileOverlay();
        HideLibrary();
        HideGallery();
        HidePhotoPlugin();
        HideStatusDrawer();
        ContentFrame.Visibility = Visibility.Visible;
        SyncLauncherChrome();
        if (TryShowWebPage("wish")) return;
        if (ContentFrame.Content is WishPage wishPage)
        {
            wishPage.ResetScroll();
            return;
        }

        ContentFrame.Navigate(
            typeof(WishPage),
            null,
            AppearanceRuntimeValues.CreateNavigationTransitionInfo());
    }

    private void ShowPhotoPlugin()
    {
        if (_photoPluginInstallation?.IsInstalled is not true)
        {
            ShowSettingsPage(destination: SettingsDestination.Plugins);
            return;
        }

        CloseProfileOverlay();
        HideLibrary();
        HideGallery();
        HideResonance();
        HideStatusDrawer();
        ContentFrame.Visibility = Visibility.Visible;
        SyncLauncherChrome();

        if (ContentFrame.Content is PhotoPluginPage photoPluginPage)
        {
            UpdatePhotoPluginPageState(photoPluginPage);
            return;
        }

        ContentFrame.Navigate(
            typeof(PhotoPluginPage),
            null,
            AppearanceRuntimeValues.CreateNavigationTransitionInfo());
    }

    /// <summary>
    /// Single source of truth for launcher chrome visibility. Every navigation
    /// path calls this instead of toggling elements itself, so adding a page
    /// cannot leave launcher content bleeding through it.
    /// </summary>
    internal event EventHandler? HomeInteractionChanged;
    internal bool HomeInteractive { get; private set; }

    internal void SetHomeDimmed(bool dimmed)
    {
        AppearanceRuntimeValues.ApplyOpacityTransition(ShellNavigation);
        ShellNavigation.IsHitTestVisible = !dimmed;
        ShellNavigation.Opacity = dimmed ? 0 : 1;
        NotifyTitleBarPassthroughChanged();
    }

    internal async Task<string?> SceneCoverAsync(string package, CancellationToken token)
    {
        var preview = await Task.Run(() => WallpaperPackageReader.Inspect(package).PreviewPath, token);
        if (preview != null) return preview;
        var cached = await GalleryThumbnailFile.GetOrCreateAsync(HomeBackground.StillFolder(ApplicationDataPaths.Root),
            null, new FileInfo(package), false, LauncherBackground.CaptureSceneFrameAsync, token);
        return cached?.FilePath;
    }

    internal string HomeStillSource => _currentBackgroundSource;

    internal void SyncLauncherChrome()
    {
        EnsureSystemMotionSubscription();
        bool contentOpen = ContentFrame.Visibility == Visibility.Visible;
        bool launcherOpen = contentOpen && ContentFrame.Content is LauncherPage;
        bool profilePickerOpen = _profileFlyout?.IsOpen == true;
        bool pageOpen = contentOpen && !launcherOpen;

        Frame? modal = LaunchSettingsFrame.Visibility == Visibility.Visible ? LaunchSettingsFrame :
            StatusDrawer.Visibility == Visibility.Visible ? StatusFrame : null;
        bool overlayOpen = modal != null || profilePickerOpen;
        bool launcherSurfaceInteractive = launcherOpen && !overlayOpen;
        SyncModalFocus(modal);
        if (HomeInteractive != launcherSurfaceInteractive)
        {
            HomeInteractive = launcherSurfaceInteractive;
            HomeInteractionChanged?.Invoke(this, EventArgs.Empty);
        }
        bool webCover = launcherOpen && _hostedLauncherPage?.WebHome?.CoverVisible == true;
        LauncherBackground.SetLauncherSurfaceState(
            launcherOpen && !webCover,
            launcherSurfaceInteractive, webCover);
        LauncherBackground.Visibility = Visible(!webCover);

        bool webHome = launcherOpen && _hostedLauncherPage?.WebHome != null;
        bool webPage = pageOpen && CurrentWebPage != null;
        ContentFrame.Margin = webPage ? default : !webHome && _homeSkin?.LeftEdge == true ? new Thickness(90, 0, 0, 0) : pageOpen ? new Thickness(0, 48, 0, 0) : default;
        ShellNavigation.Visibility = Visible(!webPage && !webHome && _studio == null);
        PageHostBackdrop.Visibility = Visible(pageOpen);

        if (_hostedLauncherPage is { } launcherPage)
        {
            launcherPage.IsHitTestVisible = launcherOpen && !overlayOpen;
        }

        RefreshOnArtSurfaceRegistration();
        NotifyTitleBarPassthroughChanged();

        static Visibility Visible(bool value) =>
            value ? Visibility.Visible : Visibility.Collapsed;
    }

    private Frame? _modalFrame;
    private UIElement? _focusBeforeModal;

    private void SyncModalFocus(Frame? modal)
    {
        if (modal == _modalFrame) return;
        if (_modalFrame == null && XamlRoot != null)
            _focusBeforeModal = FocusManager.GetFocusedElement(XamlRoot) as UIElement;
        _modalFrame = modal;
        ContentFrame.IsEnabled = ShellNavigation.IsEnabled = modal == null;
        StatusFrame.IsEnabled = modal == StatusFrame;
        LaunchSettingsFrame.IsEnabled = modal == LaunchSettingsFrame;
        if (modal != null)
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_modalFrame == modal)
                    (FocusManager.FindFirstFocusableElement(modal) as UIElement)?.Focus(FocusState.Programmatic);
            });
        else
        {
            if (_focusBeforeModal is FrameworkElement { IsLoaded: true } trigger) trigger.Focus(FocusState.Programmatic);
            _focusBeforeModal = null;
        }
    }

    private void RefreshOnArtSurfaceRegistration()
    {
        var hosts = new List<FrameworkElement>();
        if (ContentFrame.Content is LauncherPage)
            hosts.Add(ShellNavigation);
        else
            ShellNavigation.RequestedTheme = ElementTheme.Default;
        if (_profileFlyout?.Content is FrameworkElement picker) hosts.Add(picker);

        if (ContentFrame.Visibility == Visibility.Visible &&
            ContentFrame.Content is LauncherPage { OnArtHost: { } launcherOnArtHost })
        {
            hosts.Add(launcherOnArtHost);
        }

        if (LaunchSettingsFrame.Visibility == Visibility.Visible &&
            LaunchSettingsFrame.Content is LaunchSettingsPage { OnArtHost: { } launchSettingsOnArtHost })
        {
            hosts.Add(launchSettingsOnArtHost);
        }

        if (StatusDrawer.Visibility == Visibility.Visible)
        {
            hosts.Add(StatusDrawer);
        }
        else
        {
            StatusDrawer.RequestedTheme = ElementTheme.Default;
        }

        _backdrop.AttachOnArtSurface(hosts.ToArray());
    }

    /// <summary>
    /// Announces the drag-strip change twice: once now, and once after layout
    /// has run. A region that just became visible still measures zero, so the
    /// immediate pass would register nothing and swallow its clicks.
    /// </summary>
    private void NotifyTitleBarPassthroughChanged()
    {
        TitleBarPassthroughChanged?.Invoke(this, EventArgs.Empty);
        DispatcherQueue?.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => TitleBarPassthroughChanged?.Invoke(this, EventArgs.Empty));
    }

    private void HideLibrary()
    {
        if (ContentFrame.Content is JournalPage)
        {
            ContentFrame.Visibility = Visibility.Collapsed;
        }
    }

    private void HideResonance()
    {
        if (ContentFrame.Content is WishPage)
        {
            ContentFrame.Visibility = Visibility.Collapsed;
        }
    }

    private void HidePhotoPlugin()
    {
        if (ContentFrame.Content is PhotoPluginPage)
        {
            ContentFrame.Visibility = Visibility.Collapsed;
        }
    }

    private void HideGallery()
    {
        if (ContentFrame.Content is GalleryPage galleryPage)
        {
            galleryPage.SetSurfaceActive(false);
            galleryPage.ClosePreview();
            ContentFrame.Visibility = Visibility.Collapsed;
            _returnToSettingsAfterGallery = false;
            SyncLauncherChrome();
        }
    }

    private void OnCloseGalleryClicked(object sender, RoutedEventArgs e)
    {
        if (_returnToSettingsAfterGallery)
        {
            _returnToSettingsAfterGallery = false;
            ShowSettingsPage(restoreDestination: true);
            return;
        }

        CloseDetails();
    }
}
