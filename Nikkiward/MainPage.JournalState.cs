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
using Nikkiward.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private async Task LoadJournalSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _journalCache.LoadAsync(cancellationToken);
            if (snapshot is not null)
            {
                ApplyJournalSnapshot(snapshot);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"手账缓存读取失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task LoadResonanceHistoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _resonanceCache.LoadAsync(cancellationToken);
            if (snapshot is not null)
            {
                ApplyResonanceHistory(snapshot);
                await ApplyWishHistoryFromResonanceAsync(snapshot, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError(
                $"共鸣历史缓存读取失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task LoadWishHistoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _wishHistoryStore.LoadAsync(cancellationToken);
            _wishHistoryProjection = WishHistoryProjector.Project(
                snapshot.Entries,
                snapshot.Summary,
                TimeZoneInfo.Local);
            _wishHistoryCapturedAtUtc = snapshot.CapturedAtUtc;
            ApplyWishHistoryProjection(_wishHistoryProjection, snapshot.CapturedAtUtc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError(
                $"心愿历史读取失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ApplyResonanceHistory(ResonanceHistorySnapshot snapshot)
    {
        _resonanceSnapshot = snapshot;
        var itemCount = snapshot.Banners.Sum(banner => banner.Items.Count);
        var obtainedCount = snapshot.Banners.Sum(
            banner => banner.Items.Count(item => item.ObtainCount > 0));
        var totalPulls = snapshot.Banners.Sum(
            banner => ParseDisplayedInteger(banner.TotalPulls));
        _hostedWishPage?.ApplySnapshot(
            snapshot,
            $"{snapshot.Banners.Count} 个活动 · {itemCount} 条服装记录",
            snapshot.Banners.Count.ToString(CultureInfo.CurrentCulture),
            itemCount.ToString(CultureInfo.CurrentCulture),
            obtainedCount.ToString(CultureInfo.CurrentCulture),
            totalPulls.ToString(CultureInfo.CurrentCulture),
            snapshot.CapturedAtUtc.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.CurrentCulture));
    }

    private async Task ApplyWishHistoryFromResonanceAsync(
        ResonanceHistorySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var rows = snapshot.Banners
            .SelectMany((banner, bannerIndex) => banner.Items
                .Where(item => item.TimestampUtc is not null)
                .Select((item, itemIndex) =>
                new WishHistoryCaptureRow
                {
                    StableId = item.StableId,
                    TimestampUtc = item.TimestampUtc,
                    PoolId = FirstWishValue(banner.PoolId, NormalizeWishPart(banner.PatchTitle)),
                    PoolName = item.PoolName ?? FirstWishValue(
                        banner.PoolName,
                        banner.OutfitTitle,
                        banner.PatchTitle),
                    ItemId = item.ItemId,
                    ItemName = item.ItemName,
                    Rarity = item.Rarity,
                    PullNumber = item.PullNumber,
                    ImageUri = item.ImageUri ?? item.ImageUrl,
                    SlotIndex = item.SlotIndex >= 0 ? item.SlotIndex : itemIndex,
                }))
            .ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        var imported = WishHistoryCaptureAdapter.FromRows(rows, snapshot.CapturedAtUtc);
        await MergeWishHistoryAsync(
            imported,
            BuildWishSummary(snapshot),
            snapshot.CapturedAtUtc,
            cancellationToken);
    }

    private async Task MergeWishHistoryAsync(
        IEnumerable<WishHistoryEntry> imported,
        WishHistorySummary? summary,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _wishHistoryStore.MergeAndSaveAsync(
                imported,
                summary,
                capturedAtUtc,
                cancellationToken);
            _wishHistoryProjection = WishHistoryProjector.Project(
                snapshot.Entries,
                snapshot.Summary,
                TimeZoneInfo.Local);
            _wishHistoryCapturedAtUtc = snapshot.CapturedAtUtc;
            ApplyWishHistoryProjection(_wishHistoryProjection, snapshot.CapturedAtUtc);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError(
                $"心愿历史保存失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ApplyWishHistoryProjection(
        WishHistoryProjection projection,
        DateTimeOffset capturedAtUtc)
    {
        _hostedWishPage?.ApplyWishProjection(
            projection,
            capturedAtUtc == default
                ? string.Empty
                : capturedAtUtc.ToLocalTime().ToString(
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.CurrentCulture));
    }

    private static WishHistorySummary BuildWishSummary(ResonanceHistorySnapshot snapshot)
    {
        var totalPulls = snapshot.Banners
            .Select(banner => ParseDisplayedInteger(banner.TotalPulls))
            .Where(value => value > 0)
            .DefaultIfEmpty()
            .Sum();
        var averagePulls = snapshot.Banners
            .Select(banner => ParseDisplayedDecimal(banner.AveragePulls))
            .Where(value => value > 0)
            .ToArray();
        return new WishHistorySummary
        {
            TotalPulls = totalPulls > 0 ? totalPulls : null,
            FiveStarCount = null,
            AveragePullsPerFiveStar = averagePulls.Length == 0
                ? null
                : averagePulls.Average(),
            PullsUntilGuarantee = null,
        };
    }

    private static decimal ParseDisplayedDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var match = Regex.Match(
            value,
            @"\d+(?:[.,]\d+)?",
            RegexOptions.CultureInvariant);
        return match.Success &&
               decimal.TryParse(
                   match.Value.Replace(',', '.'),
                   NumberStyles.Number,
                   CultureInfo.InvariantCulture,
                   out var parsed)
            ? parsed
            : 0;
    }

    private static string NormalizeWishPart(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();

    private static string? FirstWishValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static int ParseDisplayedInteger(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var matches = Regex.Matches(value, @"\d[\d,]*", RegexOptions.CultureInvariant);
        if (matches.Count == 0)
        {
            return 0;
        }

        var normalized = matches[^1].Value.Replace(",", string.Empty, StringComparison.Ordinal);
        return int.TryParse(
            normalized,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;
    }

    private void ApplyJournalSnapshot(JournalSnapshot snapshot)
    {
        _journalSnapshot = snapshot;
        _journalDurationText = NormalizeJournalHours(snapshot.GameHours);
        _journalDurationSourceText = "来源：官方奇想手账 · 本地快照";
        _journalDurationDetailText =
            $"最近同步于 {snapshot.CapturedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}；快照只包含页面展示的非敏感统计和公开图片地址。";
        UpdateJournalDurationUi();
        _hostedJournalPage?.ApplySnapshot(
            snapshot,
            _journalDurationText,
            _journalDurationSourceText);
    }

    private async void OnJournalClearCacheRequested(object? sender, EventArgs e)
    {
        await ClearJournalCacheAsync();
    }

    private async Task ClearJournalCacheAsync()
    {
        try
        {
            await _journalCache.ClearAsync(_lifetimeCancellation?.Token ?? CancellationToken.None);
            await _resonanceCache.ClearAsync(_lifetimeCancellation?.Token ?? CancellationToken.None);
            _journalSnapshot = null;
            _resonanceSnapshot = null;
            _journalDurationText = "奇想手账未同步";
            _journalDurationSourceText = "来源：官方手账 · 尚未同步";
            _journalDurationDetailText = "打开官方奇想手账并点击同步后，页面统计会重新写入本地快照。";
            UpdateJournalDurationUi();
            _hostedJournalPage?.ResetState(_journalDurationSourceText);
            if (_wishHistoryProjection is null)
            {
                _hostedWishPage?.ResetState();
            }
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"手账缓存清除失败：{ex.GetType().Name}: {ex.Message}");
            await TryShowDialogAsync("手账缓存清除失败", ViewModel.LastErrorText);
        }
    }

    private static bool IsOfficialJournalUri(Uri? uri) =>
        uri is not null &&
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        uri.Host.Equals("myl.nuanpaper.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsJournalContentUri(Uri? uri) =>
        IsOfficialJournalUri(uri) &&
        JournalRouteIntentProjector.Project(uri) == JournalRouteIntent.Overview;

    private static bool IsResonanceHistoryUri(Uri? uri) =>
        IsOfficialJournalUri(uri) &&
        JournalRouteIntentProjector.Project(uri) == JournalRouteIntent.ResonanceHistory;

    private static string NormalizeJournalHours(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "奇想手账未同步";
        }

        var match = Regex.Match(
            value,
            @"(?<![\d.])(\d+(?:[\.,]\d+)?)\s*(?:h|小时|hours?)(?!\w)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? $"{match.Groups[1].Value.Replace(',', '.')}h" : value.Trim();
    }

    private void UpdateJournalDurationUi()
    {
        _hostedLauncherPage?.ApplyJournalDuration(
            _journalDurationText,
            _journalDurationDetailText);
    }
}
