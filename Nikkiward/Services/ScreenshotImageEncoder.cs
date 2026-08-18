using Microsoft.Graphics.Canvas;
using Starward.Codec.AVIF;
using Starward.Codec.JpegXL;
using Starward.Codec.JpegXL.CMS;
using Starward.Codec.JpegXL.CodeStream;
using Starward.Codec.JpegXL.Encode;
using Starward.Codec.PNG;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;

namespace Nikkiward.Services;

internal static class ScreenshotImageEncoder
{
    public static async Task EncodeSdrAsync(
        CanvasBitmap bitmap,
        Stream destination,
        string extension,
        int quality,
        bool writeColorProfile,
        byte[] xmpData)
    {
        using var normalized = CopyToRgba8(bitmap);
        var pixels = normalized.GetPixelBytes();
        var width = normalized.SizeInPixels.Width;
        var height = normalized.SizeInPixels.Height;
        switch (extension)
        {
            case ".png":
                await EncodePngAsync(
                    destination,
                    width,
                    height,
                    pixels,
                    writeColorProfile,
                    xmpData).ConfigureAwait(false);
                break;
            case ".avif":
                await EncodeAvifAsync(
                    destination,
                    width,
                    height,
                    pixels,
                    quality,
                    writeColorProfile,
                    hdr: false,
                    xmpData).ConfigureAwait(false);
                break;
            case ".jxl":
                await EncodeJpegXlAsync(
                    destination,
                    width,
                    height,
                    pixels,
                    quality,
                    writeColorProfile,
                    xmpData).ConfigureAwait(false);
                break;
            default:
                throw new NotSupportedException($"Unsupported screenshot extension: {extension}");
        }
    }

    public static async Task EncodeHdrAvifAsync(
        CanvasBitmap bitmap,
        Stream destination,
        int quality,
        byte[] xmpData)
    {
        if (bitmap.Format is not DirectXPixelFormat.R16G16B16A16Float)
        {
            throw new ArgumentException("HDR AVIF encoding requires an R16G16B16A16Float bitmap.", nameof(bitmap));
        }

        var pqPixels = ConvertScRgbToPqBt2020(bitmap.GetPixelBytes());
        await EncodeAvifAsync(
            destination,
            bitmap.SizeInPixels.Width,
            bitmap.SizeInPixels.Height,
            pqPixels,
            quality,
            writeColorProfile: true,
            hdr: true,
            xmpData).ConfigureAwait(false);
    }

    public static async Task EncodeHdrJpegXlAsync(
        CanvasBitmap bitmap,
        Stream destination,
        int quality,
        float maxLuminance,
        byte[] xmpData)
    {
        if (bitmap.Format is not DirectXPixelFormat.R16G16B16A16Float)
        {
            throw new ArgumentException("HDR JPEG XL encoding requires an R16G16B16A16Float bitmap.", nameof(bitmap));
        }

        var pixels = ClampScRgbHalfPixels(bitmap.GetPixelBytes());
        var format = JxlPixelFormat.R16G16B16A16Float;
        var lossless = quality >= 100;
        await Task.Run(() =>
        {
            using var encoder = new JxlEncoder
            {
                RunnerThreads = (uint)GetSuggestedThreads(),
            };
            var info = new JxlBasicInfo(
                bitmap.SizeInPixels.Width,
                bitmap.SizeInPixels.Height,
                format,
                true)
            {
                UsesOriginalProfile = lossless,
                IntensityTarget = Math.Max(80, maxLuminance),
            };
            encoder.SetBasicInfo(info);
            var colorEncoding = new JxlColorEncoding
            {
                Primaries = (JxlPrimaries)1,
                WhitePoint = JxlWhitePoint.D65,
                TransferFunction = JxlTransferFunction.Linear,
            };
            encoder.SetColorEncoding(colorEncoding);
            encoder.AddBox(JxlBoxType.XMP, xmpData, false);
            var frameSettings = encoder.CreateFrameSettings();
            frameSettings.Distance = QualityToJpegXlDistance(quality);
            frameSettings.Lossless = lossless;
            frameSettings.AddImageFrame(format, pixels);
            encoder.Encode(destination);
        }).ConfigureAwait(false);
    }

    public static byte[] ToneMapHdrToSdr(CanvasBitmap bitmap)
    {
        if (bitmap.Format is not DirectXPixelFormat.R16G16B16A16Float)
        {
            throw new ArgumentException("HDR tone mapping requires an R16G16B16A16Float bitmap.", nameof(bitmap));
        }

        var source = bitmap.GetPixelBytes();
        var destination = new byte[source.Length / 2];
        for (var sourceOffset = 0; sourceOffset < source.Length; sourceOffset += 8)
        {
            var destinationOffset = sourceOffset / 2;
            destination[destinationOffset] = LinearToSrgbByte(ReadHalf(source, sourceOffset));
            destination[destinationOffset + 1] = LinearToSrgbByte(ReadHalf(source, sourceOffset + 2));
            destination[destinationOffset + 2] = LinearToSrgbByte(ReadHalf(source, sourceOffset + 4));
            destination[destinationOffset + 3] = 255;
        }

        return destination;
    }

    private static CanvasRenderTarget CopyToRgba8(CanvasBitmap source)
    {
        var target = new CanvasRenderTarget(
            CanvasDevice.GetSharedDevice(),
            source.SizeInPixels.Width,
            source.SizeInPixels.Height,
            96,
            DirectXPixelFormat.R8G8B8A8UIntNormalized,
            CanvasAlphaMode.Premultiplied);
        using var drawingSession = target.CreateDrawingSession();
        drawingSession.DrawImage(source);
        return target;
    }

    private static async Task EncodePngAsync(
        Stream destination,
        uint width,
        uint height,
        byte[] rgbaPixels,
        bool writeColorProfile,
        byte[] xmpData)
    {
        using var memory = new MemoryStream();
        var encoder = await BitmapEncoder.CreateAsync(
            BitmapEncoder.PngEncoderId,
            memory.AsRandomAccessStream());
        encoder.SetPixelData(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Premultiplied,
            width,
            height,
            96,
            96,
            rgbaPixels);
        await encoder.FlushAsync();

        PngChunk? colorChunk = null;
        PngChunk? srgbChunk = null;
        if (writeColorProfile)
        {
            colorChunk = new PngChunk(4, PngChunkType.cICP);
            ref var cicp = ref colorChunk.GetcICPChunk();
            cicp.ColorPrimaries = 1;
            cicp.TransferFunction = 13;
            cicp.MatrixCoefficients = 0;
            cicp.FullRangeFlag = 1;
            colorChunk.UpdateCrc32();
            srgbChunk = new PngChunk(1, PngChunkType.sRGB);
        }

        var xmpContent = new byte[22 + xmpData.Length];
        "XML:com.adobe.xmp"u8.CopyTo(xmpContent);
        xmpData.CopyTo(xmpContent.AsSpan(22));
        var xmpChunk = new PngChunk(PngChunkType.iTXt, xmpContent);

        destination.Write(PngReader.PngSignature);
        memory.Position = 0;
        using var reader = new PngReader(memory);
        var metadataWritten = false;
        while (reader.GetNextChunk() is { } currentChunk &&
               currentChunk.Type != PngChunkType.IEND)
        {
            if (!metadataWritten &&
                (currentChunk.Type == PngChunkType.sRGB ||
                 currentChunk.Type == PngChunkType.gAMA ||
                 currentChunk.Type == PngChunkType.PLTE ||
                 currentChunk.Type == PngChunkType.IDAT))
            {
                if (colorChunk is not null)
                {
                    destination.Write(colorChunk.ChunkData.Span);
                }

                if (srgbChunk is not null)
                {
                    destination.Write(srgbChunk.ChunkData.Span);
                }

                destination.Write(xmpChunk.ChunkData.Span);
                metadataWritten = true;
            }

            if (currentChunk.Type != PngChunkType.sRGB &&
                currentChunk.Type != PngChunkType.gAMA &&
                currentChunk.Type != PngChunkType.cICP &&
                currentChunk.Type != PngChunkType.iCCP &&
                currentChunk.Type != PngChunkType.cHRM)
            {
                destination.Write(currentChunk.ChunkData.Span);
            }
        }

        destination.Write(PngReader.IENDSignature);
    }

    private static async Task EncodeAvifAsync(
        Stream destination,
        uint width,
        uint height,
        byte[] pixels,
        int quality,
        bool writeColorProfile,
        bool hdr,
        byte[] xmpData)
    {
        await Task.Run(() =>
        {
            var depth = hdr ? 16u : 8u;
            using var encoder = new avifEncoderLite
            {
                Quality = quality,
                QualityAlpha = quality,
                MaxThreads = GetSuggestedThreads(),
            };
            using var rgb = new avifRGBImageWrapper(
                width,
                height,
                depth,
                avifRGBFormat.RGBA)
            {
                MaxThreads = GetSuggestedThreads(),
            };
            rgb.SetPixelBytes(pixels);
            using var image = new avifImageWrapper(
                width,
                height,
                Math.Clamp(depth, 8, 12),
                avifPixelFormat.YUV444);
            if (writeColorProfile)
            {
                image.ColorPrimaries = hdr
                    ? avifColorPrimaries.BT2020
                    : avifColorPrimaries.BT709;
                image.TransferCharacteristics = hdr
                    ? avifTransferCharacteristics.SMPTE2084
                    : avifTransferCharacteristics.SRGB;
                image.MatrixCoefficients = hdr
                    ? avifMatrixCoefficients.BT2020_NCL
                    : avifMatrixCoefficients.BT709;
            }
            else
            {
                image.ColorPrimaries = avifColorPrimaries.Unspecified;
                image.TransferCharacteristics = avifTransferCharacteristics.Unspecified;
                image.MatrixCoefficients = avifMatrixCoefficients.Unspecified;
            }

            image.SetXMPMetadata(xmpData);
            image.FromRGBImage(rgb);
            encoder.AddImage(image, 1, avifAddImageFlag.Single);
            destination.Write(encoder.Encode());
        }).ConfigureAwait(false);
    }

    private static async Task EncodeJpegXlAsync(
        Stream destination,
        uint width,
        uint height,
        byte[] rgbaPixels,
        int quality,
        bool writeColorProfile,
        byte[] xmpData)
    {
        var format = JxlPixelFormat.R8G8B8A8UInt;
        var lossless = quality >= 100;
        await Task.Run(() =>
        {
            using var encoder = new JxlEncoder
            {
                RunnerThreads = (uint)GetSuggestedThreads(),
            };
            encoder.SetBasicInfo(
                new JxlBasicInfo(width, height, format, true)
                {
                    UsesOriginalProfile = lossless,
                });
            if (writeColorProfile)
            {
                var colorEncoding = new JxlColorEncoding
                {
                    Primaries = (JxlPrimaries)1,
                    WhitePoint = JxlWhitePoint.D65,
                    TransferFunction = JxlTransferFunction.sRGB,
                };
                encoder.SetColorEncoding(colorEncoding);
            }

            encoder.AddBox(JxlBoxType.XMP, xmpData, false);
            var frameSettings = encoder.CreateFrameSettings();
            frameSettings.Distance = QualityToJpegXlDistance(quality);
            frameSettings.Lossless = lossless;
            frameSettings.AddImageFrame(format, rgbaPixels);
            encoder.Encode(destination);
        }).ConfigureAwait(false);
    }

    private static byte[] ConvertScRgbToPqBt2020(byte[] source)
    {
        var destination = new byte[source.Length];
        for (var offset = 0; offset < source.Length; offset += 8)
        {
            var red = Math.Max(0, ReadHalf(source, offset));
            var green = Math.Max(0, ReadHalf(source, offset + 2));
            var blue = Math.Max(0, ReadHalf(source, offset + 4));

            var bt2020Red = 0.627404f * red + 0.329283f * green + 0.0433136f * blue;
            var bt2020Green = 0.069097f * red + 0.919540f * green + 0.0113612f * blue;
            var bt2020Blue = 0.0163916f * red + 0.0880132f * green + 0.895595f * blue;

            WriteUInt16(destination, offset, PqToUInt16(bt2020Red * 80));
            WriteUInt16(destination, offset + 2, PqToUInt16(bt2020Green * 80));
            WriteUInt16(destination, offset + 4, PqToUInt16(bt2020Blue * 80));
            WriteUInt16(destination, offset + 6, ushort.MaxValue);
        }

        return destination;
    }

    private static byte[] ClampScRgbHalfPixels(byte[] source)
    {
        var destination = new byte[source.Length];
        for (var offset = 0; offset < source.Length; offset += 8)
        {
            WriteHalf(destination, offset, Math.Max(0, ReadHalf(source, offset)));
            WriteHalf(destination, offset + 2, Math.Max(0, ReadHalf(source, offset + 2)));
            WriteHalf(destination, offset + 4, Math.Max(0, ReadHalf(source, offset + 4)));
            WriteHalf(destination, offset + 6, 1);
        }

        return destination;
    }

    private static ushort PqToUInt16(float luminanceInNits)
    {
        const double c1 = 3424d / 4096d;
        const double c2 = 2413d / 128d;
        const double c3 = 2392d / 128d;
        const double m1 = 2610d / 16384d;
        const double m2 = 2523d / 32d;
        var normalized = Math.Clamp(luminanceInNits / 10000d, 0, 1);
        var powered = Math.Pow(normalized, m1);
        var encoded = Math.Pow((c1 + c2 * powered) / (1 + c3 * powered), m2);
        return checked((ushort)Math.Round(Math.Clamp(encoded, 0, 1) * ushort.MaxValue));
    }

    private static byte LinearToSrgbByte(float scRgb)
    {
        var normalized = Math.Max(0, scRgb);
        normalized /= 1 + normalized;
        var srgb = normalized <= 0.0031308f
            ? normalized * 12.92f
            : 1.055f * MathF.Pow(normalized, 1f / 2.4f) - 0.055f;
        return checked((byte)Math.Round(Math.Clamp(srgb, 0, 1) * byte.MaxValue));
    }

    private static float ReadHalf(byte[] source, int offset) =>
        (float)BitConverter.UInt16BitsToHalf(
            BitConverter.ToUInt16(source, offset));

    private static void WriteHalf(byte[] destination, int offset, float value) =>
        WriteUInt16(
            destination,
            offset,
            BitConverter.HalfToUInt16Bits((Half)value));

    private static void WriteUInt16(byte[] destination, int offset, ushort value)
    {
        destination[offset] = (byte)value;
        destination[offset + 1] = (byte)(value >> 8);
    }

    private static float QualityToJpegXlDistance(int quality) =>
        quality switch
        {
            >= 100 => 0,
            >= 90 => 1,
            _ => 2,
        };

    private static int GetSuggestedThreads()
    {
        var processorCount = Environment.ProcessorCount;
        return processorCount switch
        {
            >= 16 => processorCount - 4,
            >= 8 => processorCount - 2,
            _ => Math.Max(1, processorCount),
        };
    }
}
