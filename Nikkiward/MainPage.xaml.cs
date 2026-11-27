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

namespace Nikkiward;

public sealed partial class MainPage : Page
{
    private const string JournalPagePath = "/tools/journal";
    private const string ResonanceHistoryPagePath = "/tools/journal/clothesPress";
    private const string PhotoAlbumPluginId = "nikkiward.photo-album-importer";
    private const string PhotoAlbumPluginDisplayName = "无限暖暖照片导入";
    private const string PhotoAlbumPluginVersion = "1.3";
    private static readonly JsonSerializerOptions JournalCaptureJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private const string DefaultBackgroundSource =
        "ms-appx:///Assets/NikkiDefaultBackground.jpg";
    private const ArtPreferredTheme DefaultBackgroundPreferredTheme =
        ArtPreferredTheme.Dark;
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly SemaphoreSlim _manualLaunchGate = new(1, 1);
    private readonly SemaphoreSlim _journalCaptureGate = new(1, 1);
    private readonly JournalSnapshotCache _journalCache = new();
    private readonly ResonanceHistoryCache _resonanceCache = new();
    private readonly WishHistoryStore _wishHistoryStore = new();
    private readonly LocalPluginCatalog _pluginCatalog = new();

    // Constructed on the UI thread so the service captures a real DispatcherQueue;
    // with a null queue it would mutate brushes on whichever thread published.
    private readonly ArtBackdropService _backdrop = new();
    private readonly MotionBackgroundImporter _motionBackgroundImporter;
    private readonly WallpaperAssetImporter _wallpaperAssetImporter;
    private CancellationTokenSource? _lifetimeCancellation;
    private DispatcherTimer? _launchStateTimer;
    private int _launchStateRefreshInProgress;
    private string? _manualGameRootPath;
    private string? _manualLauncherRootPath;
    private string? _manualChannelStoreRootPath;
    private CancellationTokenSource? _shellNavigationDebounceCancellation;
    private LauncherPage? _hostedLauncherPage;
    private JournalPage? _hostedJournalPage;
    private bool _journalSyncInProgress;
    private bool _resonanceSyncInProgress;
    private bool _photoPluginOperationInProgress;
    private string? _journalLastAutoSyncedUrl;
    private JournalRouteIntent _journalRouteIntent = JournalRouteIntent.Unknown;
    private DateTimeOffset _journalNextAutomaticSyncUtc = DateTimeOffset.MinValue;
    private int _journalConsecutiveFailures;
    private string? _resonanceLastAutoSyncedUrl;
    private DateTimeOffset _resonanceNextAutomaticSyncUtc = DateTimeOffset.MinValue;
    private int _resonanceConsecutiveFailures;
    private JournalSnapshot? _journalSnapshot;
    private ResonanceHistorySnapshot? _resonanceSnapshot;
    private WishHistoryProjection? _wishHistoryProjection;
    private DateTimeOffset _wishHistoryCapturedAtUtc;
    private LocalPluginInstallation? _photoPluginInstallation;
    private string _journalDurationText = "奇想手账未同步";
    private string _journalDurationDetailText =
        "打开官方奇想手账后，将页面显示的“游戏时长”数值粘贴到奇想手账页面。";
    private string _currentBackgroundSource = DefaultBackgroundSource;
    private DispatcherTimer? _backgroundCarouselTimer;
    private int _backgroundCarouselIndex;

    public MainPageViewModel ViewModel { get; } = new(
        App.Settings,
        new WindowsInstallationInspector(),
        new RedactedDiagnosticReportExporter());

    public string JournalSnapshotPath => _journalCache.SnapshotPath;

    public string JournalAssetsPath => _journalCache.AssetsPath;

    public string ResonanceSnapshotPath => _resonanceCache.SnapshotPath;

    public string WishHistoryPath => _wishHistoryStore.HistoryPath;

    public string JournalWebViewDataPath => JournalPage.WebViewDataPath;

    /// <summary>
    /// Raised whenever the set of interactive elements inside the window drag
    /// strip changes, so the shell can refresh its passthrough regions.
    /// </summary>
    public event EventHandler? TitleBarPassthroughChanged;

    /// <summary>
    /// Every element currently sitting inside the drag strip that must keep
    /// receiving pointer input. Derived on demand so navigation cannot leave a
    /// stale region registered.
    /// </summary>
    public IReadOnlyList<FrameworkElement> TitleBarPassthroughRegions
    {
        get
        {
            if (_studio != null) return [_studio.Toolbar];
            if (ContentFrame.IsEnabled && ContentFrame.Visibility == Visibility.Visible && CurrentWebPage is { } pages) return [pages.Toolbar];
            var regions = new List<FrameworkElement>(2);
            if (HomeInteractive && _hostedLauncherPage?.WebHome is { } web)
            {
                regions.Add(web.Masthead);
                if (web.MastheadTool.Width > 0) regions.Add(web.MastheadTool);
            }

            return regions;
        }
    }

    public MainPage()
    {
        _motionBackgroundImporter = new(() => ViewModel.GalleryReadOnlyRoots);
        _wallpaperAssetImporter = new(() => ViewModel.GalleryReadOnlyRoots);
        InitializeComponent();
        NativeSkin.Observe(this, ApplyNativeSurface);
        ApplyHomeSkin(ViewModel.AppearanceSettings.HomeSkin);
        HomeDataChanged += RefreshWebJournal;
        ContentFrame.Navigated += OnContentFrameNavigated;
        Unloaded += OnPageUnloaded;

        ContentFrame.Visibility = Visibility.Visible;
        ContentFrame.Navigate(
            typeof(LauncherPage),
            new LauncherNavigationContext(ViewModel));
        UpdateJournalDurationUi();

        RefreshOnArtSurfaceRegistration();
    }

    internal ArtBackdropService HomeBackdrop => _backdrop;
    private HomeSkin? _homeSkin;

    private void ApplyHomeSkin(string id)
    {
        var skin = HomeSkins.Resolve(id);
        if (skin == _homeSkin) return;
        LauncherBackground.ApplyArtFrame(default, default);
        _homeSkin = skin;
        SyncLauncherChrome();
        NotifyTitleBarPassthroughChanged();
    }
}
