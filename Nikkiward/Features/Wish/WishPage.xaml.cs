using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nikkiward.Features.Shell;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Wish;

public sealed partial class WishPage : PageBase
{
    private readonly List<WishHistoryRow> _allRows = [];
    private readonly List<WishHistoryMonthMarker> _visibleMonthMarkers = [];
    private string _selectedPoolKey = "*";

    public ObservableCollection<WishHistoryRow> WishRows { get; } = [];

    public ObservableCollection<WishPoolFilter> PoolFilters { get; } = [];

    public ObservableCollection<ResonanceBannerCardViewModel> ResonanceBanners { get; } = [];

    public override string PageTitle => "心愿记录";

    public override FrameworkElement? MastheadInteractionRegion => WishLoginButton;

    public event EventHandler? LoginRequested;

    public WishPage()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
        WishLoginButton.Loaded += OnMastheadChanged;
        WishLoginButton.SizeChanged += OnMastheadSizeChanged;
        ResetState();
    }

    public void ApplySnapshot(
        ResonanceHistorySnapshot snapshot,
        string statusText,
        string bannerCountText,
        string itemCountText,
        string obtainedCountText,
        string totalPullsText,
        string lastSyncedText)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ResonanceBanners.Clear();
        foreach (var banner in snapshot.Banners)
        {
            ResonanceBanners.Add(new ResonanceBannerCardViewModel(banner));
        }

        WishStatusText.Text = statusText;
        WishLastSyncedText.Text = lastSyncedText;
        var itemCount = snapshot.Banners.Sum(banner => banner.Items.Count);
        var obtainedCount = snapshot.Banners.Sum(
            banner => banner.Items.Count(item => item.ObtainCount > 0));
        var totalPulls = snapshot.Banners.Sum(
            banner => ParseInteger(banner.TotalPulls));
        var averages = snapshot.Banners
            .Select(banner => ParseDecimal(banner.AveragePulls))
            .Where(value => value > 0)
            .ToArray();
        SetTileValues(
            totalPulls > 0 ? totalPulls.ToString("N0") : totalPullsText,
            obtainedCount.ToString("N0"),
            averages.Length > 0 ? averages.Average().ToString("0.#") : "暂无数据",
            snapshot.Banners.Count.ToString("N0"));
        WardrobeCountText.Text = $"{snapshot.Banners.Count} 个活动 · {itemCount} 个服装槽位 · {obtainedCount} 个已拥有";
        UpdateContentVisibility();
    }

    public void ApplyWishProjection(
        WishHistoryProjection projection,
        string lastSyncedText)
    {
        ArgumentNullException.ThrowIfNull(projection);
        _allRows.Clear();
        _allRows.AddRange(projection.Rows);
        WishLastSyncedText.Text = lastSyncedText;
        WishStatusText.Text = _allRows.Count == 0
            ? "尚未同步"
            : $"{_allRows.Count} 条历史记录";

        var totalPulls = FormatNumber(projection.Summary.TotalPulls);
        var fiveStarCount = FormatNumber(projection.Summary.FiveStarCount);
        var averagePulls = projection.Summary.AveragePullsPerFiveStar is decimal average
            ? average.ToString("0.#")
            : "暂无数据";
        var guarantee = FormatNumber(projection.Summary.PullsUntilGuarantee);
        SetTileValues(totalPulls, fiveStarCount, averagePulls, guarantee);
        RebuildPoolFilters();
        ApplySelectedFilter();

        UpdateContentVisibility();
    }

    public void ResetState()
    {
        _allRows.Clear();
        ResonanceBanners.Clear();
        WishRows.Clear();
        _visibleMonthMarkers.Clear();
        WishMonthScrollBar.Labels.Clear();
        PoolFilters.Clear();
        _selectedPoolKey = "*";
        WishStatusText.Text = "尚未同步";
        WishLastSyncedText.Text = string.Empty;
        SetTileValues("暂无数据", "暂无数据", "暂无数据", "暂无数据");
        HistoryContent.Visibility = Visibility.Collapsed;
        EmptyStateIsland.Visibility = Visibility.Visible;
        WardrobeCountText.Text = string.Empty;
    }

    public void SetStatus(string statusText)
    {
        WishStatusText.Text = statusText;
    }

    public void ResetScroll()
    {
        WishHistoryScrollView.ScrollTo(0, 0);
    }

    private void RebuildPoolFilters()
    {
        var filters = _allRows
            .Select(row => new
            {
                Key = row.Entry.PoolId ?? row.PoolLabel,
                Label = row.PoolLabel,
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .DistinctBy(item => item.Key, StringComparer.Ordinal)
            .Take(12)
            .ToArray();
        PoolFilters.Clear();
        PoolFilters.Add(new WishPoolFilter
        {
            Key = "*",
            Label = "全部",
            IsSelected = _selectedPoolKey == "*",
        });
        foreach (var filter in filters)
        {
            PoolFilters.Add(new WishPoolFilter
            {
                Key = filter.Key,
                Label = filter.Label,
                IsSelected = string.Equals(
                    _selectedPoolKey,
                    filter.Key,
                    StringComparison.Ordinal),
            });
        }
    }

    private void ApplySelectedFilter()
    {
        WishRows.Clear();
        string? previousMonthKey = null;
        foreach (var row in _allRows.Where(row =>
                     _selectedPoolKey == "*" ||
                     string.Equals(
                         row.Entry.PoolId ?? row.PoolLabel,
                         _selectedPoolKey,
                         StringComparison.Ordinal)))
        {
            var startsMonth = !string.Equals(
                previousMonthKey,
                row.MonthKey,
                StringComparison.Ordinal);
            WishRows.Add(row with { StartsMonth = startsMonth });
            previousMonthKey = row.MonthKey;
        }

        RebuildVisibleMonthMarkers();
        QueueMonthLabelsRefresh();
    }

    private void RebuildVisibleMonthMarkers()
    {
        _visibleMonthMarkers.Clear();
        string? previousMonthKey = null;
        for (var index = 0; index < WishRows.Count; index++)
        {
            var row = WishRows[index];
            if (string.Equals(previousMonthKey, row.MonthKey, StringComparison.Ordinal))
            {
                continue;
            }

            _visibleMonthMarkers.Add(new WishHistoryMonthMarker
            {
                MonthKey = row.MonthKey,
                MonthLabel = row.MonthLabel,
                RowIndex = index,
            });
            previousMonthKey = row.MonthKey;
        }
    }

    private void QueueMonthLabelsRefresh() =>
        DispatcherQueue.TryEnqueue(RefreshMonthLabels);

    private void RefreshMonthLabels()
    {
        WishMonthScrollBar.Labels.Clear();
        if (_visibleMonthMarkers.Count == 0 || WishRows.Count == 0)
        {
            return;
        }

        var rowStride = WishHistoryRepeater.ActualHeight / WishRows.Count;
        if (rowStride <= 0 || double.IsNaN(rowStride))
        {
            return;
        }

        var timelineOffset = WishHistoryRepeater
            .TransformToVisual(WishScrollContent)
            .TransformPoint(default).Y;
        foreach (var marker in _visibleMonthMarkers)
        {
            var offset = Math.Min(
                WishHistoryScrollView.ScrollableHeight,
                timelineOffset + (marker.RowIndex * rowStride));
            WishMonthScrollBar.Labels.Add(new AnnotatedScrollBarLabel(
                marker.MonthLabel,
                offset));
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        WishHistoryScrollView.ScrollPresenter.VerticalScrollController =
            WishMonthScrollBar.ScrollController;
        QueueMonthLabelsRefresh();
    }

    private void OnWishHistoryRepeaterSizeChanged(
        object sender,
        SizeChangedEventArgs e) =>
        QueueMonthLabelsRefresh();

    private void OnWishScrollExtentChanged(
        ScrollView sender,
        object args) =>
        QueueMonthLabelsRefresh();

    private void OnWishMonthDetailLabelRequested(
        AnnotatedScrollBar sender,
        AnnotatedScrollBarDetailLabelRequestedEventArgs args)
    {
        if (sender.Labels.Count == 0)
        {
            return;
        }

        args.Content = sender.Labels
            .OrderBy(label => Math.Abs(label.ScrollOffset - args.ScrollOffset))
            .First()
            .Content;
    }

    private void SetTileValues(
        string totalPulls,
        string fiveStarCount,
        string averagePulls,
        string guarantee)
    {
        TotalPullsTile.Value = totalPulls;
        FiveStarTile.Value = fiveStarCount;
        AveragePullsTile.Value = averagePulls;
        GuaranteeTile.Value = guarantee;
        TotalPullsCompactTile.Value = totalPulls;
        FiveStarCompactTile.Value = fiveStarCount;
        AveragePullsCompactTile.Value = averagePulls;
        GuaranteeCompactTile.Value = guarantee;
    }

    private void UpdateContentVisibility()
    {
        var hasBanners = ResonanceBanners.Count > 0;
        var hasHistory = _allRows.Count > 0;
        HistoryContent.Visibility = hasBanners || hasHistory
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyStateIsland.Visibility = hasBanners || hasHistory
            ? Visibility.Collapsed
            : Visibility.Visible;
        WardrobeSection.Visibility = hasBanners
            ? Visibility.Visible
            : Visibility.Collapsed;
        TimelineIsland.Visibility = hasHistory
            ? Visibility.Visible
            : Visibility.Collapsed;
        PoolFilterIsland.Visibility = hasHistory
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnPoolFilterClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
        {
            _selectedPoolKey = key;
            RebuildPoolFilters();
            ApplySelectedFilter();
        }
    }

    private void OnEmptyOpenClicked(object sender, RoutedEventArgs e) =>
        LoginRequested?.Invoke(this, EventArgs.Empty);

    private void OnLoginClicked(object sender, RoutedEventArgs e) =>
        LoginRequested?.Invoke(this, EventArgs.Empty);

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && _selectedPoolKey != "*")
        {
            _selectedPoolKey = "*";
            RebuildPoolFilters();
            ApplySelectedFilter();
            e.Handled = true;
        }
    }

    private void OnMastheadChanged(object sender, RoutedEventArgs e) =>
        NotifyMastheadInteractionRegionChanged();

    private void OnMastheadSizeChanged(object sender, SizeChangedEventArgs e) =>
        NotifyMastheadInteractionRegionChanged();

    private static string FormatNumber(int? value) =>
        value is int number
            ? number.ToString("N0")
            : "暂无数据";

    private static int ParseInteger(string? value)
    {
        var digits = new string((value ?? string.Empty)
            .Where(character => char.IsDigit(character))
            .ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : 0;
    }

    private static decimal ParseDecimal(string? value)
    {
        var normalized = new string((value ?? string.Empty)
            .Where(character => char.IsDigit(character) || character is '.' or ',')
            .ToArray())
            .Replace(',', '.');
        return decimal.TryParse(
            normalized,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;
    }
}
