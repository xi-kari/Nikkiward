using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nikkiward.Features.GameUpdate;

internal static class PackageInstall
{
    private const string Owner = ".nikkiward-update";
    internal enum Action { Cleanup, Directory, Backup, Install, Version }
    private sealed record Step(Action Action, string Path);
    private sealed record Journal(string Game, Step[] Steps, int Completed, bool Committed,
        Dictionary<string, DateTime> DirectoryTimes);

    internal static void Apply(PackageDescription package, string archive, string game, string staging,
        Func<IReadOnlyList<string>?> processes, Action<string>? report = null,
        Func<string, long>? availableSpace = null, Action<Action>? afterMutation = null)
    {
        using var packageLock = File.OpenRead(archive);
        PackageDownload.Verify(packageLock, package.Md5);
        report?.Invoke("安装包 MD5 已通过");
        CheckProcesses(processes);
        (game, staging) = Roots(game, staging);
        using var locks = new InstallLocks(game, staging);
        var owner = UpdatePaths.Absolute(Path.Combine(game, Owner));
        if (Path.Exists(owner))
            throw new UpdateException(UpdateFailure.Busy, "此安装有未完成的事务，请使用原暂存目录恢复。");
        var product = UpdatePaths.Absolute(Path.Combine(game, "product.db"));
        byte[] versionBytes = ReadVersion(product, package.Version);
        var entries = PackageArchive.List(archive);
        var steps = Plan(game, package.CleanupDirs, entries);
        foreach (var step in steps)
            if (UpdatePaths.Within(Path.GetFullPath(archive), Path.Combine(game, step.Path)))
                throw new UpdateException(UpdateFailure.UnsafePath, "安装包位于待替换的路径中。");
        var drive = Path.GetPathRoot(staging)!;
        var needed = checked(entries.Where(e => !e.IsDirectory).Sum(e => e.Size) + (1L << 30));
        if ((availableSpace?.Invoke(drive) ?? new DriveInfo(drive).AvailableFreeSpace) < needed)
            throw new UpdateException(UpdateFailure.Space, "暂存卷剩余空间不足。");
        if (File.Exists(staging) || Directory.Exists(staging) && Directory.EnumerateFileSystemEntries(staging).Any())
            throw new UpdateException(UpdateFailure.Staging, "需要空的暂存目录，有日志时请先恢复。");
        Directory.CreateDirectory(staging);
        var journal = new Journal(game, [], 0, false, []);
        Save(staging, journal);
        try
        {
            PackageArchive.Extract(archive, Path.Combine(staging, "new"), entries);
            report?.Invoke("暂存解压和逐文件长度核对已完成");
            CheckProcesses(processes);
            if (!steps.SequenceEqual(Plan(game, package.CleanupDirs, entries)) ||
                !ReadVersion(product, package.Version).SequenceEqual(versionBytes))
                throw new UpdateException(UpdateFailure.Busy, "解压期间安装内容发生变化。");
            File.WriteAllBytes(Path.Combine(staging, "version.new"), versionBytes);
            File.WriteAllText(Path.Combine(staging, "owner.new"), staging);
            var directoryTimes = steps.Select(step => Path.GetDirectoryName(step.Path) ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase).Where(p => Directory.Exists(Path.Combine(game, p)))
                .ToDictionary(p => p, p => Directory.GetLastWriteTimeUtc(Path.Combine(game, p)));
            journal = journal with { Steps = steps, DirectoryTimes = directoryTimes };
            Save(staging, journal);
            File.Move(Path.Combine(staging, "owner.new"), owner);
            for (var i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                Forward(game, staging, step);
                afterMutation?.Invoke(step.Action);
                journal = journal with { Completed = i + 1 };
                Save(staging, journal);
                report?.Invoke($"{step.Action} {step.Path}");
            }
            Save(staging, journal with { Committed = true });
        }
        catch (Exception ex) when (ex is not SimulatedInterruption)
        {
            RecoverLocked(game, staging);
            throw;
        }
        FinishCommit(game, staging);
        report?.Invoke("基础包更新已提交");
    }

    internal sealed class SimulatedInterruption : Exception;

    private static (string Game, string Stage) Roots(string game, string staging)
    {
        game = UpdatePaths.Absolute(game, workingDirectory: true);
        staging = UpdatePaths.Absolute(staging, workingDirectory: true);
        if (!string.Equals(Path.GetPathRoot(game), Path.GetPathRoot(staging), StringComparison.OrdinalIgnoreCase) ||
            UpdatePaths.Within(game, staging) || UpdatePaths.Within(staging, game))
            throw new UpdateException(UpdateFailure.Staging, "游戏目录与暂存目录须同卷，且不能相互包含。");
        return (game, staging);
    }

    private sealed class InstallLocks : IDisposable
    {
        private readonly List<Mutex> locks = [];
        public InstallLocks(string game, string staging)
        {
            try
            {
                foreach (var path in new[] { game, staging })
                {
                    var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
                    var mutex = new Mutex(false, @"Global\Nikkiward.GameUpdate." + id);
                    try
                    {
                        if (!mutex.WaitOne(0))
                        {
                            mutex.Dispose();
                            throw new UpdateException(UpdateFailure.Busy, "此目录正在更新。");
                        }
                    }
                    catch (AbandonedMutexException) { }
                    locks.Add(mutex);
                }
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            foreach (var mutex in locks) { mutex.ReleaseMutex(); mutex.Dispose(); }
        }
    }

    private static void CheckProcesses(Func<IReadOnlyList<string>?> processes)
    {
        IReadOnlyList<string>? paths;
        try { paths = processes(); }
        catch (Exception ex) { throw new UpdateException(UpdateFailure.ProcessUnknown, "无法查询运行中的进程。", ex); }
        if (paths == null)
            throw new UpdateException(UpdateFailure.ProcessUnknown, "无法确定运行中的进程路径。");
        if (paths.Count > 0)
            throw new UpdateException(UpdateFailure.ProcessRunning, $"游戏或启动器仍在运行：{paths[0]}");
    }

    private static byte[] ReadVersion(string path, int target)
    {
        try
        {
            CheckWritable(path);
            byte[] bytes = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(bytes);
            var reader = new Utf8JsonReader(bytes);
            int start = -1, end = 0;
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1 || !reader.ValueTextEquals("version")) continue;
                reader.Read();
                if (start >= 0 || reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var old) || old <= 0 || old >= target)
                    throw new UpdateException(UpdateFailure.Version, "当前构建号无效或已达到目标版本。");
                start = checked((int)reader.TokenStartIndex);
                end = checked((int)reader.BytesConsumed);
            }
            if (start < 0) throw new UpdateException(UpdateFailure.Version, "product.db 缺少构建号。");
            return [.. bytes.AsSpan(0, start), .. JsonSerializer.SerializeToUtf8Bytes(target), .. bytes.AsSpan(end)];
        }
        catch (JsonException ex) { throw new UpdateException(UpdateFailure.Version, "product.db 格式无效。", ex); }
        catch (Exception ex) when (ex is not UpdateException && ex is IOException or UnauthorizedAccessException)
        { throw new UpdateException(UpdateFailure.Version, "无法读取 product.db。", ex); }
    }

    private static Step[] Plan(string game, IReadOnlyList<string> cleanup, PackageEntry[] entries)
    {
        var steps = new List<Step>();
        var removed = new List<string>();
        foreach (var path in cleanup)
        {
            if (removed.Any(p => UpdatePaths.Within(path, p))) continue;
            var target = UpdatePaths.Absolute(Path.Combine(game, path));
            if (File.Exists(target) || UpdatePaths.Within(path, "product.db") || UpdatePaths.Within(path, Owner))
                throw new UpdateException(UpdateFailure.UnsafePath, $"无法清理该目录：{path}");
            if (!Directory.Exists(target)) continue;
            CheckTree(target, checkLocks: true);
            removed.Add(path);
            steps.Add(new(Action.Cleanup, path));
        }
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (UpdatePaths.Within(entry.Path, "product.db") || UpdatePaths.Within(entry.Path, Owner))
                throw new UpdateException(UpdateFailure.UnsafePath, "安装包与版本文件冲突。");
            UpdatePaths.Absolute(Path.Combine(game, entry.Path));
            for (var path = entry.IsDirectory ? entry.Path : Path.GetDirectoryName(entry.Path);
                 !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
                directories.Add(path);
        }
        foreach (var dir in directories.OrderBy(p => p.Length))
        {
            var target = Path.Combine(game, dir);
            if (removed.Any(p => UpdatePaths.Within(dir, p)) || !Directory.Exists(target))
            {
                if (File.Exists(target) && !removed.Any(p => UpdatePaths.Within(dir, p)))
                    throw new IOException($"目标目录被文件占用：{dir}");
                steps.Add(new(Action.Directory, dir));
            }
        }
        foreach (var entry in entries.Where(e => !e.IsDirectory))
        {
            var target = Path.Combine(game, entry.Path);
            if (!removed.Any(p => UpdatePaths.Within(entry.Path, p)))
            {
                if (Directory.Exists(target)) throw new IOException($"目标文件被目录占用：{entry.Path}");
                if (File.Exists(target))
                {
                    CheckWritable(target);
                    steps.Add(new(Action.Backup, entry.Path));
                }
            }
            steps.Add(new(Action.Install, entry.Path));
        }
        steps.Add(new(Action.Version, "product.db"));
        return steps.ToArray();
    }

    private static void CheckTree(string dir, bool checkLocks)
    {
        if (checkLocks) CheckWritable(dir);
        foreach (var path in Directory.EnumerateFileSystemEntries(dir))
        {
            UpdatePaths.Absolute(path);
            if (Directory.Exists(path)) CheckTree(path, checkLocks);
            else if (checkLocks)
            {
                CheckWritable(path);
                try
                {
                    using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                }
                catch (IOException ex) when (ex.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021))
                {
                    throw new UpdateException(UpdateFailure.Busy, $"文件被占用：{path}", ex);
                }
            }
        }
    }

    private static void CheckWritable(string path)
    {
        if (File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
            throw new UpdateException(UpdateFailure.ReadOnly, $"旧文件或目录为只读，无法提交清理：{path}");
    }

    private static void Forward(string game, string staging, Step step)
    {
        var target = UpdatePaths.Absolute(Path.Combine(game, step.Path));
        var old = UpdatePaths.Absolute(Path.Combine(staging, "old", step.Path));
        switch (step.Action)
        {
            case Action.Cleanup:
                Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                Directory.Move(target, old);
                break;
            case Action.Directory:
                Directory.CreateDirectory(target);
                break;
            case Action.Backup:
                Directory.CreateDirectory(Path.GetDirectoryName(old)!);
                File.Move(target, old);
                break;
            case Action.Install:
                File.Move(Path.Combine(staging, "new", step.Path), target);
                break;
            case Action.Version:
                File.Replace(Path.Combine(staging, "version.new"), target, Path.Combine(staging, "version.old"));
                break;
        }
    }

    private static void Save(string staging, Journal journal)
    {
        var temp = Path.Combine(staging, "journal.tmp");
        File.Delete(temp);
        using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, journal);
            file.Flush(true);
        }
        File.Move(temp, Path.Combine(staging, "journal.json"), true);
    }

    internal static void Recover(string game, string staging)
    {
        (game, staging) = Roots(game, staging);
        using var locks = new InstallLocks(game, staging);
        RecoverLocked(game, staging);
    }

    private static void RecoverLocked(string game, string staging)
    {
        var log = UpdatePaths.Absolute(Path.Combine(staging, "journal.json"));
        if (!File.Exists(log))
        {
            var temp = UpdatePaths.Absolute(Path.Combine(staging, "journal.tmp"));
            if (File.Exists(temp) && Directory.EnumerateFileSystemEntries(staging).Count() == 1)
                File.Delete(temp);
            return;
        }
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllBytes(log));
        if (journal == null || !game.Equals(journal.Game, StringComparison.OrdinalIgnoreCase) ||
            journal.DirectoryTimes == null || journal.Steps == null || journal.Completed < 0 || journal.Completed > journal.Steps.Length)
            throw new UpdateException(UpdateFailure.Journal, "日志不属于此安装或进度无效。");
        CheckTree(staging, checkLocks: false);
        foreach (var dir in journal.DirectoryTimes.Keys)
            UpdatePaths.Absolute(Path.Combine(game, dir.Length == 0 ? "" : UpdatePaths.Relative(dir)));
        foreach (var step in journal.Steps)
        {
            var path = UpdatePaths.Relative(step.Path, subtree: step.Action != Action.Directory);
            if (UpdatePaths.Within(path, Owner) || !Enum.IsDefined(step.Action) || (step.Action == Action.Version) != path.Equals("product.db", StringComparison.OrdinalIgnoreCase))
                throw new UpdateException(UpdateFailure.Journal, "日志操作无效。");
            UpdatePaths.Absolute(Path.Combine(game, path));
        }
        if (journal.Committed)
        {
            FinishCommit(game, staging);
            return;
        }
        var owner = UpdatePaths.Absolute(Path.Combine(game, Owner));
        if (File.Exists(owner) && string.Equals(File.ReadAllText(owner), staging, StringComparison.OrdinalIgnoreCase))
        {
            for (var i = Math.Min(journal.Completed, journal.Steps.Length - 1); i >= 0; i--)
            {
                var step = journal.Steps[i];
                var target = UpdatePaths.Absolute(Path.Combine(game, step.Path));
                var old = Path.Combine(staging, "old", step.Path);
                var fresh = Path.Combine(staging, "new", step.Path);
                switch (step.Action)
                {
                    case Action.Version when File.Exists(Path.Combine(staging, "version.old")):
                        var versionBackup = Path.Combine(staging, "version.old");
                        if (File.Exists(target)) File.Replace(versionBackup, target, null);
                        else File.Move(versionBackup, target);
                        break;
                    case Action.Install when !File.Exists(fresh) && File.Exists(target):
                        Directory.CreateDirectory(Path.GetDirectoryName(fresh)!);
                        File.Move(target, fresh);
                        break;
                    case Action.Backup when File.Exists(old):
                        File.Move(old, target);
                        break;
                    case Action.Directory when Directory.Exists(target):
                        Directory.Delete(target);
                        break;
                    case Action.Cleanup when Directory.Exists(old):
                        Directory.Move(old, target);
                        break;
                }
                journal = i == 0 ? journal with { Steps = [], Completed = 0 } : journal with { Completed = i - 1 };
                Save(staging, journal);
            }
            foreach (var (dir, time) in journal.DirectoryTimes.Where(item => item.Key.Length != 0))
                Directory.SetLastWriteTimeUtc(Path.Combine(game, dir), time);
            File.Move(owner, Path.Combine(staging, "owner.released"));
        }
        else if (journal.Steps.Length > 0 && !File.Exists(Path.Combine(staging, "owner.new")))
            throw new UpdateException(UpdateFailure.Journal, "事务标记与暂存日志不一致，已保留备份。");
        if (File.Exists(Path.Combine(staging, "owner.released")) && !File.Exists(owner) &&
            journal.DirectoryTimes.TryGetValue("", out var rootTime))
            Directory.SetLastWriteTimeUtc(game, rootTime);
        Clean(staging);
    }

    private static void FinishCommit(string game, string staging)
    {
        try { Clean(staging, game); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UpdateException(UpdateFailure.CleanupPending, "更新已提交，暂存清理未完成。请恢复以继续清理。", ex);
        }
    }

    private static void Clean(string staging, string? committedGame = null)
    {
        foreach (var dir in new[] { "old", "new" })
        {
            var path = Path.Combine(staging, dir);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        foreach (var name in new[] { "version.old", "version.new", "owner.new", "owner.released", "journal.tmp" })
            File.Delete(Path.Combine(staging, name));
        if (committedGame != null)
        {
            var owner = UpdatePaths.Absolute(Path.Combine(committedGame, Owner));
            if (File.Exists(owner) && string.Equals(File.ReadAllText(owner), staging, StringComparison.OrdinalIgnoreCase))
                File.Delete(owner);
        }
        File.Delete(Path.Combine(staging, "journal.json"));
    }
}
