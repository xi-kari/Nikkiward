using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Nikkiward.Serialization;
using Nikkiward.Services;

namespace Nikkiward.Features.Gallery;

public sealed record GalleryFavoriteProtectionPreferences
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool IsEnabled { get; init; } = true;

    public required string ActiveRootPath { get; init; }

    public IReadOnlyList<string> KnownRootPaths { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<string> OverlappingRoots { get; init; } = [];

    [JsonIgnore]
    public bool IsPausedForOverlap => OverlappingRoots.Contains(ActiveRootPath, StringComparer.OrdinalIgnoreCase);
}

public sealed record GalleryProtectedFavorite(
    GalleryFavoriteProtectionEntry Entry,
    string ProtectedPath,
    bool IsUsingProtectedCopy);

public sealed record GalleryFavoriteProtectionResult(
    bool IsEnabled,
    GalleryFavoriteProtectionEntry? Entry,
    string? ProtectedPath,
    bool IsPausedForOverlap = false);

public sealed record GalleryFavoriteState(
    string ScopeId,
    IReadOnlySet<string> StarredPaths,
    IReadOnlyList<GalleryProtectedFavorite> ProtectedFavorites,
    bool IsPausedForOverlap);

public sealed record GalleryFavoriteProtectionOverview(
    GalleryFavoriteProtectionPreferences Preferences,
    GalleryFavoriteProtectionStatistics Statistics,
    IReadOnlyList<string> UnavailableRootPaths);

public sealed class GalleryFavoriteProtectionService
{
    public const string OverlapMessage = "收藏保护因目录重叠暂停。请在相册设置中选择与图库、游戏目录互不包含的保护目录。";

    private sealed class DirectoryOverlapException() :
        InvalidOperationException("保护目录与图库或游戏目录重叠。请选择与它们互不包含的目录。");
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonTypeInfo<GalleryFavoriteProtectionPreferences> PreferencesJsonTypeInfo =
        new NikkiwardJsonContext(SerializerOptions).GalleryFavoriteProtectionPreferences;

    private static readonly SemaphoreSlim PreferencesGate = new(1, 1);

    private readonly string _defaultRootPath;
    private readonly Func<IReadOnlyList<string>> _readOnlyRoots;
    private readonly Func<string, GalleryFavoriteProtectionStore> _createStore;
    private readonly GalleryAnnotationStore _annotations;
    private readonly SemaphoreSlim _directoryGate = new(1, 1);

    public GalleryFavoriteProtectionService(
        string? localApplicationDataPath = null,
        string? picturesPath = null,
        Func<IReadOnlyList<string>>? readOnlyRoots = null,
        Func<string, GalleryFavoriteProtectionStore>? createStore = null)
    {
        _readOnlyRoots = readOnlyRoots ?? (() => []);
        _createStore = createStore ?? (path => new GalleryFavoriteProtectionStore(path));
        _annotations = new GalleryAnnotationStore(localApplicationDataPath);
        var applicationRoot = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? Nikkiward.Services.ApplicationDataPaths.Root
            : Path.Combine(
                Path.GetFullPath(localApplicationDataPath),
                "Nikkiward");

        SettingsFilePath = Path.Combine(
            applicationRoot,
            "Gallery",
            "protection-settings.json");

        var picturesRoot = string.IsNullOrWhiteSpace(picturesPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            : picturesPath;
        _defaultRootPath = string.IsNullOrWhiteSpace(picturesRoot)
            ? Path.Combine(applicationRoot, "Gallery", "ProtectedFavorites")
            : Path.Combine(Path.GetFullPath(picturesRoot), "Nikkiward", "ProtectedFavorites");
    }

    public string SettingsFilePath { get; }

    private async Task<IDisposable> EnterDirectoryOperationAsync(CancellationToken cancellationToken)
    {
        await _directoryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new DirectoryOperation(_directoryGate);
    }

    private sealed class DirectoryOperation(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    internal async Task ChangeGalleryDirectoriesAsync(Func<Task> save, CancellationToken cancellationToken)
    {
        using var operation = await EnterDirectoryOperationAsync(cancellationToken);
        await save();
    }

    public async Task<GalleryFavoriteProtectionPreferences> GetPreferencesAsync(
        CancellationToken cancellationToken = default)
    {
        await PreferencesGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadPreferencesCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            PreferencesGate.Release();
        }
    }

    public async Task<GalleryFavoriteProtectionPreferences> SetEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        await PreferencesGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var preferences = await LoadPreferencesCoreAsync(cancellationToken).ConfigureAwait(false);
            var updated = preferences with { IsEnabled = isEnabled };
            await SavePreferencesCoreAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            PreferencesGate.Release();
        }
    }

    public async Task<GalleryFavoriteProtectionPreferences> SetActiveRootAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoot = NormalizeRootPath(rootPath);
        using var operation = await EnterDirectoryOperationAsync(cancellationToken).ConfigureAwait(false);

        await PreferencesGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfOverlapping(normalizedRoot);
            EnsureWritableRoot(normalizedRoot);
            var preferences = await LoadPreferencesCoreAsync(cancellationToken).ConfigureAwait(false);
            var knownRoots = preferences.KnownRootPaths
                .Concat(new[] { preferences.ActiveRootPath, normalizedRoot }.Where(HasStoreData))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var updated = preferences with
            {
                ActiveRootPath = normalizedRoot,
                KnownRootPaths = knownRoots,
            };
            await SavePreferencesCoreAsync(updated, cancellationToken).ConfigureAwait(false);
            return ApplyDirectoryPolicy(updated);
        }
        finally
        {
            PreferencesGate.Release();
        }
    }

    public async Task<GalleryFavoriteProtectionResult> ProtectAsync(
        string scopeId,
        string relativePath,
        string originalPath,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterDirectoryOperationAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await GetPreferencesForProtectionAsync(cancellationToken).ConfigureAwait(false);
        if (!preferences.IsEnabled || preferences.IsPausedForOverlap)
        {
            return new(preferences.IsEnabled, null, null, preferences.IsPausedForOverlap);
        }

        var store = CreateStore(preferences.ActiveRootPath, preferences);
        GalleryFavoriteProtectionEntry entry;
        try
        {
            entry = await store.ProtectAsync(scopeId, relativePath, originalPath, cancellationToken).ConfigureAwait(false);
        }
        catch (DirectoryOverlapException)
        {
            return new(preferences.IsEnabled, null, null, IsPausedForOverlap: true);
        }
        var protectedPath = await store.ResolveProtectedPathAsync(entry, verifyHash: false, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("The protected favorite was published but cannot be resolved.");

        return new GalleryFavoriteProtectionResult(true, entry, protectedPath);
    }

    public async Task<GalleryFavoriteState> LoadGalleryAsync(
        string? profileId, string? galleryRoot, CancellationToken cancellationToken = default)
    {
        galleryRoot = string.IsNullOrWhiteSpace(galleryRoot) ? null : GalleryAnnotationStore.NormalizeGalleryRoot(galleryRoot);
        var scope = GalleryAnnotationStore.CreateScopeId(profileId, galleryRoot);
        GalleryFavoriteProtectionPreferences preferences;
        IReadOnlySet<string> starred;
        (GalleryFavoriteProtectionStore Store, IReadOnlyList<GalleryFavoriteProtectionEntry> Entries)[] snapshots;

        using (await EnterDirectoryOperationAsync(cancellationToken).ConfigureAwait(false))
        {
            preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
            var legacyScope = galleryRoot is not null && !string.IsNullOrWhiteSpace(profileId) ? $"profile:{profileId.Trim()}" : null;
            using var indexes = await OpenIndexesAsync(preferences, writable: legacyScope is not null,
                waitForWriter: false, cancellationToken).ConfigureAwait(false);
            var proven = legacyScope is not null && indexes.Complete && !preferences.IsPausedForOverlap
                ? FindLegacyPaths(indexes.Stores, galleryRoot!, scope, legacyScope) : [];
            if (proven.Count == 0)
            {
                starred = await _annotations.LoadStarredAsync(scope, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Publish the stars first. A retry can still identify every old manifest entry by its original path.
                starred = await _annotations.MigrateAndLoadStarredAsync(scope, legacyScope!, proven, cancellationToken)
                    .ConfigureAwait(false);
                foreach (var index in indexes.Stores)
                {
                    await index.MigrateAsync(legacyScope!, scope, proven, cancellationToken).ConfigureAwait(false);
                }
            }

            snapshots = indexes.Stores.Select(index => (index.Store, index.Entries)).ToArray();
        }

        var favorites = new List<GalleryProtectedFavorite>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (store, entries) in snapshots)
        {
            var verifiedFallbacks = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.ScopeId != scope || !starred.Contains(entry.RelativePath) || seen.Contains(entry.RelativePath))
                {
                    continue;
                }

                var fallback = !File.Exists(entry.OriginalPath);
                if (!verifiedFallbacks.TryGetValue(entry.Sha256, out var path) || !fallback)
                {
                    path = await store.ResolveProtectedPathAsync(entry, verifyHash: fallback, cancellationToken).ConfigureAwait(false);
                    if (fallback)
                    {
                        verifiedFallbacks[entry.Sha256] = path;
                    }
                }
                if (path is not null)
                {
                    seen.Add(entry.RelativePath);
                    favorites.Add(new(entry, path, fallback));
                }
            }
        }
        return new(scope, starred, favorites, preferences.IsPausedForOverlap);
    }

    private static HashSet<string> FindLegacyPaths(
        IReadOnlyList<GalleryFavoriteProtectionStore.ManifestIndex> indexes, string galleryRoot, string scope, string legacyScope)
    {
        var proven = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var legacy = indexes.SelectMany(index => index.Entries.Where(entry => entry.ScopeId == legacyScope)
            .Select(entry => (index, entry)));
        var conflicts = indexes.ToDictionary(index => index, index => index.Entries.Where(entry => entry.ScopeId == scope)
            .Select(entry => entry.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase));
        foreach (var group in legacy.GroupBy(item => item.entry.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var expected = Path.GetFullPath(Path.Combine(galleryRoot, group.Key));
            if (group.All(item => item.index.CanWrite && !conflicts[item.index].Contains(group.Key) &&
                string.Equals(Path.GetFullPath(item.entry.OriginalPath), expected, StringComparison.OrdinalIgnoreCase)))
            {
                proven.Add(group.Key);
            }
        }
        return proven;
    }

    public async Task<bool> TryUnstarAsync(
        string scopeId, string relativePath, bool confirmed = false, CancellationToken cancellationToken = default)
    {
        using var operation = await EnterDirectoryOperationAsync(cancellationToken).ConfigureAwait(false);
        var path = GalleryAnnotationStore.NormalizeRelativePath(relativePath);
        var preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        using var indexes = await OpenIndexesAsync(preferences, writable: true, waitForWriter: true,
            cancellationToken).ConfigureAwait(false);
        var matches = indexes.Stores.Where(index => !preferences.IsPausedForOverlap &&
                !preferences.OverlappingRoots.Contains(index.Store.RootPath, StringComparer.OrdinalIgnoreCase))
            .SelectMany(index => index.Entries.Where(entry => entry.ScopeId == scopeId &&
                string.Equals(entry.RelativePath, path, StringComparison.OrdinalIgnoreCase))
                .Select(entry => (index, entry))).ToArray();
        var originals = new List<IDisposable>();
        try
        {
            foreach (var (index, entry) in matches)
            {
                if (!index.CanWrite)
                {
                    throw new IOException("保护目录当前只读，已保留收藏。");
                }
                if (confirmed || index.Entries.Any(other => other != entry &&
                    string.Equals(other.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase)) ||
                    !File.Exists(Path.Combine(index.Store.RootPath, entry.ObjectPath)))
                {
                    continue;
                }

                try
                {
                    // Keep the original from being deleted between the decision and the release.
                    originals.Add(File.OpenHandle(entry.OriginalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    return false;
                }
            }

            var previous = new Stack<(GalleryFavoriteProtectionStore.ManifestIndex Index,
                IReadOnlyList<GalleryFavoriteProtectionEntry> Entries)>();
            try
            {
                foreach (var (index, entry) in matches)
                {
                    var entries = index.Entries;
                    await index.SaveAsync(entries.Where(item => item != entry).ToArray(), cancellationToken).ConfigureAwait(false);
                    previous.Push((index, entries));
                }

                await _annotations.SetStarredAsync(scopeId, relativePath, false, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                foreach (var (index, entries) in previous)
                {
                    await index.SaveAsync(entries, CancellationToken.None).ConfigureAwait(false);
                }
                throw;
            }

            // Only reclaim bytes after every manifest and the annotation file have committed.
            foreach (var (index, entry) in matches)
            {
                index.DeleteReleasedObject(entry);
            }
            return true;
        }
        finally
        {
            foreach (var original in originals)
            {
                original.Dispose();
            }
        }
    }

    private async Task<ManifestIndexes> OpenIndexesAsync(
        GalleryFavoriteProtectionPreferences preferences, bool writable, bool waitForWriter, CancellationToken token)
    {
        var indexes = new ManifestIndexes();
        var roots = EnumerateRoots(preferences).ToArray();
        try
        {
            // Every operation taking multiple store locks uses the same order, including after a root switch.
            foreach (var root in roots.OrderBy(ResolveDirectoryLinks, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                var canWrite = writable && !preferences.IsPausedForOverlap &&
                    !preferences.OverlappingRoots.Contains(root, StringComparer.OrdinalIgnoreCase);
                try
                {
                    var index = await CreateStore(root, preferences).OpenIndexAsync(canWrite, token, waitForWriter).ConfigureAwait(false);
                    indexes.Stores.Add(index);
                    indexes.Complete &= index.HasManifest ||
                        !preferences.KnownRootPaths.Contains(root, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex) when (IsStoreError(ex))
                {
                    indexes.Complete = false;
                }
            }

            indexes.Stores.Sort((left, right) => Array.IndexOf(roots, left.Store.RootPath)
                .CompareTo(Array.IndexOf(roots, right.Store.RootPath)));
            return indexes;
        }
        catch
        {
            indexes.Dispose();
            throw;
        }
    }

    private sealed class ManifestIndexes : IDisposable
    {
        public List<GalleryFavoriteProtectionStore.ManifestIndex> Stores { get; } = [];
        public bool Complete { get; set; } = true;

        public void Dispose()
        {
            foreach (var index in Stores)
            {
                index.Dispose();
            }
        }
    }

    private static bool IsStoreError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or ArgumentException or NotSupportedException or InvalidDataException or JsonException;

    public async Task<GalleryFavoriteProtectionOverview> GetOverviewAsync(
        bool verify,
        CancellationToken cancellationToken = default)
    {
        using var operation = verify ? await EnterDirectoryOperationAsync(cancellationToken).ConfigureAwait(false) : null;
        var preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<(string RootPath, GalleryFavoriteProtectionEntry Entry)>();
        var unavailableRootPaths = new List<string>();
        foreach (var rootPath in EnumerateRoots(preferences))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var store = CreateStore(rootPath, preferences);
                var rootEntries = verify && !preferences.IsPausedForOverlap &&
                    !preferences.OverlappingRoots.Contains(rootPath, StringComparer.OrdinalIgnoreCase)
                    ? await store.VerifyAsync(cancellationToken).ConfigureAwait(false)
                    : store.GetEntries();
                entries.AddRange(rootEntries.Select(entry => (rootPath, entry)));
            }
            catch (Exception ex) when (IsStoreError(ex))
            {
                unavailableRootPaths.Add(rootPath);
            }
        }

        var uniqueObjects = entries
            .Where(item => IsProtectedObjectUsable(item.Entry.Status))
            .GroupBy(
                item => $"{item.RootPath}\0{item.Entry.Sha256}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First().Entry)
            .ToArray();
        var statistics = new GalleryFavoriteProtectionStatistics
        {
            EntryCount = entries.Count,
            HealthyEntryCount = entries.Count(item =>
                item.Entry.Status == GalleryFavoriteProtectionStatus.Protected),
            OriginalMissingCount = entries.Count(item =>
                item.Entry.Status == GalleryFavoriteProtectionStatus.OriginalMissing),
            OriginalChangedCount = entries.Count(item =>
                item.Entry.Status == GalleryFavoriteProtectionStatus.OriginalChanged),
            ObjectMissingCount = entries.Count(item =>
                item.Entry.Status == GalleryFavoriteProtectionStatus.ObjectMissing),
            ObjectCorruptCount = entries.Count(item =>
                item.Entry.Status == GalleryFavoriteProtectionStatus.ObjectCorrupt),
            UniqueObjectCount = uniqueObjects.Length,
            ProtectedBytes = uniqueObjects.Sum(entry => entry.OriginalLength),
        };
        return new GalleryFavoriteProtectionOverview(
            preferences,
            statistics,
            unavailableRootPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public Task<GalleryFavoriteProtectionOverview> VerifyAsync(
        CancellationToken cancellationToken = default) =>
        GetOverviewAsync(verify: true, cancellationToken);

    public async Task<GalleryFavoriteProtectionCleanupResult> CleanUnstarredAsync(
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        using var operation = await EnterDirectoryOperationAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        var result = new GalleryFavoriteProtectionCleanupResult();
        if (preferences.IsPausedForOverlap)
        {
            return result;
        }

        foreach (var rootPath in EnumerateRoots(preferences).Except(preferences.OverlappingRoots, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var store = CreateStore(rootPath, preferences);
            var current = await store.CleanUnstarredAsync(
                    scopeId,
                    _annotations,
                    cancellationToken)
                .ConfigureAwait(false);
            result = result with
            {
                RemovedEntryCount = result.RemovedEntryCount + current.RemovedEntryCount,
                RemovedObjectCount = result.RemovedObjectCount + current.RemovedObjectCount,
                ReclaimedBytes = result.ReclaimedBytes + current.ReclaimedBytes,
            };
        }

        return result;
    }

    private async Task<GalleryFavoriteProtectionPreferences> LoadPreferencesCoreAsync(
        CancellationToken cancellationToken)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ApplyDirectoryPolicy(CreateDefaultPreferences());
        }

        await using var input = stream;
        var preferences = await JsonSerializer.DeserializeAsync(
            input,
            PreferencesJsonTypeInfo,
            cancellationToken).ConfigureAwait(false);
        return ApplyDirectoryPolicy(ValidatePreferences(preferences));
    }

    private async Task<GalleryFavoriteProtectionPreferences> GetPreferencesForProtectionAsync(CancellationToken token)
    {
        await PreferencesGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var preferences = await LoadPreferencesCoreAsync(token).ConfigureAwait(false);
            if (preferences.IsEnabled && !preferences.IsPausedForOverlap &&
                !preferences.KnownRootPaths.Contains(preferences.ActiveRootPath, StringComparer.OrdinalIgnoreCase))
            {
                // Record a used root before publishing. If its manifest later disappears, migration must wait for it.
                preferences = preferences with { KnownRootPaths = [.. preferences.KnownRootPaths, preferences.ActiveRootPath] };
                await SavePreferencesCoreAsync(preferences, token).ConfigureAwait(false);
            }
            return preferences;
        }
        finally
        {
            PreferencesGate.Release();
        }
    }

    private static bool HasStoreData(string root)
    {
        var store = new GalleryFavoriteProtectionStore(root);
        return File.Exists(store.ManifestPath) || Directory.Exists(store.ObjectsPath);
    }

    private async Task SavePreferencesCoreAsync(
        GalleryFavoriteProtectionPreferences preferences,
        CancellationToken cancellationToken)
    {
        preferences = ValidatePreferences(preferences);
        var directory = Path.GetDirectoryName(SettingsFilePath)
            ?? throw new InvalidOperationException("The gallery settings directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{SettingsFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    preferences,
                    PreferencesJsonTypeInfo,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, SettingsFilePath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private GalleryFavoriteProtectionPreferences CreateDefaultPreferences() =>
        new()
        {
            ActiveRootPath = _defaultRootPath,
            KnownRootPaths = File.Exists(_annotations.AnnotationFilePath) || HasStoreData(_defaultRootPath)
                ? [_defaultRootPath] : [],
        };

    private static GalleryFavoriteProtectionPreferences ValidatePreferences(
        GalleryFavoriteProtectionPreferences? preferences)
    {
        if (preferences is null ||
            preferences.SchemaVersion != GalleryFavoriteProtectionPreferences.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(preferences.ActiveRootPath) ||
            !Path.IsPathFullyQualified(preferences.ActiveRootPath) ||
            preferences.KnownRootPaths is null)
        {
            throw new InvalidDataException("The favorite protection preferences are invalid.");
        }

        var activeRoot = NormalizeRootPath(preferences.ActiveRootPath);
        var knownRoots = preferences.KnownRootPaths
            .Select(NormalizeRootPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return preferences with
        {
            ActiveRootPath = activeRoot,
            KnownRootPaths = knownRoots,
        };
    }

    private static IEnumerable<string> EnumerateRoots(
        GalleryFavoriteProtectionPreferences preferences) =>
        preferences.KnownRootPaths
            .Prepend(preferences.ActiveRootPath)
            .DistinctBy(ResolveDirectoryLinks, StringComparer.OrdinalIgnoreCase);

    private static bool IsProtectedObjectUsable(GalleryFavoriteProtectionStatus status) =>
        status is GalleryFavoriteProtectionStatus.Protected
            or GalleryFavoriteProtectionStatus.OriginalMissing
            or GalleryFavoriteProtectionStatus.OriginalChanged;

    private static string NormalizeRootPath(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(rootPath.Trim().Trim('"')));
    }

    private GalleryFavoriteProtectionPreferences ApplyDirectoryPolicy(GalleryFavoriteProtectionPreferences preferences)
    {
        var readOnly = GetReadOnlyDirectories();
        return preferences with
        {
            OverlappingRoots = EnumerateRoots(preferences)
                .Where(root => Overlaps(root, readOnly)).ToArray(),
        };
    }

    private void ThrowIfOverlapping(string root)
    {
        if (Overlaps(root, GetReadOnlyDirectories()))
        {
            throw new DirectoryOverlapException();
        }
    }

    private GalleryFavoriteProtectionStore CreateStore(string root, GalleryFavoriteProtectionPreferences preferences)
    {
        var store = _createStore(root);
        store.CheckWriteAccess = () =>
        {
            var readOnly = GetReadOnlyDirectories();
            if (Overlaps(root, readOnly) || (root != preferences.ActiveRootPath && Overlaps(preferences.ActiveRootPath, readOnly)))
            {
                throw new DirectoryOverlapException();
            }
        };
        return store;
    }

    private (string Path, string Resolved)[] GetReadOnlyDirectories() =>
        _readOnlyRoots().Select(GalleryAnnotationStore.NormalizeGalleryRoot)
            .Select(path => (path, ResolveDirectoryLinks(path))).ToArray();

    private static bool Overlaps(string root, (string Path, string Resolved)[] readOnly)
    {
        var resolved = ResolveDirectoryLinks(root);
        return readOnly.Any(directory => VariantPathPolicy.PathsOverlap(root, directory.Path) ||
            VariantPathPolicy.PathsOverlap(resolved, directory.Resolved));
    }

    internal static string ResolveDirectoryLinks(string path)
    {
        var fullPath = NormalizeRootPath(path);
        var resolved = Path.GetPathRoot(fullPath)!;
        foreach (var part in fullPath[resolved.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(resolved, part));
            resolved = directory.FullName;
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0 &&
                directory.ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                resolved = target.FullName;
            }
        }
        return resolved;
    }

    public async Task<string> GetRootToOpenAsync(CancellationToken cancellationToken = default)
    {
        using var operation = await EnterDirectoryOperationAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfOverlapping(preferences.ActiveRootPath);
        Directory.CreateDirectory(preferences.ActiveRootPath);
        return preferences.ActiveRootPath;
    }

    private static void EnsureWritableRoot(string rootPath)
    {
        Directory.CreateDirectory(rootPath);
        if ((File.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The favorite protection root cannot be a reparse point.");
        }

        var probePath = Path.Combine(rootPath, $".nikkiward-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            TryDelete(probePath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
