using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nikkiward.Features.GameUpdate;

internal enum UpdateFailure
{
    Description, UnsafePath, Digest, Archive, Password, Version, ProcessUnknown,
    ProcessRunning, Staging, Space, Busy, Journal, ReadOnly, CleanupPending
}

internal sealed class UpdateException(UpdateFailure reason, string message, Exception? inner = null)
    : IOException(message, inner)
{
    internal UpdateFailure Reason { get; } = reason;
}

internal sealed record PackageDescription
{
    internal const string Host = "x6cn-game-package.papegames.com";
    internal int Version { get; }
    internal Uri Url { get; }
    internal string Md5 { get; }
    internal IReadOnlyList<string> CleanupDirs { get; }

    internal PackageDescription(int version, Uri url, string md5, IEnumerable<string> cleanupDirs)
    {
        CheckUrl(url);
        if (version <= 0 || !Regex.IsMatch(md5, @"\A[0-9a-fA-F]{32}\z"))
            throw new UpdateException(UpdateFailure.Description, "包描述的版本或 MD5 无效。");
        Version = version;
        Url = url;
        Md5 = md5;
        CleanupDirs = Array.AsReadOnly(cleanupDirs.Select(p => UpdatePaths.Relative(p, subtree: true))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Length).ToArray());
    }

    internal static void CheckUrl(Uri url)
    {
        if (!url.IsAbsoluteUri || url.Scheme != "https" || url.IdnHost != Host || url.UserInfo.Length != 0)
            throw new UpdateException(UpdateFailure.Description, "包地址必须使用国服官方 CDN 的 HTTPS 地址。");
    }

    internal static PackageDescription Parse(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var item = doc.RootElement;
            if (item.TryGetProperty("latest", out var latest)) item = latest;
            var version = int.Parse(item.GetProperty("version").ToString(), NumberStyles.None, CultureInfo.InvariantCulture);
            return new(version, new Uri(item.GetProperty("url").GetString() ?? "", UriKind.Absolute),
                item.GetProperty("md5").GetString() ?? "",
                item.GetProperty("cleanupDirs").EnumerateArray().Select(e => e.GetString() ?? ""));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or OverflowException or InvalidOperationException)
        {
            throw new UpdateException(UpdateFailure.Description, "无法解析包描述。", ex);
        }
    }
}

internal static class UpdatePaths
{
    private static readonly string[] Protected = [@"X6Game\Saved", @"X6Game\ScreenShot", "DownloadCache"];
    private static readonly Regex Device = new(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|(?:COM|LPT)[0-9¹²³])(?: *\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool Within(string path, string parent) =>
        path.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + "\\", StringComparison.OrdinalIgnoreCase);

    internal static string Relative(string path, bool subtree = false)
    {
        var relative = path.Replace('/', '\\');
        if (relative.Split('\\').Any(s => s.Length == 0 || s.EndsWith('.') || s.EndsWith(' ') ||
            s.Contains('~') || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Device.IsMatch(s)) ||
            Protected.Any(p => Within(relative, p) || (subtree && Within(p, relative))))
            throw new UpdateException(UpdateFailure.UnsafePath, $"路径无效或涉及受保护的目录：{path}");
        return relative;
    }

    internal static string Absolute(string path, bool workingDirectory = false)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Path.IsPathFullyQualified(path) || full.StartsWith(@"\\") || full.Contains('~'))
            throw new UpdateException(UpdateFailure.UnsafePath, $"需要本地卷上的完整路径：{path}");
        var relative = full[Path.GetPathRoot(full)!.Length..];
        var parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(s => s.EndsWith(' ') || s.EndsWith('.') ||
                s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Device.IsMatch(s)))
            throw new UpdateException(UpdateFailure.UnsafePath, $"路径含无效文件名：{path}");
        if (workingDirectory && Enumerable.Range(0, parts.Length).Any(i =>
                Protected.Any(p => Within(string.Join('\\', parts.Skip(i)), p))))
            throw new UpdateException(UpdateFailure.UnsafePath, $"工作目录位于受保护的路径中：{path}");
        for (string? part = full; part != null; part = Path.GetDirectoryName(part))
        {
            try
            {
                if (File.GetAttributes(part).HasFlag(FileAttributes.ReparsePoint))
                    throw new UpdateException(UpdateFailure.UnsafePath, $"路径经过重解析点：{part}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return full;
    }
}
