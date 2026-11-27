using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Nikkiward.Features.Background;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.Shell;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Launcher.Skins;

internal sealed class WebHomeView : UserControl, IDisposable
{
    private readonly WebView2 _web = new() { Opacity = 0 };
    internal Border Masthead { get; } = new() { Width = 0, Height = 52, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    internal Border MastheadTool { get; } = new() { Width = 0, Height = 46, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly string _skin, _page;
    private LauncherPage _host = null!;
    private MainPage _shell = null!;
    private AppWindow _window = null!;
    private CancellationTokenSource? _scan;
    private HomePhoto[] _photos = [];
    private Dictionary<string, GalleryPhotoItemViewModel> _photoItems = [];
    private WebHomeState? _last;
    private (string Art, string Preview) _artKey;
    private CancellationTokenSource? _coverWork;

    private HomeBackground? _cover;
    internal bool CoverVisible { get; private set; }
    private int _mastheadVersion;
    private bool _disposed, _ready, _queued, _photosQueued;

    internal WebHomeView(string skin)
    {
        _skin = skin;
        _page = WebHomeBridge.Page(skin);
        Content = new Grid { Children = { _web, Masthead, MastheadTool } };
        Loaded += OnLoaded;
        SizeChanged += (_, _) => MeasureMasthead();
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;
        for (DependencyObject? p = this; p != null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is LauncherPage host) _host = host;
            if (p is MainPage shell) { _shell = shell; break; }
        }
        _window = AppWindow.GetFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
        _window.Changed += OnWindowChanged;
        App.MainWindow.ForegroundActivationChanged += OnActivation;
        _host.ViewModel.PropertyChanged += OnChanged;
        _shell.HomeBackdrop.PropertyChanged += OnChanged;
        _shell.HomeDataChanged += OnHomeDataChanged;
        _shell.HomeInteractionChanged += OnInteraction;
        GlassCapabilities.Current.TierChanged += OnInteraction;
        _host.RecentPhotos.CollectionChanged += OnPhotosChanged;
        try
        {
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(ApplicationDataPaths.Root, "HomeWebView2"),
                new CoreWebView2EnvironmentOptions());
            if (_disposed) return;
            await _web.EnsureCoreWebView2Async(env);
            if (_disposed) return;
            var core = _web.CoreWebView2;
            var settings = core.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPinchZoomEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsNonClientRegionSupportEnabled = true;
#if !DEBUG
            settings.AreDevToolsEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
#endif
            core.SetVirtualHostNameToFolderMapping(WebHomeBridge.Host, Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.Deny);
            var media = HomeBackground.CreateMediaFolders(ApplicationDataPaths.Root);
            core.SetVirtualHostNameToFolderMapping(HomeBackground.StillHost, media.Still, CoreWebView2HostResourceAccessKind.DenyCors);
            core.SetVirtualHostNameToFolderMapping(HomeBackground.MotionHost, media.Motion, CoreWebView2HostResourceAccessKind.DenyCors);
            ulong pageNavigation = 0;
            core.NavigationStarting += (_, e) =>
            {
                e.Cancel = !WebHomeBridge.AllowsPage(_page, e.Uri);
                if (e.Cancel) return;
                pageNavigation = e.NavigationId;
                _ready = false;
                _web.Opacity = 0;
                CoverVisible = false;
                _shell.SyncLauncherChrome();
            };
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
            core.ProcessFailed += (_, e) =>
            {
                if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)
                    DispatcherQueue.TryEnqueue(() => { if (!_disposed) _host.UseCinema(e.ProcessFailedKind.ToString()); });
            };
            core.WebMessageReceived += OnMessage;
            core.NavigationCompleted += async (_, e) =>
            {
                if (_disposed || e.NavigationId != pageNavigation) return;
                if (!e.IsSuccess)
                {
                    DispatcherQueue.TryEnqueue(() => { if (!_disposed) _host.UseCinema(e.WebErrorStatus.ToString()); });
                    return;
                }
                var entry = await ReadPageAsync("typeof show === 'function' && typeof render === 'function'");
                if (entry == null) return;
                if (_disposed || e.NavigationId != pageNavigation) return;
                if (entry != "true")
                {
                    _host.UseCinema("首页脚本未加载");
                    return;
                }
                _ready = true;
                _last = null;
                SendState();
                UpdateVisibility();
            };
            core.Navigate(_page);
            QueuePhotos();
        }
        catch (Exception ex)
        {
            if (!_disposed) _host.UseCinema(ex.Message);
        }
    }

    private async Task<string?> ReadPageAsync(string script)
    {
        try { return await _web.ExecuteScriptAsync(script); }
        catch (Exception ex) when (_disposed || ex is COMException)
        {
            if (!_disposed) _host.UseCinema(ex.Message);
            return null;
        }
    }

    private async void MeasureMasthead()
    {
        if (!CoverVisible || _disposed || Hidden) return;
        int version = ++_mastheadVersion;
        var bounds = await ReadPageAsync("window.mastheadBounds?.() ?? [0,0]");
        if (bounds == null) return;
        if (_disposed || version != _mastheadVersion || Hidden || bounds == "null") return;
        var edges = JsonSerializer.Deserialize<double[]>(bounds)!;
        Masthead.Margin = new Thickness(edges[0], 0, 0, 0);
        Masthead.Width = edges[1] - edges[0];
        if (edges.Length == 4)
        {
            MastheadTool.Margin = new Thickness(edges[2], 0, 0, 0);
            MastheadTool.Width = edges[3] - edges[2];
        }
        Masthead.UpdateLayout();
        _shell.NotifyTitleBarPassthroughChanged();
    }

    private bool Hidden => !_window.IsVisible || _window.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    private void OnActivation(object? sender, NativeWindowActivationChangedEventArgs args) => QueueVisibility();

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) => QueueVisibility();

    private void QueueVisibility()
    {
        // Window activation notifications can arrive inside a synchronous Win32 input call.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed) return;
            UpdateVisibility();
            QueueState();
        });
    }

    private async void UpdateVisibility()
    {
        if (!_ready || _disposed) return;
        if (!Hidden)
        {
            if (_web.Visibility == Visibility.Visible) return;
            _web.Visibility = Visibility.Visible;
            _web.CoreWebView2.Resume();
            MeasureMasthead();
            QueueState();
        }
        else if (_web.Visibility == Visibility.Visible)
        {
            _web.Visibility = Visibility.Collapsed;
            try { await _web.CoreWebView2.TrySuspendAsync(); }
            catch (Exception ex) when (_disposed || ex.HResult == unchecked((int)0x8007139F)) { }
            if (!_disposed && !Hidden) _web.CoreWebView2.Resume();
        }
    }

    private async void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_disposed || !_ready || !WebHomeBridge.AllowsPage(_page, e.Source)) return;
        var json = e.WebMessageAsJson;

        if (json.StartsWith("\"failed:", StringComparison.Ordinal))
        {
            _host.UseCinema(JsonSerializer.Deserialize<string>(json)![7..]);
            return;
        }
        if (json == "\"ready\"")
        {
            CoverVisible = true;
            _web.Opacity = 1;
            _shell.SyncLauncherChrome();
            sender.PostWebMessageAsString("presented");
            if (_pendingOverlay is { } name) { _pendingOverlay = null; OpenOverlay(name); }
            MeasureMasthead();
            QueueState();
            return;
        }
#if DEBUG
        bool preview = HomePreview.Active;
#else
        const bool preview = false;
#endif
        if (Hidden || !_shell.HomeInteractive || WebHomeBridge.Parse(json, preview) is not { } request) return;
        if (request is { Action: "planner", Planner: { } command })
        {
            try
            {
                await _shell.PerformPlannerAction(command);
                if (!_disposed && command.Action is not "link-open")
                {
                    SendState();
                    sender.PostWebMessageAsString("planner-saved");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or COMException)
            {
                if (!_disposed) sender.PostWebMessageAsString("planner-error:" + ex.Message);
            }
            return;
        }
        if (request.Action is "settings" or "background" or "status")
        {
            OpenOverlay(request.Action);
            return;
        }
        if (await _shell.InvokeOverlayActionAsync(request)) return;
        if (_shell.InvokeHomeCommand(request.Action, request.ProfileId)) return;
        _host.InvokeAction(request.Action, request.PhotoId is { } id ? _photoItems.GetValueOrDefault(id) : null);
    }

    private string? _pendingOverlay;

    internal void OpenOverlay(string name)
    {
        if (!CoverVisible) { _pendingOverlay = name; return; }
        _web.CoreWebView2.PostWebMessageAsString("open-" + name);
        _web.Focus(FocusState.Programmatic);
    }

    private void OnInteraction(object? sender, EventArgs e)
    {
        _web.IsTabStop = _shell.HomeInteractive;
        QueueState();
    }
    private void OnHomeDataChanged(object? sender, EventArgs e) => QueueState();
    private void OnChanged(object? sender, PropertyChangedEventArgs e) => QueueState();

    private void QueueState()
    {
        if (!_ready || _queued || Hidden) return;
        _queued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _queued = false;
            if (!_disposed && !Hidden) SendState();
        });
    }

    private void SendState()
    {
        var art = _host.ViewModel.HomeArtKey;
        var key = (Art: art, Preview: WallpaperSourceRules.IsPackageExtension(Path.GetExtension(art)) ? _shell.HomeStillSource : "");
        if (_artKey != key)
        {
            _artKey = key;
            _cover = null;
            _coverWork?.Cancel();
            LoadCover(art, key.Preview);
        }
        if (_cover == null) return;
        var signals = GlassCapabilities.Current.ReadSignals();
        var model = _host.ViewModel;
        bool still = !App.MainWindow.IsForegroundActive || signals.WindowOccluded || model.HomeState == LaunchButtonState.Running || signals.Motion != AppearanceMotionMode.Full ||
            !signals.AnimationsEnabled || !signals.AdvancedEffectsEnabled || signals.HighContrast || signals.LowFrameRateMeasured ||
            signals.RemoteSession || signals.EnergySaverOn || signals.BlurConstructionFailed;
        var backdrop = _shell.HomeBackdrop;
        var theme = backdrop.IsReady ? backdrop.PreferredTheme : ArtPreferredTheme.Dark;
        var accent = ArtThemeAccentSelector.Select(backdrop.DerivedAccentLight, backdrop.DerivedAccentDark, theme);
        var (current, previous, started) = _shell.HomeSessions;
        var times = _host.AlbumTimes;

        for (int i = 0; i < _photos.Length; i++)
        {
            var photo = _photos[i];
            var time = _photoItems[photo.Id].LastWriteTimeUtc;
            var session = current != null && PlayTimeLedger.ContainsPhoto(current, time) ? "current" :
                previous != null && PlayTimeLedger.ContainsPhoto(previous, time) ? "previous" : null;
            if (photo.Session == session) continue;
            if (ReferenceEquals(_last?.Photos, _photos)) _photos = _photos.ToArray();
            _photos[i] = photo with { Session = session };
        }
        var planner = _shell.Planner;
        var state = WebHomeState.Create(model, theme == ArtPreferredTheme.Dark ? "dark" : "light", accent, still, model.AppearanceSettings.ShowRecentPhotos ? _photos : Array.Empty<HomePhoto>()) with {
            Skin = _skin, IdleSeconds = model.AppearanceSettings.AppreciationDelaySeconds,
            Checklist = _last?.Checklist is { } saved && saved.Items == planner.Items ? saved : HomeChecklist.From(planner.Items),
            Links = planner.Links, Reminders = planner.Reminders, PhotoTotal = times?.Length,
            SessionMinutes = model.HomeState == LaunchButtonState.Running && started.HasValue ? (int)(DateTimeOffset.UtcNow - started.Value).TotalMinutes : null,
            SessionStarted = model.HomeState == LaunchButtonState.Running ? started : null,
            JournalSnapshotDate = _shell.SnapshotDay, LastSessionPhotos = PlayTimeLedger.CountPhotos(times, previous), CurrentSessionPhotos = PlayTimeLedger.CountPhotos(times, current),
            Background = _cover, Palette = [$"#{backdrop.DerivedAccentLight & 0xffffff:X6}", $"#{backdrop.DerivedAccentDark & 0xffffff:X6}"] };

        if (_last is { } last && state.Profiles.SequenceEqual(last.Profiles) && state.Palette.SequenceEqual(last.Palette) && state.Components.SequenceEqual(last.Components) &&
            (state with { Profiles = last.Profiles, Palette = last.Palette, Components = last.Components }) == last) return;
        var message = _last?.Photos == state.Photos && _last?.CurrentSessionPhotos.HasValue == state.CurrentSessionPhotos.HasValue ? state with { Photos = null } : state;
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, WebHomeBridge.Json));
        _last = state;
    }

    private async void LoadCover(string art, string scenePreview)
    {
        using var work = new CancellationTokenSource();
        _coverWork = work;
        try
        {
            var root = ApplicationDataPaths.Root;
            if (WallpaperSourceRules.IsPackageExtension(Path.GetExtension(art)) &&
                AppearanceBackgroundPresets.IsBuiltInSource(scenePreview))
                scenePreview = await _shell.SceneCoverAsync(art, work.Token) ?? scenePreview;
            if (MotionSourceRules.IsSupportedExtension(Path.GetExtension(art)) &&
                !string.Equals(Path.GetDirectoryName(art), HomeBackground.MotionFolder(root), StringComparison.OrdinalIgnoreCase))
            {
                var imported = await new MotionBackgroundImporter(() => _host.ViewModel.GalleryReadOnlyRoots).ImportAsync(art, work.Token);
                art = imported.ImportedPath ?? throw new IOException(imported.Validation.RejectReason);
            }
            var cover = await HomeBackground.ResolveAsync(art, scenePreview, root, async (path, video) =>
            {
                var protectedRoots = _host.ViewModel.GalleryReadOnlyRoots;
                var poster = await Task.Run(() => new WallpaperAssetImporter(() => protectedRoots).CachePreviewAsync(path, video, work.Token), work.Token);
                return Path.GetFileName(poster) ?? throw new IOException("无法生成背景预览。");
            });
            if (work.IsCancellationRequested) return;
            _cover = cover;
            QueueState();
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            if (!work.IsCancellationRequested) _host.UseCinema(ex.Message);
        }
        finally { if (_coverWork == work) _coverWork = null; }
    }

    private void OnPhotosChanged(object? sender, NotifyCollectionChangedEventArgs e) => QueuePhotos();
    private void QueuePhotos()
    {
        _scan?.Cancel();
        if (_photosQueued) return;
        _photosQueued = true;
        DispatcherQueue.TryEnqueue(LoadPhotos);
    }

    private async void LoadPhotos()
    {
        _photosQueued = false;
        if (_disposed) return;
        using var scan = new CancellationTokenSource();
        _scan = scan;
        var items = _host.RecentPhotos.ToDictionary(_ => Guid.NewGuid().ToString("N"));
        try
        {
            var photos = await Task.WhenAll(items.Select(async pair =>
            {
                var item = pair.Value;
                var url = await Task.Run(() => item.ReadThumbnailDataUrlAsync(scan.Token), scan.Token);
                return new HomePhoto(pair.Key, item.FileName, item.ModifiedText, url);
            }));
            if (scan.IsCancellationRequested) return;
            _photoItems = items;
            _photos = photos;
            QueueState();
        }
        catch (OperationCanceledException) when (scan.IsCancellationRequested) { }
        finally { if (_scan == scan) _scan = null; }
    }

    public void Dispose()
    {
        _disposed = true;
        _coverWork?.Cancel();
        Loaded -= OnLoaded;
        _scan?.Cancel();
        if (_host != null)
        {
            _window.Changed -= OnWindowChanged;
            App.MainWindow.ForegroundActivationChanged -= OnActivation;
            _shell.HomeBackdrop.PropertyChanged -= OnChanged;
            _shell.HomeDataChanged -= OnHomeDataChanged;
            _shell.HomeInteractionChanged -= OnInteraction;
            GlassCapabilities.Current.TierChanged -= OnInteraction;
            _host.ViewModel.PropertyChanged -= OnChanged;
            _host.RecentPhotos.CollectionChanged -= OnPhotosChanged;
        }
        _web.Close();
    }
}
