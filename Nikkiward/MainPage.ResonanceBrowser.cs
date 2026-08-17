using System.Text.Json;
using Nikkiward.Features.Journal;
using Nikkiward.ViewModels;
using Nikkiward.Serialization;

namespace Nikkiward;

public sealed partial class MainPage
{
    private async Task<bool> SyncResonanceHistoryAsync(bool isAutomatic)
    {
        if (_hostedJournalPage is not { IsBrowserInitialized: true } journalPage ||
            _resonanceSyncInProgress)
        {
            return false;
        }

        if (!IsResonanceHistoryUri(journalPage.CurrentUri))
        {
            journalPage.SetBrowserStatus("请先打开共鸣衣橱页面。");
            return false;
        }

        var attemptedAtUtc = DateTimeOffset.UtcNow;
        if (isAutomatic && attemptedAtUtc < _resonanceNextAutomaticSyncUtc)
        {
            return false;
        }

        _resonanceSyncInProgress = true;
        journalPage.ClearSyncFailure();
        try
        {
            journalPage.SetBrowserStatus(isAutomatic
                ? "正在自动整理全部共鸣历史……"
                : "正在整理当前共鸣衣橱的全部历史……");

            if (!await PrepareResonanceDocumentAsync())
            {
                journalPage.SetBrowserStatus("共鸣衣橱仍在载入，正在重试同步……");
                await Task.Delay(TimeSpan.FromMilliseconds(800), _lifetimeCancellation?.Token ?? CancellationToken.None);
                if (!await PrepareResonanceDocumentAsync())
                {
                    journalPage.SetBrowserStatus(
                        "当前页面尚未返回共鸣活动记录；请确认已进入共鸣衣橱并等待页面载入完成。");
                    ScheduleResonanceRetry(attemptedAtUtc);
                    return false;
                }
            }

            var scriptResult = await ExecuteJournalScriptWithRetryAsync(
                journalPage,
                JournalWebCaptureScripts.ResonanceFull);
            var json = UnwrapWebViewScriptResult(scriptResult);
            var snapshot = JsonSerializer.Deserialize<ResonanceHistorySnapshot>(
                json,
                new NikkiwardJsonContext(JournalCaptureJsonOptions).ResonanceHistorySnapshot);
            if (snapshot is null ||
                !string.Equals(
                    snapshot.SourcePagePath,
                    ResonanceHistoryPagePath,
                    StringComparison.OrdinalIgnoreCase) ||
                snapshot.Banners.Count == 0)
            {
                journalPage.SetBrowserStatus(
                    "当前页面尚未返回共鸣活动记录；请确认已进入共鸣衣橱并等待页面载入完成。");
                ScheduleResonanceRetry(attemptedAtUtc);
                return false;
            }

            snapshot = await _resonanceCache.DownloadAndSaveAsync(
                snapshot,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            ApplyResonanceHistory(snapshot);
            await ApplyWishHistoryFromResonanceAsync(
                snapshot,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            _resonanceLastAutoSyncedUrl = journalPage.CurrentUri?.AbsoluteUri;
            _journalRouteIntent = JournalRouteIntent.Unknown;
            _resonanceConsecutiveFailures = 0;
            _resonanceNextAutomaticSyncUtc = JournalSyncScheduleProjector.Project(
                attemptedAtUtc,
                JournalSyncScheduleProjector.MinimumAutomaticInterval,
                consecutiveFailures: 0).NextAttemptAtUtc;
            journalPage.HideBrowser();
            SetShellNavigationSelection(ResonanceNavigationItem);
            ShowResonance();
            journalPage.SetBrowserStatus(
                $"已同步 {snapshot.Banners.Count} 个共鸣活动和 {snapshot.Banners.Sum(banner => banner.Items.Count)} 条服装槽位记录。");
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!_lifetimeCancellation?.IsCancellationRequested ?? true)
            {
                journalPage.SetBrowserStatus("共鸣历史同步已取消。");
            }

            return false;
        }
        catch (JsonException)
        {
            journalPage.SetBrowserStatus(
                "共鸣衣橱页面结构暂未返回可解析记录；请等待页面载入后重试。");
            ScheduleResonanceRetry(attemptedAtUtc);
            return false;
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError(
                $"共鸣历史同步失败：{ex.GetType().Name}: {ex.Message}");
            journalPage.SetBrowserStatus("共鸣历史同步失败；可刷新页面后重试。");
            ScheduleResonanceRetry(attemptedAtUtc);
            return false;
        }
        finally
        {
            _resonanceSyncInProgress = false;
        }
    }

    private void ScheduleResonanceRetry(DateTimeOffset attemptedAtUtc)
    {
        _resonanceConsecutiveFailures++;
        _resonanceNextAutomaticSyncUtc = JournalSyncScheduleProjector.Project(
            attemptedAtUtc,
            JournalSyncScheduleProjector.MinimumAutomaticInterval,
            _resonanceConsecutiveFailures).NextAttemptAtUtc;
    }

    private async Task<bool> PrepareResonanceDocumentAsync()
    {
        var journalPage = _hostedJournalPage
            ?? throw new InvalidOperationException("The journal page is not active.");
        var cancellationToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
        string? previousSignature = null;
        var stableSamples = 0;
        for (var attempt = 0; attempt < 80 && stableSamples < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scriptResult = await ExecuteJournalScriptWithRetryAsync(
                journalPage,
                JournalWebCaptureScripts.PrepareResonance);
            var signature = UnwrapWebViewScriptResult(scriptResult);
            if (string.Equals(signature, previousSignature, StringComparison.Ordinal))
            {
                stableSamples++;
            }
            else
            {
                previousSignature = signature;
                stableSamples = 0;
            }

            if (stableSamples >= 2)
            {
                try
                {
                    using var document = JsonDocument.Parse(signature);
                    var root = document.RootElement;
                    var cardCount = root.TryGetProperty("cardCount", out var cardElement)
                        ? cardElement.GetInt32()
                        : 0;
                    var imageCount = root.TryGetProperty("imageCount", out var imageElement)
                        ? imageElement.GetInt32()
                        : 0;
                    if (JournalDocumentReadinessProjector.IsResonanceReady(cardCount, imageCount))
                    {
                        await journalPage.ExecuteScriptAsync("window.scrollTo(0, 0)");
                        return true;
                    }
                }
                catch (JsonException)
                {
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(140), cancellationToken);
        }

        await journalPage.ExecuteScriptAsync("window.scrollTo(0, 0)");
        return false;
    }

    private static string UnwrapWebViewScriptResult(string scriptResult) =>
        JsonSerializer.Deserialize(
            scriptResult,
            NikkiwardJsonContext.Default.String) is { } unescaped
            ? unescaped
            : scriptResult;
}
