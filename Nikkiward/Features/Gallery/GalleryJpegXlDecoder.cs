using System.Runtime.InteropServices;
using Starward.Codec.JpegXL;
using Starward.Codec.JpegXL.CMS;
using Starward.Codec.JpegXL.CodeStream;
using Starward.Codec.JpegXL.Decode;
using static Starward.Codec.JpegXL.Decode.JxlDecoderNativeMethod;

namespace Nikkiward.Features.Gallery;

internal sealed class GalleryJpegXlDecoder : GalleryScreenshotDecoder
{
    private readonly JxlDecoderPtr _decoder;
    private GCHandle _input;

    public GalleryJpegXlDecoder(byte[] data)
    {
        _decoder = JxlDecoderCreate(default);
        if ((IntPtr)_decoder == IntPtr.Zero) throw new OutOfMemoryException();
        try
        {
            _input = GCHandle.Alloc(data, GCHandleType.Pinned);
            Check(JxlDecoderSubscribeEvents(_decoder,
                JxlDecoderStatus.BasicInfo | JxlDecoderStatus.ColorEncoding | JxlDecoderStatus.FullImage));
            JxlDecoderSetCms(_decoder, JxlCmsInterface.GetDefault());
            Check(JxlDecoderSetDesiredIntensityTarget(_decoder, 80));
            Check(JxlDecoderSetUnpremultiplyAlpha(_decoder, true));
            Check(JxlDecoderSetInput(_decoder, _input.AddrOfPinnedObject(), (nuint)data.Length));
            JxlDecoderCloseInput(_decoder);
            Check(JxlDecoderProcessInput(_decoder), JxlDecoderStatus.BasicInfo);
            JxlBasicInfo info = default;
            Check(JxlDecoderGetBasicInfo(_decoder, ref info));
            Width = info.XSize;
            Height = info.YSize;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public override uint Width { get; }
    public override uint Height { get; }

    public override byte[] ReadPixels(CancellationToken token)
    {
        Check(JxlDecoderProcessInput(_decoder), JxlDecoderStatus.ColorEncoding);
        Check(JxlDecoderSetOutputColorProfile(_decoder, JxlColorEncoding.SRGB, IntPtr.Zero, 0));
        Check(JxlDecoderProcessInput(_decoder), JxlDecoderStatus.NeedImageOutBuffer);
        token.ThrowIfCancellationRequested();
        var pixels = new byte[checked((int)(Width * Height * 4))];
        var output = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            Check(JxlDecoderSetImageOutBuffer(_decoder, JxlPixelFormat.R8G8B8A8UInt,
                output.AddrOfPinnedObject(), (nuint)pixels.Length));
            Check(JxlDecoderProcessInput(_decoder), JxlDecoderStatus.FullImage);
            return pixels;
        }
        finally
        {
            output.Free();
        }
    }

    public override void Dispose()
    {
        JxlDecoderDestroy(_decoder);
        if (_input.IsAllocated) _input.Free();
    }

    private static void Check(JxlDecoderStatus actual, JxlDecoderStatus expected = JxlDecoderStatus.Success)
    {
        if (actual != expected) throw new InvalidDataException("JPEG XL 图像无法解码");
    }
}
