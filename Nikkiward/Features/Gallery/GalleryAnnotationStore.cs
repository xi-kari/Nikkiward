using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Nikkiward.Serialization;

namespace Nikkiward.Features.Gallery;

public sealed record GalleryStarEntry
{
    public required string ScopeId { get; init; }

    public required string RelativePath { get; init; }
}

public sealed record GalleryAnnotationSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<GalleryStarEntry> Stars { get; init; } = [];
}

public sealed class GalleryAnnotationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonTypeInfo<GalleryAnnotationSnapshot> SnapshotJsonTypeInfo =
        new NikkiwardJsonContext(SerializerOptions).GalleryAnnotationSnapshot;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public GalleryAnnotationStore(string? localApplicationDataPath = null)
    {
        var localRoot = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localApplicationDataPath;
        if (string.IsNullOrWhiteSpace(localRoot))
        {
            throw new InvalidOperationException("The LocalApplicationData directory is unavailable.");
        }

        AnnotationFilePath = Path.Combine(
            Path.GetFullPath(localRoot),
            "Nikkiward",
            "Gallery",
            "stars.json");
    }

    public string AnnotationFilePath { get; }

    public static string CreateScopeId(string? profileId, string? rootPath)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            return $"profile:{profileId.Trim()}";
        }

        var normalizedRoot = string.IsNullOrWhiteSpace(rootPath)
            ? "unbound"
            : Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot));
        return $"root:{Convert.ToHexString(hash)[..24]}";
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return relativePath
            .Trim()
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar)
            .ToUpperInvariant();
    }

    public async Task<IReadOnlySet<string>> LoadStarredAsync(
        string scopeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            return snapshot.Stars
                .Where(entry => string.Equals(
                    entry.ScopeId,
                    scopeId,
                    StringComparison.Ordinal))
                .Select(entry => NormalizeRelativePath(entry.RelativePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetStarredAsync(
        string scopeId,
        string relativePath,
        bool isStarred,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        var normalizedPath = NormalizeRelativePath(relativePath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var snapshot = await LoadSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var stars = snapshot.Stars
                .Where(entry =>
                    !string.Equals(entry.ScopeId, scopeId, StringComparison.Ordinal) ||
                    !string.Equals(
                        NormalizeRelativePath(entry.RelativePath),
                        normalizedPath,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (isStarred)
            {
                stars.Add(new GalleryStarEntry
                {
                    ScopeId = scopeId,
                    RelativePath = normalizedPath,
                });
            }

            await SaveSnapshotAsync(
                snapshot with
                {
                    Stars = stars
                        .OrderBy(entry => entry.ScopeId, StringComparer.Ordinal)
                        .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GalleryAnnotationSnapshot> LoadSnapshotAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(AnnotationFilePath))
        {
            return new GalleryAnnotationSnapshot();
        }

        await using var stream = new FileStream(
            AnnotationFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var snapshot = await JsonSerializer.DeserializeAsync(
            stream,
            SnapshotJsonTypeInfo,
            cancellationToken).ConfigureAwait(false);
        if (snapshot is null ||
            snapshot.SchemaVersion != GalleryAnnotationSnapshot.CurrentSchemaVersion ||
            snapshot.Stars is null)
        {
            throw new InvalidDataException("The gallery annotation document is invalid.");
        }

        return snapshot;
    }

    private async Task SaveSnapshotAsync(
        GalleryAnnotationSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(AnnotationFilePath)
            ?? throw new InvalidOperationException("The gallery annotation directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{AnnotationFilePath}.{Guid.NewGuid():N}.tmp";
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
                    snapshot,
                    SnapshotJsonTypeInfo,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, AnnotationFilePath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

public sealed record GalleryDefaultFavoriteAsset(
    string FileName,
    string Sha256);

public sealed record GalleryDefaultFavorite(
    string ScopeId,
    string RelativePath,
    string FilePath,
    long FileSizeBytes,
    DateTime LastWriteTimeUtc);

public sealed class GalleryDefaultFavoriteSeedService
{
    public const string ScopeId = "default-favorites:v1";

    private const string SeedVersion = "1";

    private static readonly SemaphoreSlim SeedGate = new(1, 1);

    private static readonly IReadOnlyList<GalleryDefaultFavoriteAsset> DefaultAssets =
    [
        new("01.jpg", "21093DD12A21385F76AD57819FF1EB2A80AF751579CB50EAF3C598BC0768F902"),
        new("02.jpg", "0FC974EE740B09D5E620F2AC34EB23126D56E8E957422BC580CB35C5AADBBB22"),
        new("03.jpg", "79E98642EC260C9CA8F4A89A12D8294B0474B78658DAB6DE330BFCB192514880"),
        new("04.jpg", "C2ADB227F963C6C46F98874A04027E8169DEEF425AE87FC8437BA810F68E275D"),
        new("05.jpg", "EC0C9FFE241C771256CE4B8500079850DC4FCCECA9F42B8F2D644A12DD672072"),
    ];

    private readonly IReadOnlyList<GalleryDefaultFavoriteAsset> _assets;
    private readonly string _assetsRootPath;

    public GalleryDefaultFavoriteSeedService(
        string? localApplicationDataPath = null,
        string? assetsRootPath = null,
        IReadOnlyList<GalleryDefaultFavoriteAsset>? assets = null)
    {
        var localRoot = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localApplicationDataPath;
        if (string.IsNullOrWhiteSpace(localRoot))
        {
            throw new InvalidOperationException("The LocalApplicationData directory is unavailable.");
        }

        _assetsRootPath = string.IsNullOrWhiteSpace(assetsRootPath)
            ? Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultFavorites")
            : Path.GetFullPath(assetsRootPath);
        _assets = assets ?? DefaultAssets;
        if (_assets.Count == 0 ||
            _assets.Any(asset =>
                string.IsNullOrWhiteSpace(asset.FileName) ||
                Path.GetFileName(asset.FileName) != asset.FileName ||
                string.IsNullOrWhiteSpace(asset.Sha256) ||
                asset.Sha256.Length != 64 ||
                !asset.Sha256.All(char.IsAsciiHexDigit)) ||
            _assets.Select(asset => asset.FileName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != _assets.Count)
        {
            throw new ArgumentException("The default favorite asset manifest is invalid.", nameof(assets));
        }

        DestinationDirectoryPath = Path.Combine(
            Path.GetFullPath(localRoot),
            "Nikkiward",
            "Gallery",
            "DefaultFavorites");
        SeedMarkerFilePath = Path.Combine(
            Path.GetDirectoryName(DestinationDirectoryPath)
                ?? throw new InvalidOperationException("The gallery data directory is unavailable."),
            "default-favorites-v1.seed");
    }

    public string DestinationDirectoryPath { get; }

    public string SeedMarkerFilePath { get; }

    public static IReadOnlyList<GalleryDefaultFavoriteAsset> AssetManifest => DefaultAssets;

    public async Task<IReadOnlyList<GalleryDefaultFavorite>> EnsureSeededAsync(
        GalleryAnnotationStore annotationStore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(annotationStore);
        await SeedGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var seeded = await IsSeedCompleteAsync(cancellationToken).ConfigureAwait(false);
            if (!seeded)
            {
                foreach (var asset in _assets)
                {
                    await EnsureLocalCopyAsync(asset, cancellationToken).ConfigureAwait(false);
                    await annotationStore.SetStarredAsync(
                        ScopeId,
                        CreateRelativePath(asset),
                        isStarred: true,
                        cancellationToken).ConfigureAwait(false);
                }

                await WriteSeedMarkerAsync(cancellationToken).ConfigureAwait(false);
            }

            var starredPaths = await annotationStore.LoadStarredAsync(
                ScopeId,
                cancellationToken).ConfigureAwait(false);
            var favorites = new List<GalleryDefaultFavorite>(_assets.Count);
            foreach (var asset in _assets)
            {
                var relativePath = CreateRelativePath(asset);
                if (!starredPaths.Contains(GalleryAnnotationStore.NormalizeRelativePath(relativePath)))
                {
                    continue;
                }

                var localPath = await EnsureLocalCopyAsync(asset, cancellationToken).ConfigureAwait(false);
                var file = new FileInfo(localPath);
                favorites.Add(new GalleryDefaultFavorite(
                    ScopeId,
                    relativePath,
                    localPath,
                    file.Length,
                    file.LastWriteTimeUtc));
            }

            return favorites;
        }
        finally
        {
            SeedGate.Release();
        }
    }

    private static string CreateRelativePath(GalleryDefaultFavoriteAsset asset) =>
        Path.Combine("DefaultFavorites", asset.FileName);

    private async Task<bool> IsSeedCompleteAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SeedMarkerFilePath))
        {
            return false;
        }

        var value = await File.ReadAllTextAsync(SeedMarkerFilePath, cancellationToken)
            .ConfigureAwait(false);
        return string.Equals(value.Trim(), SeedVersion, StringComparison.Ordinal);
    }

    private async Task<string> EnsureLocalCopyAsync(
        GalleryDefaultFavoriteAsset asset,
        CancellationToken cancellationToken)
    {
        var sourcePath = Path.Combine(_assetsRootPath, asset.FileName);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("A packaged default favorite is missing.", sourcePath);
        }

        var expectedHash = asset.Sha256.ToUpperInvariant();
        var sourceHash = await ComputeSha256Async(sourcePath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(sourceHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException("A packaged default favorite failed integrity validation.");
        }

        Directory.CreateDirectory(DestinationDirectoryPath);
        var destinationPath = Path.Combine(DestinationDirectoryPath, asset.FileName);
        if (File.Exists(destinationPath))
        {
            var destinationHash = await ComputeSha256Async(destinationPath, cancellationToken)
                .ConfigureAwait(false);
            if (string.Equals(destinationHash, expectedHash, StringComparison.Ordinal))
            {
                return destinationPath;
            }
        }

        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            var copiedHash = await ComputeSha256Async(temporaryPath, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(copiedHash, expectedHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A default favorite copy failed integrity validation.");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return destinationPath;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task WriteSeedMarkerAsync(CancellationToken cancellationToken)
    {
        var markerDirectory = Path.GetDirectoryName(SeedMarkerFilePath)
            ?? throw new InvalidOperationException("The gallery data directory is unavailable.");
        Directory.CreateDirectory(markerDirectory);
        var temporaryPath = $"{SeedMarkerFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryPath, SeedVersion, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, SeedMarkerFilePath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
