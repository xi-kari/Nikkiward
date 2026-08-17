using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.Features.Updates;
using System.Text.Json;

namespace Nikkiward.Features.Settings;

public sealed partial class AboutSettingsView : UserControl
{
    private readonly GitHubReleaseUpdateService _updateService = new();
    private AppVersionInfo? _appVersion;
    private CancellationTokenSource? _updateCancellation;
    private Uri? _releaseUri;

    public AboutSettingsView()
    {
        InitializeComponent();
        LoadVersionInformation();
    }

    private void LoadVersionInformation()
    {
        try
        {
            _appVersion = AppVersionProvider.GetCurrent();
            VersionText.Text = _appVersion.DisplayVersion;
            RuntimeText.Text = $"{_appVersion.RuntimeIdentifier} · {_appVersion.DistributionKind}";
            if (!string.IsNullOrWhiteSpace(_appVersion.CommitSha))
            {
                CommitText.Text = _appVersion.CommitSha[..8];
                CommitRow.Visibility = Visibility.Visible;
            }
        }
        catch (InvalidOperationException ex)
        {
            VersionText.Text = "版本信息无效";
            CheckUpdateButton.IsEnabled = false;
            ShowStatus(InfoBarSeverity.Error, "版本不可用", ex.Message);
        }
    }

    private async void OnCheckUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (_appVersion is null)
        {
            return;
        }

        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        SetCheckingState(true);
        ReleaseButton.Visibility = Visibility.Collapsed;
        UpdateStatusBar.IsOpen = false;

        try
        {
            var channel = UpdateChannelSelector.SelectedIndex == 1
                ? UpdateChannel.Preview
                : UpdateChannel.Stable;
            var result = await _updateService.CheckAsync(
                channel,
                _appVersion.Version,
                cancellation.Token);
            ApplyUpdateResult(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException)
        {
            ShowStatus(InfoBarSeverity.Error, "检查失败", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_updateCancellation, cancellation))
            {
                _updateCancellation = null;
                SetCheckingState(false);
            }
            cancellation.Dispose();
        }
    }

    private void ApplyUpdateResult(UpdateCheckResult result)
    {
        _releaseUri = result.ReleaseUri;
        ReleaseButton.Visibility = result.ReleaseUri is null
            ? Visibility.Collapsed
            : Visibility.Visible;

        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                ReleaseButtonText.Text = "查看新版本";
                ShowStatus(
                    InfoBarSeverity.Success,
                    "发现新版本",
                    $"{result.CurrentVersion.ToNormalizedString()} → {result.LatestVersion!.ToNormalizedString()}");
                break;
            case UpdateCheckStatus.UpToDate:
                ReleaseButtonText.Text = "查看当前版本";
                ShowStatus(
                    InfoBarSeverity.Success,
                    "已是最新版本",
                    result.CurrentVersion.ToNormalizedString());
                break;
            default:
                ShowStatus(
                    InfoBarSeverity.Informational,
                    "暂无公开发布",
                    "当前更新源没有可用的公开 Release。");
                break;
        }
    }

    private async void OnReleaseClicked(object sender, RoutedEventArgs e)
    {
        if (_releaseUri is not null)
        {
            await OpenUriAsync(_releaseUri);
        }
    }

    private async void OnProjectLinkClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value } &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            await OpenUriAsync(uri);
        }
    }

    private async Task OpenUriAsync(Uri uri)
    {
        var opened = await Windows.System.Launcher.LaunchUriAsync(uri);
        if (!opened)
        {
            ShowStatus(InfoBarSeverity.Warning, "未能打开链接", uri.Host);
        }
    }

    private void OnUpdateChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        _updateCancellation?.Cancel();
        _releaseUri = null;
        ReleaseButton.Visibility = Visibility.Collapsed;
        UpdateStatusBar.IsOpen = false;
    }

    private void SetCheckingState(bool checking)
    {
        CheckUpdateButton.IsEnabled = !checking && _appVersion is not null;
        UpdateChannelSelector.IsEnabled = !checking;
        UpdateProgressRing.IsActive = checking;
        UpdateProgressRing.Visibility = checking ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        UpdateStatusBar.Severity = severity;
        UpdateStatusBar.Title = title;
        UpdateStatusBar.Message = message;
        UpdateStatusBar.IsOpen = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _updateCancellation?.Cancel();
    }
}
