using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Nikkiward.Services;
using Starward.Codec.AVIF;
using Windows.Graphics.Imaging;

namespace Nikkiward.Features.Gallery;

internal static class StarwardScreenshotDecoder
{
    public static Task<SoftwareBitmap> ReadBitmapAsync(string path, string extension, CancellationToken token) =>
        GalleryScreenshotDecoder.RenderAsync(path, extension, Open, (width, height, rgba) =>
        {
            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(rgba.AsBuffer(), BitmapPixelFormat.Rgba8,
                (int)width, (int)height, BitmapAlphaMode.Straight);
            return Task.FromResult(SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied));
        }, token);

    public static Task<byte[]> RenderThumbnailAsync(string path, string extension, int thumbnailEdge, CancellationToken token) =>
        GalleryScreenshotDecoder.RenderAsync(path, extension, Open,
            (width, height, rgba) => EncodeAsync(width, height, rgba, thumbnailEdge, BitmapEncoder.JpegEncoderId, token), token);

    public static Task<byte[]> RenderPngAsync(string path, string extension, CancellationToken token) =>
        GalleryScreenshotDecoder.RenderAsync(path, extension, Open,
            (width, height, rgba) => EncodeAsync(width, height, rgba, int.MaxValue, BitmapEncoder.PngEncoderId, token), token);

    private static GalleryScreenshotDecoder Open(byte[] data, string extension) =>
        extension.Equals(".avif", StringComparison.OrdinalIgnoreCase)
            ? new Avif(avifDecoderLite.Create(data))
            : new GalleryJpegXlDecoder(data);

    private sealed class Avif(avifDecoderLite decoder) : GalleryScreenshotDecoder
    {
        public override uint Width => decoder.Width;
        public override uint Height => decoder.Height;
        public override void Dispose() => decoder.Dispose();

        public override byte[] ReadPixels(CancellationToken token)
        {
            var pq = decoder.TransferCharacteristics == avifTransferCharacteristics.PQ;
            var hlg = decoder.TransferCharacteristics == avifTransferCharacteristics.HLG;
            using var image = decoder.GetNextImage();
            using var rgb = image.ToRGBImage(pq || hlg ? 16u : 8u, avifRGBFormat.RGBA);
            return pq || hlg
                ? ToneMap(rgb.GetPixelBytes(), pq ? PqToLinear.Value : HlgToLinear.Value,
                    decoder.ColorPrimaries == avifColorPrimaries.BT2020, token)
                : rgb.GetPixelBytes().ToArray();
        }
    }

    private static readonly Lazy<float[]> PqToLinear = new(() => BuildTransferTable(pq: true));
    private static readonly Lazy<float[]> HlgToLinear = new(() => BuildTransferTable(pq: false));

    private static float[] BuildTransferTable(bool pq)
    {
        var values = new float[ushort.MaxValue + 1];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = ToLinear((ushort)i, pq);
        }

        return values;
    }

    private static byte[] ToneMap(ReadOnlySpan<byte> bytes, float[] linear, bool bt2020, CancellationToken token)
    {
        var channels = MemoryMarshal.Cast<byte, ushort>(bytes);
        var pixels = new byte[channels.Length];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if ((i & 0xffff) == 0) token.ThrowIfCancellationRequested();
            var r = linear[channels[i]];
            var g = linear[channels[i + 1]];
            var b = linear[channels[i + 2]];
            if (bt2020)
            {
                (r, g, b) = (1.66049f*r - 0.58764f*g - 0.07285f*b,
                    -0.12455f*r + 1.13290f*g - 0.00835f*b,
                    -0.01815f*r - 0.10058f*g + 1.11873f*b);
            }

            pixels[i] = ScreenshotImageEncoder.LinearToSrgbByte(r);
            pixels[i + 1] = ScreenshotImageEncoder.LinearToSrgbByte(g);
            pixels[i + 2] = ScreenshotImageEncoder.LinearToSrgbByte(b);
            pixels[i + 3] = (byte)(channels[i + 3] >> 8);
        }
        return pixels;
    }

    private static float ToLinear(ushort value, bool pq)
    {
        var signal = value / (double)ushort.MaxValue;
        if (!pq)
        {
            return (float)(signal <= 0.5 ? signal * signal / 3 :
                (Math.Exp((signal - 0.55991073) / 0.17883277) + 0.28466892) / 12) * 12.5f;
        }

        // Inverse ST 2084, in scRGB units (80 nits per unit).
        var p = Math.Pow(signal, 32.0 / 2523);
        return (float)(125 * Math.Pow(Math.Max(p - 3424.0 / 4096, 0) /
            (2413.0 / 128 - 2392.0 / 128 * p), 16384.0 / 2610));
    }

    private static async Task<byte[]> EncodeAsync(uint width, uint height, byte[] rgba, int thumbnailEdge,
        Guid codec, CancellationToken token)
    {
        using var memory = new MemoryStream();
        using var stream = memory.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(codec, stream);
        encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, width, height, 96, 96, rgba);
        if (Math.Max(width, height) > thumbnailEdge)
        {
            var scale = thumbnailEdge / (double)Math.Max(width, height);
            encoder.BitmapTransform.ScaledWidth = (uint)Math.Max(1, Math.Round(width * scale));
            encoder.BitmapTransform.ScaledHeight = (uint)Math.Max(1, Math.Round(height * scale));
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        await encoder.FlushAsync().AsTask(token);
        return memory.ToArray();
    }
}
