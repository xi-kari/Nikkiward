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
using Nikkiward.Features.Wish;
using Nikkiward.Models;
using Nikkiward.Pages;
using Nikkiward.Services;
using Nikkiward.Serialization;
using Nikkiward.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private async void OnJournalOpenClicked(object sender, RoutedEventArgs e)
    {
        await OpenJournalAsync();
    }

    private async void OnJournalCacheClearClicked(object sender, RoutedEventArgs e)
    {
        await ClearJournalCacheAsync();
    }

    private async void OnJournalOpenRequested(object? sender, EventArgs e)
    {
        await OpenJournalAsync();
    }

    private async Task OpenJournalAsync()
    {
        try
        {
            SetShellNavigationSelection(LibraryNavigationItem);
            ShowLibrary();
            var journalPage = ContentFrame.Content as JournalPage
                ?? throw new InvalidOperationException("Journal page navigation did not complete.");
            _journalRouteIntent = JournalRouteIntent.Overview;
            await journalPage.ShowBrowserAsync();
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"奇想手账内置页面打开失败：{ex.GetType().Name}: {ex.Message}");
            _hostedJournalPage?.SetBrowserStatus("内置页面暂时不可用；可使用“系统浏览器打开”。");
            await TryShowDialogAsync("奇想手账内置页面打开失败", "请点击手账面板中的“系统浏览器打开”，登录后仍可回到 Nikkiward 手动同步当前页面。\n\n" + ViewModel.LastErrorText);
        }
    }

    private async void OnJournalNavigationFinished(
        object? sender,
        JournalNavigationEventArgs args)
    {
        if (_hostedJournalPage is not { } journalPage)
        {
            return;
        }

        var uri = args.Uri;
        if (uri?.AbsoluteUri.Equals("about:blank", StringComparison.OrdinalIgnoreCase) == true)
        {
            return;
        }

        if (JournalNavigationFailureProjector.Project(
                args.IsSuccess,
                args.IsCurrentNavigation,
                args.WebErrorStatus) is { } navigationFailure)
        {
            ApplyJournalCaptureFailure(
                navigationFailure,
                DateTimeOffset.UtcNow,
                isAutomatic: true);
            return;
        }

        if (!args.IsSuccess || !args.IsCurrentNavigation)
        {
            return;
        }

        await HandleJournalRouteAsync(journalPage, uri);
    }

    private async void OnJournalRouteChanged(
        object? sender,
        JournalRouteChangedEventArgs args)
    {
        if (_hostedJournalPage is not { } journalPage)
        {
            return;
        }

        await HandleJournalRouteAsync(journalPage, args.Uri);
    }

    private async Task HandleJournalRouteAsync(JournalPage journalPage, Uri? uri)
    {

        if (!IsOfficialJournalUri(uri))
        {
            journalPage.SetBrowserStatus("当前页面不是官方奇想手账域名；同步按钮保持只读禁用。");
            return;
        }

        journalPage.ClearSyncFailure();

        if (IsResonanceHistoryUri(uri))
        {
            journalPage.SetBrowserStatus("已打开共鸣衣橱；正在整理活动、共鸣次数和服装获得记录。");
            var currentUrl = uri!.AbsoluteUri;
            if (!string.Equals(_resonanceLastAutoSyncedUrl, currentUrl, StringComparison.Ordinal) &&
                DateTimeOffset.UtcNow >= _resonanceNextAutomaticSyncUtc)
            {
                await SyncResonanceHistoryAsync(isAutomatic: true);
            }
        }
        else if (IsJournalContentUri(uri))
        {
            if (JournalRouteIntentProjector.ShouldRedirectToResonance(_journalRouteIntent, uri))
            {
                journalPage.SetBrowserStatus("登录已完成；正在进入共鸣衣橱并等待活动记录加载。");
                journalPage.Navigate(JournalPage.ResonanceUri);
                return;
            }

            journalPage.SetBrowserStatus("已打开官方奇想手账；正在提取页面展示的非敏感统计与公开图片地址。");
            var currentUrl = uri!.AbsoluteUri;
            if (!string.Equals(_journalLastAutoSyncedUrl, currentUrl, StringComparison.Ordinal) &&
                DateTimeOffset.UtcNow >= _journalNextAutomaticSyncUtc)
            {
                await SyncJournalPageAsync(isAutomatic: true);
            }
        }
        else
        {
            journalPage.SetBrowserStatus("请在官方页面完成登录；登录会话由 WebView2 管理，Nikkiward 不读取凭据。");
        }
    }

    private async void OnJournalSyncRequested(object? sender, EventArgs e)
    {
        if (JournalRouteIntentProjector.ShouldRedirectToResonance(
                _journalRouteIntent,
                _hostedJournalPage?.CurrentUri))
        {
            _hostedJournalPage?.SetBrowserStatus("正在进入共鸣衣橱；登录会话保持在当前 WebView2 中。");
            _hostedJournalPage?.Navigate(JournalPage.ResonanceUri);
            return;
        }

        if (IsResonanceHistoryUri(_hostedJournalPage?.CurrentUri))
        {
            await SyncResonanceHistoryAsync(isAutomatic: false);
        }
        else
        {
            await SyncJournalPageAsync(isAutomatic: false);
        }
    }

    private async void OnJournalExternalOpenRequested(object? sender, EventArgs e)
    {
        try
        {
            var opened = await Launcher.LaunchUriAsync(JournalPage.LoginUri);
            if (!opened)
            {
                await TryShowDialogAsync("无法打开系统浏览器", JournalPage.LoginUri.AbsoluteUri);
            }
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"系统浏览器打开失败：{ex.GetType().Name}: {ex.Message}");
            await TryShowDialogAsync("系统浏览器打开失败", ViewModel.LastErrorText);
        }
    }

    private void OnJournalBrowserClosed(object? sender, EventArgs e)
    {
        var returnToResonance = IsResonanceHistoryUri(_hostedJournalPage?.LastContentUri);
        if (returnToResonance)
        {
            SetShellNavigationSelection(ResonanceNavigationItem);
            ShowResonance();
        }

        _journalRouteIntent = JournalRouteIntent.Unknown;
    }

    private async Task<bool> SyncJournalPageAsync(bool isAutomatic)
    {
        if (_hostedJournalPage is not { IsBrowserInitialized: true } journalPage ||
            _journalSyncInProgress)
        {
            return false;
        }

        if (!IsJournalContentUri(journalPage.CurrentUri))
        {
            ApplyJournalCaptureFailure(
                JournalCaptureFailureKind.NotSignedIn,
                DateTimeOffset.UtcNow,
                isAutomatic);
            return false;
        }

        if (!await _journalCaptureGate.WaitAsync(0))
        {
            return false;
        }

        _journalSyncInProgress = true;
        journalPage.ClearSyncFailure();
        journalPage.SetSyncInProgress(true);
        var attemptedAtUtc = DateTimeOffset.UtcNow;
        var captureValidated = false;
        var routeGeneration = journalPage.RouteGeneration;
        var capturedUrl = journalPage.CurrentUri?.AbsoluteUri;
        try
        {
            journalPage.SetBrowserStatus(isAutomatic
                ? "正在自动同步官方页面展示内容……"
                : "正在同步当前官方页面展示内容……");
            if (!await PrepareJournalDocumentAsync(journalPage, routeGeneration))
            {
                if (!IsCurrentOverviewRoute(journalPage, routeGeneration))
                {
                    return false;
                }

                journalPage.SetBrowserStatus("官方页面仍在载入，正在重试同步……");
                await Task.Delay(TimeSpan.FromMilliseconds(800), _lifetimeCancellation?.Token ?? CancellationToken.None);
                if (!await PrepareJournalDocumentAsync(journalPage, routeGeneration))
                {
                    if (!IsCurrentOverviewRoute(journalPage, routeGeneration))
                    {
                        return false;
                    }

                    ApplyJournalCaptureFailure(
                        JournalCaptureFailureKind.StructureChanged,
                        attemptedAtUtc,
                        isAutomatic);
                    return false;
                }
            }
            var scriptResult = await ExecuteJournalScriptWithRetryAsync(
                journalPage,
                JournalWebCaptureScripts.Overview,
                () => IsCurrentOverviewRoute(journalPage, routeGeneration));
            var json = scriptResult;
            if (JsonSerializer.Deserialize(
                    scriptResult,
                    NikkiwardJsonContext.Default.String) is { } unescaped)
            {
                json = unescaped;
            }

            var snapshot = JsonSerializer.Deserialize<JournalSnapshot>(
                json,
                new NikkiwardJsonContext(JournalCaptureJsonOptions).JournalSnapshot);
            var sourcedFieldCount = snapshot is null ? 0 : new[]
            {
                (snapshot.LoginDays, snapshot.LoginDaysSource),
                (snapshot.GameHours, snapshot.GameHoursSource),
                (snapshot.OutfitCount, snapshot.OutfitCountSource),
                (snapshot.MomoCloakCount, snapshot.MomoCloakCountSource),
                (snapshot.SketchCount, snapshot.SketchCountSource),
            }.Count(field =>
                !string.IsNullOrWhiteSpace(field.Item1) &&
                !string.IsNullOrWhiteSpace(field.Item2));
            var sourcedSectionCount = snapshot?.Sections.Count(section =>
                JournalSectionKey.IsStable(section.SectionKey) &&
                !string.IsNullOrWhiteSpace(section.Source)) ?? 0;
            var assessment = JournalCaptureAssessmentProjector.Assess(
                navigationSucceeded: true,
                isOfficialJournalPage: string.Equals(
                    snapshot?.SourcePagePath,
                    JournalPagePath,
                    StringComparison.OrdinalIgnoreCase),
                snapshot?.SourcePagePath,
                sourcedFieldCount,
                sourcedSectionCount);
            if (snapshot is null ||
                snapshot.SchemaVersion != JournalSnapshot.CurrentSchemaVersion ||
                !assessment.IsUsable)
            {
                ApplyJournalCaptureFailure(
                    assessment.FailureKind ?? JournalCaptureFailureKind.StructureChanged,
                    attemptedAtUtc,
                    isAutomatic);
                return false;
            }

            if (!IsCurrentOverviewRoute(journalPage, routeGeneration))
            {
                return false;
            }

            var existingSnapshot = await _journalCache.LoadAsync(
                _lifetimeCancellation?.Token ?? CancellationToken.None);
            if (existingSnapshot is not null &&
                JournalSnapshotQualityProjector.IsObviousRegression(
                    JournalSnapshotQualityProjector.Measure(existingSnapshot),
                    JournalSnapshotQualityProjector.Measure(snapshot)))
            {
                ApplyJournalCaptureFailure(
                    JournalCaptureFailureKind.StructureChanged,
                    attemptedAtUtc,
                    isAutomatic);
                return false;
            }

            captureValidated = true;
            snapshot = await _journalCache.DownloadAndSaveAsync(
                snapshot,
                _lifetimeCancellation?.Token ?? CancellationToken.None,
                () => IsCurrentOverviewRoute(journalPage, routeGeneration));
            if (!IsCurrentOverviewRoute(journalPage, routeGeneration))
            {
                return false;
            }

            ApplyJournalSnapshot(snapshot);
            _journalLastAutoSyncedUrl = capturedUrl;
            _journalConsecutiveFailures = 0;
            _journalNextAutomaticSyncUtc = JournalSyncScheduleProjector.Project(
                attemptedAtUtc,
                JournalSyncScheduleProjector.MinimumAutomaticInterval,
                consecutiveFailures: 0).NextAttemptAtUtc;
            journalPage.SetBrowserStatus(
                $"已同步首页统计、{snapshot.Sections.Count} 个模块与 {snapshot.Resources.Count} 项页面美术资源。");
            await journalPage.ReleaseBrowserDocumentAsync();
            journalPage.HideBrowser();
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!_lifetimeCancellation?.IsCancellationRequested ?? true)
            {
                journalPage.SetBrowserStatus("手账同步已取消。");
            }

            return false;
        }
        catch (JsonException)
        {
            ApplyJournalCaptureFailure(
                JournalCaptureFailureKind.StructureChanged,
                attemptedAtUtc,
                isAutomatic);
            return false;
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"手账同步失败：{ex.GetType().Name}: {ex.Message}");
            ApplyJournalCaptureFailure(
                captureValidated
                    ? JournalCaptureFailureKind.LocalProcessingFailure
                    : IsJournalContentUri(journalPage.CurrentUri)
                    ? JournalCaptureFailureKind.StructureChanged
                    : JournalCaptureFailureKind.NotSignedIn,
                attemptedAtUtc,
                isAutomatic);
            return false;
        }
        finally
        {
            journalPage.SetSyncInProgress(false);
            _journalSyncInProgress = false;
            _journalCaptureGate.Release();
        }
    }

    private async Task<bool> PrepareJournalDocumentAsync(
        JournalPage journalPage,
        long routeGeneration)
    {
        var cancellationToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
        string? previousSignature = null;
        var stableSamples = 0;
        for (var attempt = 0; attempt < 80; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentOverviewRoute(journalPage, routeGeneration))
            {
                return false;
            }

            var scriptResult = await ExecuteJournalScriptWithRetryAsync(
                journalPage,
                JournalWebCaptureScripts.PrepareOverview,
                () => IsCurrentOverviewRoute(journalPage, routeGeneration));
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

            if (stableSamples >= JournalDocumentReadinessProjector.RequiredStableSamples)
            {
                try
                {
                    using var document = JsonDocument.Parse(signature);
                    var root = document.RootElement;
                    var documentReady = root.TryGetProperty("documentReady", out var readyElement) &&
                        readyElement.GetBoolean();
                    var visibleLineCount = root.TryGetProperty("visibleLineCount", out var lineElement)
                        ? lineElement.GetInt32()
                        : 0;
                    var stableNodeKeyCount = root.TryGetProperty("stableNodeKeyCount", out var keyElement)
                        ? keyElement.GetInt32()
                        : 0;
                    var pendingImageCount = root.TryGetProperty("pendingImageCount", out var pendingElement)
                        ? pendingElement.GetInt32()
                        : 0;
                    if (JournalDocumentReadinessProjector.IsOverviewReady(
                            documentReady,
                            visibleLineCount,
                            stableNodeKeyCount,
                            pendingImageCount))
                    {
                        return true;
                    }
                }
                catch (JsonException)
                {
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(140), cancellationToken);
        }

        return false;
    }

    private async Task<string> ExecuteJournalScriptWithRetryAsync(
        JournalPage journalPage,
        string script,
        Func<bool>? routeGuard = null)
    {
        var cancellationToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (routeGuard?.Invoke() == false)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            try
            {
                var result = await journalPage.ExecuteScriptAsync(script);
                if (routeGuard?.Invoke() == false)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(180), cancellationToken);
            }
        }
    }

    private static bool IsCurrentOverviewRoute(JournalPage journalPage, long routeGeneration) =>
        journalPage.RouteGeneration == routeGeneration &&
        IsJournalContentUri(journalPage.CurrentUri);

    private void ApplyJournalCaptureFailure(
        JournalCaptureFailureKind kind,
        DateTimeOffset attemptedAtUtc,
        bool isAutomatic)
    {
        var projection = JournalCaptureFailureProjector.Project(kind);
        _hostedJournalPage?.ShowSyncFailure(projection);
        _hostedJournalPage?.SetBrowserStatus(projection.Message);

        if (projection.CanRetryAutomatically)
        {
            _journalConsecutiveFailures++;
            _journalNextAutomaticSyncUtc = JournalSyncScheduleProjector.Project(
                attemptedAtUtc,
                JournalSyncScheduleProjector.MinimumAutomaticInterval,
                _journalConsecutiveFailures).NextAttemptAtUtc;
        }
        else
        {
            _journalNextAutomaticSyncUtc = DateTimeOffset.MaxValue;
        }
    }

}
