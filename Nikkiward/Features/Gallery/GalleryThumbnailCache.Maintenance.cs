using Nikkiward.Services;

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

public static partial class GalleryThumbnailCache
{
    private static readonly int DecodeConcurrency =
        Math.Max(2, Environment.ProcessorCount / 2);

    private static readonly SemaphoreSlim DecodeGate = new(
        DecodeConcurrency,
        DecodeConcurrency);

    private static readonly SemaphoreSlim CacheMaintenanceGate = new(1, 1);

    private static readonly Lazy<string?> CacheFolder = new(ResolveCacheFolder);

    public static string? FolderPath => CacheFolder.Value;

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

    internal static async Task<GalleryThumbnailCacheClearResult> ClearAsync(
        string folder, IReadOnlyList<string> protectedRoots, CancellationToken cancellationToken)
    {
        var acquiredPermits = await EnterMaintenanceAsync(cancellationToken);
        try
        {
            return await Task.Run(
                () => ClearCache(PhotoDirectoryPolicy.ThumbnailFolder(folder, protectedRoots), cancellationToken),
                cancellationToken);
        }
        finally
        {
            ExitMaintenance(acquiredPermits);
        }
    }

    private static IEnumerable<string> CacheEntries(string folder)
    {
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        return Directory.EnumerateFiles(folder, "*", options)
            .Where(path => GalleryThumbnailFile.IsCacheFileName(Path.GetFileName(path)));
    }

    internal static GalleryThumbnailCacheStatistics ReadStatistics(
        string folder,
        CancellationToken cancellationToken)
    {
        try
        {
            var fileCount = 0;
            long totalBytes = 0;
            foreach (var path in CacheEntries(folder))
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
            foreach (var path in CacheEntries(folder))
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
