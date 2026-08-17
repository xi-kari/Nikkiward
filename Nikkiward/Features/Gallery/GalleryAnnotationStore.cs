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
