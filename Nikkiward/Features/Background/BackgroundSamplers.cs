using System.Runtime.InteropServices;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Nikkiward.Models;

namespace Nikkiward.Features.Background;

public sealed class StillImageSampler : IBackgroundSampler
{
    public BackgroundSourceKind Kind => BackgroundSourceKind.StillImage;

    public bool CanServe(BackgroundSourceDescriptor descriptor) => descriptor.Kind == Kind;

    public async Task<string?> TryIdentifyAsync(
        BackgroundSourceDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var file = await ArtDecoder.TryResolveAsync(descriptor.Source);
        return file is null || string.IsNullOrWhiteSpace(file.Path)
            ? null
            : await ArtDecoder.TryComputeHashAsync(file.Path, cancellationToken);
    }

    public async Task<ArtPixelBuffer?> SampleAsync(
        BackgroundSourceDescriptor descriptor,
        int targetWidth,
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        var file = await ArtDecoder.TryResolveAsync(descriptor.Source);
        return file is null
            ? null
            : await ArtDecoder.DecodeScaledAsync(file, targetWidth, cancellationToken);
    }

    public async Task<BackgroundSourceValidation> ValidateAsync(
        BackgroundSourceDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var file = await ArtDecoder.TryResolveAsync(descriptor.Source);
        if (file is null)
        {
            return new(false, "图片文件不存在或无法读取。");
        }

        var sample = await ArtDecoder.DecodeScaledAsync(file, 32, cancellationToken);
        return sample is null
            ? new(false, "图片像素无法解码。")
            : BackgroundSourceValidation.Accepted;
    }
}

public sealed class NoneSampler : IBackgroundSampler
{
    private readonly StillImageSampler _stillSampler = new();

    public BackgroundSourceKind Kind => BackgroundSourceKind.None;

    public bool CanServe(BackgroundSourceDescriptor descriptor) => descriptor.Kind == Kind;

    public Task<string?> TryIdentifyAsync(
        BackgroundSourceDescriptor descriptor,
        CancellationToken cancellationToken) =>
        _stillSampler.TryIdentifyAsync(Resolve(), cancellationToken);

    public Task<ArtPixelBuffer?> SampleAsync(
        BackgroundSourceDescriptor descriptor,
        int targetWidth,
        TimeSpan position,
        CancellationToken cancellationToken) =>
        _stillSampler.SampleAsync(Resolve(), targetWidth, position, cancellationToken);

    public Task<BackgroundSourceValidation> ValidateAsync(
        BackgroundSourceDescriptor descriptor,
        CancellationToken cancellationToken) =>
        _stillSampler.ValidateAsync(Resolve(), cancellationToken);

    private static BackgroundSourceDescriptor Resolve() =>
        BackgroundSourceDescriptor.Still(AppearanceProjector.BuiltInBackgroundSource);
}

public sealed class MotionSampler : IBackgroundSampler, IMotionFrameSource
{
    public BackgroundSourceKind Kind => BackgroundSourceKind.Motion;

    public bool CanServe(BackgroundSourceDescriptor descriptor) => descriptor.Kind == Kind;

    public Task<string?> TryIdentifyAsync(
        BackgroundSourceDescriptor descriptor,
        CancellationToken cancellationToken) =>
        ArtDecoder.TryComputeHashAsync(descriptor.Source, cancellationToken);

    private sealed record VideoInspection(BackgroundSourceValidation Validation, MotionFrameReader? ReadFrame = null);

    private sealed class VideoFrames(string path, uint width, uint height)
    {
        private TimeSpan? _duration;

        public async Task<ArtPixelBuffer?> SampleAsync(
            int targetWidth, TimeSpan position, CancellationToken cancellationToken)
        {
            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                var clip = await MediaClip.CreateFromFileAsync(file);
                cancellationToken.ThrowIfCancellationRequested();
                var composition = new MediaComposition();
                composition.Clips.Add(clip);
                var duration = _duration ??= clip.OriginalDuration;
                var safePosition = position < TimeSpan.Zero || position >= duration ? TimeSpan.Zero : position;
                var targetHeight = Math.Max(1, (int)Math.Round(targetWidth * (double)height / width));
                using var stream = await composition.GetThumbnailAsync(
                    safePosition, targetWidth, targetHeight, VideoFramePrecision.NearestFrame);
                cancellationToken.ThrowIfCancellationRequested();
                return await ArtDecoder.DecodeScaledAsync(stream, targetWidth, cancellationToken);
            }
            catch (Exception ex) when (ex is
                FileNotFoundException or
                UnauthorizedAccessException or
                IOException or
                ArgumentException or
                NotSupportedException or
                InvalidOperationException or
                COMException)
            {
                return null;
            }
        }
    }

    private static async Task<VideoInspection> ValidateAsync(string? source, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new(new(false, "没有可用的视频背景文件。"));
        }

        try
        {
            var path = Path.GetFullPath(source);
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var properties = await file.GetBasicPropertiesAsync();
            var profile = await MediaEncodingProfile.CreateFromFileAsync(file);
            cancellationToken.ThrowIfCancellationRequested();
            if (profile.Video is null)
            {
                return new(new(false, "所选文件不包含可播放的视频轨道。"));
            }

            var rate = profile.Video.FrameRate;
            var framesPerSecond = rate.Denominator == 0
                ? 0
                : rate.Numerator / (double)rate.Denominator;
            var rules = MotionSourceRules.Validate(
                new MotionSourceFacts(
                    Path.GetExtension(path),
                    properties.Size,
                    profile.Video.Width,
                    profile.Video.Height,
                    framesPerSecond,
                    profile.Video.Subtype,
                    profile.Audio?.Subtype));
            if (!rules.IsUsable)
            {
                return new(rules);
            }

            var codecs = new CodecQuery();
            var videoDecoders = await codecs.FindAllAsync(
                CodecKind.Video,
                CodecCategory.Decoder,
                profile.Video.Subtype);
            cancellationToken.ThrowIfCancellationRequested();
            if (videoDecoders.Count == 0)
            {
                return new(new(false, $"系统没有可用于视频编码 {profile.Video.Subtype} 的解码器。"));
            }

            var frames = new VideoFrames(path, profile.Video.Width, profile.Video.Height);
            return new(BackgroundSourceValidation.Accepted, frames.SampleAsync);
        }
        catch (Exception ex) when (ex is
            FileNotFoundException or
            UnauthorizedAccessException or
            IOException or
            ArgumentException or
            NotSupportedException or
            InvalidOperationException or
            COMException)
        {
            return new(new(false, "视频文件不存在、不可读取、容器不受支持或缺少系统解码器。"));
        }
    }

    public async Task<BackgroundSourceValidation> ValidateAsync(
        BackgroundSourceDescriptor descriptor, CancellationToken cancellationToken) =>
        (await ValidateAsync(CanServe(descriptor) ? descriptor.Source : null, cancellationToken)).Validation;

    async Task<MotionFrameReader?> IMotionFrameSource.OpenFrameReaderAsync(
        BackgroundSourceDescriptor descriptor, CancellationToken cancellationToken) =>
        (await ValidateAsync(CanServe(descriptor) ? descriptor.Source : null, cancellationToken)).ReadFrame;

    public async Task<ArtPixelBuffer?> SampleAsync(
        BackgroundSourceDescriptor descriptor, int targetWidth, TimeSpan position, CancellationToken cancellationToken)
    {
        var video = await ValidateAsync(CanServe(descriptor) ? descriptor.Source : null, cancellationToken);
        return video.ReadFrame is null ? null : await video.ReadFrame(targetWidth, position, cancellationToken);
    }
}

public sealed class MotionBackgroundImporter
{
    private readonly MotionSampler _sampler;
    private readonly string _rootPath;

    private readonly Func<IReadOnlyList<string>> _protectedRoots;

    public MotionBackgroundImporter(Func<IReadOnlyList<string>> protectedRoots, MotionSampler? sampler = null, string? localDataRoot = null)
    {
        _protectedRoots = protectedRoots;
        _sampler = sampler ?? new MotionSampler();
        var applicationRoot = string.IsNullOrWhiteSpace(localDataRoot)
            ? Nikkiward.Services.ApplicationDataPaths.Root
            : Path.Combine(
                Path.GetFullPath(localDataRoot),
                "Nikkiward");
        _rootPath = Path.Combine(applicationRoot, "MotionBackgrounds");
    }

    public async Task<(BackgroundSourceValidation Validation, string? ImportedPath)> ImportAsync(
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
                return (new(false, "视频文件不存在或无法读取。"), null);
            }

            if (sourceInfo.Length <= 0)
            {
                return (new(false, "视频背景文件不能为空。"), null);
            }

            var destination = WallpaperImportDirectory.Prepare(_rootPath, _protectedRoots());
            if (!destination.IsUsable) return (destination, null);
        }
        catch (Exception ex) when (ex is
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException)
        {
            return (new(false, "视频文件不存在或无法读取。"), null);
        }

        var temporary = Path.Combine(_rootPath, $"{Guid.NewGuid():N}{extension}");
        try
        {
            await MotionImportFileCopier.CopyWithRetryAsync(
                fullSourcePath,
                temporary,
                cancellationToken);
            var descriptor = BackgroundSourceDescriptor.Motion(temporary);
            var hash = await _sampler.TryIdentifyAsync(descriptor, cancellationToken);
            if (hash is null)
            {
                return (new(false, "无法计算视频背景的文件标识。"), null);
            }

            var target = Path.Combine(_rootPath, $"{hash}{extension}");
            if (File.Exists(target))
            {
                return (BackgroundSourceValidation.Accepted, target);
            }

            var validation = await _sampler.ValidateAsync(descriptor, cancellationToken);
            if (!validation.IsUsable)
            {
                return (validation, null);
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
        catch (IOException)
        {
            return (new(false, "视频文件正被其他程序占用，暂时无法读取。"), null);
        }
        catch (UnauthorizedAccessException)
        {
            return (new(false, "没有权限读取所选视频文件。"), null);
        }
        finally
        {
            TryDelete(temporary);
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
