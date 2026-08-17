using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Nikkiward.Serialization;

namespace Nikkiward.Features.Gallery;

public sealed record GalleryFavoriteProtectionPreferences
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public bool IsEnabled { get; init; } = true;

    public required string ActiveRootPath { get; init; }

    public IReadOnlyList<string> KnownRootPaths { get; init; } = [];
}

public sealed record GalleryProtectedFavorite(
    GalleryFavoriteProtectionEntry Entry,
    string ProtectedPath,
    bool IsUsingProtectedCopy);

public sealed record GalleryFavoriteProtectionResult(
    bool IsEnabled,
    GalleryFavoriteProtectionEntry? Entry,
    string? ProtectedPath);

public sealed record GalleryFavoriteProtectionOverview(
    GalleryFavoriteProtectionPreferences Preferences,
    GalleryFavoriteProtectionStatistics Statistics);

public sealed class GalleryFavoriteProtectionService
{
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

    public GalleryFavoriteProtectionService(
        string? localApplicationDataPath = null,
        string? picturesPath = null)
    {
        var localRoot = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localApplicationDataPath;
        if (string.IsNullOrWhiteSpace(localRoot))
        {
            throw new InvalidOperationException("The LocalApplicationData directory is unavailable.");
        }

        SettingsFilePath = Path.Combine(
            Path.GetFullPath(localRoot),
            "Nikkiward",
            "Gallery",
            "protection-settings.json");

        var picturesRoot = string.IsNullOrWhiteSpace(picturesPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            : picturesPath;
        _defaultRootPath = string.IsNullOrWhiteSpace(picturesRoot)
            ? Path.Combine(Path.GetFullPath(localRoot), "Nikkiward", "Gallery", "ProtectedFavorites")
            : Path.Combine(Path.GetFullPath(picturesRoot), "Nikkiward", "ProtectedFavorites");
    }

    public string SettingsFilePath { get; }

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
        EnsureWritableRoot(normalizedRoot);

        await PreferencesGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var preferences = await LoadPreferencesCoreAsync(cancellationToken).ConfigureAwait(false);
            var knownRoots = preferences.KnownRootPaths
                .Append(preferences.ActiveRootPath)
                .Append(normalizedRoot)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var updated = preferences with
            {
                ActiveRootPath = normalizedRoot,
                KnownRootPaths = knownRoots,
            };
            await SavePreferencesCoreAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
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
        var preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        if (!preferences.IsEnabled)
        {
            return new GalleryFavoriteProtectionResult(false, null, null);
        }

        var store = new GalleryFavoriteProtectionStore(preferences.ActiveRootPath);
        var entry = await store.ProtectAsync(
            scopeId,
            relativePath,
            originalPath,
            cancellationToken).ConfigureAwait(false);
        if (!store.TryResolveProtectedPath(scopeId, relativePath, out var protectedPath))
        {
            throw new InvalidDataException("The protected favorite was published but cannot be resolved.");
        }

        return new GalleryFavoriteProtectionResult(true, entry, protectedPath);
    }

    public async Task<IReadOnlyList<GalleryProtectedFavorite>> GetProtectedFavoritesAsync(
        string scopeId,
        IEnumerable<string> starredRelativePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentNullException.ThrowIfNull(starredRelativePaths);
        var starred = starredRelativePaths
            .Select(GalleryAnnotationStore.NormalizeRelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        var protectedFavorites = new List<GalleryProtectedFavorite>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rootPath in EnumerateRoots(preferences))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GalleryFavoriteProtectionStore store;
            IReadOnlyList<GalleryFavoriteProtectionEntry> entries;
            try
            {
                store = new GalleryFavoriteProtectionStore(rootPath);
                entries = store.GetEntries(scopeId);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or InvalidDataException)
            {
                continue;
            }

            foreach (var candidate in entries
                         .Where(entry => starred.Contains(entry.RelativePath))
                         .OrderByDescending(entry => entry.ProtectedAtUtc))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (seenPaths.Contains(candidate.RelativePath))
                {
                    continue;
                }

                var entry = candidate;
                var originalExists = File.Exists(entry.OriginalPath);
                if (!originalExists)
                {
                    entry = await store.VerifyAsync(
                            entry.ScopeId,
                            entry.RelativePath,
                            cancellationToken)
                        .ConfigureAwait(false) ?? entry;
                }

                if (store.TryResolveProtectedPath(
                        entry.ScopeId,
                        entry.RelativePath,
                        out var protectedPath))
                {
                    seenPaths.Add(entry.RelativePath);
                    protectedFavorites.Add(new GalleryProtectedFavorite(
                        entry,
                        protectedPath,
                        IsUsingProtectedCopy: !originalExists));
                }
            }
        }

        return protectedFavorites;
    }

    public async Task<GalleryFavoriteProtectionOverview> GetOverviewAsync(
        bool verify,
        CancellationToken cancellationToken = default)
    {
        var preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<(string RootPath, GalleryFavoriteProtectionEntry Entry)>();
        foreach (var rootPath in EnumerateRoots(preferences))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var store = new GalleryFavoriteProtectionStore(rootPath);
                var rootEntries = verify
                    ? await store.VerifyAsync(cancellationToken).ConfigureAwait(false)
                    : store.GetEntries();
                entries.AddRange(rootEntries.Select(entry => (rootPath, entry)));
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or InvalidDataException)
            {
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
        return new GalleryFavoriteProtectionOverview(preferences, statistics);
    }

    public Task<GalleryFavoriteProtectionOverview> VerifyAsync(
        CancellationToken cancellationToken = default) =>
        GetOverviewAsync(verify: true, cancellationToken);

    public async Task<GalleryFavoriteProtectionCleanupResult> CleanUnstarredAsync(
        string scopeId,
        IEnumerable<string> starredRelativePaths,
        CancellationToken cancellationToken = default)
    {
        var preferences = await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
        var result = new GalleryFavoriteProtectionCleanupResult();
        foreach (var rootPath in EnumerateRoots(preferences))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var store = new GalleryFavoriteProtectionStore(rootPath);
            var current = await store.CleanUnstarredAsync(
                    scopeId,
                    starredRelativePaths,
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
        if (!File.Exists(SettingsFilePath))
        {
            return CreateDefaultPreferences();
        }

        await using var stream = new FileStream(
            SettingsFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var preferences = await JsonSerializer.DeserializeAsync(
            stream,
            PreferencesJsonTypeInfo,
            cancellationToken).ConfigureAwait(false);
        return ValidatePreferences(preferences);
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
            KnownRootPaths = [_defaultRootPath],
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
            .Append(activeRoot)
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
            .Distinct(StringComparer.OrdinalIgnoreCase);

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
