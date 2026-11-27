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
        var stem = Path.GetFileNameWithoutExtension(name);
        for (int number = 1; ; number++)
        {
            var suffix = number == 1 ? "" : $"-{number}";
            var ending = $"-studio-{stamp}{suffix}{extension}";
            var length = Math.Min(stem.Length, 255 - ending.Length);
            if (length < stem.Length && char.IsHighSurrogate(stem[length - 1])) length--;
            var path = Path.Combine(root, stem[..length] + ending);
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

    internal JsonElement? Load(string key, out bool unreadable, IEnumerable<string> protectedRoots, bool mark = false)
    {
        unreadable = false;
        var path = Entry(mark ? key + "\0reject" : key);
        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path, new UTF8Encoding(false, true)));
            if (mark ? value.ValueKind is JsonValueKind.True or JsonValueKind.False : ValidEdits(value))
                return JsonSerializer.SerializeToElement(value);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or DecoderFallbackException or IOException or UnauthorizedAccessException) { }

        unreadable = true;
        try
        {
            CheckDestination(root, protectedRoots);
            File.Move(path, $"{path}.{Guid.NewGuid():N}.corrupt");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the original record if the directory is protected or the file is locked.
        }
        return null;
    }

    private static bool ValidEdits(JsonElement edits)
    {
        if (edits.ValueKind != JsonValueKind.Object) return false;
        foreach (var prop in edits.EnumerateObject())
        {
            var value = prop.Value;
            var valid = prop.Name switch
            {
                "fields" => value.ValueKind == JsonValueKind.Object && value.EnumerateObject().All(f => f.Value.ValueKind is JsonValueKind.True or JsonValueKind.False),
                "title" => value.ValueKind == JsonValueKind.String && value.GetString() != null,
                "frame" => value.ValueKind == JsonValueKind.String && value.GetString() is "off" or "on" or "ink" or "bar",
                "codes" => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 4 && value.EnumerateArray().All(c => c.ValueKind == JsonValueKind.String),
                "masks" => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(m => m.ValueKind == JsonValueKind.Object &&
                    new[] { "x", "y", "w", "h" }.All(k => m.TryGetProperty(k, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetDouble(out var d) && d >= 0 && d <= 1)),
                "crop" or "turns" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var index) && index is >= 0 and <= 3,
                "rot" or "exp" or "con" or "hi" or "sh" or "sat" or "tmp" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && double.IsFinite(n),
                _ => true
            };
            if (!valid) return false;
        }
        return true;
    }

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

    internal bool Rejected(string key, out bool unreadable, IEnumerable<string> protectedRoots) =>
        Load(key, out unreadable, protectedRoots, mark: true)?.ValueKind == JsonValueKind.True;
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
