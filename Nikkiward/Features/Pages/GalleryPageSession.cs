using System.Globalization;
using System.Text.Json;
using Nikkiward.Features.Gallery;
using Nikkiward.Services;

namespace Nikkiward.Features.Pages;

internal sealed class GalleryPageSession(GalleryIndex index, bool favorites)
{
    private static readonly GalleryQuerySnapshot Empty = new(new(), new GalleryQueryCatalog([], [], false), new(0, 0, 0, false));
    private GalleryQuerySnapshot _snapshot = Empty;
    private readonly SemaphoreSlim _worker = new(1);
    private CancellationTokenSource? _building;
    private GalleryQuery _requested = new();
    private string _requestedTab = "all";
    private int _publishedSequence;
    private readonly string _scope = Guid.NewGuid().ToString("N");
    private Dictionary<string, GalleryIndexPhoto> _issued = [];
    private int _offset, _restoreLength;
    internal GalleryQuery Query => _snapshot.Query;
    internal string Tab { get; private set; } = "all";
    internal string Version => _snapshot.Version.ToString("N");
    internal string Visit { get; private set; } = "";
    private int _sequence;
    internal void BeginVisit()
    {
        _restoreLength = Math.Max(_restoreLength, _issued.Count);
        _issued = [];
        _snapshot = Empty;
        Page = Empty.Page(0);
        _offset = 0;
        Visit = Guid.NewGuid().ToString("N");
        _sequence = _publishedSequence = 0;
    }

    internal bool Accept(string action, JsonElement message)
    {
        string? Field(string key) => message.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (Field("visit") != Visit) return false;
        if (action is not ("query" or "clear")) return _publishedSequence == _sequence && Field("version") == Version;
        if (!message.TryGetProperty("sequence", out var sequence) || sequence.ValueKind != JsonValueKind.Number ||
            !sequence.TryGetInt32(out var n) || n <= _sequence) return false;
        _sequence = n;
        return true;
    }
    internal GalleryQueryPage Page { get; private set; } = Empty.Page(0);

    internal void CancelBuild() => _building?.Cancel();

    internal async Task<bool> ResetAsync(IEnumerable<GalleryIndexPhoto> defaults, bool keepWindow = false,
        (PlaySession? Current, PlaySession? Previous)? sessions = null)
    {
        CancelBuild();
        using var build = new CancellationTokenSource();
        _building = build;
        var query = _requested;
        int length = keepWindow && _sequence == _publishedSequence ? Math.Max(80, Math.Max(_issued.Count, _restoreLength)) : 80;
        var prior = keepWindow && _publishedSequence == _sequence ? _snapshot : null;
        (GalleryQuerySnapshot Snapshot, GalleryQueryPage Page, Dictionary<string, GalleryIndexPhoto> Issued)? Build()
        {
            var snapshot = index.Snapshot(query, defaults, favorites, build.Token, sessions, prior);
            if (ReferenceEquals(snapshot, prior)) return null;
            var first = snapshot.Page(0, length);
            return (snapshot, first, first.Photos.ToDictionary(p => p.Id));
        }
        try
        {
            await _worker.WaitAsync(build.Token);
            try
            {
                var window = await Task.Run(Build, build.Token);
                if (build.IsCancellationRequested || window == null) return false;
                (_snapshot, Page, _issued) = window.Value;
            }
            finally { _worker.Release(); }
            _restoreLength = 0;
            Tab = _requestedTab;
            _publishedSequence = _sequence;
            _offset = 0;
            return true;
        }
        catch (OperationCanceledException) when (build.IsCancellationRequested) { return false; }
        finally { if (_building == build) _building = null; }
    }

    internal Task<bool> ChangeAsync(JsonElement m, IEnumerable<GalleryIndexPhoto> defaults,
        (PlaySession? Current, PlaySession? Previous)? sessions = null)
    {
        string? Field(string key) => m.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        _requestedTab = Field("tab") ?? "all";
        _requested = new(Text: Field("text") ?? "", Category: _requestedTab.StartsWith("category:", StringComparison.Ordinal) ? _requestedTab[9..] : null,
            Favorites: _requestedTab == "favorites", Current: _requestedTab == "current", Bursts: _requestedTab == "bursts", Exports: _requestedTab == "exports",
            Filter: Field("filter"), Clothing: long.TryParse(Field("clothing"), out var clothing) ? clothing : null,
            Color: int.TryParse(Field("color"), out var color) ? color : null,
            Sort: Field("sort") switch { "name" => GalleryQuerySort.Name, "category" => GalleryQuerySort.Category,
                "oldest" => GalleryQuerySort.Oldest, _ => GalleryQuerySort.Newest });
        return ResetAsync(defaults, sessions: sessions);
    }

    internal bool Next(string version, int offset)
    {
        if (version != Version || Page.Next?.Offset != offset) return false;
        Read(offset);
        return true;
    }

    private void Read(int offset, int length = 80)
    {
        _offset = offset;
        Page = _snapshot.Page(offset, length);
        foreach (var p in Page.Photos) _issued[p.Id] = p;
    }

    internal GalleryIndexPhoto? Find(string version, string id) => _publishedSequence == _sequence && version == Version ? _issued.GetValueOrDefault(id) : null;
    internal IEnumerable<GalleryIndexPhoto> StudioList(GalleryIndexPhoto selected, bool burst) =>
        !burst ? _snapshot.Ordered : selected.Burst is { } group ? _snapshot.BurstGroups[group] : [];

    internal object State(bool indexing, GalleryIndexProgress progress, bool missingRoot, bool overlap, bool protection,
        bool plugin, string? error)
    {
        var counts = Page.Counts;
        var tabs = new List<object>();
        void TabCount(string id, string name, int count) => tabs.Add(new { id, name, count });
        TabCount("all", "全部", counts.All);
        foreach (var (category, count) in counts.Categories.Where(c => c.Key != "Exports").OrderBy(c => c.Key))
            TabCount("category:" + category, category == "DefaultFavorites" ? "默认收藏" :
                GalleryCategoryName(category), count);
        TabCount("bursts", "连拍", counts.Bursts);
        if (!favorites) TabCount("favorites", "收藏", counts.Favorites);
        TabCount("current", "本次游玩", counts.Current);
        TabCount("exports", "导出的副本", counts.Exports);
        return new { scope = _scope, visit = Visit, sequence = _publishedSequence, version = Version, offset = _offset, next = Page.Next?.Offset, matches = Page.Matches,
            tab = Tab, text = Query.Text, filter = Query.Filter, clothing = Query.Clothing?.ToString(CultureInfo.InvariantCulture),
            color = Query.Color?.ToString(CultureInfo.InvariantCulture), sort = Query.Sort.ToString().ToLowerInvariant(),
            indexing, indexed = progress.Indexed, total = progress.Total, skipped = progress.Skipped,
            missingRoot, overlap, protection, plugin, error, tabs, filters = counts.Filters, colors = counts.Colors, clothes = counts.Clothing,
            days = Page.Days.Select(day => new { date = day.Date?.ToString("yyyy.MM.dd"), weekday = day.Date == null ? null : day.Date == DateOnly.FromDateTime(DateTime.Today)
                ? "今天" : "周" + "日一二三四五六"[(int)day.Date.Value.DayOfWeek], count = day.Date is {} date ? _snapshot.DayTotals[date].Count : Page.Matches,
                current = day.Date is {} d && _snapshot.DayTotals[d].Current, photos = day.Photos.Select(p => new { id = p.Id, name = p.FileName,
                    ratio = p.Facts.Width is > 0 && p.Facts.Height is > 0 ? (double)p.Facts.Width / p.Facts.Height.Value : 1.5,
                    time = p.LocalTime.ToString("HH:mm"), favorite = p.Favorite, protectedCopy = p.ProtectedCopy,
                    burst = p.Burst != null, filter = p.Facts.FilterId, lens = p.Facts.FocalLength }) }) };
    }

    private string GalleryCategoryName(string category) => _snapshot.CategoryNames.GetValueOrDefault(category) ?? category;
}
