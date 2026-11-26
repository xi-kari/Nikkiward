using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Nikkiward.Features.Gallery;

public enum GalleryFavoriteProtectionStatus
{
    Protected,
    OriginalMissing,
    OriginalChanged,
    ObjectMissing,
    ObjectCorrupt,
}

public sealed record GalleryFavoriteProtectionEntry
{
    public required string ScopeId { get; init; }

    public required string RelativePath { get; init; }

    public required string OriginalPath { get; init; }

    public required long OriginalLength { get; init; }

    public required DateTimeOffset OriginalLastWriteTimeUtc { get; init; }

    public required string Sha256 { get; init; }

    public required string ObjectPath { get; init; }

    public required GalleryFavoriteProtectionStatus Status { get; init; }

    public required DateTimeOffset ProtectedAtUtc { get; init; }

    public DateTimeOffset? LastVerifiedAtUtc { get; init; }
}

public sealed record GalleryFavoriteProtectionManifest
{
    public const int CurrentSchemaVersion = 1;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonRequired]
    public IReadOnlyList<GalleryFavoriteProtectionEntry> Entries { get; init; } = [];
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GalleryFavoriteProtectionManifest))]
internal sealed partial class GalleryFavoriteProtectionJsonContext : JsonSerializerContext
{
}

public sealed record GalleryFavoriteProtectionStatistics
{
    public int EntryCount { get; init; }

    public int HealthyEntryCount { get; init; }

    public int OriginalMissingCount { get; init; }

    public int OriginalChangedCount { get; init; }

    public int ObjectMissingCount { get; init; }

    public int ObjectCorruptCount { get; init; }

    public int UniqueObjectCount { get; init; }

    public long ProtectedBytes { get; init; }
}

public enum GalleryOriginalIssue { Missing, Changed, Unreadable }

internal sealed class GalleryOriginalReadLocks : IDisposable
{
    private readonly List<FileStream> _originals = [];

    public async Task<GalleryOriginalIssue?> CheckAsync(GalleryFavoriteProtectionEntry entry, CancellationToken token)
    {
        try
        {
            var original = new FileStream(entry.OriginalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            _originals.Add(original);
            var hash = await SHA256.HashDataAsync(original, token).ConfigureAwait(false);
            return Convert.ToHexString(hash).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)
                ? null : GalleryOriginalIssue.Changed;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return GalleryOriginalIssue.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return GalleryOriginalIssue.Unreadable;
        }
    }

    public void Dispose()
    {
        foreach (var original in _originals)
        {
            original.Dispose();
        }
    }
}

public sealed record GalleryCleanupCopy(string ObjectPath, string DisplayName);

public sealed record GalleryFavoriteProtectionCleanupResult
{
    public IReadOnlyList<GalleryCleanupCopy> NeedsConfirmation { get; init; } = [];

    public int RemovedEntryCount { get; init; }

    public int RemovedObjectCount { get; init; }

    public long ReclaimedBytes { get; init; }
}

public sealed class GalleryFavoriteProtectionStore
{
    private const int BufferSize = 1024 * 1024;
    private const string LockFileName = ".store.lock";
    private const string ManifestFileName = "manifest.json";
    private const string ObjectsDirectoryName = "objects";
    private const string TemporaryDirectoryName = ".tmp";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false),
        },
    };

    internal static readonly JsonTypeInfo<GalleryFavoriteProtectionManifest> ManifestJsonTypeInfo =
        new GalleryFavoriteProtectionJsonContext(SerializerOptions).GalleryFavoriteProtectionManifest;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RootGates =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _gate;
    private readonly Func<string, Stream> _openManifest;

    public GalleryFavoriteProtectionStore(string? rootPath = null, Func<string, Stream>? openManifest = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = Path.Combine(
                Nikkiward.Services.ApplicationDataPaths.Root,
                "Gallery",
                "ProtectedFavorites");
        }

        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new ArgumentException("The protection root is invalid.", nameof(rootPath));
        }

        ManifestPath = Path.Combine(RootPath, ManifestFileName);
        LockPath = Path.Combine(RootPath, LockFileName);
        ObjectsPath = Path.Combine(RootPath, ObjectsDirectoryName);
        TemporaryPath = Path.Combine(RootPath, TemporaryDirectoryName);
        _gate = RootGates.GetOrAdd(
            RootPath,
            static _ => new SemaphoreSlim(1, 1));
        _openManifest = openManifest ?? (path => new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan));
    }

    public string RootPath { get; }

    public string ManifestPath { get; }

    private string LockPath { get; }

    public string ObjectsPath { get; }

    internal string TemporaryPath { get; }

    internal Action? CheckWriteAccess { private get; set; }

    public async Task<GalleryFavoriteProtectionEntry> ProtectAsync(
        string scopeId,
        string relativePath,
        string originalPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedScopeId = NormalizeScopeId(scopeId);
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var normalizedOriginalPath = NormalizeOriginalPath(originalPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var storeLock = await AcquireStoreLockAsync(cancellationToken)
                .ConfigureAwait(false);
            EnsureStoreDirectories();
            var original = ReadOriginalMetadata(normalizedOriginalPath);
            var temporaryObjectPath = Path.Combine(
                TemporaryPath,
                $"protect-{Guid.NewGuid():N}.tmp");

            try
            {
                var copy = await CopyAndHashAsync(
                    normalizedOriginalPath,
                    temporaryObjectPath,
                    cancellationToken).ConfigureAwait(false);
                if (copy.Length != original.Length)
                {
                    throw new IOException("The source length changed while the favorite was protected.");
                }

                var refreshedOriginal = ReadOriginalMetadata(normalizedOriginalPath);
                if (refreshedOriginal.Length != original.Length ||
                    refreshedOriginal.LastWriteTimeUtc != original.LastWriteTimeUtc)
                {
                    throw new IOException("The source changed while the favorite was protected.");
                }

                var temporaryHash = await CalculateSha256Async(
                    temporaryObjectPath,
                    cancellationToken).ConfigureAwait(false);
                if (!string.Equals(copy.Sha256, temporaryHash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("The temporary protected object failed hash verification.");
                }

                var objectPath = await PublishObjectAsync(
                    temporaryObjectPath,
                    copy.Sha256,
                    copy.Length,
                    cancellationToken).ConfigureAwait(false);
                var manifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
                var previous = FindEntry(
                    manifest.Entries,
                    normalizedScopeId,
                    normalizedRelativePath);
                var now = DateTimeOffset.UtcNow;
                var entry = new GalleryFavoriteProtectionEntry
                {
                    ScopeId = normalizedScopeId,
                    RelativePath = normalizedRelativePath,
                    OriginalPath = normalizedOriginalPath,
                    OriginalLength = original.Length,
                    OriginalLastWriteTimeUtc = original.LastWriteTimeUtc,
                    Sha256 = copy.Sha256,
                    ObjectPath = objectPath,
                    Status = GalleryFavoriteProtectionStatus.Protected,
                    ProtectedAtUtc = previous?.ProtectedAtUtc ?? now,
                    LastVerifiedAtUtc = now,
                };
                var entries = manifest.Entries
                    .Where(item => !HasKey(item, normalizedScopeId, normalizedRelativePath))
                    .Append(entry)
                    .OrderBy(item => item.ScopeId, StringComparer.Ordinal)
                    .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                await SaveManifestAsync(
                    manifest with { Entries = entries },
                    cancellationToken).ConfigureAwait(false);

                return entry;
            }
            finally
            {
                TryDeleteFile(temporaryObjectPath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public GalleryFavoriteProtectionEntry? GetEntry(string scopeId, string relativePath) =>
        FindEntry(GetEntries(), NormalizeScopeId(scopeId), NormalizeRelativePath(relativePath));

    public IReadOnlyList<GalleryFavoriteProtectionEntry> GetEntries() =>
        IsRootAbsentForRead() ? [] : LoadManifest().Entries;

    public IReadOnlyList<GalleryFavoriteProtectionEntry> GetEntries(string scopeId)
    {
        var normalizedScope = NormalizeScopeId(scopeId);
        return GetEntries().Where(entry => entry.ScopeId == normalizedScope).ToArray();
    }

    internal async Task<ManifestIndex> OpenIndexAsync(
        bool writable, CancellationToken cancellationToken, bool waitForWriter = false)
    {
        if (IsRootAbsentForRead() || !File.Exists(ManifestPath))
        {
            return new(this, new(), null, hasManifest: false);
        }

        if (!writable || (File.GetAttributes(ManifestPath) & FileAttributes.ReadOnly) != 0)
        {
            return new(this, await LoadManifestAsync(cancellationToken).ConfigureAwait(false), null);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        FileStream? storeLock = null;
        try
        {
            storeLock = await AcquireStoreLockAsync(cancellationToken, waitForWriter).ConfigureAwait(false);
            var manifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
            return new(this, manifest, storeLock);
        }
        catch (Exception ex) when (storeLock is null && ex is IOException or UnauthorizedAccessException)
        {
            _gate.Release();
            return new(this, await LoadManifestAsync(cancellationToken).ConfigureAwait(false), null);
        }
        catch
        {
            storeLock?.Dispose();
            _gate.Release();
            throw;
        }
    }

    internal sealed class ManifestIndex(
        GalleryFavoriteProtectionStore store, GalleryFavoriteProtectionManifest manifest, FileStream? storeLock,
        bool hasManifest = true) : IDisposable
    {
        public GalleryFavoriteProtectionStore Store => store;
        public IReadOnlyList<GalleryFavoriteProtectionEntry> Entries => manifest.Entries;
        public bool CanWrite => storeLock is not null;
        public bool HasManifest => hasManifest;

        public async Task MigrateAsync(string legacyScope, string scope, IReadOnlySet<string> paths, CancellationToken token)
        {
            if (!CanWrite || !Entries.Any(entry => entry.ScopeId == legacyScope && paths.Contains(entry.RelativePath)))
            {
                return;
            }

            var entries = Entries.Select(entry => entry.ScopeId == legacyScope && paths.Contains(entry.RelativePath)
                ? entry with { ScopeId = scope } : entry).ToArray();
            await SaveAsync(entries, token).ConfigureAwait(false);
        }

        public async Task SaveAsync(IReadOnlyList<GalleryFavoriteProtectionEntry> entries, CancellationToken token)
        {
            if (!CanWrite)
            {
                throw new InvalidOperationException("The protection index is read-only.");
            }

            var updated = manifest with { Entries = entries };
            await store.SaveManifestAsync(updated, token).ConfigureAwait(false);
            manifest = updated;
        }

        public void DeleteReleasedObject(GalleryFavoriteProtectionEntry entry)
        {
            if (!Entries.Any(item => string.Equals(item.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase)))
            {
                store.TryDeleteObject(entry);
            }
        }

        public void Dispose()
        {
            if (storeLock is not null)
            {
                storeLock.Dispose();
                storeLock = null;
                store._gate.Release();
            }
        }
    }

    public async Task<GalleryFavoriteProtectionEntry?> VerifyAsync(
        string scopeId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedScopeId = NormalizeScopeId(scopeId);
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (IsRootAbsentForRead())
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRootAbsentForRead())
            {
                return null;
            }

            await using var storeLock = await AcquireStoreLockAsync(cancellationToken)
                .ConfigureAwait(false);
            var manifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
            var existing = FindEntry(
                manifest.Entries,
                normalizedScopeId,
                normalizedRelativePath);
            if (existing is null)
            {
                return null;
            }

            var verified = await VerifyEntryAsync(existing, cancellationToken).ConfigureAwait(false);
            var entries = manifest.Entries
                .Select(entry => HasKey(entry, normalizedScopeId, normalizedRelativePath)
                    ? verified
                    : entry)
                .ToArray();
            await SaveManifestAsync(
                manifest with { Entries = entries },
                cancellationToken).ConfigureAwait(false);
            return verified;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<GalleryFavoriteProtectionEntry>> VerifyAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsRootAbsentForRead())
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRootAbsentForRead())
            {
                return [];
            }

            await using var storeLock = await AcquireStoreLockAsync(cancellationToken)
                .ConfigureAwait(false);
            var manifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
            var objectStatuses = new Dictionary<string, GalleryFavoriteProtectionStatus>(
                StringComparer.OrdinalIgnoreCase);
            var verified = new List<GalleryFavoriteProtectionEntry>(manifest.Entries.Count);
            foreach (var entry in manifest.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                verified.Add(await VerifyEntryAsync(
                    entry,
                    objectStatuses,
                    cancellationToken).ConfigureAwait(false));
            }

            var entries = verified
                .OrderBy(entry => entry.ScopeId, StringComparer.Ordinal)
                .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (entries.Length > 0 || File.Exists(ManifestPath))
            {
                await SaveManifestAsync(
                    manifest with { Entries = entries },
                    cancellationToken).ConfigureAwait(false);
            }

            return entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GalleryFavoriteProtectionStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        using var index = await OpenIndexAsync(writable: false, cancellationToken).ConfigureAwait(false);
        var entries = index.Entries;
        var usableObjects = entries
            .Where(entry => IsProtectedObjectUsable(entry.Status))
            .GroupBy(entry => entry.Sha256, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return new GalleryFavoriteProtectionStatistics
        {
            EntryCount = entries.Count,
            HealthyEntryCount = entries.Count(entry =>
                entry.Status == GalleryFavoriteProtectionStatus.Protected),
            OriginalMissingCount = entries.Count(entry =>
                entry.Status == GalleryFavoriteProtectionStatus.OriginalMissing),
            OriginalChangedCount = entries.Count(entry =>
                entry.Status == GalleryFavoriteProtectionStatus.OriginalChanged),
            ObjectMissingCount = entries.Count(entry =>
                entry.Status == GalleryFavoriteProtectionStatus.ObjectMissing),
            ObjectCorruptCount = entries.Count(entry =>
                entry.Status == GalleryFavoriteProtectionStatus.ObjectCorrupt),
            UniqueObjectCount = usableObjects.Length,
            ProtectedBytes = usableObjects.Sum(entry => entry.OriginalLength),
        };
    }

    public Task<GalleryFavoriteProtectionCleanupResult> CleanUnstarredAsync(
        string scopeId, GalleryAnnotationStore annotations, CancellationToken cancellationToken = default) =>
        CleanUnstarredAsync([this], scopeId, annotations, null, cancellationToken);

    internal static async Task<GalleryFavoriteProtectionCleanupResult> CleanUnstarredAsync(
        IEnumerable<GalleryFavoriteProtectionStore> stores, string scopeId, GalleryAnnotationStore annotations,
        IReadOnlyCollection<GalleryCleanupCopy>? confirmed, CancellationToken token)
    {
        var scope = NormalizeScopeId(scopeId);
        var indexes = new List<ManifestIndex>();
        using var originals = new GalleryOriginalReadLocks();
        try
        {
            foreach (var store in stores.OrderBy(store => GalleryFavoriteProtectionService.ResolveDirectoryLinks(store.RootPath),
                         StringComparer.OrdinalIgnoreCase))
            {
                var index = await store.OpenIndexAsync(true, token, waitForWriter: true).ConfigureAwait(false);
                indexes.Add(index);
                if (index.HasManifest && !index.CanWrite)
                {
                    throw new IOException("保护目录当前只读，无法清理副本。");
                }
            }

            var starred = await annotations.LoadStarredAsync(scope, token).ConfigureAwait(false);
            var pending = new List<GalleryCleanupCopy>();
            var plans = new List<(ManifestIndex Index, GalleryFavoriteProtectionEntry[] Remaining, string[] Objects)>();
            foreach (var index in indexes.Where(index => index.HasManifest))
            {
                var remaining = index.Entries.Where(entry => entry.ScopeId != scope || starred.Contains(entry.RelativePath)).ToArray();
                var hashes = remaining.Select(entry => entry.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var objects = index.Store.EnumerateUnreferencedObjects(hashes, token).ToArray();
                var byHash = index.Entries.ToLookup(entry => entry.Sha256, StringComparer.OrdinalIgnoreCase);
                foreach (var path in objects)
                {
                    var hash = Path.GetFileNameWithoutExtension(path);
                    var entries = byHash[hash];
                    var copy = new GalleryCleanupCopy(path, entries.FirstOrDefault()?.RelativePath ?? hash);
                    if (confirmed?.Contains(copy) == true)
                    {
                        continue;
                    }
                    var hasOriginal = false;
                    foreach (var entry in entries)
                    {
                        if (await originals.CheckAsync(entry, token).ConfigureAwait(false) is null)
                        {
                            hasOriginal = true;
                            break;
                        }
                    }
                    if (!hasOriginal)
                    {
                        pending.Add(copy);
                    }
                }
                plans.Add((index, remaining, objects));
            }
            if (pending.Count > 0)
            {
                return new() { NeedsConfirmation = pending };
            }

            int removedEntries = 0, removedObjects = 0;
            long bytes = 0;
            foreach (var (index, remaining, objects) in plans)
            {
                var removed = index.Entries.Count - remaining.Length;
                if (removed > 0)
                {
                    await index.SaveAsync(remaining, token).ConfigureAwait(false);
                    removedEntries += removed;
                }
                foreach (var path in objects)
                {
                    var length = index.Store.TryDeleteObjectPath(path);
                    if (length >= 0)
                    {
                        removedObjects++;
                        bytes += length;
                    }
                }
            }
            return new() { RemovedEntryCount = removedEntries, RemovedObjectCount = removedObjects, ReclaimedBytes = bytes };
        }
        finally
        {
            foreach (var index in indexes)
            {
                index.Dispose();
            }
        }
    }

    public bool TryResolveProtectedPath(
        string scopeId,
        string relativePath,
        out string protectedPath)
    {
        var entry = GetEntry(scopeId, relativePath);
        protectedPath = entry is null ? string.Empty :
            ResolveProtectedPathAsync(entry, verifyHash: true, CancellationToken.None).GetAwaiter().GetResult() ?? string.Empty;
        return protectedPath.Length > 0;
    }

    internal async Task<string?> ResolveProtectedPathAsync(
        GalleryFavoriteProtectionEntry entry, bool verifyHash, CancellationToken cancellationToken)
    {
        try
        {
            var candidate = ResolveObjectPath(entry);
            EnsureObjectPathDirectoriesAreSafe(candidate);
            if (verifyHash)
            {
                return await IsObjectValidAsync(candidate, entry.OriginalLength, entry.Sha256, cancellationToken)
                    .ConfigureAwait(false) ? candidate : null;
            }

            var file = new FileInfo(candidate);
            return IsProtectedObjectUsable(entry.Status) && file.Exists && file.Length == entry.OriginalLength &&
                (file.Attributes & FileAttributes.ReparsePoint) == 0 ? candidate : null;
        }
        catch (Exception ex) when (ex is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private GalleryFavoriteProtectionManifest LoadManifest()
    {
        EnsureRootPathIsSafeForRead();
        if (!File.Exists(ManifestPath))
        {
            return new GalleryFavoriteProtectionManifest();
        }

        EnsureFileIsNotReparsePoint(ManifestPath);
        using var stream = _openManifest(ManifestPath);
        var manifest = JsonSerializer.Deserialize(stream, ManifestJsonTypeInfo);
        return ValidateManifest(manifest);
    }

    private async Task<GalleryFavoriteProtectionManifest> LoadManifestAsync(
        CancellationToken cancellationToken)
    {
        EnsureRootPathIsSafeForRead();
        if (!File.Exists(ManifestPath))
        {
            return new GalleryFavoriteProtectionManifest();
        }

        EnsureFileIsNotReparsePoint(ManifestPath);
        await using var stream = _openManifest(ManifestPath);
        var manifest = await JsonSerializer.DeserializeAsync(
            stream,
            ManifestJsonTypeInfo,
            cancellationToken).ConfigureAwait(false);
        return ValidateManifest(manifest);
    }

    private async Task SaveManifestAsync(
        GalleryFavoriteProtectionManifest manifest,
        CancellationToken cancellationToken)
    {
        manifest = ValidateManifest(manifest);
        EnsureStoreDirectories();
        var temporaryManifestPath = Path.Combine(
            RootPath,
            $".{ManifestFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryManifestPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    manifest,
                    ManifestJsonTypeInfo,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(ManifestPath))
            {
                EnsureFileIsNotReparsePoint(ManifestPath);
            }

            CheckWriteAccess?.Invoke();
            File.Move(temporaryManifestPath, ManifestPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryManifestPath);
        }
    }

    private async Task<string> PublishObjectAsync(
        string temporaryObjectPath,
        string sha256,
        long length,
        CancellationToken cancellationToken)
    {
        CheckWriteAccess?.Invoke();
        var objectRelativePath = BuildObjectRelativePath(sha256);
        var objectPath = ResolveObjectPath(sha256, objectRelativePath);
        var objectDirectory = Path.GetDirectoryName(objectPath)
            ?? throw new InvalidOperationException("The protected object directory is unavailable.");
        Directory.CreateDirectory(objectDirectory);
        EnsureDirectoryIsNotReparsePoint(objectDirectory);

        if (File.Exists(objectPath) &&
            await IsObjectValidAsync(
                objectPath,
                length,
                sha256,
                cancellationToken).ConfigureAwait(false))
        {
            return objectRelativePath;
        }

        if (File.Exists(objectPath))
        {
            EnsureFileIsNotReparsePoint(objectPath);
        }

        CheckWriteAccess?.Invoke();
        File.Move(temporaryObjectPath, objectPath, overwrite: true);
        if (!await IsObjectValidAsync(
                objectPath,
                length,
                sha256,
                cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException("The published protected object failed hash verification.");
        }

        return objectRelativePath;
    }

    private async Task<GalleryFavoriteProtectionEntry> VerifyEntryAsync(
        GalleryFavoriteProtectionEntry entry,
        CancellationToken cancellationToken)
    {
        var objectStatuses = new Dictionary<string, GalleryFavoriteProtectionStatus>(
            StringComparer.OrdinalIgnoreCase);
        return await VerifyEntryAsync(
            entry,
            objectStatuses,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<GalleryFavoriteProtectionEntry> VerifyEntryAsync(
        GalleryFavoriteProtectionEntry entry,
        IDictionary<string, GalleryFavoriteProtectionStatus> objectStatuses,
        CancellationToken cancellationToken)
    {
        if (!objectStatuses.TryGetValue(entry.Sha256, out var status))
        {
            var objectPath = ResolveObjectPath(entry);
            if (!File.Exists(objectPath))
            {
                status = GalleryFavoriteProtectionStatus.ObjectMissing;
            }
            else
            {
                try
                {
                    EnsureObjectPathDirectoriesAreSafe(objectPath);
                    status = await IsObjectValidAsync(
                            objectPath,
                            entry.OriginalLength,
                            entry.Sha256,
                            cancellationToken).ConfigureAwait(false)
                        ? GalleryFavoriteProtectionStatus.Protected
                        : GalleryFavoriteProtectionStatus.ObjectCorrupt;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException
                    or InvalidDataException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException)
                {
                    status = GalleryFavoriteProtectionStatus.ObjectCorrupt;
                }
            }

            objectStatuses[entry.Sha256] = status;
        }

        if (status == GalleryFavoriteProtectionStatus.Protected)
        {
            status = await GetOriginalStatusAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        return entry with
        {
            Status = status,
            LastVerifiedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static async Task<GalleryFavoriteProtectionStatus> GetOriginalStatusAsync(
        GalleryFavoriteProtectionEntry entry,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(entry.OriginalPath))
        {
            return GalleryFavoriteProtectionStatus.OriginalMissing;
        }

        try
        {
            var original = new FileInfo(entry.OriginalPath);
            if (!original.Exists)
            {
                return GalleryFavoriteProtectionStatus.OriginalMissing;
            }

            if ((original.Attributes & FileAttributes.ReparsePoint) != 0 ||
                original.Length != entry.OriginalLength)
            {
                return GalleryFavoriteProtectionStatus.OriginalChanged;
            }

            var hash = await CalculateSha256Async(
                original.FullName,
                cancellationToken).ConfigureAwait(false);
            return string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase)
                ? GalleryFavoriteProtectionStatus.Protected
                : GalleryFavoriteProtectionStatus.OriginalChanged;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return GalleryFavoriteProtectionStatus.OriginalMissing;
        }
    }

    private static async Task<CopyResult> CopyAndHashAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            long length = 0;
            while (true)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
                length += read;
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new CopyResult(
                length,
                Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static async Task<string> CalculateSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static async Task<bool> IsObjectValidAsync(
        string path,
        long expectedLength,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists ||
                file.Length != expectedLength ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            var actualSha256 = await CalculateSha256Async(
                file.FullName,
                cancellationToken).ConfigureAwait(false);
            return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static OriginalMetadata ReadOriginalMetadata(string originalPath)
    {
        var original = new FileInfo(originalPath);
        if (!original.Exists)
        {
            throw new FileNotFoundException("The favorite source file does not exist.", originalPath);
        }

        if ((original.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The favorite source file cannot be a reparse point.");
        }

        return new OriginalMetadata(
            original.Length,
            new DateTimeOffset(DateTime.SpecifyKind(
                original.LastWriteTimeUtc,
                DateTimeKind.Utc)));
    }

    private GalleryFavoriteProtectionManifest ValidateManifest(
        GalleryFavoriteProtectionManifest? manifest)
    {
        if (manifest is null ||
            manifest.SchemaVersion != GalleryFavoriteProtectionManifest.CurrentSchemaVersion ||
            manifest.Entries is null)
        {
            throw new InvalidDataException("The favorite protection manifest is invalid.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var objectLengths = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in manifest.Entries)
        {
            if (entry is null ||
                string.IsNullOrWhiteSpace(entry.ScopeId) ||
                string.IsNullOrWhiteSpace(entry.RelativePath) ||
                string.IsNullOrWhiteSpace(entry.OriginalPath) ||
                entry.OriginalLength < 0 ||
                !IsSha256(entry.Sha256) ||
                !Enum.IsDefined(entry.Status) ||
                entry.ProtectedAtUtc == default ||
                !Path.IsPathFullyQualified(entry.OriginalPath) ||
                IsPathWithinRoot(entry.OriginalPath))
            {
                throw new InvalidDataException("The favorite protection manifest contains an invalid entry.");
            }

            var normalizedScopeId = NormalizeScopeId(entry.ScopeId);
            var normalizedRelativePath = NormalizeRelativePath(entry.RelativePath);
            if (objectLengths.TryGetValue(entry.Sha256, out var objectLength))
            {
                if (objectLength != entry.OriginalLength)
                {
                    throw new InvalidDataException(
                        "The favorite protection manifest contains conflicting object metadata.");
                }
            }
            else
            {
                objectLengths[entry.Sha256] = entry.OriginalLength;
            }

            if (!string.Equals(entry.ScopeId, normalizedScopeId, StringComparison.Ordinal) ||
                !string.Equals(
                    entry.RelativePath,
                    normalizedRelativePath,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    entry.ObjectPath,
                    BuildObjectRelativePath(entry.Sha256),
                    StringComparison.OrdinalIgnoreCase) ||
                !keys.Add(CreateEntryKey(normalizedScopeId, normalizedRelativePath)))
            {
                throw new InvalidDataException("The favorite protection manifest contains a conflicting entry.");
            }

            _ = ResolveObjectPath(entry);
        }

        return manifest;
    }

    private void EnsureStoreDirectories()
    {
        EnsureRootDirectory();
        Directory.CreateDirectory(ObjectsPath);
        EnsureDirectoryIsNotReparsePoint(ObjectsPath);
        Directory.CreateDirectory(TemporaryPath);
        EnsureDirectoryIsNotReparsePoint(TemporaryPath);
    }

    private void EnsureRootDirectory()
    {
        CheckWriteAccess?.Invoke();
        Directory.CreateDirectory(RootPath);
        EnsureDirectoryIsNotReparsePoint(RootPath);
    }

    private void EnsureRootPathIsSafeForRead()
    {
        if (Directory.Exists(RootPath))
        {
            EnsureDirectoryIsNotReparsePoint(RootPath);
        }
    }

    internal bool IsUnusedRoot()
    {
        if (IsRootAbsentForRead())
        {
            for (var parent = Directory.GetParent(RootPath); parent is not null; parent = parent.Parent)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(parent.FullName);
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    continue;
                }
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    return false;
                }
                // An offline directory mount can still have readable attributes on its host volume.
                using var siblings = parent.EnumerateFileSystemInfos().GetEnumerator();
                siblings.MoveNext();
                return true;
            }
            return false;
        }

        var children = Directory.GetFileSystemEntries(RootPath);
        if (children.Contains(ManifestPath, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!children.Contains(ObjectsPath, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }
        EnsureDirectoryIsNotReparsePoint(ObjectsPath);
        return Directory.EnumerateFileSystemEntries(ObjectsPath).All(directory =>
        {
            var attributes = File.GetAttributes(directory);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory &&
                !Directory.EnumerateFileSystemEntries(directory).Any();
        });
    }

    private bool IsRootAbsentForRead()
    {
        try
        {
            var attributes = File.GetAttributes(RootPath);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new IOException("The favorite protection root is not a directory.");
            }

            EnsureDirectoryIsNotReparsePoint(RootPath);
            return false;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
    }

    private async Task<FileStream> AcquireStoreLockAsync(
        CancellationToken cancellationToken,
        bool waitForWriter = true)
    {
        EnsureRootDirectory();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckWriteAccess?.Invoke();
            try
            {
                var stream = new FileStream(
                    LockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous);
                try
                {
                    EnsureFileIsNotReparsePoint(LockPath);
                    CheckWriteAccess?.Invoke();
                    return stream;
                }
                catch
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            catch (IOException ex) when (waitForWriter && IsSharingViolation(ex))
            {
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsSharingViolation(IOException exception) =>
        (exception.HResult & 0xFFFF) is 32 or 33;

    private string ResolveObjectPath(GalleryFavoriteProtectionEntry entry) =>
        ResolveObjectPath(entry.Sha256, entry.ObjectPath);

    private string ResolveObjectPath(string sha256, string objectRelativePath)
    {
        var expectedRelativePath = BuildObjectRelativePath(sha256);
        if (!string.Equals(
                objectRelativePath,
                expectedRelativePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The protected object path does not match its hash.");
        }

        var path = Path.GetFullPath(Path.Combine(
            RootPath,
            objectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureWithinRoot(path);
        return path;
    }

    private static string BuildObjectRelativePath(string sha256)
    {
        var normalized = sha256.ToLowerInvariant();
        return $"{ObjectsDirectoryName}/{normalized[..2]}/{normalized}.bin";
    }

    private void EnsureWithinRoot(string path)
    {
        var rootPrefix = Path.EndsInDirectorySeparator(RootPath)
            ? RootPath
            : RootPath + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The protected object path escapes the store root.");
        }
    }

    private static void EnsureDirectoryIsNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The favorite protection directory cannot be a reparse point.");
        }
    }

    private static void EnsureFileIsNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The protected object cannot be a reparse point.");
        }
    }

    private void EnsureObjectPathDirectoriesAreSafe(string objectPath)
    {
        if (!Directory.Exists(RootPath))
        {
            throw new DirectoryNotFoundException("The favorite protection root is unavailable.");
        }

        EnsureDirectoryIsNotReparsePoint(RootPath);
        if (!Directory.Exists(ObjectsPath))
        {
            throw new DirectoryNotFoundException("The protected objects directory is unavailable.");
        }

        EnsureDirectoryIsNotReparsePoint(ObjectsPath);
        var objectDirectory = Path.GetDirectoryName(objectPath)
            ?? throw new InvalidDataException("The protected object directory is invalid.");
        if (!Directory.Exists(objectDirectory))
        {
            throw new DirectoryNotFoundException("The protected object directory is unavailable.");
        }

        EnsureDirectoryIsNotReparsePoint(objectDirectory);
    }

    private long TryDeleteObject(GalleryFavoriteProtectionEntry entry)
    {
        try
        {
            return TryDeleteObjectPath(ResolveObjectPath(entry));
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return -1;
        }
    }

    private long TryDeleteObjectPath(string objectPath)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(objectPath);
            EnsureWithinRoot(normalizedPath);
            if (!File.Exists(normalizedPath))
            {
                return -1;
            }

            EnsureObjectPathDirectoriesAreSafe(normalizedPath);
            EnsureFileIsNotReparsePoint(normalizedPath);
            var length = new FileInfo(normalizedPath).Length;
            CheckWriteAccess?.Invoke();
            File.Delete(normalizedPath);
            return length;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return -1;
        }
    }

    private IEnumerable<string> EnumerateUnreferencedObjects(
        IReadOnlySet<string> referencedHashes,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(ObjectsPath))
        {
            yield break;
        }

        EnsureDirectoryIsNotReparsePoint(ObjectsPath);
        foreach (var objectDirectory in Directory.EnumerateDirectories(
                     ObjectsPath,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prefix = Path.GetFileName(objectDirectory);
            if (prefix.Length != 2 || !prefix.All(Uri.IsHexDigit))
            {
                continue;
            }

            EnsureDirectoryIsNotReparsePoint(objectDirectory);
            foreach (var objectPath in Directory.EnumerateFiles(
                         objectDirectory,
                         "*.bin",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileNameWithoutExtension(objectPath);
                if (!IsSha256(fileName) ||
                    !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    referencedHashes.Contains(fileName))
                {
                    continue;
                }

                var canonicalPath = ResolveObjectPath(
                    fileName,
                    BuildObjectRelativePath(fileName));
                if (!string.Equals(
                        objectPath,
                        canonicalPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return canonicalPath;
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static GalleryFavoriteProtectionEntry? FindEntry(
        IEnumerable<GalleryFavoriteProtectionEntry> entries,
        string scopeId,
        string relativePath) =>
        entries.FirstOrDefault(entry => HasKey(entry, scopeId, relativePath));

    private static bool HasKey(
        GalleryFavoriteProtectionEntry entry,
        string scopeId,
        string relativePath) =>
        string.Equals(entry.ScopeId, scopeId, StringComparison.Ordinal) &&
        string.Equals(entry.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase);

    private static string CreateEntryKey(string scopeId, string relativePath) =>
        $"{scopeId}\0{relativePath}";

    private static string NormalizeScopeId(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        var normalized = scopeId.Trim();
        if (normalized.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("The profile scope contains an invalid character.", nameof(scopeId));
        }

        return normalized;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var input = relativePath.Trim();
        if (Path.IsPathRooted(input))
        {
            throw new ArgumentException("The gallery path must be relative.", nameof(relativePath));
        }

        var normalized = GalleryAnnotationStore.NormalizeRelativePath(input);
        var segments = normalized.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            segments.Any(segment =>
                segment is "." or ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new ArgumentException("The gallery path is invalid.", nameof(relativePath));
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private string NormalizeOriginalPath(string originalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        var normalized = Path.GetFullPath(originalPath.Trim().Trim('"'));
        if (IsPathWithinRoot(normalized))
        {
            throw new ArgumentException(
                "The favorite source file cannot be inside the protection store.",
                nameof(originalPath));
        }

        return normalized;
    }

    private bool IsPathWithinRoot(string path)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (string.Equals(normalizedPath, RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootPrefix = Path.EndsInDirectorySeparator(RootPath)
            ? RootPath
            : RootPath + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsProtectedObjectUsable(GalleryFavoriteProtectionStatus status) =>
        status is GalleryFavoriteProtectionStatus.Protected
            or GalleryFavoriteProtectionStatus.OriginalMissing
            or GalleryFavoriteProtectionStatus.OriginalChanged;

    private readonly record struct CopyResult(long Length, string Sha256);

    private readonly record struct OriginalMetadata(
        long Length,
        DateTimeOffset LastWriteTimeUtc);
}
