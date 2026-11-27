using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nikkiward.Services;

namespace Nikkiward.Features.Gallery;

internal sealed partial class GalleryIndex
{
    private sealed record CacheEntry(long Size, long Modified, GalleryIndexFacts Facts);
    private sealed record Source(FileInfo File, string Relative, bool Exported, DateTime Modified);
    private sealed record Row(string Path, GalleryIndexPhoto Photo, string LogicalPath);
    private readonly string? _root;
    private readonly string _exports;
    private readonly string _cache;
    private readonly Func<string, string, CancellationToken, Task<GalleryIndexFacts>> _read;
    private readonly Func<string, (string Id, string DisplayName)> _category;
    private readonly SemaphoreSlim _refresh = new(1);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Row> _rows = [];
    private Dictionary<string, CacheEntry>? _memoryCache;
    private readonly List<string> _issues = [];
    private GalleryIndexProgress _progress = new(0, 0, 0, false);
    private GalleryQuerySnapshot? _snapshot;

    internal GalleryIndex(string? root, string exports, string cacheFolder,
        Func<string, string, CancellationToken, Task<GalleryIndexFacts>> read,
        Func<string, (string Id, string DisplayName)> category)
    {
        _root = root is null ? null : GalleryAnnotationStore.NormalizeGalleryRoot(root);
        _exports = GalleryAnnotationStore.NormalizeGalleryRoot(exports);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{_root}\0{_exports}".ToUpperInvariant()));
        _cache = Path.Combine(cacheFolder, Convert.ToHexString(hash)[..32] + ".v2.json");
        _read = read;
        _category = category;
    }

    internal GalleryIndexProgress Progress { get { lock (_gate) return _progress; } }
    internal string[] Issues { get { lock (_gate) return [.. _issues]; } }
    internal string? ResolvePath(string id)
    {
        lock (_gate) return _rows.GetValueOrDefault(id)?.Path;
    }

    private Lazy<GalleryIndexPhoto[]>? _photos;
    private readonly Dictionary<bool, (IEnumerable<GalleryIndexPhoto> Defaults, Lazy<GalleryQueryCatalog> Catalog)> _catalogs = [];
    private readonly Lock _cursorGate = new();

    private void InvalidateCatalog()
    {
        _photos = null;
        _catalogs.Clear();
    }

    private Lazy<GalleryIndexPhoto[]> Photos()
    {
        if (_photos != null) return _photos;
        var rows = _rows.Values.Select(r => r.Photo).ToArray();
        return _photos = new(() => WithBursts(rows));
    }

    internal GalleryQuerySnapshot Snapshot(GalleryQuery query, IEnumerable<GalleryIndexPhoto> defaults, bool favoritesRoute,
        CancellationToken token = default, (PlaySession? Current, PlaySession? Previous)? sessions = null,
        GalleryQuerySnapshot? previousSnapshot = null)
    {
        Lazy<GalleryQueryCatalog> catalog;
        GalleryIndexProgress progress;
        lock (_gate)
        {
            if (!_catalogs.TryGetValue(favoritesRoute, out var entry) || !ReferenceEquals(entry.Defaults, defaults))
            {
                var rows = Photos();
                entry = (defaults, new(() => new GalleryQueryCatalog(rows.Value, defaults, favoritesRoute)));
                _catalogs[favoritesRoute] = entry;
            }
            catalog = entry.Catalog;
            progress = _progress;
        }
        var data = catalog.Value;
        token.ThrowIfCancellationRequested();
        if (sessions.HasValue) data = data.ForSessions(sessions.Value);
        if (previousSnapshot?.Catalog == data && previousSnapshot.Query == query && previousSnapshot.Progress == progress)
            return previousSnapshot;
        return new(query, data, progress, token);
    }

    internal GalleryQueryPage Query(GalleryQuery query, GalleryQueryCursor? cursor = null)
    {
        lock (_cursorGate)
        {
            if (cursor is null)
                _snapshot = Snapshot(query, Array.Empty<GalleryIndexPhoto>(), false);
            else if (_snapshot?.Version != cursor.Version || _snapshot.Query != query || cursor.Offset < 0)
                throw new ArgumentException("相册查询已失效，请从第一页重新查询。", nameof(cursor));
            return _snapshot!.Page(cursor?.Offset ?? 0);
        }
    }

    private static GalleryIndexPhoto[] WithBursts(GalleryIndexPhoto[] rows)
    {
        var photos = new List<GalleryIndexPhoto>(rows.Length);
        var byEligibility = rows.ToLookup(p => !p.Exported && GalleryFiles.IsLocalCaptureCategory(p.CategoryId));
        photos.AddRange(byEligibility[false]);
        foreach (var directory in byEligibility[true].GroupBy(p => Path.GetDirectoryName(p.RelativePath), StringComparer.OrdinalIgnoreCase))
        {
            int start = photos.Count;
            foreach (var photo in directory.OrderBy(p => p.LocalTime.ToUniversalTime()).ThenBy(p => p.RelativePath, StringComparer.Ordinal))
            {
                if (photos.Count > start && photo.LocalTime.ToUniversalTime() - photos[^1].LocalTime.ToUniversalTime() <= TimeSpan.FromSeconds(2))
                {
                    var previous = photos[^1];
                    var group = previous.Burst ?? previous.Id;
                    photos[^1] = previous with { Burst = group };
                    photos.Add(photo with { Burst = group });
                }
                else photos.Add(photo);
            }
        }
        return photos.ToArray();
    }

    internal Task RefreshAsync(GalleryFavoriteState favorites, PlaySession? current, PlaySession? previous,
        IReadOnlySet<string>? exportStars = null, CancellationToken token = default, GalleryFavoriteState? exportState = null) =>
        Task.Run(async () =>
        {
            await _refresh.WaitAsync(token);
try { await RefreshCoreAsync(favorites, current, previous, exportState?.StarredPaths ?? exportStars, token, exportState); }
            finally { _refresh.Release(); }
        }, token);

    internal static bool CanWriteCache(string folder, string? gallery, string exports)
    {
        var actual = GalleryFavoriteProtectionService.ResolveDirectoryLinks(folder);
        return new[] { gallery, exports }.OfType<string>().All(root =>
            !VariantPathPolicy.PathsOverlap(actual, GalleryFavoriteProtectionService.ResolveDirectoryLinks(root)));
    }

    private async Task RefreshCoreAsync(GalleryFavoriteState favorites, PlaySession? current, PlaySession? previous,
        IReadOnlySet<string>? exportStars, CancellationToken token, GalleryFavoriteState? exportState)
    {
        KeyValuePair<string, Row>[] existing;
        lock (_gate)
        {
            existing = _rows.ToArray();
            _issues.Clear();
            _progress = new(_rows.Count, _rows.Count, 0, true);
        }
        try
        {
            var ids = existing.ToDictionary(pair => pair.Value.LogicalPath, pair => pair.Key, StringComparer.OrdinalIgnoreCase);
            var sources = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);
            void Collect(string? root, bool exported)
            {
                if (root is null || !Directory.Exists(root)) return;
                try
                {
                    foreach (var file in GalleryFiles.Enumerate(root, token))
                        sources[file.FullName] = new(file, Path.GetRelativePath(root, file.FullName), exported, file.LastWriteTimeUtc);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Issue(root, ex); }
            }
            Collect(_root, false);
            Collect(_exports, true);
            foreach (var fallback in favorites.ProtectedFavorites.Concat(exportState?.ProtectedFavorites ?? []).Where(p => p.IsUsingProtectedCopy))
            {
                var entry = fallback.Entry;
                // Two missing originals can refer to the same protected object.
                sources.TryAdd(entry.OriginalPath, new(new(fallback.ProtectedPath), entry.RelativePath,
                    entry.ScopeId == exportState?.ScopeId, entry.OriginalLastWriteTimeUtc.UtcDateTime));
            }
            var removed = new List<string>();
            foreach (var (id, row) in existing)
            {
                if (!sources.TryGetValue(row.LogicalPath, out var source) || row.Path != source.File.FullName ||
                    _memoryCache?.GetValueOrDefault(CacheKey(row.Path, row.LogicalPath)) is not { } prior ||
                    !source.File.Exists || prior.Size != source.File.Length || prior.Modified != source.File.LastWriteTimeUtc.Ticks)
                    removed.Add(id);
            }
            lock (_gate)
            {
                foreach (var id in removed) _rows.Remove(id);
                if (removed.Count != 0) InvalidateCatalog();
                _progress = _progress with { Total = sources.Count, Indexed = _rows.Count };
            }
            var persist = CanWriteCache(Path.GetDirectoryName(_cache)!, _root, _exports);
            var cache = _memoryCache ?? new(StringComparer.OrdinalIgnoreCase);
            if (_memoryCache == null && persist && File.Exists(_cache))
            {
                try
                {
                    using var input = File.OpenRead(_cache);
                    var saved = await JsonSerializer.DeserializeAsync<Dictionary<string, CacheEntry>>(input, cancellationToken: token);
                    if (saved != null) cache = new(saved, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Issue(_cache, ex); }
            }
            var updated = new System.Collections.Concurrent.ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
            await Parallel.ForEachAsync(sources, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = token }, async (pair, ct) =>
            {
                var (logicalPath, source) = pair;
                var file = source.File;
                try
                {
                    using var hold = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var size = hold.Length;
                    var modified = File.GetLastWriteTimeUtc(file.FullName).Ticks;
                    var key = CacheKey(file.FullName, logicalPath);
                    if (!cache.TryGetValue(key, out var cached) || cached?.Size != size || cached.Modified != modified ||
                        cached.Facts is not { Colors: not null, ClothingIds: not null })
                        cached = new(size, modified, await _read(file.FullName, logicalPath, ct));
                    ct.ThrowIfCancellationRequested();
                    updated[key] = cached;
                    var name = Path.GetFileName(source.Relative);
                    var mtime = string.Equals(logicalPath, file.FullName, StringComparison.OrdinalIgnoreCase)
                        ? new DateTime(modified, DateTimeKind.Utc) : source.Modified;
                    var utc = GalleryTimestamp.Resolve(name, mtime);
                    var category = source.Exported ? ("Exports", "Nikkiward 导出的副本") : _category(source.Relative);
                    var stars = source.Exported ? exportStars : favorites.StarredPaths;
                    var session = !source.Exported && current != null && PlayTimeLedger.ContainsPhoto(current, utc) ? "current" :
                        !source.Exported && previous != null && PlayTimeLedger.ContainsPhoto(previous, utc) ? "previous" : null;
                    var id = ids.GetValueOrDefault(logicalPath) ?? Guid.NewGuid().ToString("N");
                    var photo = new GalleryIndexPhoto(id, name, source.Relative, category.Item1, category.Item2,
                        utc.ToLocalTime(), size, cached.Facts, stars?.Contains(GalleryAnnotationStore.NormalizeRelativePath(source.Relative)) == true,
                        source.Exported, session, ProtectedCopy: !file.FullName.Equals(logicalPath, StringComparison.OrdinalIgnoreCase));
                    lock (_gate)
                    {
                        _rows[id] = new(file.FullName, photo, logicalPath);
                        InvalidateCatalog();
                        _progress = _progress with { Indexed = _rows.Count };
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Issue(logicalPath, ex);
                    lock (_gate)
                    {
                        if (ids.TryGetValue(logicalPath, out var id) && _rows.Remove(id)) InvalidateCatalog();
                        _progress = _progress with { Indexed = _rows.Count, Skipped = _progress.Skipped + 1 };
                    }
                }
            });
            if (persist && (cache.Count != updated.Count || updated.Any(p => !ReferenceEquals(cache.GetValueOrDefault(p.Key), p.Value))))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cache)!);
                var temp = _cache + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await using (var output = File.Create(temp))
                        await JsonSerializer.SerializeAsync(output, updated, cancellationToken: token);
                    File.Move(temp, _cache, true);
                }
                finally { File.Delete(temp); }
            }
            _memoryCache = new(updated, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Issue(_cache, ex); }
        finally { lock (_gate) _progress = _progress with { IsIndexing = false }; }
    }

    private void Issue(string path, Exception error)
    {
        lock (_gate) _issues.Add($"{path}: {error.Message}");
    }

    private static string CacheKey(string physical, string logical) =>
        physical.Equals(logical, StringComparison.OrdinalIgnoreCase) ? physical : physical + "|" + logical;
}
