using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;

namespace Nikkiward.Features.GameUpdate;

internal sealed record PackageEntry(string Path, long Size, bool IsDirectory, DateTime? Modified);
internal static class PackageArchive
{
    internal static PackageEntry[] List(string package)
    {
        try
        {
            using var archive = SevenZipArchive.OpenArchive(package);
            var paths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var entries = archive.Entries.Select(entry =>
            {
                if (entry.IsEncrypted) throw new UpdateException(UpdateFailure.Password, "安装包需要密码。");
                var path = UpdatePaths.Relative(entry.Key ?? "", subtree: !entry.IsDirectory);
                if (!paths.TryAdd(path, entry.IsDirectory) || entry.LinkTarget != null)
                    throw new UpdateException(UpdateFailure.UnsafePath, $"重复路径或链接：{path}");
                return new PackageEntry(path, entry.Size, entry.IsDirectory, entry.LastModifiedTime);
            }).ToArray();
            foreach (var path in paths.Keys)
                for (var parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                    if (paths.TryGetValue(parent, out var dir) && !dir)
                        throw new UpdateException(UpdateFailure.UnsafePath, $"文件路径冲突：{parent}");
            return entries;
        }
        catch (Exception ex) when (ex is not UpdateException && ex is SharpCompressException or InvalidOperationException or EndOfStreamException or InvalidDataException)
        {
            throw new UpdateException(UpdateFailure.Archive, "无法读取 7z 安装包。", ex);
        }
    }

    internal static void Extract(string package, string destination)
    {
        using var lease = File.OpenRead(package);
        var entries = List(package);
        Extract(package, destination, entries);
    }

    internal static void Extract(string package, string destination, PackageEntry[] entries)
    {
        UpdatePaths.Absolute(destination, workingDirectory: true);
        foreach (var entry in entries)
            UpdatePaths.Absolute(Path.Combine(destination, entry.Path), workingDirectory: true);
        Directory.CreateDirectory(destination);
        foreach (var dir in entries.Where(e => e.IsDirectory))
            Directory.CreateDirectory(Path.Combine(destination, dir.Path));

        var files = entries.Where(e => !e.IsDirectory).ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
        try
        {
            using var archive = SevenZipArchive.OpenArchive(package);
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory) continue;
                var entry = files[UpdatePaths.Relative(reader.Entry.Key!, subtree: true)];
                var target = Path.Combine(destination, entry.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    reader.WriteEntryTo(output);
                    if (output.Length != entry.Size) throw new InvalidDataException($"解压长度不符：{entry.Path}");
                }
                if (entry.Modified is DateTime time) File.SetLastWriteTimeUtc(target, time.ToUniversalTime());
                files.Remove(entry.Path);
            }
            if (files.Count != 0) throw new InvalidDataException("包中缺少清单列出的文件。");
        }
        catch (Exception ex) when (ex is SharpCompressException or EndOfStreamException or InvalidDataException)
        {
            throw new UpdateException(UpdateFailure.Archive, "无法解压 7z 安装包。", ex);
        }
    }
}
