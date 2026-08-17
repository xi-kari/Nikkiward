using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nikkiward.Models;

namespace Nikkiward.Services;

public static class VariantManifestFactory
{
    public static VariantManifest Freeze(
        string manifestId,
        string gameBuildId,
        GameVariantId variantId,
        IEnumerable<VariantManifestEntry> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameBuildId);
        ArgumentNullException.ThrowIfNull(entries);

        var frozenEntries = entries
            .Select(CloneAndNormalize)
            .OrderBy(entry => entry.TargetRelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.TargetRelativePath, StringComparer.Ordinal)
            .ToArray();

        var manifest = new VariantManifest
        {
            ManifestId = manifestId.Trim(),
            GameBuildId = gameBuildId.Trim(),
            VariantId = variantId,
            Entries = Array.AsReadOnly(frozenEntries),
            ContentSha256 = string.Empty,
        };

        manifest = manifest with
        {
            ContentSha256 = VariantManifestDigest.Compute(manifest),
        };

        var validation = VariantManifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join(" ", validation.Errors), nameof(entries));
        }

        return manifest;
    }

    private static VariantManifestEntry CloneAndNormalize(VariantManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!VariantPathPolicy.TryNormalizeRelativePath(
                entry.TargetRelativePath,
                out var targetRelativePath,
                out var targetError))
        {
            throw new ArgumentException(targetError, nameof(entry));
        }

        string? sourceRelativePath = null;
        if (!string.IsNullOrWhiteSpace(entry.SourceRelativePath))
        {
            if (!VariantPathPolicy.TryNormalizeRelativePath(
                    entry.SourceRelativePath,
                    out sourceRelativePath,
                    out var sourceError))
            {
                throw new ArgumentException(sourceError, nameof(entry));
            }
        }

        return entry with
        {
            TargetRelativePath = targetRelativePath,
            SourceRelativePath = sourceRelativePath,
            Sha256 = entry.Sha256?.Trim().ToUpperInvariant(),
        };
    }
}

public static class VariantManifestValidator
{
    public static VariantManifestValidationResult Validate(VariantManifest? manifest)
    {
        var errors = new List<string>();
        if (manifest is null)
        {
            errors.Add("Manifest is required.");
            return new VariantManifestValidationResult { Errors = errors };
        }

        if (manifest.SchemaVersion != VariantManifest.CurrentSchemaVersion)
        {
            errors.Add($"Unsupported manifest schema version: {manifest.SchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.ManifestId))
        {
            errors.Add("ManifestId is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.GameBuildId))
        {
            errors.Add("GameBuildId is required.");
        }

        if (manifest.VariantId == GameVariantId.Unknown ||
            VariantDefinitionCatalog.Find(manifest.VariantId) is null)
        {
            errors.Add("Manifest VariantId is not a frozen Nikkiward variant.");
        }

        if (manifest.Entries is null)
        {
            errors.Add("Manifest entries are required.");
        }
        else
        {
            ValidateEntries(manifest.Entries, errors);
        }

        var computedDigest = VariantManifestDigest.Compute(manifest);
        if (!VariantHash.IsSha256(manifest.ContentSha256) ||
            !string.Equals(computedDigest, manifest.ContentSha256, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Manifest ContentSha256 does not match its canonical content.");
        }

        return new VariantManifestValidationResult
        {
            Errors = Array.AsReadOnly(errors.ToArray()),
            ComputedContentSha256 = computedDigest,
        };
    }

    private static void ValidateEntries(
        IReadOnlyList<VariantManifestEntry> entries,
        List<string> errors)
    {
        var normalizedTargets = new List<string>(entries.Count);
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null)
            {
                errors.Add($"Entry {index} is null.");
                continue;
            }

            if (!VariantPathPolicy.TryNormalizeRelativePath(
                    entry.TargetRelativePath,
                    out var targetRelativePath,
                    out var targetError))
            {
                errors.Add($"Entry {index}: {targetError}");
                continue;
            }

            normalizedTargets.Add(targetRelativePath);
            if (!seenTargets.Add(targetRelativePath))
            {
                errors.Add($"Entry {index}: duplicate target path '{targetRelativePath}'.");
            }

            if (entry.Classification is VariantFileClassification.Unknown)
            {
                errors.Add($"Entry {index}: Unknown classification cannot be materialized.");
                continue;
            }

            if (entry.Classification is VariantFileClassification.AbsentPath)
            {
                if (entry.SourceKind is not VariantSourceKind.None ||
                    !string.IsNullOrWhiteSpace(entry.SourceRelativePath) ||
                    entry.Length is not null ||
                    entry.Sha256 is not null)
                {
                    errors.Add($"Entry {index}: AbsentPath cannot carry source identity.");
                }

                continue;
            }

            if (entry.SourceKind is VariantSourceKind.None)
            {
                errors.Add($"Entry {index}: a materialized file requires a source kind.");
            }

            if (!VariantPathPolicy.TryNormalizeRelativePath(
                    entry.SourceRelativePath,
                    out _,
                    out var sourceError))
            {
                errors.Add($"Entry {index}: {sourceError}");
            }

            if (entry.Length is null or < 0)
            {
                errors.Add($"Entry {index}: a materialized file requires a non-negative length.");
            }

            if (!VariantHash.IsSha256(entry.Sha256))
            {
                errors.Add($"Entry {index}: a materialized file requires a SHA-256 digest.");
            }

            if (entry.Classification is VariantFileClassification.SharedImmutable &&
                entry.SourceKind is not VariantSourceKind.SharedContent)
            {
                errors.Add($"Entry {index}: SharedImmutable must come from SharedContent.");
            }

            if ((entry.Classification is VariantFileClassification.VariantExclusive or
                 VariantFileClassification.VariantMutable) &&
                entry.SourceKind is not VariantSourceKind.VariantOverlay)
            {
                errors.Add($"Entry {index}: variant-owned files must come from VariantOverlay.");
            }
        }

        var targetSet = normalizedTargets.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targetSet)
        {
            var segments = target.Split(Path.DirectorySeparatorChar);
            for (var segmentCount = 1; segmentCount < segments.Length; segmentCount++)
            {
                var ancestor = string.Join(Path.DirectorySeparatorChar, segments.Take(segmentCount));
                if (targetSet.Contains(ancestor))
                {
                    errors.Add($"Target path '{ancestor}' conflicts with descendant '{target}'.");
                    break;
                }
            }
        }
    }
}

public static class VariantManifestDigest
{
    public static string Compute(VariantManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", manifest.SchemaVersion);
            writer.WriteString("manifestId", manifest.ManifestId ?? string.Empty);
            writer.WriteString("gameBuildId", manifest.GameBuildId ?? string.Empty);
            writer.WriteNumber("variantId", (int)manifest.VariantId);
            writer.WriteStartArray("entries");

            foreach (var entry in (manifest.Entries ?? Array.Empty<VariantManifestEntry>())
                         .Where(entry => entry is not null)
                         .OrderBy(entry => entry.TargetRelativePath, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(entry => entry.TargetRelativePath, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("targetRelativePath", CanonicalPath(entry.TargetRelativePath));
                if (entry.SourceRelativePath is null)
                {
                    writer.WriteNull("sourceRelativePath");
                }
                else
                {
                    writer.WriteString("sourceRelativePath", CanonicalPath(entry.SourceRelativePath));
                }

                writer.WriteNumber("classification", (int)entry.Classification);
                writer.WriteNumber("sourceKind", (int)entry.SourceKind);
                if (entry.Length is null)
                {
                    writer.WriteNull("length");
                }
                else
                {
                    writer.WriteNumber("length", entry.Length.Value);
                }

                if (entry.Sha256 is null)
                {
                    writer.WriteNull("sha256");
                }
                else
                {
                    writer.WriteString("sha256", entry.Sha256.ToUpperInvariant());
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static string CanonicalPath(string? path) =>
        (path ?? string.Empty).Replace('/', '\\');
}

public static class VariantPathPolicy
{
    private static readonly char[] InvalidFileNameCharacters =
        ['<', '>', ':', '"', '|', '?', '*', '\0'];

    private static readonly HashSet<string> ReservedDeviceNames = new(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static bool TryNormalizeRelativePath(
        string? relativePath,
        out string normalizedPath,
        out string? error)
    {
        normalizedPath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            error = "Relative path is required.";
            return false;
        }

        var value = relativePath.Trim();
        if (Path.IsPathRooted(value) || value.StartsWith('\\') || value.StartsWith('/'))
        {
            error = $"Rooted path '{relativePath}' is not allowed.";
            return false;
        }

        var segments = value.Split(['\\', '/'], StringSplitOptions.None);
        if (segments.Length == 0)
        {
            error = "Relative path has no segments.";
            return false;
        }

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
            {
                error = $"Relative path '{relativePath}' contains an empty or traversal segment.";
                return false;
            }

            if (segment.EndsWith(' ') || segment.EndsWith('.'))
            {
                error = $"Relative path '{relativePath}' contains a Windows-ambiguous segment.";
                return false;
            }

            if (segment.Any(character =>
                    character < 32 || InvalidFileNameCharacters.Contains(character)))
            {
                error = $"Relative path '{relativePath}' contains an invalid Windows character.";
                return false;
            }

            var deviceStem = segment.Split('.', 2)[0];
            if (ReservedDeviceNames.Contains(deviceStem))
            {
                error = $"Relative path '{relativePath}' contains a reserved Windows device name.";
                return false;
            }
        }

        normalizedPath = string.Join(Path.DirectorySeparatorChar, segments);
        return true;
    }

    public static bool TryResolveWithinRoot(
        string rootPath,
        string? relativePath,
        out string fullPath,
        out string? error)
    {
        fullPath = string.Empty;
        if (!TryNormalizeRelativePath(relativePath, out var normalizedRelativePath, out error))
        {
            return false;
        }

        try
        {
            var root = NormalizeFullPath(rootPath);
            var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            fullPath = Path.GetFullPath(Path.Combine(root, normalizedRelativePath));
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Resolved path '{fullPath}' is outside root '{root}'.";
                fullPath = string.Empty;
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Path resolution failed: {ex.GetType().Name}.";
            fullPath = string.Empty;
            return false;
        }
    }

    public static string NormalizeFullPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool PathsOverlap(string firstPath, string secondPath)
    {
        var first = NormalizeFullPath(firstPath);
        var second = NormalizeFullPath(secondPath);
        return IsSameOrDescendant(first, second) || IsSameOrDescendant(second, first);
    }

    public static bool IsSameOrDescendant(string candidatePath, string rootPath)
    {
        var candidate = NormalizeFullPath(candidatePath);
        var root = NormalizeFullPath(rootPath);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsReparsePoint(string path)
    {
        try
        {
            var currentPath = NormalizeFullPath(path);
            while (!string.IsNullOrEmpty(currentPath))
            {
                if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                var parent = Path.GetDirectoryName(currentPath);
                if (string.IsNullOrEmpty(parent) ||
                    string.Equals(parent, currentPath, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                currentPath = parent;
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    public static bool ContainsReparsePointInExistingChain(string rootPath, string candidatePath)
    {
        if (ContainsReparsePoint(rootPath))
        {
            return true;
        }

        var root = NormalizeFullPath(rootPath);
        var current = File.Exists(candidatePath)
            ? candidatePath
            : Directory.Exists(candidatePath)
                ? candidatePath
                : Path.GetDirectoryName(candidatePath);

        while (!string.IsNullOrEmpty(current) && IsSameOrDescendant(current, root))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            if (string.Equals(NormalizeFullPath(current), root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current);
        }

        return false;
    }
}

internal static class VariantHash
{
    public static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    public static async Task<string> ComputeFileSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}

internal sealed record VariantFileVerification(
    bool Passed,
    long? ActualLength,
    string? ActualSha256,
    string? FailureDetail);

internal static class VariantFileVerifier
{
    public static async Task<VariantFileVerification> VerifyAsync(
        string filePath,
        long expectedLength,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(filePath);
            if (!file.Exists)
            {
                return new(false, null, null, "File is missing.");
            }

            if (file.Length != expectedLength)
            {
                return new(false, file.Length, null,
                    $"Length mismatch: expected {expectedLength}, observed {file.Length}.");
            }

            var sha256 = await VariantHash.ComputeFileSha256Async(filePath, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(sha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new(false, file.Length, sha256, "SHA-256 mismatch.");
            }

            return new(true, file.Length, sha256, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return new(false, null, null, $"File verification failed: {ex.GetType().Name}.");
        }
    }
}

internal static class VariantPlanDigest
{
    public static string Compute(VariantMaterializationPlan plan)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("variantId", (int)plan.VariantId);
            writer.WriteString("manifestContentSha256", plan.ManifestContentSha256);
            writer.WriteString("sharedContentRootPath", plan.SharedContentRootPath);
            writer.WriteString("variantOverlayRootPath", plan.VariantOverlayRootPath);
            writer.WriteString("targetRootPath", plan.TargetRootPath);
            writer.WriteStartArray("items");
            foreach (var item in plan.Items)
            {
                writer.WriteStartObject();
                writer.WriteString("targetRelativePath", item.TargetRelativePath.Replace('/', '\\'));
                writer.WriteString("sourcePath", item.SourcePath);
                writer.WriteString("targetPath", item.TargetPath);
                writer.WriteNumber("classification", (int)item.Classification);
                writer.WriteNumber("sourceKind", (int)item.SourceKind);
                writer.WriteNumber("action", (int)item.Action);
                if (item.ExpectedLength is null)
                {
                    writer.WriteNull("expectedLength");
                }
                else
                {
                    writer.WriteNumber("expectedLength", item.ExpectedLength.Value);
                }

                writer.WriteString("expectedSha256", item.ExpectedSha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
