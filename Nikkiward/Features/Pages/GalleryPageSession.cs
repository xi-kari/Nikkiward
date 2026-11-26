using System.Globalization;
using System.Text.Json;
using Nikkiward.Features.Gallery;

namespace Nikkiward.Features.Pages;

internal sealed class GalleryPageSession(GalleryIndex index, bool favorites)
{
    private GalleryQuerySnapshot _snapshot = null!;
    private readonly string _scope = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, GalleryIndexPhoto> _issued = [];
    private Dictionary<DateOnly, (int Count, bool Current)> _days = [];
    private int _offset;
    internal GalleryQuery Query { get; private set; } = new();
    internal string Tab { get; private set; } = "all";
    internal string Version => _snapshot.Version.ToString("N");
    internal string Visit { get; private set; } = "";
    private int _sequence;
    internal void BeginVisit()
    {
        Visit = Guid.NewGuid().ToString("N");
        _sequence = 0;
    }

    internal bool Accept(string action, JsonElement message)
    {
        string? Field(string key) => message.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (Field("visit") != Visit) return false;
        if (action is not ("query" or "clear")) return Field("version") == Version;
        if (!message.TryGetProperty("sequence", out var sequence) || sequence.ValueKind != JsonValueKind.Number ||
            !sequence.TryGetInt32(out var n) || n <= _sequence) return false;
        _sequence = n;
        return true;
    }
    internal GalleryQueryPage Page { get; private set; } = null!;

    internal void Reset(IEnumerable<GalleryIndexPhoto> defaults, bool keepWindow = false)
    {
        _snapshot = index.Snapshot(Query, defaults, favorites);
        _days = _snapshot.Ordered.GroupBy(p => DateOnly.FromDateTime(p.LocalTime))
            .ToDictionary(g => g.Key, g => (g.Count(), g.Any(p => p.Session == "current")));
        int length = keepWindow ? Math.Max(80, _issued.Count) : 80;
        _issued.Clear();
        Read(0, length);
    }

    internal void Change(JsonElement m, IEnumerable<GalleryIndexPhoto> defaults)
    {
        string? Field(string key) => m.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        Tab = Field("tab") ?? "all";
        Query = new(Text: Field("text") ?? "", Category: Tab.StartsWith("category:", StringComparison.Ordinal) ? Tab[9..] : null,
            Favorites: Tab == "favorites", Current: Tab == "current", Bursts: Tab == "bursts", Exports: Tab == "exports",
            Filter: Field("filter"), Clothing: long.TryParse(Field("clothing"), out var clothing) ? clothing : null,
            Color: int.TryParse(Field("color"), out var color) ? color : null,
            Sort: Field("sort") switch { "name" => GalleryQuerySort.Name, "category" => GalleryQuerySort.Category,
                "oldest" => GalleryQuerySort.Oldest, _ => GalleryQuerySort.Newest });
        Reset(defaults);
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

    internal GalleryIndexPhoto? Find(string version, string id) => version == Version ? _issued.GetValueOrDefault(id) : null;
    internal IEnumerable<GalleryIndexPhoto> StudioList(GalleryIndexPhoto selected, bool burst) =>
        burst ? index.BurstGroup(selected.Id) : _snapshot.Ordered;

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
        return new { scope = _scope, visit = Visit, sequence = _sequence, version = Version, offset = _offset, next = Page.Next?.Offset, matches = Page.Matches,
            tab = Tab, text = Query.Text, filter = Query.Filter, clothing = Query.Clothing?.ToString(CultureInfo.InvariantCulture),
            color = Query.Color?.ToString(CultureInfo.InvariantCulture), sort = Query.Sort.ToString().ToLowerInvariant(),
            indexing, indexed = progress.Indexed, total = progress.Total, skipped = progress.Skipped,
            missingRoot, overlap, protection, plugin, error, tabs, filters = counts.Filters, colors = counts.Colors, clothes = counts.Clothing,
            days = Page.Days.Select(day => new { date = day.Date.ToString("yyyy.MM.dd"), weekday = day.Date == DateOnly.FromDateTime(DateTime.Today)
                ? "今天" : "周" + "日一二三四五六"[(int)day.Date.DayOfWeek], count = _days[day.Date].Count,
                current = _days[day.Date].Current, photos = day.Photos.Select(p => new { id = p.Id, name = p.FileName,
                    ratio = p.Facts.Width is > 0 && p.Facts.Height is > 0 ? (double)p.Facts.Width / p.Facts.Height.Value : 1.5,
                    time = p.LocalTime.ToString("HH:mm"), favorite = p.Favorite, protectedCopy = p.ProtectedCopy,
                    burst = p.Burst != null, filter = p.Facts.FilterId, lens = p.Facts.FocalLength }) }) };
    }

    private string GalleryCategoryName(string category) => _snapshot.CategoryNames.GetValueOrDefault(category) ?? category;
}
