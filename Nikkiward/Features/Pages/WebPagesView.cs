using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Nikkiward.Features.Shell;
using Nikkiward.Services;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Pages;

public sealed partial class WebPagesView : Page
{
    private const string Host = "pages.nikkiward.invalid";
    private const string Address = "https://" + Host + "/pages/index.html";
    private readonly WebTitleBarRegions _regions = new();
    internal IReadOnlyList<FrameworkElement> TitleBarRegions => _regions.Elements;
    private MainPage _shell = null!;
    private PageAdapter _adapter = null!;
    private string? _page;
    internal string? CurrentPage => _page;
    private bool _ready, _closed;
    private string? _failure;
    private int _settingsRefreshQueued;
    private (string Skin, string Tone) _appearance;

    public WebPagesView()
    {
        InitializeComponent();
        _regions.AddTo(Root);
        SizeChanged += (_, _) => MeasureTitleBar();
        Loaded += Start;
    }

    // Reads where the navigation and the command button sit, so the rest of the top strip moves the window.
    // Measured again shortly after loading, once the web fonts have settled the button widths.
    private async void MeasureTitleBar(int delay = 0)
    {
        try
        {
            if (delay > 0) await Task.Delay(delay);
            if (!_ready || _closed || Web.CoreWebView2 is not { } core) return;
            if (_regions.Apply(await core.ExecuteScriptAsync("window.mastheadBounds?.() ?? null")))
                _shell?.NotifyTitleBarPassthroughChanged();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException) { }
    }

    internal void Show(MainPage shell, string page, PageAdapter adapter)
    {
        if (_shell == null) shell.Unloaded += OnShellUnloaded;
        _shell = shell;
        if (_page != page) ReleaseImages();
        _page = page;
        _adapter = adapter;
        if (_failure != null) { Fail(_failure); return; }
        shell.ViewModel.PropertyChanged -= AppearanceChanged;
        shell.ViewModel.PropertyChanged += AppearanceChanged;
        shell.HomeBackdrop.PropertyChanged -= AppearanceChanged;
        shell.HomeBackdrop.PropertyChanged += AppearanceChanged;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        _shell.ViewModel.PropertyChanged -= AppearanceChanged;
        _shell.HomeBackdrop.PropertyChanged -= AppearanceChanged;
        if (_ready) Post(new { type = "leave" });
        ReleaseImages();
        _page = null;
        if (_failure != null || args.SourcePageType == typeof(WebPageErrorView)) Close();
        base.OnNavigatedFrom(args);
    }

    private void OnShellUnloaded(object sender, RoutedEventArgs args) => Close();

    private void Close()
    {
        _closed = true;
        ReleaseImages();
        _shell.Unloaded -= OnShellUnloaded;
        _shell.ViewModel.PropertyChanged -= AppearanceChanged;
        _shell.HomeBackdrop.PropertyChanged -= AppearanceChanged;
        NavigationCacheMode = NavigationCacheMode.Disabled;
        Web.Close();
    }

    private void AppearanceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        // Settings shows live state (channel tools, discovery, checks, gamepad), so any model change may matter.
        // Refreshes coalesce per dispatcher pass, and the page redraws only when something visible changed.
        if (sender == _shell.ViewModel && _page == "settings")
        {
            if (Interlocked.Exchange(ref _settingsRefreshQueued, 1) == 0)
                DispatcherQueue.TryEnqueue(() =>
                {
                    Interlocked.Exchange(ref _settingsRefreshQueued, 0);
                    if (_page == "settings") Refresh();
                });
        }
        if (!DispatcherQueue.HasThreadAccess) { DispatcherQueue.TryEnqueue(UpdateAppearance); return; }
        UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        if (!_ready || _closed || _page == null) return;
        var value = (_shell.StudioSkin, _shell.StudioTone);
        if (value == _appearance) return;
        _appearance = value;
        Post(new { type = "appearance", skin = value.Item1, tone = value.Item2 });
    }

    internal void Refresh()
    {
        if (_ready && !_closed && _page != null)
        {
            _appearance = (_shell.StudioSkin, _shell.StudioTone);
            _adapter.PostState(_page, _appearance.Skin, _appearance.Tone, json => Web.CoreWebView2.PostWebMessageAsJson(json), Fail);
        }
    }

    private void Post(object value)
    {
        try { Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value)); }
        catch (COMException ex) { Fail(ex.Message); }
    }

    private void Fail(string reason)
    {
        _ready = false;
        _failure = reason;
        if (!_closed && _page is { } page) _shell.WebPageFailed(this, page, reason);
    }

    private async void Start(object sender, RoutedEventArgs args)
    {
        Loaded -= Start;
        try
        {
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(null,
                Path.Combine(ApplicationDataPaths.Root, "HomeWebView2"), new CoreWebView2EnvironmentOptions());
            if (_closed) return;
            await Web.EnsureCoreWebView2Async(env);
            if (_closed) return;
            var core = Web.CoreWebView2;
            // Only after initialization: setting it earlier makes the control initialize on its own.
            Web.DefaultBackgroundColor = WebViewDefaults.Background;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsNonClientRegionSupportEnabled = true;
            core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.Deny);
            core.SetVirtualHostNameToFolderMapping("wish-assets.nikkiward.invalid", _shell.EnsureWishAssetsDirectory(), CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => e.Cancel = e.Uri != Address;
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
            core.ProcessFailed += (_, e) => Fail(e.ProcessFailedKind.ToString());
            core.NavigationCompleted += async (_, e) =>
            {
                if (_closed) return;
                if (!e.IsSuccess) { Fail(e.WebErrorStatus.ToString()); return; }
                try
                {
                    if (await core.ExecuteScriptAsync("typeof receivePage === 'function'") != "true")
                    { Fail("功能页面脚本未加载"); return; }
                    if (_closed) return;
                    _ready = true;
                    Refresh();
                    MeasureTitleBar();
                    MeasureTitleBar(800);
                }
                catch (Exception ex) { Fail($"{ex.GetType().Name}: {ex.Message}"); }
            };
            core.AddWebResourceRequestedFilter(ImagePrefix + "*", CoreWebView2WebResourceContext.Image);
            core.WebResourceRequested += ImageRequested;
            core.WebMessageReceived += Message;
            core.Navigate(Address);
        }
        catch (Exception ex)
        { Fail($"{ex.GetType().Name}: {ex.Message}"); }
    }

    private async void Message(CoreWebView2 core, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_closed || _page == null || args.Source != Address || !IsEnabled) return;
        using var doc = JsonDocument.Parse(args.WebMessageAsJson);
        var m = doc.RootElement;
        string Field(string key) => m.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        if (m.ValueKind != JsonValueKind.Object) return;
        var action = Field("action");
        if (action == "navigate")
        {
            if (PageRoutes.NavigationDestination(m, _page, _ready) is { } destination)
                await _shell.NavigateWebPageAsync(destination);
            return;
        }
        if (!_ready || Field("page") != _page) return;
        var page = _page;
        if (action == "module-error")
        {
            var detail = Field("reason");
            Fail(string.IsNullOrWhiteSpace(detail) ? "页面模块未加载：" + page : detail);
            return;
        }
        if (!PageRoutes.Allows(page, Field("page"), _adapter, action)) return;
        try { await _adapter.Execute(action, m); }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            if (!_closed && _page == page) Post(new { type = "error", page,
                message = ex is JsonException ? "JSON 内容无效，请选择 Nikkiward 导出的文件。" : ex.Message });
        }
    }
}

