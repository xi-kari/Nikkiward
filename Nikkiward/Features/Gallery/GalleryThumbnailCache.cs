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

    private static readonly Lazy<string?> CacheFolder = new(ResolveCacheFolder);

    public static string? FolderPath => CacheFolder.Value;

    /// <summary>
    /// Returns the path of a cached JPEG thumbnail for <paramref name="filePath"/>,
    /// generating it on first use. Returns null when the source is unreadable or
    /// the cache folder cannot be created; callers fall back to the full image.
    /// </summary>
    public static async Task<string?> TryGetThumbnailPathAsync(
        string filePath,
        string extension,
        string? galleryRoot,
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
            if (!source.Exists || (source.Length < MinimumSourceBytes && !GalleryFileTypes.UsesScreenshotCodec(extension)))
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

        await DecodeGate.WaitAsync(cancellationToken);
        try
        {
            return await GalleryThumbnailFile.GetOrCreateAsync(folder, galleryRoot, source, async () =>
                GalleryFileTypes.UsesScreenshotCodec(extension)
                    ? await StarwardScreenshotDecoder.RenderThumbnailAsync(filePath, extension, ThumbnailWidth, cancellationToken)
                    : await RenderThumbnailAsync(filePath, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
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

    private static async Task<byte[]?> RenderThumbnailAsync(
        string filePath,
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

            return await ArtDecoder.EncodeJpegAsync(buffer, ThumbnailQuality);
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
        catch (DirectoryNotFoundException)
        {
            return new GalleryThumbnailCacheStatistics(true, 0, 0);
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
        catch (DirectoryNotFoundException)
        {
            return new GalleryThumbnailCacheClearResult(true, 0, 0, 0);
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

    private static string? ResolveCacheFolder()
    {
        try
        {
            return Path.Combine(
                Nikkiward.Services.ApplicationDataPaths.Root,
                "GalleryCache",
                "Thumbnails");
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }
}
