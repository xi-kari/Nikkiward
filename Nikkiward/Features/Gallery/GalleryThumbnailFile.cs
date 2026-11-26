using System.Security.Cryptography;
using System.Text;
using Nikkiward.Services;

namespace Nikkiward.Features.Gallery;

internal sealed record GalleryThumbnail(string? FilePath, byte[]? Bytes);

internal static class GalleryThumbnailFile
{
    public static async Task<GalleryThumbnail?> GetOrCreateAsync(string? folder, string? galleryRoot, FileInfo source,
        bool allowMemory, Func<Task<byte[]?>> render, CancellationToken token)
    {
        var path = GetCachePath(folder, galleryRoot, source);
        if (File.Exists(path)) return new GalleryThumbnail(path, null);

        if (path is null && !allowMemory) return null;
        var bytes = await render();
        if (bytes is not { Length: > 0 }) return null;
        if (path is not null)
        {
            try
            {
                await WriteCacheFileAsync(path, bytes, token);
                return new GalleryThumbnail(path, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return allowMemory ? new GalleryThumbnail(null, bytes) : null;
    }

    private static async Task WriteCacheFileAsync(string path, byte[] bytes, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string? GetCachePath(string? folder, string? galleryRoot, FileInfo source)
    {
        try
        {
            if (folder is null || (galleryRoot is not null &&
                (VariantPathPolicy.IsSameOrDescendant(folder, galleryRoot) ||
                 VariantPathPolicy.IsSameOrDescendant(GalleryFavoriteProtectionService.ResolveDirectoryLinks(folder),
                     GalleryFavoriteProtectionService.ResolveDirectoryLinks(galleryRoot)))))
            {
                return null;
            }

            return Path.Combine(folder, BuildCacheFileName(source));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
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
