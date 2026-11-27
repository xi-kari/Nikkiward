namespace Nikkiward.Features.Gallery;

internal static class GalleryFiles
{
    private static readonly Dictionary<string, (string Name, bool LocalCapture)> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NikkiPhotos_HighQuality"] = ("高清拍摄", true),
        ["NikkiPhotos_LowQuality"] = ("低清副本", true),
        ["MagazinePhotos"] = ("杂志照片", true),
        ["ClockInPhoto"] = ("打卡照片", true),
        ["CloudPhotos"] = ("云端照片", false),
        ["CloudPhotos_LowQuality"] = ("云端低清", false),
        ["ScreenShot"] = ("系统截图", true),
        ["Collage"] = ("拼贴照片", true),
        ["CollagePhoto"] = ("拼贴照片", true)
    };

    internal static bool IsLocalCaptureCategory(string category) => Categories.TryGetValue(category, out var value) && value.LocalCapture;

    internal static (string Id, string DisplayName) ResolveCategory(string relativePath)
    {
        foreach (var segment in relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            if (Categories.TryGetValue(segment, out var value)) return (segment, value.Name);
        return relativePath.EndsWith("PendingUp.jpeg", StringComparison.OrdinalIgnoreCase)
            ? ("PendingUp", "待上传") : ("Other", "其他");
    }

    internal static void ReplaceDefaultFavorites<T>(List<T> photos, Predicate<T> isDefault,
        IReadOnlyList<GalleryDefaultFavorite> defaults, Func<GalleryDefaultFavorite, T> project)
    {
        photos.RemoveAll(isDefault);
        photos.AddRange(defaults.Select(project));
    }

    internal static string DefaultRoot(string gameRoot) => Path.Combine(gameRoot, "X6Game", "Saved", "GamePlayPhotos");

    internal static IEnumerable<FileInfo> Enumerate(string root, CancellationToken token)
    {
        const FileAttributes recallOnAccess = (FileAttributes)0x400000;
        const FileAttributes recallOnOpen = (FileAttributes)0x40000;
        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint | recallOnAccess | recallOnOpen,
            IgnoreInaccessible = true,
            RecurseSubdirectories = true
        };
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
        {
            token.ThrowIfCancellationRequested();
            if (GalleryFileTypes.IsSupported(file.Extension)) yield return file;
        }
    }

    internal static IOrderedEnumerable<FileInfo> NewestFirst(string root, CancellationToken token) =>
        Enumerate(root, token).OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.FullName, StringComparer.Ordinal);
}
