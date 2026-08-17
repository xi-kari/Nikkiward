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

    public static Uri LoginUri { get; } = new("https://myl.nuanpaper.com/tools/journal/login");

    public static Uri ResonanceUri { get; } = new("https://myl.nuanpaper.com/tools/journal/clothesPress");

    public static string WebViewDataPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nikkiward",
        WebView2FolderName);

    public override string PageTitle => "奇想手账";

    public bool IsBrowserInitialized => _browserInitialized;

    public Uri? CurrentUri => JournalWebView.Source;

    public Uri? LastContentUri => _lastContentUri;

    public event EventHandler? OpenRequested;

    public event EventHandler? SyncRequested;

    public event EventHandler? ExternalOpenRequested;

    public event EventHandler? ClearCacheRequested;

    public event EventHandler? BrowserClosed;

    public event EventHandler<JournalNavigationEventArgs>? NavigationFinished;

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
        JournalWebView.Visibility = isInProgress ? Visibility.Collapsed : Visibility.Visible;
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
            HideBrowser();
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
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                null,
                WebViewDataPath,
                null);
            await JournalWebView.EnsureCoreWebView2Async(environment);
            _browserInitialized = true;
            JournalWebView.Source = LoginUri;
        }
        catch
        {
            _ensureBrowserTask = null;
            throw;
        }
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

        NavigationFinished?.Invoke(
            this,
            new JournalNavigationEventArgs(
                completedUri,
                args.IsSuccess,
                args.WebErrorStatus.ToString(),
                isCurrentNavigation));
    }

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
