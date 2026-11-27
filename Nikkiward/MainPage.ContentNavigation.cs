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

    internal event EventHandler? HomeInteractionChanged;
    internal bool HomeInteractive { get; private set; }

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
        bool pageOpen = contentOpen && !launcherOpen;

        if (HomeInteractive != launcherOpen)
        {
            HomeInteractive = launcherOpen;
            HomeInteractionChanged?.Invoke(this, EventArgs.Empty);
        }
        bool webCover = launcherOpen && _hostedLauncherPage?.WebHome?.CoverVisible == true;
        LauncherBackground.SetLauncherSurfaceState(
            launcherOpen && !webCover,
            launcherOpen, webCover);
        LauncherBackground.Visibility = Visible(!webCover);

        ContentFrame.Margin = pageOpen && CurrentWebPage == null ? new Thickness(0, 48, 0, 0) : default;
        PageHostBackdrop.Visibility = Visible(pageOpen);

        if (_hostedLauncherPage is { } launcherPage)
        {
            launcherPage.IsHitTestVisible = launcherOpen;
        }

        RefreshOnArtSurfaceRegistration();
        NotifyTitleBarPassthroughChanged();

        static Visibility Visible(bool value) =>
            value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshOnArtSurfaceRegistration()
    {
        var hosts = new List<FrameworkElement>();

        if (ContentFrame.Visibility == Visibility.Visible &&
            ContentFrame.Content is LauncherPage { OnArtHost: { } launcherOnArtHost })
        {
            hosts.Add(launcherOnArtHost);
        }

        _backdrop.AttachOnArtSurface(hosts.ToArray());
    }

    /// <summary>
    /// Announces the drag-strip change twice: once now, and once after layout
    /// has run. A region that just became visible still measures zero, so the
    /// immediate pass would register nothing and swallow its clicks.
    /// </summary>
    internal void NotifyTitleBarPassthroughChanged()
    {
        TitleBarPassthroughChanged?.Invoke(this, EventArgs.Empty);
        DispatcherQueue?.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => TitleBarPassthroughChanged?.Invoke(this, EventArgs.Empty));
    }
}
