using Nikkiward.Features.Background;

namespace Nikkiward.Features.Gallery;

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
public static partial class GalleryThumbnailCache
{
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

    internal static async Task<GalleryThumbnail?> TryGetThumbnailAsync(
        string filePath,
        string extension,
        string? galleryRoot,
        CancellationToken cancellationToken = default, bool memoryOnly = false)
    {
        var folder = memoryOnly ? null : CacheFolder.Value;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var usesScreenshotCodec = GalleryFileTypes.UsesScreenshotCodec(extension);
        FileInfo source;
        try
        {
            source = new FileInfo(filePath);
            if (!source.Exists || (source.Length < MinimumSourceBytes && !usesScreenshotCodec))
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
            return await GalleryThumbnailFile.GetOrCreateAsync(folder, galleryRoot, source, memoryOnly || usesScreenshotCodec, async () =>
                usesScreenshotCodec
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

}
