namespace Nikkiward.Features.Gallery;

internal sealed record GalleryWatchTarget(string DirectoryPath, string Filter, bool Recursive)
{
    internal static GalleryWatchTarget Resolve(string rootPath)
    {
        var current = new DirectoryInfo(rootPath);
        string filter = "*";
        while (!current.Exists && current.Parent is { } parent)
        {
            filter = current.Name;
            current = parent;
        }
        return new(current.FullName, filter, filter == "*");
    }
}
