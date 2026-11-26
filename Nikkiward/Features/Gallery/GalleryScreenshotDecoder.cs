namespace Nikkiward.Features.Gallery;

internal abstract class GalleryScreenshotDecoder : IDisposable
{
    private const long MaximumPixels = 16_777_216;
    private static readonly SemaphoreSlim DecodeGate = new(1, 1);

    public abstract uint Width { get; }
    public abstract uint Height { get; }
    public abstract byte[] ReadPixels(CancellationToken token);
    public abstract void Dispose();

    public static async Task<T> RenderAsync<T>(string path, string extension,
        Func<byte[], string, GalleryScreenshotDecoder> open, Func<uint, uint, byte[], Task<T>> render,
        CancellationToken token)
    {
        await DecodeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                using var stream = File.OpenRead(path);
                if (stream.Length > 128L * 1024 * 1024)
                {
                    throw new InvalidDataException("文件超过 128 MiB，无法预览");
                }

                var data = new byte[(int)stream.Length];
                await stream.ReadExactlyAsync(data, token);
                using var decoder = open(data, extension);
                var width = decoder.Width;
                var height = decoder.Height;
                if (width == 0 || height == 0 || (ulong)width * height > MaximumPixels)
                {
                    throw new InvalidDataException("图像尺寸无效或超过 1677 万像素，无法预览");
                }

                token.ThrowIfCancellationRequested();
                var pixels = decoder.ReadPixels(token);
                token.ThrowIfCancellationRequested();
                return await render(width, height, pixels);
            }, token).ConfigureAwait(false);
        }
        finally
        {
            DecodeGate.Release();
        }
    }
}
