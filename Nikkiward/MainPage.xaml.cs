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

public sealed partial class MainPage : Page
{
    private const double CompactLaunchSettingsHostWidth = 900d;
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
    private readonly MotionBackgroundImporter _motionBackgroundImporter = new();
    private CancellationTokenSource? _lifetimeCancellation;
    private DispatcherTimer? _launchStateTimer;
    private int _launchStateRefreshInProgress;
    private string? _manualGameRootPath;
    private string? _manualLauncherRootPath;
    private string? _manualChannelStoreRootPath;
    private SettingsDestination _activeSettingsDestination = SettingsDestination.Overview;
    private bool _launchSettingsOpen;
    private bool _returnToSettingsAfterGallery;
    private int _statusDrawerAnimationVersion;
    private int _launchSettingsAnimationVersion;
    private CancellationTokenSource? _shellNavigationDebounceCancellation;
    private bool _xamlInitialized;
    private ITitleBarMasthead? _hostedMasthead;
    private LauncherPage? _hostedLauncherPage;
    private LaunchSettingsPage? _hostedLaunchSettingsPage;
    private JournalPage? _hostedJournalPage;
    private ProfilePage? _hostedProfilePage;
    private SettingsPage? _hostedSettingsPage;
    private StatusPage? _hostedStatusPage;
    private PhotoPluginPage? _hostedPhotoPluginPage;
    private WishPage? _hostedWishPage;
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
    private string _journalDurationSourceText = "来源：官方手账 · 本次会话";
    private string _journalDurationDetailText =
        "打开官方奇想手账后，将页面显示的“游戏时长”数值粘贴到奇想手账页面。";
    private string _backgroundStatusText =
        "本次会话使用 Nikkiward 默认背景；可随时选择本地图片覆盖。";
    private string _currentBackgroundSource = DefaultBackgroundSource;
    private DispatcherTimer? _backgroundCarouselTimer;
    private int _backgroundCarouselIndex;

    public MainPageViewModel ViewModel { get; } = new(
        new JsonUserSettingsStore(),
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
            var regions = new List<FrameworkElement>(2);
            if (ProfileQuickSwitchHost.Visibility == Visibility.Visible)
            {
                regions.Add(ProfileQuickSwitchHost);
            }

            if (ContentFrame.Visibility == Visibility.Visible &&
                ContentFrame.Content is ITitleBarMasthead masthead &&
                masthead.MastheadInteractionRegion is { } mastheadRegion)
            {
                regions.Add(mastheadRegion);
            }

            if (LaunchSettingsFrame.Visibility == Visibility.Visible &&
                LaunchSettingsFrame.Content is ITitleBarMasthead launchSettingsMasthead &&
                launchSettingsMasthead.MastheadInteractionRegion is { } launchSettingsRegion)
            {
                regions.Add(launchSettingsRegion);
            }

            return regions;
        }
    }

    public MainPage()
    {
        InitializeComponent();
        ApplyLaunchSettingsHostLayout(RootGrid.ActualWidth);
        AppearanceRuntimeValues.ApplyOpacityTransition(ProfileQuickSwitchRail);
        _xamlInitialized = true;
        ContentFrame.Navigated += OnContentFrameNavigated;
        LaunchSettingsFrame.Navigated += OnLaunchSettingsFrameNavigated;
        Unloaded += OnPageUnloaded;

        ContentFrame.Visibility = Visibility.Visible;
        ContentFrame.Navigate(
            typeof(LauncherPage),
            new LauncherNavigationContext(ViewModel));
        SetShellNavigationSelection(LauncherNavigationItem);
        UpdateJournalDurationUi();

        RefreshOnArtSurfaceRegistration();
    }

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyLaunchSettingsHostLayout(e.NewSize.Width);

    private void ApplyLaunchSettingsHostLayout(double width) =>
        LaunchSettingsFrame.Margin = width < CompactLaunchSettingsHostWidth
            ? new Thickness(56, 48, 8, 8)
            : new Thickness(56, 60, 24, 24);
}
