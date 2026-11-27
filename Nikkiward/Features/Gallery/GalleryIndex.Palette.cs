namespace Nikkiward.Features.Gallery;

internal sealed partial class GalleryIndex
{
    internal static int[] Palette(byte[] bgra)
    {
        var bins = new Dictionary<int, (int Count, int R, int G, int B)>();
        for (int i = 0; i < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 128) continue;
            int r = bgra[i + 2], g = bgra[i + 1], b = bgra[i];
            var key = ((r >> 5) << 6) | ((g >> 5) << 3) | (b >> 5);
            var bin = bins.GetValueOrDefault(key);
            bins[key] = (bin.Count + 1, bin.R + r, bin.G + g, bin.B + b);
        }
        return bins.OrderByDescending(p => p.Value.Count).ThenBy(p => p.Key).Take(5)
            .Select(p => p.Value).Select(v => ((v.R / v.Count) << 16) | ((v.G / v.Count) << 8) | (v.B / v.Count)).ToArray();
    }
}
