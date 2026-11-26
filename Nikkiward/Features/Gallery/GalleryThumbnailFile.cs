using System.Security.Cryptography;
using System.Text;
using Nikkiward.Services;

namespace Nikkiward.Features.Gallery;

internal static class GalleryThumbnailFile
{
    public static async Task<string?> GetOrCreateAsync(string folder, string? galleryRoot, FileInfo source,
        Func<Task<byte[]?>> render, CancellationToken token)
    {
        if (galleryRoot is not null &&
            (VariantPathPolicy.IsSameOrDescendant(folder, galleryRoot) ||
             VariantPathPolicy.IsSameOrDescendant(GalleryFavoriteProtectionService.ResolveDirectoryLinks(folder),
                 GalleryFavoriteProtectionService.ResolveDirectoryLinks(galleryRoot))))
        {
            return null;
        }

        var path = Path.Combine(folder, BuildCacheFileName(source));
        if (File.Exists(path)) return path;
        var bytes = await render();
        if (bytes is not { Length: > 0 }) return null;
        Directory.CreateDirectory(folder);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
            return path;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    /// <summary>
    /// Keyed on path plus mtime plus length, so replacing a photo in place
    /// produces a different key rather than a stale hit.
    /// </summary>
    private static string BuildCacheFileName(FileInfo source)
    {
        var key = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{source.FullName.ToLowerInvariant()}|{source.LastWriteTimeUtc.Ticks}|{source.Length}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return $"{Convert.ToHexString(hash).ToLowerInvariant()[..32]}.jpg";
    }
}
