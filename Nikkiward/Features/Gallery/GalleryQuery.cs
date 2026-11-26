using Nikkiward.Services;

namespace Nikkiward.Features.Gallery;

internal sealed record GalleryIndexFacts(int? Width, int? Height, bool HasParameters, string? FilterId,
    long[] ClothingIds, double? FocalLength, double? Aperture, int[] Colors);

internal sealed record GalleryIndexPhoto(string Id, string FileName, string RelativePath, string CategoryId,
    string CategoryName, DateTime LocalTime, long Size, GalleryIndexFacts Facts, bool Favorite,
    bool Exported, string? Session, string? Burst = null, bool ProtectedCopy = false);

internal enum GalleryQuerySort { Newest, Oldest, Name, Category }

internal sealed record GalleryQuery(string Text = "", string? Category = null, bool Favorites = false,
    bool Current = false, bool Bursts = false, bool Exports = false, string? Filter = null,
    long? Clothing = null, int? Color = null, GalleryQuerySort Sort = GalleryQuerySort.Newest);

internal sealed record GalleryIndexProgress(int Indexed, int Total, int Skipped, bool IsIndexing);
internal sealed record GalleryQueryCursor(Guid Version, int Offset);
internal sealed record GalleryQueryCounts(int All, int Favorites, int Current, int Bursts, int Exports,
    Dictionary<string, int> Categories, Dictionary<string, int> Filters, Dictionary<long, int> Clothing,
    Dictionary<int, int> Colors);
internal sealed record GalleryQueryDay(DateOnly? Date, GalleryIndexPhoto[] Photos);
internal sealed record GalleryQueryPage(Guid Version, GalleryIndexPhoto[] Photos, GalleryQueryDay[] Days,
    int Matches, GalleryQueryCursor? Next, GalleryQueryCounts Counts, GalleryIndexProgress Progress);

internal sealed class GalleryQueryCatalog
{
    internal readonly GalleryIndexPhoto[] Originals, Exports;
    internal readonly GalleryQueryCounts Counts, ExportCounts;
    internal Dictionary<string, string> CategoryNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal readonly ILookup<string, GalleryIndexPhoto> BurstGroups;

    internal GalleryQueryCatalog(GalleryIndexPhoto[] rows, IEnumerable<GalleryIndexPhoto> defaults, bool favoritesRoute)
    {
        BurstGroups = rows.Where(p => p.Burst != null).ToLookup(p => p.Burst!);
        var photos = rows.Concat(defaults);
        var parts = (favoritesRoute ? photos.Where(p => p.Favorite) : photos).ToLookup(p => p.Exported);
        Originals = parts[false].ToArray();
        Exports = parts[true].ToArray();
        foreach (var p in Originals) CategoryNames.TryAdd(p.CategoryId, p.CategoryName);
        var totals = new GalleryQueryCounts(Originals.Length, Originals.Count(p => p.Favorite),
            Originals.Count(p => p.Session == "current"), Originals.Count(p => p.Burst != null), Exports.Length,
            Originals.CountBy(p => p.CategoryId, StringComparer.OrdinalIgnoreCase).ToDictionary(StringComparer.OrdinalIgnoreCase), [], [], []);
        Counts = WithOptions(totals, Originals);
        ExportCounts = WithOptions(totals, Exports);
    }

    private readonly Lock _sessionGate = new();
    private (PlaySession? Current, PlaySession? Previous) _sessions;
    private GalleryQueryCatalog? _classified;

    internal GalleryQueryCatalog ForSessions((PlaySession? Current, PlaySession? Previous) sessions)
    {
        lock (_sessionGate)
        {
            if (_classified != null && sessions == _sessions) return _classified;
            _sessions = sessions;
            var last = _classified ?? this;
            GalleryIndexPhoto[]? changed = null;
            for (int i = 0; i < last.Originals.Length; i++)
            {
                var p = last.Originals[i];
                if (p.CategoryId == "DefaultFavorites") continue;
                var utc = p.LocalTime.ToUniversalTime();
                string? session = sessions.Current != null && PlayTimeLedger.ContainsPhoto(sessions.Current, utc) ? "current" :
                    sessions.Previous != null && PlayTimeLedger.ContainsPhoto(sessions.Previous, utc) ? "previous" : null;
                if (p.Session == session) continue;
                changed ??= (GalleryIndexPhoto[])last.Originals.Clone();
                changed[i] = p with { Session = session };
            }
            return _classified = changed == null ? last : new(this, changed);
        }
    }

    private GalleryQueryCatalog(GalleryQueryCatalog catalog, GalleryIndexPhoto[] originals)
    {
        Originals = originals;
        BurstGroups = catalog.BurstGroups;
        Exports = catalog.Exports;
        CategoryNames = catalog.CategoryNames;
        Counts = catalog.Counts with { Current = Originals.Count(p => p.Session == "current") };
        ExportCounts = catalog.ExportCounts with { Current = Counts.Current };
    }

    private static GalleryQueryCounts WithOptions(GalleryQueryCounts totals, GalleryIndexPhoto[] source)
    {
        var swatches = source.SelectMany(p => p.Facts.Colors.Distinct()).CountBy(c => c)
            .OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key);
        var colors = new Dictionary<int, int>();
        foreach (var (color, _) in swatches)
        {
            if (colors.Keys.Any(c => Near(c, color))) continue;
            colors[color] = source.Count(p => p.Facts.Colors.Any(c => Near(c, color)));
            if (colors.Count == 8) break;
        }
        return totals with {
            Filters = source.Select(p => p.Facts.FilterId).OfType<string>().CountBy(id => id, StringComparer.OrdinalIgnoreCase).ToDictionary(StringComparer.OrdinalIgnoreCase),
            Clothing = source.SelectMany(p => p.Facts.ClothingIds.Distinct()).CountBy(id => id).ToDictionary(), Colors = colors };
    }

    internal static bool Near(int a, int b)
    {
        int Delta(int shift) => ((a >> shift) & 255) - ((b >> shift) & 255);
        var r = Delta(16);
        var g = Delta(8);
        var blue = Delta(0);
        return r * r + g * g + blue * blue <= 48 * 48;
    }
}

internal sealed class GalleryQuerySnapshot
{
    internal readonly Guid Version = Guid.NewGuid();
    internal readonly GalleryQuery Query;
    internal readonly ILookup<string, GalleryIndexPhoto> BurstGroups;
    private readonly GalleryIndexPhoto[] _matches;
    private readonly GalleryQueryCounts _counts;
    internal readonly GalleryIndexProgress Progress;
    internal readonly GalleryQueryCatalog Catalog;
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    internal IReadOnlyList<GalleryIndexPhoto> Ordered => _matches;
    internal Dictionary<string, string> CategoryNames { get; }
    internal readonly Dictionary<DateOnly, (int Count, bool Current)> DayTotals;

    internal GalleryQuerySnapshot(GalleryQuery query, GalleryQueryCatalog catalog, GalleryIndexProgress progress, CancellationToken token = default)
    {
        Query = query;
        BurstGroups = catalog.BurstGroups;
        Progress = progress;
        Catalog = catalog;
        CategoryNames = catalog.CategoryNames;
        _counts = query.Exports ? catalog.ExportCounts : catalog.Counts;
        var source = query.Exports ? catalog.Exports : catalog.Originals;
        var term = query.Text.Trim();
        bool Contains(string? s) => s?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
        var filtered = source.Where(p =>
        {
            token.ThrowIfCancellationRequested();
            return (!query.Favorites || p.Favorite) && (!query.Current || p.Session == "current") &&
            (!query.Bursts || p.Burst != null) &&
            (query.Category == null || Names.Equals(p.CategoryId, query.Category)) &&
            (query.Filter == null || Names.Equals(p.Facts.FilterId, query.Filter)) &&
            (query.Clothing == null || p.Facts.ClothingIds.Contains(query.Clothing.Value)) &&
            (query.Color == null || p.Facts.Colors.Any(c => GalleryQueryCatalog.Near(c, query.Color.Value))) &&
            (term.Length == 0 || Contains(p.RelativePath) || Contains(p.CategoryName) ||
             Contains(p.Facts.FilterId) || p.Facts.ClothingIds.Any(id => Contains(id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        });
        var ordered = query.Sort switch
        {
            GalleryQuerySort.Oldest => filtered.OrderBy(p => p.LocalTime.ToUniversalTime()),
            GalleryQuerySort.Name => filtered.OrderBy(p => p.FileName, Names),
            GalleryQuerySort.Category => filtered.OrderBy(p => p.CategoryName, Names),
            _ => filtered.OrderByDescending(p => p.LocalTime.ToUniversalTime())
        };
        _matches = ordered.ThenBy(p => p.RelativePath, StringComparer.Ordinal).ThenBy(p => p.Exported).ToArray();
        token.ThrowIfCancellationRequested();
        DayTotals = query.Sort is GalleryQuerySort.Newest or GalleryQuerySort.Oldest
            ? _matches.GroupBy(p => DateOnly.FromDateTime(p.LocalTime)).ToDictionary(g => g.Key, g => (g.Count(), g.Any(p => p.Session == "current")))
            : [];
    }

    internal GalleryQueryPage Page(int offset, int length = 80)
    {
        var photos = _matches.Skip(offset).Take(length).ToArray();
        int end = offset + photos.Length;
        GalleryQueryDay[] days = Query.Sort is GalleryQuerySort.Name or GalleryQuerySort.Category
            ? [new(null, photos)] : photos.GroupBy(p => DateOnly.FromDateTime(p.LocalTime))
            .Select(g => new GalleryQueryDay(g.Key, g.ToArray())).ToArray();
        return new(Version, photos, days, _matches.Length,
            end < _matches.Length ? new(Version, end) : null, _counts, Progress);
    }
}
