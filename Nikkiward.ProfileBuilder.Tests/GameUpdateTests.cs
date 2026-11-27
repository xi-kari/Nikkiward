using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.VisualBasic.FileIO;
using Nikkiward.Features.GameUpdate;

internal static class GameUpdateTests
{
    private static readonly string[] Cleanup = ["X6Game/Content/Paks", "X6Game/Binaries/Win64"];
    private const string Digest = "8d46e844c4a05931669c9fa656f0e370";
    private const string Product = """{"name":"InfinityNikki Launcher","version":2828}""";
    private static readonly Uri Url = new($"https://{PackageDescription.Host}/base.7z");
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "GameUpdate", name + ".7z");
    private static readonly Dictionary<string,string> Expected = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"InfinityNikki.exe"] = "c9b475499fa3c69c4d1de2cce3a44576d8fd4df2ade54d3c07a86d4460a1165e",
        [@"Engine\Binaries\ThirdParty\Lib\new.dll"] = "1dd49fdf798f8ff547b206e48cfa25c01a4b92eab7ae94ec9934b009bedfc2ee",
        [@"Engine\Content\shared.txt"] = "17321de4403ebb1f4d22fd34854bb721fa19c5192657e4b0087a3b9c70adaec7",
        [@"X6Game\Binaries\Win64\X6Game-Win64-Shipping.exe"] = "760804c4e79d5573110a30304c198024b22b0da94f88f64f7f38b5fa9c8095fe",
        [@"X6Game\Binaries\Win64\Plugins\helper.dll"] = "9da7c969132ea94b289b8594e17bf5dd035bb511a1a6da584a696624c04e12b5",
        [@"X6Game\Content\Paks\Default_Core_3011_P.pak"] = "5efaa6006c989f6d90e46f977e2ecb1e7c459eeffe0e9a67736a8bb82a6a3b4d",
        [@"X6Game\Content\Paks\Default_Core_3011_P.ucas"] = "00d039ad7c533865dde99585dc267d3c4a4823149eba0176791e72c139cbcce4",
        [@"X6Game\Content\V_Activity\movie.bk2"] = "e90f91ddc6a42fd8b44f79a21c22d98137bb8551bfb88611a00c865ff7e362ce"
    };

    internal static readonly (string, Func<Task>)[] All =
    [
        Case("archive manifest hashes and UTC times", Archive),
        Case("archive rejects traversal aliases and protected entries before extraction", Unsafe),
        Case("truncated archive reports unreadable", () => Reject(UpdateFailure.Archive, () => PackageArchive.List(Fixture("truncated")))),
        Case("apply preserves saved and replaces only selected files", Success),
        Case("locked cleanup leaves original tree intact", Locked),
        Case("late sharing failure rolls back previous renames", LateLock),
        Case("recovery handles unpublished journal and missing version target", RecoveryEdges),
        Case("committed cleanup resumes without reverting version", Committed),
        Case("recover after cleanup rename", () => Interrupted(PackageInstall.Action.Cleanup)),
        Case("recover after package file rename", () => Interrupted(PackageInstall.Action.Install)),
        Case("recover after version replacement", () => Interrupted(PackageInstall.Action.Version)),
        Case("replacement preserves other hard link", HardLink),
        Case("preconditions refuse before any writes", Preconditions),
        Case("caller process list is checked again before installation", ProcessRecheck),
        Case("optional official package matches snapshot", Sample)
    ];
    private static (string, Func<Task>) Case(string name, Action test) =>
        ("game update " + name, () => { test(); return Task.CompletedTask; });

    internal static void Check(bool ok, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(ok))] string? expr = null)
    { if (!ok) throw new Exception(expr); }

    private static UpdateException Reject(UpdateFailure reason, Action test)
    {
        try { test(); }
        catch (UpdateException ex) when (ex.Reason == reason) { return ex; }
        throw new Exception($"缺少预期拒绝：{reason}");
    }

    private sealed class Copy : IDisposable
    {
        internal readonly string Root = Directory.CreateTempSubdirectory().FullName;
        internal string Game => Path.Combine(Root, "game");
        internal string Stage => Path.Combine(Root, "stage");
        internal string At(string file) => Path.Combine(Game, file);
        internal Copy()
        {
            foreach (var path in new[] { "Engine/retained", "X6Game/Saved/photo", "X6Game/Saved/Paks/user", "X6Game/ScreenShot/img",
                "DownloadCache/cache", "X6Game/Content/Paks/old2828", "X6Game/Binaries/Win64/old2828", "X6Game/Binaries/Win64/extra",
                "InfinityNikki.exe", "Engine/Content/shared.txt", "product.db" })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(At(path))!);
                File.WriteAllText(At(path), path switch { "product.db" => Product, "Engine/Content/shared.txt" => "unchanged across versions\n", _ => path });
            }
        }
        internal void Apply(Action<PackageInstall.Action>? hook = null) =>
            PackageInstall.Apply(new(3011, Url, Digest, Cleanup), Fixture("base-3011"), Game, Stage, () => [], afterMutation: hook);
        public void Dispose() => Directory.Delete(Root, true);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static string[] Snapshot(string dir) => Directory.EnumerateFileSystemEntries(dir, "*", System.IO.SearchOption.AllDirectories)
        .Select(p => Path.GetRelativePath(dir, p) + (Directory.Exists(p) ? "/" : " " + Hash(p))).Order(StringComparer.Ordinal).ToArray();

    private static void Archive()
    {
        using var copy = new Copy();
        var manifest = PackageArchive.List(Fixture("base-3011"));
        Check(manifest.Length == 20 && manifest.Count(e => e.IsDirectory) == 12 && manifest.Sum(e => e.Size) == 7255);
        PackageArchive.Extract(Fixture("base-3011"), copy.Stage);
        foreach (var entry in manifest.Where(e => !e.IsDirectory))
        {
            var path = Path.Combine(copy.Stage, entry.Path);
            Check(Hash(path) == Expected[entry.Path] && new FileInfo(path).Length == entry.Size);
            Check(File.GetLastWriteTimeUtc(path) == new DateTime(2026, 11, 20, 10, 44, 19, DateTimeKind.Utc));
        }
    }

    private static void Unsafe()
    {
        using var copy = new Copy();
        foreach (var path in new[] { "../escape", @"C:\escape", @"\\server\x", "con.txt", "foo:stream", "X6Game/Saved./p",
            "DownloadCache/p", "X6Game/SCREEN~1/p" })
            Reject(UpdateFailure.UnsafePath, () => UpdatePaths.Relative(path));
        Reject(UpdateFailure.UnsafePath, () => PackageArchive.Extract(Fixture("protected-entry"), copy.Stage));
        Reject(UpdateFailure.UnsafePath, () => PackageArchive.Extract(Fixture("base-3011"), copy.At("X6Game/Saved")));
        Check(UpdatePaths.Within(copy.Game, Path.GetPathRoot(copy.Game)!));
        Check(!Directory.Exists(copy.Stage));
    }

    private static void Success()
    {
        using var copy = new Copy();
        var before = Snapshot(copy.Game);
        copy.Apply();
        foreach (var item in Expected) Check(Hash(copy.At(item.Key)) == item.Value);
        foreach (var dir in Cleanup)
        {
            var paths = Directory.GetFiles(copy.At(dir), "*", System.IO.SearchOption.AllDirectories);
            Check(paths.All(p => Expected.ContainsKey(Path.GetRelativePath(copy.Game, p))));
        }
        foreach (var p in new[] { "Engine/retained", "X6Game/Saved/photo", "X6Game/Saved/Paks/user", "X6Game/ScreenShot/img", "DownloadCache/cache" })
            Check(before.Contains(Path.GetRelativePath(copy.Game, copy.At(p)) + " " + Hash(copy.At(p))));
        Check(File.ReadAllText(copy.At("product.db")) == Product.Replace("2828", "3011"));
        Check(!Directory.EnumerateFileSystemEntries(copy.Stage).Any() && File.Exists(Fixture("base-3011")));
    }

    private static void Locked()
    {
        using var copy = new Copy();
        var original = Snapshot(copy.Game);
        var locked = Path.GetFullPath(copy.At("X6Game/Binaries/Win64/extra"));
        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var busy = Reject(UpdateFailure.Busy, () => copy.Apply());
            Check(busy.Message.Contains(locked, StringComparison.OrdinalIgnoreCase));
            Reject(UpdateFailure.ProcessRunning, () => PackageInstall.Apply(new(3011, Url, Digest, Cleanup),
                Fixture("base-3011"), copy.Game, copy.Stage, () => [copy.At("InfinityNikki.exe")]));
        }
        Check(original.SequenceEqual(Snapshot(copy.Game)) && !Directory.Exists(copy.Stage));
    }

    private static void Interrupted(PackageInstall.Action action)
    {
        using var copy = new Copy();
        var original = Snapshot(copy.Game);
        try { copy.Apply(step => { if (step == action) throw new PackageInstall.SimulatedInterruption(); }); }
        catch (PackageInstall.SimulatedInterruption) { }
        Check(File.Exists(Path.Combine(copy.Stage, "journal.json")));
        Reject(UpdateFailure.Busy, () => PackageInstall.Apply(new(3012, Url, Digest, Cleanup), Fixture("base-3011"),
            copy.Game, Path.Combine(copy.Root, "second"), () => []));
        Check(Recover(["--game-update-recover", copy.Game, copy.Stage]) == 0);
        Check(original.SequenceEqual(Snapshot(copy.Game)) && !Directory.EnumerateFileSystemEntries(copy.Stage).Any());
        PackageInstall.Recover(copy.Game, copy.Stage);
        Check(original.SequenceEqual(Snapshot(copy.Game)) && !Directory.EnumerateFileSystemEntries(copy.Stage).Any());
    }

    private static void LateLock()
    {
        using var copy = new Copy();
        var original = Snapshot(copy.Game);
        FileStream? locked = null;
        try
        {
            copy.Apply(step =>
            {
                if (step == PackageInstall.Action.Cleanup && locked == null)
                    locked = File.Open(copy.At("InfinityNikki.exe"), FileMode.Open, FileAccess.Read, FileShare.None);
            });
            throw new Exception("锁定的替换目标未阻止更新");
        }
        catch (IOException) when (locked != null) { }
        finally { locked?.Dispose(); }
        Check(original.SequenceEqual(Snapshot(copy.Game)) && !Directory.EnumerateFileSystemEntries(copy.Stage).Any());
    }

    private static void RecoveryEdges()
    {
        using var copy = new Copy();
        Directory.CreateDirectory(copy.Stage);
        File.WriteAllText(Path.Combine(copy.Stage, "journal.tmp"), "{");
        PackageInstall.Recover(copy.Game, copy.Stage);
        var original = Snapshot(copy.Game);
        try { copy.Apply(step => { if (step == PackageInstall.Action.Version) throw new PackageInstall.SimulatedInterruption(); }); }
        catch (PackageInstall.SimulatedInterruption) { }
        File.Move(copy.At("product.db"), Path.Combine(copy.Stage, "version.new"));
        PackageInstall.Recover(copy.Game, copy.Stage);
        Check(original.SequenceEqual(Snapshot(copy.Game)) && !Directory.EnumerateFileSystemEntries(copy.Stage).Any());
    }

    private static void Committed()
    {
        using var copy = new Copy();
        FileStream? locked = null;
        try
        {
            Reject(UpdateFailure.CleanupPending, () => copy.Apply(step =>
            {
                if (step == PackageInstall.Action.Version)
                    locked = File.OpenRead(Path.Combine(copy.Stage, "old", "InfinityNikki.exe"));
            }));
        }
        finally { locked?.Dispose(); }
        PackageInstall.Recover(copy.Game, copy.Stage);
        Check(File.ReadAllText(copy.At("product.db")) == Product.Replace("2828", "3011"));
        Check(!Directory.EnumerateFileSystemEntries(copy.Stage).Any());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string link, string existing, IntPtr reserved);
    private static void HardLink()
    {
        using var copy = new Copy();
        var alias = Path.Combine(copy.Root, "other-channel.exe");
        Check(CreateHardLink(alias, copy.At("InfinityNikki.exe"), IntPtr.Zero));
        copy.Apply();
        Check(File.ReadAllText(alias) == "InfinityNikki.exe" && Hash(copy.At("InfinityNikki.exe")) == Expected["InfinityNikki.exe"]);
    }

    private static void Preconditions()
    {
        using var copy = new Copy();
        var original = Snapshot(copy.Game);
        void Attempt(UpdateFailure reason, string? stage = null, int version = 3011, string md5 = Digest,
            Func<IReadOnlyList<string>?>? processes = null, long space = long.MaxValue)
        {
            Reject(reason, () => PackageInstall.Apply(new(version, Url, md5, Cleanup), Fixture("base-3011"),
                copy.Game, stage ?? copy.Stage, processes ?? (() => []), availableSpace: _ => space));
            Check(original.SequenceEqual(Snapshot(copy.Game)) && !Directory.Exists(copy.Stage));
        }
        Attempt(UpdateFailure.ProcessRunning, processes: () => [copy.At("InfinityNikki.exe")]);
        var launcher = Path.Combine(copy.Root, "other-game", "launcher.exe");
        Attempt(UpdateFailure.ProcessRunning, processes: () => [launcher]);
        var first = Path.Combine(copy.Root, "other-game", "custom.exe");
        var running = Reject(UpdateFailure.ProcessRunning, () => PackageInstall.Apply(new(3011, Url, Digest, Cleanup),
            Fixture("base-3011"), copy.Game, copy.Stage, () => [first, launcher]));
        Check(running.Message.Contains(first, StringComparison.Ordinal));
        Attempt(UpdateFailure.ProcessUnknown, processes: () => null);
        Attempt(UpdateFailure.Version, version: 2828);
        Attempt(UpdateFailure.Space, space: (1L << 30) + 7254);
        Attempt(UpdateFailure.Digest, md5: new string('0', 32));
        Attempt(UpdateFailure.Staging, copy.At("stage"));
        var other = copy.Game.StartsWith("C:", StringComparison.OrdinalIgnoreCase) ? "D:" : "C:";
        Attempt(UpdateFailure.Staging, other + @"\other-stage");
        var stageFile = Path.Combine(copy.Root, "stage-file");
        File.WriteAllText(stageFile, "保留");
        Attempt(UpdateFailure.Staging, stageFile);
        var retained = copy.At("X6Game/Binaries/Win64/extra");
        var attributes = File.GetAttributes(retained);
        try
        {
            File.SetAttributes(retained, attributes | FileAttributes.ReadOnly);
            Attempt(UpdateFailure.ReadOnly);
        }
        finally { File.SetAttributes(retained, attributes); }
        File.Delete(copy.At("product.db"));
        original = Snapshot(copy.Game);
        Attempt(UpdateFailure.Version);
    }

    private static void ProcessRecheck()
    {
        using var copy = new Copy();
        var original = Snapshot(copy.Game);
        var launcher = Path.Combine(copy.Root, "other-game", "launcher.exe");
        var checks = 0;
        var running = Reject(UpdateFailure.ProcessRunning, () => PackageInstall.Apply(new(3011, Url, Digest, Cleanup),
            Fixture("base-3011"), copy.Game, copy.Stage, () => ++checks == 1 ? [] : [launcher]));
        Check(checks == 2 && running.Message.Contains(launcher, StringComparison.Ordinal));
        Check(original.SequenceEqual(Snapshot(copy.Game)) && !Directory.EnumerateFileSystemEntries(copy.Stage).Any());
    }

    private static void Sample()
    {
        var package = Environment.GetEnvironmentVariable("NIKKIWARD_UPDATE_SAMPLE");
        var csv = Environment.GetEnvironmentVariable("NIKKIWARD_UPDATE_SNAPSHOT");
        if (string.IsNullOrEmpty(package) || string.IsNullOrEmpty(csv))
        {
            Console.WriteLine("SKIP 官方包逐文件比对（需要 NIKKIWARD_UPDATE_SAMPLE 和 NIKKIWARD_UPDATE_SNAPSHOT）");
            return;
        }
        using var reader = new TextFieldParser(csv);
        reader.SetDelimiters(",");
        var header = reader.ReadFields()!;
        var path = Array.FindIndex(header, h => h.Equals("path", StringComparison.OrdinalIgnoreCase));
        var sha = Array.FindIndex(header, h => h.Equals("sha256", StringComparison.OrdinalIgnoreCase));
        var hashes = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        while (!reader.EndOfData)
        {
            var row = reader.ReadFields()!;
            hashes.Add(row[path].Replace('/', '\\'), row[sha]);
        }
        using var copy = new Copy();
        PackageArchive.Extract(package, copy.Stage);
        foreach (var file in Directory.GetFiles(copy.Stage, "*", System.IO.SearchOption.AllDirectories))
            Check(Hash(file).Equals(hashes[Path.GetRelativePath(copy.Stage, file)], StringComparison.OrdinalIgnoreCase));
    }

    internal static int Recover(string[] args)
    {
        try
        {
            if (args.Length != 3)
                throw new ArgumentException("--game-update-recover <安装目录> <暂存目录>");
            PackageInstall.Recover(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
            Console.WriteLine("RESULT game-update recover=success");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RESULT game-update recover=failed {ex.Message.ReplaceLineEndings(" ")}");
            return 1;
        }
    }

    internal static int Replay(string[] args)
    {
        try
        {
            if (args.Length != 6 || !int.TryParse(args[4], out var version))
                throw new ArgumentException("--game-update-replay <包路径> <安装副本> <暂存目录> <目标构建号> <MD5>");
            PackageInstall.Apply(new(version, Url, args[5], Cleanup), args[1], Path.GetFullPath(args[2]),
                Path.GetFullPath(args[3]), RunningPaths, Console.WriteLine);
            Console.WriteLine($"RESULT game-update version={version} applied=true");
            return 0;
        }
        catch (UpdateException ex) when (ex.Reason == UpdateFailure.CleanupPending)
        {
            Console.WriteLine($"RESULT game-update applied=true cleanup=pending {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RESULT game-update applied=false {ex.Message}");
            return 1;
        }
    }

    private static IReadOnlyList<string>? RunningPaths()
    {
        var paths = new List<string>();
        foreach (var name in new[] { "InfinityNikki", "X6Game-Win64-Shipping", "launcher", "xstarter", "InfinityNikki Launcher" })
            foreach (var process in Process.GetProcessesByName(name))
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is not string path) return null;
                        paths.Add(path);
                    }
                    catch (Win32Exception) { return null; }
                    catch (InvalidOperationException) when (process.HasExited) { }
                }
        return paths;
    }
}
