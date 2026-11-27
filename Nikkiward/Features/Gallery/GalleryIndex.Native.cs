using Microsoft.UI.Dispatching;
using Nikkiward.Features.Background;
using Nikkiward.Services;
using Nikkiward.ViewModels;
using Windows.Graphics.Imaging;

namespace Nikkiward.Features.Gallery;

internal sealed partial class GalleryIndex
{
    internal static GalleryIndex Open(string? root, PhotoStudioStore studio)
    {
        var exportRoot = studio.ExportRoot;
        var metadata = new Nuan5GalleryMetadataService();
        return new GalleryIndex(root, exportRoot, Path.Combine(ApplicationDataPaths.Root, "GalleryIndex"),
            ReadAsync, GalleryFiles.ResolveCategory);

        async Task<GalleryIndexFacts> ReadAsync(string path, string logicalPath, CancellationToken token)
        {
            var parameters = await metadata.ReadIndexMetadataAsync(path, logicalPath, token);
            token.ThrowIfCancellationRequested();
            if (parameters.Status == GalleryPhotoMetadataStatus.FileUnavailable)
                throw new IOException("无法读取照片参数。");
            int? width = null, height = null;
            int[] colors = [];
            try
            {
                using var source = File.OpenRead(path);
                using var original = source.AsRandomAccessStream();
                try
                {
                    var header = await BitmapDecoder.CreateAsync(original);
                    width = (int)header.OrientedPixelWidth;
                    height = (int)header.OrientedPixelHeight;
                }
                catch (Exception ex) when (IsImageError(ex)) { }

                GalleryThumbnail? thumb = null;
                var extension = Path.GetExtension(logicalPath);
                if (GalleryThumbnailCache.FolderPath is { } folder && CanWriteCache(folder, root, exportRoot))
                    thumb = await GalleryThumbnailCache.TryGetThumbnailAsync(path, extension, root, token);
                else if (GalleryFileTypes.UsesScreenshotCodec(extension))
                    thumb = new(null, await StarwardScreenshotDecoder.RenderThumbnailAsync(path, extension, 48, token));
                using Stream? file = thumb?.FilePath is { } cached ? File.OpenRead(cached) :
                    thumb?.Bytes is { } bytes ? new MemoryStream(bytes) : null;
                using var thumbnail = file?.AsRandomAccessStream();
                original.Seek(0);
                var sample = await ArtDecoder.DecodeScaledAsync(thumbnail ?? original, 48, token);
                if (sample != null) colors = Palette(sample.Pixels);
            }
            catch (Exception ex) when (IsImageError(ex))
            {
                if (width == null && !parameters.HasParameters)
                    throw new IOException("无法解码照片。", ex);
            }
            return new(width, height, parameters.HasParameters, parameters.Camera?.Filter?.Id,
                parameters.ClothingIds.Distinct().ToArray(), parameters.Camera?.FocalLength,
                parameters.Camera?.Aperture, colors);
        }
    }

    private static bool IsImageError(Exception ex) => ex is System.Runtime.InteropServices.COMException
        or ArgumentException or NotSupportedException or InvalidDataException or IOException or DllNotFoundException;

    internal IDisposable Watch(DispatcherQueue dispatcher, Action refresh) =>
        new GalleryIndexWatch(dispatcher, refresh, _root, _exports);

    private sealed class GalleryIndexWatch : IDisposable
    {
        private readonly GalleryFolderWatcher[] _watchers;
        internal GalleryIndexWatch(DispatcherQueue dispatcher, Action refresh, params string?[] roots)
        {
            _watchers = roots.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Select(root =>
            {
                var watcher = new GalleryFolderWatcher(dispatcher, refresh);
                watcher.Watch(root);
                return watcher;
            }).ToArray();
        }
        public void Dispose()
        {
            foreach (var watcher in _watchers) watcher.Dispose();
        }
    }
}
