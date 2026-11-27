using System.Security.Cryptography;
using Nikkiward.Features.Gallery;

namespace Nikkiward.Features.Background;

public sealed class WallpaperAssetImporter
{
    private const long MaximumStillBytes = 512L * 1024 * 1024;
    private const int FrameWidth = 4096;
    private readonly string _stillRootPath;
    private readonly WallpaperPackageCache _packageCache;
    private readonly Func<IReadOnlyList<string>> _protectedRoots;

    public WallpaperAssetImporter(Func<IReadOnlyList<string>> protectedRoots, string? localDataRoot = null)
    {
        var applicationRoot = string.IsNullOrWhiteSpace(localDataRoot)
            ? Nikkiward.Services.ApplicationDataPaths.Root
            : Path.Combine(
                Path.GetFullPath(localDataRoot),
                "Nikkiward");
        _protectedRoots = protectedRoots;
        _stillRootPath = Path.Combine(applicationRoot, "WallpaperImports", "Still");
        _packageCache = new WallpaperPackageCache(Path.Combine(applicationRoot, "WallpaperImports", "Packages"));
    }

    public async Task<(BackgroundSourceValidation Validation, string? ImportedPath)> ImportPackageAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        return await _packageCache.ImportAsync(sourcePath, cancellationToken, _protectedRoots());
    }

    public async Task<(BackgroundSourceValidation Validation, string? ImportedPath)> ImportStillAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        string fullSourcePath;
        string extension;
        try
        {
            fullSourcePath = Path.GetFullPath(sourcePath);
            extension = Path.GetExtension(fullSourcePath).ToLowerInvariant();
            var sourceInfo = new FileInfo(fullSourcePath);
            if (!sourceInfo.Exists)
            {
                return (new(false, "图片文件不存在或无法读取。"), null);
            }

            if (sourceInfo.Length <= 0 || sourceInfo.Length > MaximumStillBytes)
            {
                return (new(false, "图片文件大小超出支持范围。"), null);
            }

            var destination = WallpaperImportDirectory.Prepare(_stillRootPath, _protectedRoots());
            if (!destination.IsUsable) return (destination, null);
        }
        catch (Exception ex) when (ex is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return (new(false, "图片文件不存在或无法读取。"), null);
        }

        if (extension == ".jxl")
        {
            var preview = await CachePreviewAsync(fullSourcePath, false, cancellationToken);
            return preview is null ? (new(false, "无法解码 JPEG XL 背景。"), null) : (BackgroundSourceValidation.Accepted, preview);
        }

        var temporary = Path.Combine(
            _stillRootPath,
            $"{Guid.NewGuid():N}{extension}");
        try
        {
            await MotionImportFileCopier.CopyWithRetryAsync(
                fullSourcePath,
                temporary,
                cancellationToken);
            var hash = await ComputeHashAsync(temporary, cancellationToken);
            var target = Path.Combine(_stillRootPath, $"{hash}{extension}");
            if (File.Exists(target))
            {
                return (BackgroundSourceValidation.Accepted, target);
            }

            try
            {
                File.Move(temporary, target, overwrite: false);
            }
            catch (IOException) when (File.Exists(target))
            {
                return (BackgroundSourceValidation.Accepted, target);
            }

            return (BackgroundSourceValidation.Accepted, target);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return (new(false, "图片文件正被其他程序占用，暂时无法读取。"), null);
        }
        catch (UnauthorizedAccessException)
        {
            return (new(false, "没有权限读取所选图片文件。"), null);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    internal async Task<string?> CachePreviewAsync(string path, bool video, CancellationToken token)
    {
        var destination = WallpaperImportDirectory.Prepare(_stillRootPath, _protectedRoots());
        if (!destination.IsUsable) throw new IOException(destination.RejectReason);
        var file = new FileInfo(path);
        var cached = await GalleryThumbnailFile.GetOrCreateAsync(_stillRootPath, null, file, false, async () =>
        {
            if (GalleryFileTypes.UsesScreenshotCodec(file.Extension))
                return await StarwardScreenshotDecoder.RenderThumbnailAsync(path, file.Extension, FrameWidth, token);
            IBackgroundSampler sampler = video ? new MotionSampler() : new StillImageSampler();
            var frame = await sampler.SampleAsync(video ? BackgroundSourceDescriptor.Motion(path) : BackgroundSourceDescriptor.Still(path),
                FrameWidth, TimeSpan.Zero, token);
            return frame == null ? null : await ArtDecoder.EncodeJpegAsync(frame, 0.95);
        }, token);
        return cached?.FilePath;
    }

    public async Task<(BackgroundSourceValidation Validation, string? ImportedPath)> ImportMotionFrameAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var sampler = new MotionSampler();
        var sample = await sampler.SampleAsync(
            BackgroundSourceDescriptor.Motion(sourcePath),
            FrameWidth,
            TimeSpan.Zero,
            cancellationToken);
        if (sample is null)
        {
            return (new(false, "无法从视频壁纸生成光栅卡片预览帧。"), null);
        }

        var encoded = await ArtDecoder.EncodeJpegAsync(sample, 0.95);
        if (encoded.Length == 0)
        {
            return (new(false, "视频壁纸预览帧为空。"), null);
        }

        var destination = WallpaperImportDirectory.Prepare(_stillRootPath, _protectedRoots());
        if (!destination.IsUsable) return (destination, null);
        var hash = Convert.ToHexString(
            SHA256.HashData(encoded)).ToLowerInvariant();
        var target = Path.Combine(_stillRootPath, $"{hash}.jpg");
        if (!File.Exists(target))
        {
            var temporary = Path.Combine(
                _stillRootPath,
                $"{Guid.NewGuid():N}.jpg");
            try
            {
                await File.WriteAllBytesAsync(temporary, encoded, cancellationToken);
                try
                {
                    File.Move(temporary, target, overwrite: false);
                }
                catch (IOException) when (File.Exists(target))
                {
                }
            }
            finally
            {
                TryDelete(temporary);
            }
        }

        return (BackgroundSourceValidation.Accepted, target);
    }

    private static async Task<string> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
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

    private static async Task<(BackgroundSourceValidation Validation, string? ImportedPath)> ImportManagedFileAsync(
        string sourcePath,
        string destinationRoot,
        long maximumBytes,
        string requiredExtension,
        string displayType,
        CancellationToken cancellationToken = default)
    {
        string fullSourcePath;
        try
        {
            fullSourcePath = Path.GetFullPath(sourcePath);
            var sourceInfo = new FileInfo(fullSourcePath);
            if (!sourceInfo.Exists ||
                sourceInfo.Length <= 0 ||
                sourceInfo.Length > maximumBytes ||
                !string.Equals(
                    Path.GetExtension(fullSourcePath),
                    requiredExtension,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (new(false, $"{displayType}不存在或超出支持范围。"), null);
            }

            Directory.CreateDirectory(destinationRoot);
        }
        catch (Exception ex) when (ex is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return (new(false, $"{displayType}不存在或无法读取。"), null);
        }

        var temporary = Path.Combine(
            destinationRoot,
            $"{Guid.NewGuid():N}{requiredExtension}");
        try
        {
            await MotionImportFileCopier.CopyWithRetryAsync(
                fullSourcePath,
                temporary,
                cancellationToken);
            var hash = await ComputeHashAsync(temporary, cancellationToken);
            var target = Path.Combine(destinationRoot, $"{hash}{requiredExtension}");
            if (File.Exists(target))
            {
                return (BackgroundSourceValidation.Accepted, target);
            }

            try
            {
                File.Move(temporary, target, overwrite: false);
            }
            catch (IOException) when (File.Exists(target))
            {
            }

            return (BackgroundSourceValidation.Accepted, target);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return (new(false, $"{displayType}正被其他程序占用，暂时无法读取。"), null);
        }
        catch (UnauthorizedAccessException)
        {
            return (new(false, $"没有权限读取所选{displayType}。"), null);
        }
        finally
        {
            TryDelete(temporary);
        }
    }
}
