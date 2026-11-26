namespace Nikkiward.Features.Gallery;

internal static class GalleryFiles
{
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

    internal static IEnumerable<FileInfo> Recent(string root, CancellationToken token) =>
        Enumerate(root, token).OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.FullName, StringComparer.Ordinal).Take(4);
}
