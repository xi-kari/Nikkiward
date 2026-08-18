using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Nikkiward.Features.Shell;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Journal;

public sealed partial class JournalPage : PageBase
{
    private const string WebView2FolderName = "JournalWebView2";
    private bool _browserInitialized;
    private bool _browserDialogOpen;
    private bool _browserClosing;
    private Uri? _lastContentUri;
    private Task? _ensureBrowserTask;
    private ulong _currentNavigationId;
    private Uri? _currentNavigationUri;
    private Uri? _retryingNavigationUri;
    private long _routeGeneration;

    public static Uri LoginUri { get; } = new("https://myl.nuanpaper.com/tools/journal/login");

    public static Uri ResonanceUri { get; } = new("https://myl.nuanpaper.com/tools/journal/clothesPress");

    public static Uri WebView2RuntimeDownloadUri { get; } = new(
        "https://developer.microsoft.com/microsoft-edge/webview2/");

    public static string WebViewDataPath => Path.Combine(
        Nikkiward.Services.ApplicationDataPaths.Root,
        WebView2FolderName);

    public override string PageTitle => "奇想手账";

    public bool IsBrowserInitialized => _browserInitialized;

    public Uri? CurrentUri => _currentNavigationUri ?? JournalWebView.Source;

    public long RouteGeneration => Interlocked.Read(ref _routeGeneration);

    public Uri? LastContentUri => _lastContentUri;

    public event EventHandler? OpenRequested;

    public event EventHandler? SyncRequested;

    public event EventHandler? ExternalOpenRequested;

    public event EventHandler? ClearCacheRequested;

    public event EventHandler? BrowserClosed;

    public event EventHandler<JournalNavigationEventArgs>? NavigationFinished;

    public event EventHandler<JournalRouteChangedEventArgs>? RouteChanged;

    public JournalPage()
    {
        InitializeComponent();
        SummaryPanel.OpenRequested += OnSummaryOpenRequested;
    }

    public async Task ShowBrowserAsync(Uri? targetUri = null)
    {
        BrowserPanel.Visibility = Visibility.Visible;
        BrowserStatusText.Text = "正在准备官方手账登录页；登录会话由 WebView2 独立保存。";
        await EnsureBrowserAsync();
        if (targetUri is not null && !Equals(JournalWebView.Source, targetUri))
        {
            JournalWebView.Source = targetUri;
        }

        JournalWebView.Visibility = Visibility.Visible;
        if (!_browserDialogOpen)
        {
            BrowserDialog.XamlRoot = XamlRoot;
            _browserDialogOpen = true;
            try
            {
                await BrowserDialog.ShowAsync();
            }
            finally
            {
                _browserDialogOpen = false;
                await ReleaseBrowserDocumentAsync();
                BrowserClosed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Navigate(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        JournalWebView.Source = uri;
    }

    public async Task<string> ExecuteScriptAsync(string script)
    {
        if (!_browserInitialized)
        {
            throw new InvalidOperationException("The journal browser is not initialized.");
        }

        return await JournalWebView.ExecuteScriptAsync(script);
    }

    public void SetBrowserStatus(string statusText) => BrowserStatusText.Text = statusText;

    public void HideBrowser()
    {
        BrowserPanel.Visibility = Visibility.Collapsed;
        if (_browserDialogOpen && !_browserClosing)
        {
            BrowserDialog.Hide();
        }
    }

    public void SetSyncInProgress(bool isInProgress)
    {
        SyncProgressIsland.Visibility = isInProgress ? Visibility.Visible : Visibility.Collapsed;
        JournalWebView.Opacity = isInProgress ? 0 : 1;
        JournalWebView.IsHitTestVisible = !isInProgress;
        BrowserDialogProgress.Visibility = isInProgress ? Visibility.Visible : Visibility.Collapsed;
        SummaryPanel.SetSyncInProgress(isInProgress);
    }

    public void ShowSyncFailure(JournalCaptureFailureProjection failure)
    {
        SyncStatusBar.Message = MainPageViewModel.RedactUiText(failure.Message);
        SyncStatusBar.Severity = InfoBarSeverity.Warning;
        SyncStatusBar.IsOpen = true;
    }

    public void ClearSyncFailure() => SyncStatusBar.IsOpen = false;

    public async Task ReleaseBrowserDocumentAsync()
    {
        if (_browserInitialized && JournalWebView.Source?.AbsoluteUri != "about:blank")
        {
            JournalWebView.Source = new Uri("about:blank");
            await Task.Yield();
        }

        JournalWebView.Visibility = Visibility.Collapsed;
    }

    public void ApplySnapshot(JournalSnapshot snapshot, string durationText, string durationSourceText)
    {
        SummaryPanel.ApplySnapshot(snapshot, durationText, durationSourceText);
        EmptyStateIsland.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Visible;
        if (SnapshotPanel.ApplySnapshot(snapshot))
        {
            ResetScroll();
        }
    }

    public void ResetState(string durationSourceText)
    {
        EmptyStateIsland.Visibility = Visibility.Visible;
        SummaryPanel.Visibility = Visibility.Collapsed;
        SummaryPanel.ResetState(durationSourceText);
        SnapshotPanel.ResetState();
        ResetScroll();
    }

    public void ResetScroll() => ContentScrollViewer.ChangeView(null, 0, null, true);

    private Task EnsureBrowserAsync()
    {
        if (_browserInitialized)
        {
            if (JournalWebView.Source is null ||
                JournalWebView.Source.AbsoluteUri.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                JournalWebView.Source = LoginUri;
            }

            return Task.CompletedTask;
        }

        return _ensureBrowserTask ??= InitializeBrowserAsync();
    }

    private async Task InitializeBrowserAsync()
    {
        try
        {
            Directory.CreateDirectory(WebViewDataPath);
            var runtimeVersion = GetWebView2RuntimeVersion();
            var environmentOptions = new CoreWebView2EnvironmentOptions();
            environmentOptions.AdditionalBrowserArguments = "--disable-quic";
            environmentOptions.Language = "zh-CN";
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                null,
                WebViewDataPath,
                environmentOptions);
            await JournalWebView.EnsureCoreWebView2Async(environment);
            JournalWebView.CoreWebView2.SourceChanged += OnCoreSourceChanged;
            JournalWebView.CoreWebView2.ProcessFailed += OnCoreProcessFailed;
            _browserInitialized = true;
            BrowserStatusText.Text = $"WebView2 {runtimeVersion} 已就绪，正在打开官方手账。";
            JournalWebView.Source = LoginUri;
        }
        catch
        {
            _ensureBrowserTask = null;
            throw;
        }
    }

    private static string GetWebView2RuntimeVersion()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString(null);
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }
        catch (Exception ex)
        {
            throw new JournalWebView2RuntimeUnavailableException(ex);
        }

        throw new JournalWebView2RuntimeUnavailableException();
    }

    private void OnSummaryOpenRequested(object? sender, EventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnEmptyOpenClicked(object sender, RoutedEventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (_browserInitialized && JournalWebView.CanGoBack)
        {
            JournalWebView.GoBack();
        }
    }

    private void OnForwardClicked(object sender, RoutedEventArgs e)
    {
        if (_browserInitialized && JournalWebView.CanGoForward)
        {
            JournalWebView.GoForward();
        }
    }

    private void OnReloadClicked(object sender, RoutedEventArgs e)
    {
        if (_browserInitialized)
        {
            _retryingNavigationUri = null;
            JournalWebView.Reload();
        }
    }

    private void OnSyncClicked(object sender, RoutedEventArgs e) =>
        SyncRequested?.Invoke(this, EventArgs.Empty);

    private void OnExternalClicked(object sender, RoutedEventArgs e) =>
        ExternalOpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnClearCacheClicked(object sender, RoutedEventArgs e) =>
        ClearCacheRequested?.Invoke(this, EventArgs.Empty);

    private void OnBrowserCloseClicked(object sender, RoutedEventArgs e)
    {
        HideBrowser();
        BrowserStatusText.Text = "内置手账页面已关闭。";
    }

    private void OnBrowserDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        _browserClosing = true;
        BrowserPanel.Visibility = Visibility.Collapsed;
        _browserClosing = false;
    }

    private void OnNavigationStarting(
        WebView2 sender,
        CoreWebView2NavigationStartingEventArgs args)
    {
        _currentNavigationId = args.NavigationId;
        _currentNavigationUri = Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)
            ? uri
            : null;
        if (_retryingNavigationUri is null ||
            !Equals(_retryingNavigationUri, _currentNavigationUri))
        {
            _retryingNavigationUri = null;
        }
        Interlocked.Increment(ref _routeGeneration);
    }

    private void OnCoreSourceChanged(
        CoreWebView2 sender,
        CoreWebView2SourceChangedEventArgs args)
    {
        if (args.IsNewDocument ||
            !Uri.TryCreate(sender.Source, UriKind.Absolute, out var uri))
        {
            return;
        }

        _currentNavigationUri = uri;
        Interlocked.Increment(ref _routeGeneration);
        if (!uri.AbsoluteUri.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            _lastContentUri = uri;
        }

        RouteChanged?.Invoke(this, new JournalRouteChangedEventArgs(uri));
    }

    private void OnNavigationCompleted(
        WebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args)
    {
        var isCurrentNavigation = args.NavigationId == _currentNavigationId;
        var completedUri = isCurrentNavigation
            ? _currentNavigationUri ?? JournalWebView.Source
            : null;
        BackButton.IsEnabled = JournalWebView.CanGoBack;
        ForwardButton.IsEnabled = JournalWebView.CanGoForward;
        if (completedUri?.AbsoluteUri.Equals(
                "about:blank",
                StringComparison.OrdinalIgnoreCase) == false &&
            isCurrentNavigation)
        {
            _lastContentUri = completedUri;
        }

        var webErrorStatus = args.WebErrorStatus.ToString();
        NavigationFinished?.Invoke(
            this,
            new JournalNavigationEventArgs(
                completedUri,
                args.IsSuccess,
                webErrorStatus,
                isCurrentNavigation));

        if (args.IsSuccess && isCurrentNavigation)
        {
            _retryingNavigationUri = null;
            return;
        }

        if (isCurrentNavigation &&
            completedUri is not null &&
            !completedUri.AbsoluteUri.Equals("about:blank", StringComparison.OrdinalIgnoreCase) &&
            JournalNavigationFailureProjector.ShouldRetry(webErrorStatus) &&
            !Equals(_retryingNavigationUri, completedUri))
        {
            _retryingNavigationUri = completedUri;
            BrowserStatusText.Text = "官方手账连接暂时失败，正在自动重试……";
            _ = RetryNavigationAsync(completedUri);
        }
    }

    private async Task RetryNavigationAsync(Uri uri)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(700));
            if (!_browserInitialized ||
                !Equals(CurrentUri, uri) ||
                JournalWebView.Source is null ||
                JournalWebView.Source.AbsoluteUri.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            JournalWebView.Reload();
        }
        catch (Exception ex)
        {
            BrowserStatusText.Text = $"官方手账重试失败：{ex.GetType().Name}。可使用系统浏览器打开。";
        }
    }

    private void OnCoreProcessFailed(
        CoreWebView2 sender,
        CoreWebView2ProcessFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            BrowserStatusText.Text =
                $"内置网页运行时异常（{args.ProcessFailedKind}）；请刷新或使用系统浏览器打开。";
        });
    }

}

public sealed class JournalRouteChangedEventArgs : EventArgs
{
    public JournalRouteChangedEventArgs(Uri uri)
    {
        Uri = uri;
    }

    public Uri Uri { get; }
}

public sealed class JournalNavigationEventArgs : EventArgs
{
    public JournalNavigationEventArgs(
        Uri? uri,
        bool isSuccess,
        string webErrorStatus,
        bool isCurrentNavigation)
    {
        Uri = uri;
        IsSuccess = isSuccess;
        WebErrorStatus = webErrorStatus;
        IsCurrentNavigation = isCurrentNavigation;
    }

    public Uri? Uri { get; }

    public bool IsSuccess { get; }

    public string WebErrorStatus { get; }

    public bool IsCurrentNavigation { get; }
}

public sealed class JournalWebView2RuntimeUnavailableException : InvalidOperationException
{
    public JournalWebView2RuntimeUnavailableException(Exception? innerException = null)
        : base(
            "未检测到 Microsoft Edge WebView2 Evergreen Runtime，请先安装 WebView2 Runtime。",
            innerException)
    {
    }
}
