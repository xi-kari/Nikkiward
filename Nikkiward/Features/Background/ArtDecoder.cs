using System.Security.Cryptography;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Nikkiward.Features.Background;

/// <summary>
/// The only place in the backdrop pipeline that touches WinRT imaging.
/// Everything downstream operates on <see cref="ArtPixelBuffer"/> so the
/// analysis stages stay pure and testable.
/// </summary>
public static class ArtDecoder
{
    private const int HashBufferBytes = 128 * 1024;

    /// <summary>
    /// Resolves either a local filesystem path or an <c>ms-appx:///</c> URI to a
    /// <see cref="StorageFile"/>. Returns null when the source cannot be resolved.
    /// </summary>
    public static async Task<StorageFile?> TryResolveAsync(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                !uri.IsFile &&
                uri.Scheme.Length > 1)
            {
                return await StorageFile.GetFileFromApplicationUriAsync(uri);
            }

            return await StorageFile.GetFileFromPathAsync(Path.GetFullPath(source));
        }
        catch (Exception ex) when (ex is FileNotFoundException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// SHA-256 of the file contents, lowercase hex. Streams the file so a large
    /// artwork never lands in memory twice.
    /// </summary>
    public static async Task<string?> TryComputeHashAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
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
                HashBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decodes the artwork once, scaled so its long edge is at most
    /// <paramref name="maxWidth"/> across. This single decode feeds both the
    /// palette sample and the blur bake, per the shared-IO requirement.
    /// </summary>
    public static async Task<ArtPixelBuffer?> DecodeScaledAsync(
        StorageFile file,
        int maxWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (maxWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth));
        }

        using var stream = await file.OpenReadAsync();
        return await DecodeScaledAsync(stream, maxWidth, cancellationToken);
    }

    public static async Task<ArtPixelBuffer?> DecodeScaledAsync(
        IRandomAccessStream stream,
        int maxWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxWidth));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var decoder = await BitmapDecoder.CreateAsync(stream);

        // Oriented dimensions already account for the EXIF rotation we request
        // below; using PixelWidth here would swap the aspect on rotated photos.
        var sourceWidth = (int)decoder.OrientedPixelWidth;
        var sourceHeight = (int)decoder.OrientedPixelHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return null;
        }

        var scale = Math.Min(1.0, maxWidth / (double)sourceWidth);
        var targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));

        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)targetWidth,
            ScaledHeight = (uint)targetHeight,
            InterpolationMode = BitmapInterpolationMode.Fant,
        };

        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
        cancellationToken.ThrowIfCancellationRequested();

        return new ArtPixelBuffer(pixelData.DetachPixelData(), targetWidth, targetHeight);
    }

    /// <summary>
    /// Encodes a buffer to JPEG bytes. Kept separate from the file write so the
    /// caller can use the project's atomic temp-then-move discipline.
    /// </summary>
    public static async Task<byte[]> EncodeJpegAsync(ArtPixelBuffer buffer, double quality = 0.86)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        using var stream = new InMemoryRandomAccessStream();
        var options = new BitmapPropertySet
        {
            { "ImageQuality", new BitmapTypedValue(quality, Windows.Foundation.PropertyType.Single) },
        };
        var encoder = await BitmapEncoder.CreateAsync(
            BitmapEncoder.JpegEncoderId,
            stream,
            options);

        // The depth plate is fully opaque; ignoring alpha keeps the JPEG honest
        // instead of letting the encoder composite against an unknown colour.
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)buffer.Width,
            (uint)buffer.Height,
            96,
            96,
            buffer.Pixels);
        await encoder.FlushAsync();

        var bytes = new byte[stream.Size];
        if (bytes.Length == 0)
        {
            return bytes;
        }

        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)bytes.Length);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
