using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Nikkiward.Features.Shell;
using Nikkiward.Models;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Settings;

public sealed partial class SettingsPage : PageBase
{
    private MainPageViewModel _viewModel = null!;
    private SettingsStoragePaths _storagePaths = null!;
    private SettingsDestination _activeDestination;
    private readonly SettingsHomeView _homeView = new();
    private readonly GeneralAppearanceSettingsView _generalView = new();
    private readonly CommonSettingsView _commonView = new();
    private readonly DownloadSettingsView _downloadView = new();
    private readonly FileManagementSettingsView _fileManagementView = new();
    private readonly ScreenshotSettingsView _screenshotView = new();
    private readonly HotkeySettingsView _hotkeyView = new();
    private readonly GallerySettingsView _galleryView = new();
    private readonly JournalStorageSettingsView _journalView = new();
    private readonly PluginSettingsView _pluginView = new();
    private readonly GamepadSettingsView _gamepadView = new();
    private readonly AboutSettingsView _aboutView = new();
    private DiagnosticsSettingsView? _diagnosticsView;
    private ContractSettingsView? _contractView;
    private bool _developerModeEnabled;

    public override string PageTitle => "设置";

    public override FrameworkElement? MastheadInteractionRegion => HeaderActions;

    public SettingsDestination ActiveDestination => _activeDestination;

    public event EventHandler? CloseRequested;
    public event EventHandler<SettingsDestinationEventArgs>? DestinationChanged;
    public event EventHandler<SettingsDestinationEventArgs>? ExternalDestinationRequested;
    public event EventHandler? PhotoPluginImportRequested;
    public event EventHandler? PhotoPluginOpenRequested;
    public event EventHandler? PhotoPluginUninstallRequested;
    public event EventHandler? GalleryRootChooseRequested;
    public event EventHandler? GalleryRootResetRequested;
    public event EventHandler? GalleryOpenRequested;
    public event EventHandler<GalleryProtectionEnabledChangedEventArgs>? GalleryProtectionEnabledChanged;
    public event EventHandler? GalleryProtectionRootChooseRequested;
    public event EventHandler? GalleryProtectionRootOpenRequested;
    public event EventHandler? GalleryProtectionVerifyRequested;
    public event EventHandler? GalleryProtectionCleanRequested;
    public event EventHandler? GalleryCacheRefreshRequested;
    public event EventHandler? GalleryCacheClearRequested;
    public event EventHandler? NikkiGalleryRegisterRequested;
    public event EventHandler? NikkiGalleryOpenRequested;
    public event EventHandler? NikkiGalleryDisconnectRequested;
    public event EventHandler? JournalOpenRequested;
    public event EventHandler? JournalCacheClearRequested;
    public event EventHandler<AppearanceSettingsChangedEventArgs>? AppearanceSettingsChanged;
    public event EventHandler<GeneralSettingsChangedEventArgs>? GeneralSettingsChanged;
    public event EventHandler? VisualEffectsRequested;
    public event EventHandler<DownloadSettingsChangedEventArgs>? DownloadSettingsChanged;
    public event EventHandler<DownloadPathRequestedEventArgs>? DownloadPathRequested;
    public event EventHandler<UserDataFolderRequestedEventArgs>? UserDataFolderRequested;
    public event EventHandler? FileBackupRequested;
    public event EventHandler? FileOpenBackupRequested;
    public event EventHandler? FileDeleteAllSettingsRequested;
    public event EventHandler? FileOpenLogsRequested;
    public event EventHandler? FileClearCacheRequested;
    public event EventHandler<bool>? FileClearLauncherBackgroundChanged;
    public event EventHandler<ScreenshotSettingsChangedEventArgs>? ScreenshotSettingsChanged;
    public event EventHandler<ScreenshotFolderRequestedEventArgs>? ScreenshotFolderRequested;
    public event EventHandler? ScreenshotTestCaptureRequested;
    public event EventHandler? ScreenshotClearThumbnailCacheRequested;
    public event EventHandler<HotkeySettingsChangedEventArgs>? HotkeySettingsChanged;
    public event EventHandler? BackgroundChooseRequested;
    public event EventHandler? BackgroundResetRequested;
    public event EventHandler<GamepadSettingsChangedEventArgs>? GamepadSettingsChanged;
    public event EventHandler? GamepadRuntimeDownloadRequested;
    public event EventHandler? DiagnosticsExportRequested;
    public event EventHandler? ProviderValidationDetailsRequested;
    public event EventHandler<DeveloperModeChangedEventArgs>? DeveloperModeChanged;

    public SettingsPage()
    {
        InitializeComponent();
        HeaderActions.Loaded += OnMastheadChanged;
        HeaderActions.SizeChanged += OnMastheadSizeChanged;
        _homeView.DestinationRequested += OnHomeDestinationRequested;
        _homeView.DeveloperModeChanged += (_, e) =>
            DeveloperModeChanged?.Invoke(this, e);
        _generalView.AppearanceSettingsChanged += (_, e) =>
            AppearanceSettingsChanged?.Invoke(this, e);
        _generalView.ChooseBackgroundRequested += (_, _) => BackgroundChooseRequested?.Invoke(this, EventArgs.Empty);
        _generalView.ResetBackgroundRequested += (_, _) => BackgroundResetRequested?.Invoke(this, EventArgs.Empty);
        _commonView.SettingsChanged += (_, e) => GeneralSettingsChanged?.Invoke(this, e);
        _commonView.VisualEffectsRequested += (_, _) => VisualEffectsRequested?.Invoke(this, EventArgs.Empty);
        _downloadView.SettingsChanged += (_, e) => DownloadSettingsChanged?.Invoke(this, e);
        _downloadView.PathRequested += (_, e) => DownloadPathRequested?.Invoke(this, e);
        _fileManagementView.DataFolderRequested += (_, e) => UserDataFolderRequested?.Invoke(this, e);
        _fileManagementView.BackupRequested += (_, _) => FileBackupRequested?.Invoke(this, EventArgs.Empty);
        _fileManagementView.OpenBackupRequested += (_, _) => FileOpenBackupRequested?.Invoke(this, EventArgs.Empty);
        _fileManagementView.DeleteAllSettingsRequested += (_, _) => FileDeleteAllSettingsRequested?.Invoke(this, EventArgs.Empty);
        _fileManagementView.OpenLogsRequested += (_, _) => FileOpenLogsRequested?.Invoke(this, EventArgs.Empty);
        _fileManagementView.ClearCacheRequested += (_, _) => FileClearCacheRequested?.Invoke(this, EventArgs.Empty);
        _fileManagementView.ClearLauncherBackgroundChanged += (_, enabled) => FileClearLauncherBackgroundChanged?.Invoke(this, enabled);
        _screenshotView.SettingsChanged += (_, e) => ScreenshotSettingsChanged?.Invoke(this, e);
        _screenshotView.FolderRequested += (_, e) => ScreenshotFolderRequested?.Invoke(this, e);
        _screenshotView.TestCaptureRequested += (_, _) => ScreenshotTestCaptureRequested?.Invoke(this, EventArgs.Empty);
        _screenshotView.ClearThumbnailCacheRequested += (_, _) => ScreenshotClearThumbnailCacheRequested?.Invoke(this, EventArgs.Empty);
        _hotkeyView.SettingsChanged += (_, e) => HotkeySettingsChanged?.Invoke(this, e);
        _journalView.JournalOpenRequested += (_, _) => JournalOpenRequested?.Invoke(this, EventArgs.Empty);
        _journalView.JournalCacheClearRequested += (_, _) => JournalCacheClearRequested?.Invoke(this, EventArgs.Empty);
        _pluginView.ImportRequested += (_, _) => PhotoPluginImportRequested?.Invoke(this, EventArgs.Empty);
        _pluginView.OpenRequested += (_, _) => PhotoPluginOpenRequested?.Invoke(this, EventArgs.Empty);
        _pluginView.UninstallRequested += (_, _) => PhotoPluginUninstallRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.ChooseRootRequested += (_, _) => GalleryRootChooseRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.ResetRootRequested += (_, _) => GalleryRootResetRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.OpenGalleryRequested += (_, _) => GalleryOpenRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.ProtectionEnabledChanged += (_, e) => GalleryProtectionEnabledChanged?.Invoke(this, e);
        _galleryView.ChooseProtectionRootRequested += (_, _) => GalleryProtectionRootChooseRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.OpenProtectionRootRequested += (_, _) => GalleryProtectionRootOpenRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.VerifyProtectionRequested += (_, _) => GalleryProtectionVerifyRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.CleanProtectionRequested += (_, _) => GalleryProtectionCleanRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.RefreshCacheRequested += (_, _) => GalleryCacheRefreshRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.ClearCacheRequested += (_, _) => GalleryCacheClearRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.RegisterNikkiGalleryRequested += (_, _) => NikkiGalleryRegisterRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.OpenNikkiGalleryRequested += (_, _) => NikkiGalleryOpenRequested?.Invoke(this, EventArgs.Empty);
        _galleryView.DisconnectNikkiGalleryRequested += (_, _) => NikkiGalleryDisconnectRequested?.Invoke(this, EventArgs.Empty);
        _gamepadView.SettingsChanged += (_, e) => GamepadSettingsChanged?.Invoke(this, e);
        _gamepadView.RuntimeDownloadRequested += (_, _) => GamepadRuntimeDownloadRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnEntering(NavigationEventArgs e)
    {
        if (e.Parameter is not SettingsNavigationContext context)
        {
            throw new InvalidOperationException("Settings navigation context is required.");
        }

        _viewModel = context.ViewModel;
        _storagePaths = context.StoragePaths;
        _journalView.ApplyPaths(_storagePaths);
        ApplyDeveloperMode(context.DeveloperModeEnabled);
        NavigateTo(context.InitialDestination);
        base.OnEntering(e);
    }

    public void NavigateTo(SettingsDestination destination)
    {
        if (IsDeveloperDestination(destination) && !_developerModeEnabled)
        {
            destination = SettingsDestination.Overview;
        }

        if (destination is SettingsDestination.Status)
        {
            ExternalDestinationRequested?.Invoke(this, new SettingsDestinationEventArgs(destination));
            return;
        }

        _activeDestination = destination;
        var selection = destination switch
        {
            SettingsDestination.General => Select(_generalView, GeneralItem, "外观", "主题、背景、强调色、字体、动效与界面密度"),
            SettingsDestination.Common => Select(_commonView, CommonItem, "通用", "语言、关闭行为、Profile 快速切换与系统体验"),
            SettingsDestination.Download => Select(_downloadView, DownloadItem, "下载", "默认安装路径、硬链接与速度限制"),
            SettingsDestination.FileManagement => Select(_fileManagementView, FileManagementItem, "文件管理", "数据文件夹、备份、日志与缓存"),
            SettingsDestination.Screenshot => Select(_screenshotView, ScreenshotItem, "游戏截图", "截图路径、快捷键、格式与质量"),
            SettingsDestination.Hotkeys => Select(_hotkeyView, HotkeysItem, "键盘快捷键", "主窗口与截图快捷键"),
            SettingsDestination.Gallery => Select(_galleryView, GalleryItem, "相册", "图库位置、缓存与高级工具"),
            SettingsDestination.Journal => Select(_journalView, JournalItem, "奇想手账与缓存", "网页登录会话、页面快照与公开图片资源"),
            SettingsDestination.Files => Select(_journalView, FilesItem, "本地资源", "手账快照与图片缓存路径"),
            SettingsDestination.Plugins => Select(_pluginView, PluginsItem, "插件", "导入与卸载本地插件"),
            SettingsDestination.Components => Select(GetDiagnosticsView(), ComponentsItem, "组件验证", "版本、签名与 SHA-256 的只读身份检查"),
            SettingsDestination.Gamepad => Select(_gamepadView, GamepadItem, "手柄增强", "Xbox 协议手柄的导航键与分享键映射"),
            SettingsDestination.Advanced => Select(_contractView ??= new ContractSettingsView(_viewModel), AdvancedItem, "高级", "协议与运行时边界"),
            SettingsDestination.Diagnostics => Select(GetDiagnosticsView(), DiagnosticsItem, "脱敏诊断", "导出报告与 provider 验证事务"),
            SettingsDestination.Contract => Select(GetContractView(), ContractItem, "隔离契约", "渠道、路径、参数与 IPC 证据边界"),
            SettingsDestination.About => Select(_aboutView, AboutItem, "关于", "版本、更新与项目信息"),
            _ => Select(_homeView, OverviewItem, "设置中心", "外观、插件与输入设置"),
        };
        var (view, item, title, subtitle) = selection;
        ContentHost.Children.Clear();
        ContentHost.Children.Add(view);
        SettingsNavigation.SelectedItem = item;
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
        ContentScrollViewer.ChangeView(null, 0, null, true);
        DestinationChanged?.Invoke(this, new SettingsDestinationEventArgs(destination));
    }

    public void ApplyPhotoPluginState(
        string statusText,
        bool canImport,
        bool canOpen,
        bool canUninstall) =>
        _pluginView.ApplyState(statusText, canImport, canOpen, canUninstall);

    public void ApplyGalleryState(GallerySettingsViewState state) =>
        _galleryView.ApplyState(state);

    public void ApplyAppearanceState(
        AppearanceSettings settings,
        string statusText,
        string? source)
    {
        _generalView.ApplySettings(settings);
        _generalView.BackgroundStatus = statusText;
        if (!string.IsNullOrWhiteSpace(source))
        {
            _generalView.CurrentBackgroundSource = new BitmapImage(new Uri(source));
        }
    }

    public void ApplyGeneralSettings(GeneralSettings settings) => _commonView.ApplySettings(settings);

    public void ApplyDownloadSettings(DownloadSettings settings) => _downloadView.ApplySettings(settings);

    public void ApplyFileManagementState(FileManagementSettingsViewState state)
        => _fileManagementView.ApplyState(state);

    public void ApplyScreenshotSettings(ScreenshotSettings settings, string folderPath) =>
        _screenshotView.ApplySettings(settings, folderPath);

    public void ApplyHotkeySettings(GeneralSettings general, ScreenshotSettings screenshot) =>
        _hotkeyView.ApplySettings(general, screenshot);

    public void ApplyHotkeyRegistrationStatus(string message) =>
        _hotkeyView.ApplyRegistrationStatus(message);

    public void ApplyScreenshotStatus(string message) =>
        _screenshotView.ApplyStatus(message);

    public void ApplyGamepadState(GamepadSettings settings, GamepadRuntimeViewState runtime) =>
        _gamepadView.ApplyState(settings, runtime);

    public void ApplyDeveloperMode(bool enabled)
    {
        _developerModeEnabled = enabled;
        var visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        DeveloperSeparator.Visibility = visibility;
        JournalItem.Visibility = visibility;
        FilesItem.Visibility = visibility;
        PluginsItem.Visibility = visibility;
        StatusItem.Visibility = visibility;
        ComponentsItem.Visibility = visibility;
        DiagnosticsItem.Visibility = visibility;
        ContractItem.Visibility = visibility;
        _homeView.ApplyDeveloperMode(enabled);

        if (!enabled && IsDeveloperDestination(_activeDestination))
        {
            NavigateTo(SettingsDestination.Overview);
        }
    }

    private DiagnosticsSettingsView CreateDiagnosticsView(MainPageViewModel viewModel)
    {
        var view = new DiagnosticsSettingsView(viewModel);
        view.ProviderDetailsRequested += (_, _) => ProviderValidationDetailsRequested?.Invoke(this, EventArgs.Empty);
        view.ExportRequested += (_, _) => DiagnosticsExportRequested?.Invoke(this, EventArgs.Empty);
        return view;
    }

    private DiagnosticsSettingsView GetDiagnosticsView() =>
        _diagnosticsView ??= CreateDiagnosticsView(_viewModel);

    private ContractSettingsView GetContractView() =>
        _contractView ??= new ContractSettingsView(_viewModel);

    private void OnCloseClicked(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnPaneToggleClicked(object sender, RoutedEventArgs e) =>
        SettingsNavigation.IsPaneOpen = !SettingsNavigation.IsPaneOpen;

    private void OnItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem { Tag: string tag } && TryParseDestination(tag, out var destination))
        {
            NavigateTo(destination);
            if (SettingsNavigation.PaneDisplayMode != NavigationViewPaneDisplayMode.Left)
            {
                SettingsNavigation.IsPaneOpen = false;
            }
        }
    }

    private void OnHomeDestinationRequested(object? sender, string tag)
    {
        if (TryParseDestination(tag, out var destination))
        {
            NavigateTo(destination);
        }
    }

    private void OnMastheadChanged(object sender, RoutedEventArgs e) => NotifyMastheadInteractionRegionChanged();
    private void OnMastheadSizeChanged(object sender, SizeChangedEventArgs e) => NotifyMastheadInteractionRegionChanged();

    private static bool TryParseDestination(string tag, out SettingsDestination destination) =>
        Enum.TryParse(tag, true, out destination);

    private static bool IsDeveloperDestination(SettingsDestination destination) =>
        destination is SettingsDestination.Journal or
            SettingsDestination.Files or
            SettingsDestination.Plugins or
            SettingsDestination.Status or
            SettingsDestination.Components or
            SettingsDestination.Diagnostics or
            SettingsDestination.Contract;

    private static (FrameworkElement View, NavigationViewItem Item, string Title, string Subtitle) Select(
        FrameworkElement view,
        NavigationViewItem item,
        string title,
        string subtitle) =>
        (view, item, title, subtitle);
}
