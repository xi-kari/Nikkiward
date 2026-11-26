using Nikkiward.Features.Pages;
using Nikkiward.Services;
using Nikkiward.ViewModels;
using Windows.Storage.Pickers;

namespace Nikkiward;

public sealed partial class MainPage
{
    internal string EnsureWishAssetsDirectory() => _resonanceCache.EnsureAssetsDirectory();

    private static string? CachedWishImage(string? path) => string.IsNullOrEmpty(path) ? null :
        "https://wish-assets.nikkiward.invalid/" + Uri.EscapeDataString(Path.GetFileName(path));

    private PageAdapter WishPageAdapter() => new("wish", ["import", "export", "sync"],
        WishPageState, (action, _) => HandleWishActionAsync(action));

    private object WishPageState()
    {
        var projection = _wishHistoryProjection;
        var pools = (projection?.Rows ?? []).GroupBy(r => r.Entry.PoolId ?? r.PoolLabel)
            .Select((group, index) =>
            {
                var timeline = WishHistoryProjector.PoolTimeline(group);
                var rows = timeline.Select(item => item.Row).ToArray();
                var records = timeline.Select(item => new {
                    month = item.Row.MonthKey, time = item.Row.TimeLabel, name = item.Row.ItemLabel,
                    rarity = item.Row.RarityLabel, pull = item.Row.Entry.PullNumber, distance = item.Distance
                }).ToArray();
                return new { name = rows.Select(r => r.Entry.PoolName).FirstOrDefault(n => n != null)
                        ?? rows[0].Entry.PoolId ?? $"未命名卡池 {index + 1}",
                    period = $"{rows[^1].LocalTimestamp:MM-dd} – {rows[0].LocalTimestamp:MM-dd}",
                    count = rows.Length, five = rows.All(r => r.Entry.Rarity.HasValue) ? rows.Count(r => r.Entry.Rarity == 5) : (int?)null,
                    distance = rows[0].Entry.Rarity == 5 ? 0 : records[0].distance, records };
            }).ToArray();
        var summary = projection?.Summary;
        var wardrobeSummary = _resonanceSnapshot == null ? null : BuildWishSummary(_resonanceSnapshot);
        var banners = _resonanceSnapshot?.Banners.Select(b => new {
            name = FirstWishValue(b.PoolName, b.OutfitTitle, b.PatchTitle), patch = b.PatchTitle, outfit = b.OutfitTitle,
            cover = CachedWishImage(b.LocalCoverFilePath), rarity = b.Rarity, average = b.AveragePulls, total = b.TotalPulls, completion = b.CompletionText, remaining = b.RemainingText,
            slots = b.Items.OrderBy(i => i.SlotIndex).Select(i => new { name = i.ItemName ?? $"服装槽位 {i.SlotIndex + 1}", count = i.ObtainCount, image = CachedWishImage(i.LocalFilePath) })
        });
        var date = _wishHistoryCapturedAtUtc == default ? _resonanceSnapshot?.CapturedAtUtc : _wishHistoryCapturedAtUtc;
        return new { date = date?.ToLocalTime().ToString("yyyy-MM-dd"), total = summary?.TotalPulls ?? wardrobeSummary?.TotalPulls,
            five = summary?.FiveStarCount, average = (summary?.AveragePullsPerFiveStar ?? wardrobeSummary?.AveragePullsPerFiveStar)?.ToString("0.#"),
            guarantee = summary?.PullsUntilGuarantee, pools, banners };
    }

    private bool _wishPickerOpen;

    private async Task HandleWishActionAsync(string action)
    {
        if (action == "sync") { await OpenResonanceJournalAsync(); return; }
        if (_wishPickerOpen) return;
        _wishPickerOpen = true;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            if (action == "export")
            {
                var path = Win32FolderDialog.PickJsonSavePath(hwnd, "Nikkiward-心愿共鸣.json");
                if (path != null) await _wishHistoryStore.ExportFileAsync(path, ViewModel.GalleryReadOnlyRoots);
                return;
            }
            var open = new FileOpenPicker();
            open.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(open, hwnd);
            var source = await open.PickSingleFileAsync();
            if (source == null) return;
            using var input = await source.OpenStreamForReadAsync();
            var snapshot = await _wishHistoryStore.ImportAsync(input);
            _wishHistoryCapturedAtUtc = snapshot.CapturedAtUtc;
            _wishHistoryProjection = WishHistoryProjector.Project(snapshot.Entries, snapshot.Summary, TimeZoneInfo.Local);
            ApplyWishHistoryProjection(_wishHistoryProjection, snapshot.CapturedAtUtc);
        }
        finally { _wishPickerOpen = false; }
    }
}

