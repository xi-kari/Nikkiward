namespace Nikkiward.Features.Gallery;

internal sealed record GalleryIndexFacts(int? Width, int? Height, bool HasParameters, string? FilterId,
    long[] ClothingIds, double? FocalLength, double? Aperture, int[] Colors);

internal sealed record GalleryIndexPhoto(string Id, string FileName, string RelativePath, string CategoryId,
    string CategoryName, DateTime LocalTime, long Size, GalleryIndexFacts Facts, bool Favorite,
    bool Exported, string? Session, string? Burst = null);

internal enum GalleryQuerySort { Newest, Oldest, Name, Category }

internal sealed record GalleryQuery(string Text = "", string? Category = null, bool Favorites = false,
    bool Current = false, bool Bursts = false, bool Exports = false, string? Filter = null,
    long? Clothing = null, int? Color = null, GalleryQuerySort Sort = GalleryQuerySort.Newest);

internal sealed record GalleryIndexProgress(int Indexed, int Total, int Skipped, bool IsIndexing);
internal sealed record GalleryQueryCursor(Guid Version, int Offset);
internal sealed record GalleryQueryCounts(int All, int Favorites, int Current, int Bursts, int Exports,
    Dictionary<string, int> Categories, Dictionary<string, int> Filters, Dictionary<long, int> Clothing,
    Dictionary<int, int> Colors);
internal sealed record GalleryQueryDay(DateOnly Date, GalleryIndexPhoto[] Photos);
internal sealed record GalleryQueryPage(Guid Version, GalleryIndexPhoto[] Photos, GalleryQueryDay[] Days,
    int Matches, GalleryQueryCursor? Next, GalleryQueryCounts Counts, GalleryIndexProgress Progress);

internal sealed class GalleryQuerySnapshot
{
    internal readonly Guid Version = Guid.NewGuid();
    internal readonly GalleryQuery Query;
    private readonly GalleryIndexPhoto[] _matches;
    private readonly GalleryQueryCounts _counts;
    private readonly GalleryIndexProgress _progress;
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    internal GalleryQuerySnapshot(GalleryQuery query, GalleryIndexPhoto[] photos, GalleryIndexProgress progress)
    {
        Query = query;
        _progress = progress;
        var swatches = photos.SelectMany(p => p.Facts.Colors.Distinct()).CountBy(c => c)
            .OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key);
        var colors = new Dictionary<int, int>();
        foreach (var (color, _) in swatches)
        {
            if (colors.Keys.Any(c => Near(c, color))) continue;
            colors[color] = photos.Count(p => p.Facts.Colors.Any(c => Near(c, color)));
            if (colors.Count == 8) break;
        }
        _counts = new(photos.Length, photos.Count(p => p.Favorite), photos.Count(p => p.Session == "current"),
            photos.Count(p => p.Burst != null), photos.Count(p => p.Exported),
            photos.CountBy(p => p.CategoryId, Names).ToDictionary(Names),
            photos.Select(p => p.Facts.FilterId).OfType<string>().CountBy(id => id, Names).ToDictionary(Names),
            photos.SelectMany(p => p.Facts.ClothingIds.Distinct()).CountBy(id => id).ToDictionary(), colors);

        var term = query.Text.Trim();
        bool Contains(string? s) => s?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
        var filtered = photos.Where(p =>
            (!query.Favorites || p.Favorite) && (!query.Current || p.Session == "current") &&
            (!query.Bursts || p.Burst != null) && (!query.Exports || p.Exported) &&
            (query.Category == null || Names.Equals(p.CategoryId, query.Category)) &&
            (query.Filter == null || Names.Equals(p.Facts.FilterId, query.Filter)) &&
            (query.Clothing == null || p.Facts.ClothingIds.Contains(query.Clothing.Value)) &&
            (query.Color == null || p.Facts.Colors.Any(c => Near(c, query.Color.Value))) &&
            (term.Length == 0 || Contains(p.RelativePath) || Contains(p.CategoryName) ||
             Contains(p.Facts.FilterId) || p.Facts.ClothingIds.Any(id => Contains(id.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        var ordered = query.Sort switch
        {
            GalleryQuerySort.Oldest => filtered.OrderBy(p => p.LocalTime.ToUniversalTime()),
            GalleryQuerySort.Name => filtered.OrderBy(p => p.FileName, Names),
            GalleryQuerySort.Category => filtered.OrderBy(p => p.CategoryName, Names),
            _ => filtered.OrderByDescending(p => p.LocalTime.ToUniversalTime())
        };
        _matches = ordered.ThenBy(p => p.RelativePath, StringComparer.Ordinal).ThenBy(p => p.Exported).ToArray();
    }

    internal GalleryQueryPage Page(int offset)
    {
        var photos = _matches.Skip(offset).Take(80).ToArray();
        int end = offset + photos.Length;
        return new(Version, photos, photos.GroupBy(p => DateOnly.FromDateTime(p.LocalTime))
            .Select(g => new GalleryQueryDay(g.Key, g.ToArray())).ToArray(), _matches.Length,
            end < _matches.Length ? new(Version, end) : null, _counts, _progress);
    }

    private static bool Near(int a, int b)
    {
        int Delta(int shift) => ((a >> shift) & 255) - ((b >> shift) & 255);
        var r = Delta(16);
        var g = Delta(8);
        var blue = Delta(0);
        return r * r + g * g + blue * blue <= 48 * 48;
    }
}
