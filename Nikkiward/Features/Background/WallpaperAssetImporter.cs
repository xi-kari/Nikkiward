using System.Security.Cryptography;

namespace Nikkiward.Features.Background;

public sealed class WallpaperAssetImporter
{
    private const long MaximumStillBytes = 512L * 1024 * 1024;
    private const int FrameWidth = 4096;
    private readonly string _stillRootPath;
    private readonly WallpaperPackageCache _packageCache;

    public WallpaperAssetImporter(string? localDataRoot = null)
    {
        var applicationRoot = string.IsNullOrWhiteSpace(localDataRoot)
            ? Nikkiward.Services.ApplicationDataPaths.Root
            : Path.Combine(
                Path.GetFullPath(localDataRoot),
                "Nikkiward");
        _stillRootPath = Path.Combine(applicationRoot, "WallpaperImports", "Still");
        _packageCache = new WallpaperPackageCache(
            Path.Combine(applicationRoot, "WallpaperImports", "Packages"));
    }

    public async Task<(BackgroundSourceValidation Validation, string? ImportedPath)> ImportPackageAsync(
        string sourcePath,
        CancellationToken cancellationToken) =>
        await _packageCache.ImportAsync(sourcePath, cancellationToken);

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

            Directory.CreateDirectory(_stillRootPath);
        }
        catch (Exception ex) when (ex is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return (new(false, "图片文件不存在或无法读取。"), null);
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

        Directory.CreateDirectory(_stillRootPath);
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
