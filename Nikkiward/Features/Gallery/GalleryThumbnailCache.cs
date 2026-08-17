using System.Security.Cryptography;
using System.Text;
using Nikkiward.Features.Background;

namespace Nikkiward.Features.Gallery;

public sealed record GalleryThumbnailCacheStatistics(
    bool IsAvailable,
    int FileCount,
    long TotalBytes);

public sealed record GalleryThumbnailCacheClearResult(
    bool IsAvailable,
    int DeletedFileCount,
    long DeletedBytes,
    int FailedFileCount);

/// <summary>
/// Disk cache for gallery thumbnails. Without it every scroll re-decodes a full
/// 4K photo, because a <c>BitmapImage</c> with a <c>UriSource</c> caches nothing
/// across sessions.
/// </summary>
/// <remarks>
/// The cache-key and atomic-write discipline follow Starward 0.18.1
/// (MIT, Copyright (c) 2023 Scighost), but the decode runs through
/// <see cref="ArtDecoder"/> on WIC rather than Win2D, and the key includes the
/// source mtime and length so editing a photo in place cannot serve a stale
/// thumbnail — upstream keys on the path alone and never invalidates.
/// </remarks>
public static class GalleryThumbnailCache
{
    private static readonly int DecodeConcurrency =
        Math.Max(2, Environment.ProcessorCount / 2);

    /// <summary>
    /// Long-edge budget for a cached thumbnail. The grid asks for 360 logical
    /// pixels, so this leaves room for a 2x display without a second decode.
    /// </summary>
    private const int ThumbnailWidth = 720;

    private const double ThumbnailQuality = 0.75;

    /// <summary>
    /// Enough header bytes to be a real image. Guards against a zero-length or
    /// truncated file, which upstream turns into an unhandled exception.
    /// </summary>
    private const long MinimumSourceBytes = 64;

    private static readonly SemaphoreSlim DecodeGate = new(
        DecodeConcurrency,
        DecodeConcurrency);

    private static readonly SemaphoreSlim CacheMaintenanceGate = new(1, 1);

    private static readonly Lazy<string?> CacheFolder = new(CreateCacheFolder);

    public static string? FolderPath => CacheFolder.Value;

    /// <summary>
    /// Returns the path of a cached JPEG thumbnail for <paramref name="filePath"/>,
    /// generating it on first use. Returns null when the source is unreadable or
    /// the cache folder cannot be created; callers fall back to the full image.
    /// </summary>
    public static async Task<string?> TryGetThumbnailPathAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var folder = CacheFolder.Value;
        if (folder is null || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        FileInfo source;
        try
        {
            source = new FileInfo(filePath);
            if (!source.Exists || source.Length < MinimumSourceBytes)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }

        var cachePath = Path.Combine(folder, BuildCacheFileName(source));
        await DecodeGate.WaitAsync(cancellationToken);
        try
        {
            // A sibling call may have produced it while this one queued.
            if (File.Exists(cachePath))
            {
                return cachePath;
            }

            return await RenderThumbnailAsync(source.FullName, cachePath, cancellationToken);
        }
        finally
        {
            DecodeGate.Release();
        }
    }

    public static async Task<GalleryThumbnailCacheStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var folder = CacheFolder.Value;
        if (folder is null)
        {
            return new GalleryThumbnailCacheStatistics(false, 0, 0);
        }

        var acquiredPermits = await EnterMaintenanceAsync(cancellationToken);
        try
        {
            return await Task.Run(
                () => ReadStatistics(folder, cancellationToken),
                cancellationToken);
        }
        finally
        {
            ExitMaintenance(acquiredPermits);
        }
    }

    public static async Task<GalleryThumbnailCacheClearResult> ClearAsync(
        CancellationToken cancellationToken = default)
    {
        var folder = CacheFolder.Value;
        if (folder is null)
        {
            return new GalleryThumbnailCacheClearResult(false, 0, 0, 0);
        }

        var acquiredPermits = await EnterMaintenanceAsync(cancellationToken);
        try
        {
            return await Task.Run(
                () => ClearCache(folder, cancellationToken),
                cancellationToken);
        }
        finally
        {
            ExitMaintenance(acquiredPermits);
        }
    }

    private static async Task<string?> RenderThumbnailAsync(
        string filePath,
        string cachePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var file = await ArtDecoder.TryResolveAsync(filePath);
            if (file is null)
            {
                return null;
            }

            var buffer = await ArtDecoder.DecodeScaledAsync(
                file,
                ThumbnailWidth,
                cancellationToken);
            if (buffer is null)
            {
                return null;
            }

            var bytes = await ArtDecoder.EncodeJpegAsync(buffer, ThumbnailQuality);
            if (bytes.Length == 0)
            {
                return null;
            }

            // Temp-then-move so a cancelled or crashed render never leaves a
            // half-written JPEG that later looks like a valid cache hit.
            var tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
            try
            {
                File.Move(tempPath, cachePath, overwrite: true);
            }
            catch (IOException)
            {
                TryDelete(tempPath);
                return File.Exists(cachePath) ? cachePath : null;
            }

            return cachePath;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A single unreadable or unsupported photo must not take the grid
            // down; the caller shows the original image instead.
            return null;
        }
    }

    /// <summary>
    /// Keyed on path plus mtime plus length, so replacing a photo in place
    /// produces a different key rather than a stale hit.
    /// </summary>
    private static string BuildCacheFileName(FileInfo source)
    {
        var key = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{source.FullName.ToLowerInvariant()}|{source.LastWriteTimeUtc.Ticks}|{source.Length}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return $"{Convert.ToHexString(hash).ToLowerInvariant()[..32]}.jpg";
    }

    private static GalleryThumbnailCacheStatistics ReadStatistics(
        string folder,
        CancellationToken cancellationToken)
    {
        try
        {
            var fileCount = 0;
            long totalBytes = 0;
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    totalBytes += new FileInfo(path).Length;
                    fileCount++;
                }
                catch (Exception ex) when (ex is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException)
                {
                }
            }

            return new GalleryThumbnailCacheStatistics(true, fileCount, totalBytes);
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
            return new GalleryThumbnailCacheStatistics(false, 0, 0);
        }
    }

    private static GalleryThumbnailCacheClearResult ClearCache(
        string folder,
        CancellationToken cancellationToken)
    {
        var deletedFileCount = 0;
        long deletedBytes = 0;
        var failedFileCount = 0;

        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                long length = 0;
                try
                {
                    length = new FileInfo(path).Length;
                    File.Delete(path);
                    deletedFileCount++;
                    deletedBytes += length;
                }
                catch (Exception ex) when (ex is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or NotSupportedException)
                {
                    failedFileCount++;
                }
            }

            return new GalleryThumbnailCacheClearResult(
                true,
                deletedFileCount,
                deletedBytes,
                failedFileCount);
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
            return new GalleryThumbnailCacheClearResult(
                false,
                deletedFileCount,
                deletedBytes,
                failedFileCount);
        }
    }

    private static async Task<int> EnterMaintenanceAsync(CancellationToken cancellationToken)
    {
        await CacheMaintenanceGate.WaitAsync(cancellationToken);
        var acquiredPermits = 0;
        try
        {
            while (acquiredPermits < DecodeConcurrency)
            {
                await DecodeGate.WaitAsync(cancellationToken);
                acquiredPermits++;
            }

            return acquiredPermits;
        }
        catch
        {
            if (acquiredPermits > 0)
            {
                DecodeGate.Release(acquiredPermits);
            }

            CacheMaintenanceGate.Release();
            throw;
        }
    }

    private static void ExitMaintenance(int acquiredPermits)
    {
        if (acquiredPermits > 0)
        {
            DecodeGate.Release(acquiredPermits);
        }

        CacheMaintenanceGate.Release();
    }

    private static string? CreateCacheFolder()
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Nikkiward",
                "GalleryCache",
                "Thumbnails");
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
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
