using Nikkiward.Features.Gallery;

namespace Nikkiward.ViewModels;

public sealed class GalleryPhotoItemViewModel
{
    private readonly string? _galleryRoot;
    internal static GalleryPhotoItemViewModel CreatePhoto(FileInfo file, string rootPath, string scopeId)
    {
        var relativePath = Path.GetRelativePath(rootPath, file.FullName);
        return new GalleryPhotoItemViewModel(
            file.FullName,
            relativePath,
            file.Name,
            file.Length,
            GalleryTimestamp.Resolve(file.Name, file.LastWriteTimeUtc),
            scopeId, rootPath);
    }

    public GalleryPhotoItemViewModel(
        string filePath,
        string relativePath,
        string fileName,
        long fileSizeBytes,
        DateTime lastWriteTimeUtc,
        string annotationScopeId,
        string? galleryRoot)
    {
        FilePath = filePath;
        RelativePath = relativePath;
        StarKey = GalleryAnnotationStore.NormalizeRelativePath(relativePath);
        FileName = fileName;
        FileSizeBytes = fileSizeBytes;
        LastWriteTimeUtc = DateTime.SpecifyKind(lastWriteTimeUtc, DateTimeKind.Utc);
        AnnotationScopeId = annotationScopeId;
        _galleryRoot = galleryRoot;
    }

    public string FilePath { get; }
    internal string OriginalPath => IsUsingProtectedCopy && _galleryRoot != null ? Path.Combine(_galleryRoot, RelativePath) : FilePath;

    public string RelativePath { get; }

    public string StarKey { get; }

    public string AnnotationScopeId { get; }

    public string FileName { get; }

    public long FileSizeBytes { get; }

    /// <summary>
    /// Capture time in UTC, from the file name when it carries one, otherwise the
    /// file system write time. See <see cref="GalleryTimestamp"/>.
    /// </summary>
    public DateTime LastWriteTimeUtc { get; }

    public string ModifiedText => GalleryTimestamp.FormatHomeTime(LastWriteTimeUtc.ToLocalTime(), DateTime.Today);

    public string FullModifiedText => LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public bool IsStarred { get; private set; }
    public void SetStarred(bool value) => IsStarred = value;
    public bool IsUsingProtectedCopy { get; internal set; }

    public string Extension => Path.GetExtension(RelativePath);

    public bool UsesScreenshotCodec => GalleryFileTypes.UsesScreenshotCodec(Extension);

    internal bool MemoryOnlyThumbnails { get; init; }

    internal async Task<string?> ReadThumbnailDataUrlAsync(CancellationToken token)
    {
        var thumbnail = await GalleryThumbnailCache.TryGetThumbnailAsync(FilePath, Extension, _galleryRoot, token, MemoryOnlyThumbnails);
        if (thumbnail == null) return null;
        try
        {
            var bytes = thumbnail.Bytes ?? await File.ReadAllBytesAsync(thumbnail.FilePath!, token);
            return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

}
