using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nikkiward.Services;

namespace Nikkiward.Features.Gallery;

internal sealed class StudioSession<T>(IEnumerable<T> photos) where T : class
{
    internal Dictionary<string,T> Photos { get; } = photos.ToDictionary(_ => Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string,T> _tickets = [];

    internal string Issue(string id)
    {
        var ticket = Guid.NewGuid().ToString("N");
        _tickets.Add(ticket, Photos[id]);
        return ticket;
    }

    internal T? Consume(string ticket) => _tickets.Remove(ticket, out var photo) ? photo : null;

    internal string? LastExport { get; private set; }

    internal async Task<string> ExportAsync(string root, string name, string extension, DateTime time, byte[] data, CancellationToken token)
    {
        var stamp = time.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var stem = $"{Path.GetFileNameWithoutExtension(name)}-studio-{stamp}";
        for (int number = 1; ; number++)
        {
            var suffix = number == 1 ? "" : $"-{number}";
            var path = Path.Combine(root, stem + suffix + extension);
            FileStream file;
            try { file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 80 or 183) { continue; }
            try
            {
                using (file) await file.WriteAsync(data, token);
            }
            catch { File.Delete(path); throw; }

            LastExport = path;
            return path;
        }
    }

    internal static bool Allows(string action) => action is "back" or "open" or "inspect" or "image" or "thumb"
        or "save" or "favorite" or "reject" or "reveal-export" or "locate" or "copy-code" or "export" or "copy" or "background";
}

internal sealed class PhotoStudioStore(string root)
{
    internal static async Task ImportBackgroundAsync(string root, string originalName, byte[] png,
        Func<string, string, CancellationToken, Task> import, CancellationToken token)
    {
        var path = Path.Combine(root, $"{Guid.NewGuid():N}.png");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            using (file) await file.WriteAsync(png, token);
            await import(path, originalName, token);
        }
        finally { File.Delete(path); }
    }

    internal string Root => root;
    private string Entry(string key) => Path.Combine(root, Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()))) + ".json");

    internal JsonElement? Load(string key) => File.Exists(Entry(key))
        ? JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Entry(key))) : null;

    internal void Save(string key, JsonElement value)
    {
        Directory.CreateDirectory(root);
        var path = Entry(key);
        var temp = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, value.GetRawText());
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }

    internal bool Rejected(string key) => Load(key + "\0reject")?.ValueKind == JsonValueKind.True;
    internal void SetRejected(string key, bool value) => Save(key + "\0reject", JsonSerializer.SerializeToElement(value));

    internal string ExportRoot
    {
        get => File.Exists(Path.Combine(root, "exports.txt")) ? File.ReadAllText(Path.Combine(root, "exports.txt")) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Nikkiward", "Exports");
        set
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "exports.txt"), value);
        }
    }

    internal static void CheckDestination(string path, IEnumerable<string> protectedRoots)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            protectedRoots.Any(root => VariantPathPolicy.PathsOverlap(path, root) ||
                VariantPathPolicy.PathsOverlap(path, GalleryFavoriteProtectionService.ResolveDirectoryLinks(root))))
            throw new IOException("请选择图库和游戏目录以外的本地导出目录。");
        var existing = new DirectoryInfo(path);
        while (!existing.Exists && existing.Parent is { } parent) existing = parent;
        if (VariantPathPolicy.ContainsReparsePoint(existing.FullName))
            throw new IOException("导出目录不能经过目录链接。");
    }
}
