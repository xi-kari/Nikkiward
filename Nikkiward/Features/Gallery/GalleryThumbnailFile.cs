using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nikkiward.Services;

namespace Nikkiward.Features.Gallery;

internal sealed record GalleryThumbnail(string? FilePath, byte[]? Bytes);

internal static partial class GalleryThumbnailFile
{
    public static async Task<GalleryThumbnail?> GetOrCreateAsync(string? folder, string? galleryRoot, FileInfo source,
        bool allowMemory, Func<Task<byte[]?>> render, CancellationToken token)
    {
        var path = GetCachePath(folder, galleryRoot, source);
        if (path != null)
        {
            if (File.Exists(path)) return new GalleryThumbnail(path, null);
            var legacy = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(path)[CachePrefix.Length..]);
            if (File.Exists(legacy)) return new GalleryThumbnail(legacy, null);
        }

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

    private const string CachePrefix = "nikkiward-thumb-";

    [GeneratedRegex(@"\A" + CachePrefix + @"[0-9a-f]{32}\.jpg(?:\.[0-9a-f]{32}\.tmp)?\z")]
    private static partial Regex CacheFilePattern();

    internal static bool IsCacheFileName(string name) => CacheFilePattern().IsMatch(name);

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
        return $"{CachePrefix}{Convert.ToHexString(hash).ToLowerInvariant()[..32]}.jpg";
    }
}
