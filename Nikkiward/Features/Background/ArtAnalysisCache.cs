using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Nikkiward.Serialization;

namespace Nikkiward.Features.Background;

/// <summary>
/// File-backed <see cref="IArtAnalysisCache"/>. Analyses live one JSON document
/// per artwork hash so a miss only ever costs a single small read, and a corrupt
/// or stale entry degrades to a miss rather than an error.
/// </summary>
public sealed class ArtAnalysisCache : IArtAnalysisCache
{
    private const int SupportedSchemaVersion = 3;
    private const int HashLength = 64;
    private const double MinScrimOpacity = 0.12;
    private const double MaxScrimOpacity = 0.52;
    private const int StreamBufferSize = 16 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly JsonTypeInfo<ArtAnalysis> ArtAnalysisJsonTypeInfo =
        new NikkiwardJsonContext(JsonOptions).ArtAnalysis;

    public ArtAnalysisCache(string? localApplicationDataPath = null)
    {
        var root = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localApplicationDataPath;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.GetTempPath();
        }

        var applicationRoot = Path.Combine(Path.GetFullPath(root), "Nikkiward");
        RootPath = Path.Combine(applicationRoot, "PaletteCache");
        BlurCachePath = Path.Combine(applicationRoot, "ArtCache");
    }

    /// <summary>Directory holding one analysis document per artwork hash.</summary>
    public string RootPath { get; }

    /// <summary>Directory holding the baked blur copies.</summary>
    public string BlurCachePath { get; }

    /// <summary>
    /// Path the blur bake for <paramref name="artHash"/> is expected at. The file
    /// may not exist yet; this only names it.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="artHash"/> is not a SHA-256 hex digest.</exception>
    public string GetBlurFilePath(string artHash)
    {
        ThrowIfInvalidHash(artHash, nameof(artHash));
        return Path.Combine(BlurCachePath, artHash + "-blur.jpg");
    }

    /// <summary>
    /// Reads the cached analysis for <paramref name="artHash"/>, or returns
    /// <see langword="null"/> when it is absent, stale, unreadable or does not
    /// belong to the requested hash.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="artHash"/> is not a SHA-256 hex digest.</exception>
    public async Task<ArtAnalysis?> LoadAsync(string artHash, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidHash(artHash, nameof(artHash));

        var filePath = GetAnalysisFilePath(artHash);
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var analysis = await JsonSerializer.DeserializeAsync<ArtAnalysis>(
                stream,
                ArtAnalysisJsonTypeInfo,
                cancellationToken).ConfigureAwait(false);
            if (analysis is null || analysis.SchemaVersion != SupportedSchemaVersion)
            {
                return null;
            }

            if (!string.Equals(analysis.ArtHash, artHash, StringComparison.Ordinal))
            {
                return null;
            }

            Repair(analysis);
            return analysis;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes <paramref name="analysis"/> atomically. IO failures propagate so the
    /// caller can decide whether a cache write is worth surfacing.
    /// </summary>
    /// <exception cref="ArgumentException">The analysis hash is not a SHA-256 hex digest.</exception>
    public async Task SaveAsync(ArtAnalysis analysis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ThrowIfInvalidHash(analysis.ArtHash, nameof(analysis));

        var filePath = GetAnalysisFilePath(analysis.ArtHash);
        var temporaryPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(RootPath);

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, analysis, ArtAnalysisJsonTypeInfo, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    /// <summary>
    /// Trims both cache directories to the <paramref name="maxFiles"/> most
    /// recently written entries. Best effort: a file that cannot be deleted is
    /// left in place and does not stop the sweep.
    /// </summary>
    public void PruneBlurCache(int maxFiles = 24)
    {
        var keep = Math.Max(0, maxFiles);
        PruneDirectory(BlurCachePath, "*-blur.jpg", keep);
        PruneDirectory(RootPath, "*.json", keep);
    }

    private static void PruneDirectory(string directoryPath, string searchPattern, int keep)
    {
        List<FileInfo> candidates;
        try
        {
            var directory = new DirectoryInfo(directoryPath);
            if (!directory.Exists)
            {
                return;
            }

            candidates = directory
                .EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly)
                .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(keep)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in candidates)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void TryDeleteTemporaryFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string GetAnalysisFilePath(string artHash) => Path.Combine(RootPath, artHash + ".json");

    private void Repair(ArtAnalysis analysis)
    {
        analysis.ScrimOpacity = Clamp(analysis.ScrimOpacity, MinScrimOpacity, MaxScrimOpacity);
        analysis.MeanLuminance = Clamp(analysis.MeanLuminance, 0, 1);
        analysis.MastheadLuminance = Clamp(analysis.MastheadLuminance, 0, 1);
        analysis.MastheadP95Luminance = Clamp(analysis.MastheadP95Luminance, 0, 1);
        analysis.CtaLuminance = Clamp(analysis.CtaLuminance, 0, 1);
        analysis.CtaP95Luminance = Clamp(analysis.CtaP95Luminance, 0, 1);
        analysis.DominantHue = Clamp(analysis.DominantHue, -1, 360);
        analysis.DominantHueWeight = Clamp(analysis.DominantHueWeight, 0, 1);
        analysis.Regions = RepairRegions(analysis);

        // The document is untrusted input, so only ever hand back the one path this
        // cache would itself have written. Trusting the stored string would let a
        // tampered entry point the UI at any readable file on the machine.
        var expectedBlurPath = GetBlurFilePath(analysis.ArtHash);
        if (!string.Equals(analysis.BlurredArtPath, expectedBlurPath, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(expectedBlurPath))
        {
            analysis.BlurredArtPath = null;
        }
    }

    private static IReadOnlyList<ArtRegionLuminance> RepairRegions(ArtAnalysis analysis)
    {
        var known = new Dictionary<string, ArtRegionLuminance>(StringComparer.Ordinal);
        foreach (var region in analysis.Regions ?? Array.Empty<ArtRegionLuminance>())
        {
            if (region.RegionId is not ("global" or "masthead" or "notice" or "cta" or "pill") ||
                known.ContainsKey(region.RegionId))
            {
                continue;
            }

            known.Add(
                region.RegionId,
                region with
                {
                    MeanLuminance = Clamp(region.MeanLuminance, 0, 1),
                    P95Luminance = Clamp(region.P95Luminance, 0, 1),
                });
        }

        AddFallback("global", analysis.MeanLuminance, analysis.MeanLuminance);
        AddFallback("masthead", analysis.MastheadLuminance, analysis.MastheadP95Luminance);
        AddFallback("notice", analysis.MeanLuminance, analysis.MeanLuminance);
        AddFallback("cta", analysis.CtaLuminance, analysis.CtaP95Luminance);
        AddFallback("pill", analysis.MeanLuminance, analysis.MeanLuminance);
        return [
            known["global"],
            known["masthead"],
            known["notice"],
            known["cta"],
            known["pill"],
        ];

        void AddFallback(string id, double mean, double p95)
        {
            known.TryAdd(id, new ArtRegionLuminance(id, mean, p95));
        }
    }

    // Math.Clamp propagates NaN, so a corrupt document could smuggle one past a
    // plain clamp; the lower bound doubles as the NaN replacement.
    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value))
        {
            return min;
        }

        return Math.Clamp(value, min, max);
    }

    private static void ThrowIfInvalidHash(string? artHash, string parameterName)
    {
        if (!IsValidHash(artHash))
        {
            throw new ArgumentException(
                "The artwork hash must be 64 lowercase hexadecimal characters.",
                parameterName);
        }
    }

    // Rejecting anything but a bare SHA-256 digest is what keeps a caller-supplied
    // hash from steering either cache path outside its directory.
    private static bool IsValidHash(string? artHash)
    {
        if (artHash is null || artHash.Length != HashLength)
        {
            return false;
        }

        foreach (var character in artHash)
        {
            var isHexDigit = character is >= '0' and <= '9' || character is >= 'a' and <= 'f';
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }
}
