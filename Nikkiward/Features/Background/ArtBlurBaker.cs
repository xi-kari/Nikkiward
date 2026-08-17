namespace Nikkiward.Features.Background;

/// <summary>
/// Pure CPU pre-bake of the L1 depth layer: a wide Gaussian-approximating blur
/// followed by a partial desaturation. The result is baked once per artwork at
/// load time, so nothing on the runtime path ever blurs or re-blurs live.
/// </summary>
public sealed class ArtBlurBaker : IArtBlurBaker
{
    /// <summary>
    /// Working width the caller downsamples the source artwork to before
    /// calling <see cref="Bake"/>. <see cref="Bake"/> itself never resizes.
    /// </summary>
    public const int BakeWidth = 320;

    /// <summary>
    /// Target Gaussian standard deviation, in pixels of the
    /// <see cref="BakeWidth"/>-wide copy.
    /// </summary>
    public const double Sigma = 12.0;

    /// <summary>
    /// Number of successive box blurs used to approximate the Gaussian.
    /// Three is the usual accuracy/cost sweet spot.
    /// </summary>
    public const int BoxPasses = 3;

    /// <summary>
    /// Fraction of the way each pixel is pulled toward its own luminance.
    /// </summary>
    public const double DesaturationAmount = 0.30;

    private const double LumaWeightR = 0.2126;
    private const double LumaWeightG = 0.7152;
    private const double LumaWeightB = 0.0722;

    private static readonly int[] BoxRadii = ComputeBoxRadii(Sigma, BoxPasses);

    /// <inheritdoc />
    public ArtPixelBuffer Bake(ArtPixelBuffer downsampled)
    {
        ArgumentNullException.ThrowIfNull(downsampled);

        var width = downsampled.Width;
        var height = downsampled.Height;
        var count = width * height * 4;

        // Two scratch buffers, ping-ponged: horizontal writes front -> back,
        // vertical writes back -> front, so each box pass lands back in front.
        var front = new float[count];
        var back = new float[count];

        var source = downsampled.Pixels;
        for (var i = 0; i < count; i++)
        {
            front[i] = source[i];
        }

        for (var pass = 0; pass < BoxRadii.Length; pass++)
        {
            var radius = BoxRadii[pass];
            BlurHorizontal(front, back, width, height, radius);
            BlurVertical(back, front, width, height, radius);
        }

        var destination = new byte[count];
        Desaturate(front, destination, count);
        return new ArtPixelBuffer(destination, width, height);
    }

    /// <summary>
    /// Kovesi's box-size fit: <paramref name="passes"/> box blurs whose widths
    /// bracket the ideal width reproduce a Gaussian of the requested sigma. The
    /// first <c>m</c> passes take the smaller odd width, the rest take that
    /// width plus two.
    /// </summary>
    private static int[] ComputeBoxRadii(double sigma, int passes)
    {
        var radii = new int[passes];
        if (sigma <= 0.0)
        {
            return radii;
        }

        var variance = 12.0 * sigma * sigma;
        var wIdeal = Math.Sqrt((variance / passes) + 1.0);
        var wl = (int)Math.Floor(wIdeal);
        if (wl % 2 == 0)
        {
            wl--;
        }

        // A box must span at least one pixel, else the pass is a no-op divide.
        if (wl < 1)
        {
            wl = 1;
        }

        var wu = wl + 2;
        var mIdeal = (variance - (passes * wl * wl) - (4.0 * passes * wl) - (3.0 * passes))
            / ((-4.0 * wl) - 4.0);
        var m = (int)Math.Round(mIdeal, MidpointRounding.AwayFromZero);
        m = Math.Clamp(m, 0, passes);

        for (var i = 0; i < passes; i++)
        {
            radii[i] = i < m ? (wl - 1) / 2 : (wu - 1) / 2;
        }

        return radii;
    }

    private static void BlurHorizontal(float[] source, float[] destination, int width, int height, int radius)
    {
        if (radius <= 0)
        {
            Array.Copy(source, destination, width * height * 4);
            return;
        }

        var scale = 1.0f / ((2 * radius) + 1);
        var last = width - 1;
        for (var y = 0; y < height; y++)
        {
            var rowBase = y * width * 4;
            for (var c = 0; c < 4; c++)
            {
                // Prime the window over x = 0 with edge extension. Indices are
                // clamped, so radius >= width stays in bounds.
                var accumulator = 0.0f;
                for (var k = -radius; k <= radius; k++)
                {
                    accumulator += source[rowBase + (Math.Clamp(k, 0, last) * 4) + c];
                }

                destination[rowBase + c] = accumulator * scale;
                for (var x = 1; x < width; x++)
                {
                    var entering = Math.Clamp(x + radius, 0, last);
                    var leaving = Math.Clamp(x - radius - 1, 0, last);
                    accumulator += source[rowBase + (entering * 4) + c]
                        - source[rowBase + (leaving * 4) + c];
                    destination[rowBase + (x * 4) + c] = accumulator * scale;
                }
            }
        }
    }

    private static void BlurVertical(float[] source, float[] destination, int width, int height, int radius)
    {
        if (radius <= 0)
        {
            Array.Copy(source, destination, width * height * 4);
            return;
        }

        var scale = 1.0f / ((2 * radius) + 1);
        var rowStride = width * 4;
        var last = height - 1;
        for (var x = 0; x < width; x++)
        {
            var columnBase = x * 4;
            for (var c = 0; c < 4; c++)
            {
                var accumulator = 0.0f;
                for (var k = -radius; k <= radius; k++)
                {
                    accumulator += source[(Math.Clamp(k, 0, last) * rowStride) + columnBase + c];
                }

                destination[columnBase + c] = accumulator * scale;
                for (var y = 1; y < height; y++)
                {
                    var entering = Math.Clamp(y + radius, 0, last);
                    var leaving = Math.Clamp(y - radius - 1, 0, last);
                    accumulator += source[(entering * rowStride) + columnBase + c]
                        - source[(leaving * rowStride) + columnBase + c];
                    destination[(y * rowStride) + columnBase + c] = accumulator * scale;
                }
            }
        }
    }

    private static void Desaturate(float[] source, byte[] destination, int count)
    {
        for (var i = 0; i < count; i += 4)
        {
            double b = source[i];
            double g = source[i + 1];
            double r = source[i + 2];

            // Perceptual weights applied straight to non-linear sRGB values: a
            // deliberate cheap approximation for a background plate, not a
            // colour-managed luminance conversion.
            var luma = (LumaWeightR * r) + (LumaWeightG * g) + (LumaWeightB * b);

            destination[i] = ToByte(b + ((luma - b) * DesaturationAmount));
            destination[i + 1] = ToByte(g + ((luma - g) * DesaturationAmount));
            destination[i + 2] = ToByte(r + ((luma - r) * DesaturationAmount));
            destination[i + 3] = ToByte(source[i + 3]);
        }
    }

    private static byte ToByte(double value)
    {
        // Clamp before the half-up bias so 255.x can never wrap the cast.
        return (byte)(Math.Clamp(value, 0.0, 255.0) + 0.5);
    }
}
