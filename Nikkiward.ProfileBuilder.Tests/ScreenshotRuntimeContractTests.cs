using Nikkiward.Features.Gallery;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;

internal static class ScreenshotRuntimeContractTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        (nameof(ScreenshotSelectionAndLegacyOutput), ScreenshotSelectionAndLegacyOutput),
        (nameof(ScreenshotRechecksBeforePublish), ScreenshotRechecksBeforePublish),
        (nameof(ThumbnailCleanupPreservesPhotos), ThumbnailCleanupPreservesPhotos),
        (nameof(BulkCleanupPreservesAliasedThumbnails), BulkCleanupPreservesAliasedThumbnails),
        (nameof(TestCaptureDisclosesDisplayAndClipboard), TestCaptureDisclosesDisplayAndClipboard),
        ("capture closes drawing before handing off the frame", () => TestHandoff("success")),
        ("capture disposes a rejected target", () => TestHandoff("rejected")),
        ("capture disposes resources after a drawing error", () => TestHandoff("error")),
        ("capture disposes resources when cancellation wins", () => TestHandoff("cancelled")),
        ("cancelled screenshot pair publishes no files", () => TestStaging(true)),
        ("failed screenshot encoder leaves no partial image", () => TestStaging(false)),
        ("screenshot display ids resolve through the window display area", DisplayIdsUseWindowDisplayArea),
    ];

    private static async Task TestStaging(bool cancel)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            using var cancellation = new CancellationTokenSource();
            using (var output = new Nikkiward.Services.ScreenshotOutput(() => []))
            {
                await output.StageAsync(Path.Combine(folder.FullName, "main.avif"),
                    stream => stream.WriteAsync(new byte[] { 1 }).AsTask(), cancellation.Token);
                try
                {
                    await output.StageAsync(Path.Combine(folder.FullName, "sdr.png"), stream =>
                    {
                        stream.WriteByte(2);
                        if (!cancel) throw new IOException();
                        cancellation.Cancel();
                        return Task.CompletedTask;
                    }, cancellation.Token);
                    output.Publish(cancellation.Token);
                    throw new InvalidOperationException("incomplete capture published");
                }
                catch (OperationCanceledException) when (cancel) { }
                catch (IOException) when (!cancel) { }
            }

            Assert(!folder.EnumerateFileSystemInfos().Any(), "temporary and final images must be absent");
        }
        finally
        {
            folder.Delete(true);
        }
    }

    private sealed class TestJunction(string path) : IDisposable
    {
        public void Dispose() => Directory.Delete(path, recursive: false);
    }

    private static async Task<TestJunction> JunctionAsync(string parent, string name, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J {name} {target}")
        {
            WorkingDirectory = parent, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
        };
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync();
        Assert(process.ExitCode == 0, "create directory junction");
        return new TestJunction(Path.Combine(parent, name));
    }

    private static async Task MustReject(Func<Task> operation, string reason)
    {
        try { await operation(); }
        catch (IOException ex) when (ex.Message.Contains(reason, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("expected refusal: " + reason);
    }

    private static async Task ScreenshotSelectionAndLegacyOutput()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var album = temp.CreateSubdirectory("album").FullName;
            using var albumLink = await JunctionAsync(temp.FullName, "linked", "album");
            var linked = Path.Combine(temp.FullName, "linked");
            var game = temp.CreateSubdirectory("game").FullName;
            var relocated = temp.CreateSubdirectory("photos").FullName;
            var savedFolder = Directory.CreateDirectory(Path.Combine(game, "X6Game", "Saved")).FullName;
            using var gamePhotosLink = await JunctionAsync(savedFolder, "GamePlayPhotos", Path.GetRelativePath(savedFolder, relocated));
            var store = new JsonUserSettingsStore(temp.FullName);
            await store.SaveAsync(new UserSettings {
                Profiles = [new() { ProfileId = "game", GameRootPath = game }],
                GalleryProfiles = [new() { ProfileId = "inactive", RootPath = linked }],
                Screenshot = new() { FolderPath = album }
            });
            var session = new WindowSettingsSession(store);
            await session.LoadAsync();
            var page = new MainPageViewModel(session, new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
            using var output = new ScreenshotOutput(() => page.GalleryReadOnlyRoots);
            foreach (var path in new[] { album, linked, game, relocated, Path.Combine(album, "new"), Path.Combine(linked, "new") })
            {
                await MustReject(() => page.SaveScreenshotSettingsAsync(page.ScreenshotSettings with { FolderPath = path }), "设置 → 游戏截图");
                await MustReject(() => output.StageAsync(Path.Combine(path, "test.png"),
                    _ => throw new InvalidOperationException("encoder invoked"), default), "设置 → 游戏截图");
            }
            Assert(!Directory.EnumerateFileSystemEntries(album).Any(), "protected folder must remain empty");
            Assert((await store.LoadAsync()).Screenshot.FolderPath == album, "rejected choice changed old settings");
            var sibling = Path.Combine(temp.FullName, "album-other");
            await page.SaveScreenshotSettingsAsync(page.ScreenshotSettings with { FolderPath = sibling });
            await output.StageAsync(Path.Combine(sibling, "ok.png"), s => s.WriteAsync(new byte[] { 42 }).AsTask(), default);
            output.Publish(default);
            Assert(File.ReadAllBytes(Path.Combine(sibling, "ok.png")).SequenceEqual(new byte[] { 42 }), "safe sibling capture");
        }
        finally { temp.Delete(true); }
    }

    private static async Task ScreenshotRechecksBeforePublish()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            string[] roots = [];
            using (var output = new ScreenshotOutput(() => roots))
            {
                var path = Path.Combine(temp.FullName, "main.avif");
                await output.StageAsync(path, _ => Task.CompletedTask, default);
                roots = [temp.FullName];
                await MustReject(() => output.StageAsync(Path.Combine(temp.FullName, "sdr.png"), _ => Task.CompletedTask, default), "截图目录");
                await MustReject(() => { output.Publish(default); return Task.CompletedTask; }, "截图目录");
            }
            Assert(!temp.EnumerateFileSystemInfos().Any(), "no temporary files or published images");
        }
        finally { temp.Delete(true); }
    }

    private static async Task ThumbnailCleanupPreservesPhotos()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var cache = temp.CreateSubdirectory(Path.Combine("GalleryCache", "Thumbnails")).FullName;
            using var cacheLink = await JunctionAsync(temp.FullName, "linked", @"GalleryCache\Thumbnails");
            var source = Path.Combine(cache, "旅行.jpg");
            byte[] original = [1, 2, 3];
            await File.WriteAllBytesAsync(source, original);
            var owned = (await GalleryThumbnailFile.GetOrCreateAsync(cache, null, new FileInfo(source), true,
                () => Task.FromResult<byte[]?>([5]), default))!.FilePath!;
            var legacy = Path.Combine(cache, Path.GetFileName(owned)["nikkiward-thumb-".Length..]);
            File.Copy(owned, legacy);
            var temporary = owned + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, [6]);
            var nested = Path.Combine(Directory.CreateDirectory(Path.Combine(cache, "originals")).FullName, Path.GetFileName(owned));
            File.WriteAllBytes(nested, original);
            var ambiguous = Path.Combine(cache, "0123456789abcdef0123456789abcdef.jpg");
            File.WriteAllBytes(ambiguous, original);
            var unknown = owned + ".other.tmp";
            File.WriteAllBytes(unknown, original);
            var service = new SettingsMaintenanceService(Path.Combine(temp.FullName, "settings.json"), Path.Combine(temp.FullName, "browser"));
            foreach (var root in new[] { cache, Path.GetDirectoryName(cache)!, Path.GetDirectoryName(nested)!, Path.Combine(temp.FullName, "linked") })
            {
                await MustReject(() => GalleryThumbnailCache.ClearAsync(cache, [root], default), "重叠");
                await MustReject(() => service.ClearCachesAsync(false, [root]), "重叠");
                Assert(File.Exists(owned) && File.Exists(temporary), "refused cleanup removed a cache file");
            }
            var result = await service.ClearCachesAsync(false, [Path.Combine(temp.FullName, "other")]);
            Assert(result.DeletedFileCount == 2 && !File.Exists(owned) && !File.Exists(temporary), "only generated entries removed");
            var cached = await GalleryThumbnailFile.GetOrCreateAsync(cache, null, new FileInfo(source), true,
                () => throw new InvalidOperationException("legacy thumbnail was not reused"), default);
            Assert(cached?.FilePath == legacy, "read-only legacy cache hit");
            foreach (var path in new[] { source, nested, unknown, ambiguous })
                Assert(File.ReadAllBytes(path).SequenceEqual(original), "original changed: " + path);
        }
        finally { temp.Delete(true); }
    }

    private static async Task BulkCleanupPreservesAliasedThumbnails()
    {
        var temp = Directory.CreateTempSubdirectory();
        try
        {
            var logs = temp.CreateSubdirectory("Logs").FullName;
            var galleryCache = temp.CreateSubdirectory("GalleryCache").FullName;
            using var thumbnailsLink = await JunctionAsync(galleryCache, "Thumbnails", @"..\Logs");
            var photo = Path.Combine(logs, "搭配.jpg");
            File.WriteAllBytes(photo, [7]);
            var service = new SettingsMaintenanceService(Path.Combine(temp.FullName, "settings.json"), Path.Combine(temp.FullName, "browser"));
            var cleared = await service.ClearCachesAsync(false, []);
            Assert(cleared.FailedPaths.Contains(logs), "overlapping recursive cleaner must report a skipped directory");
            Assert(File.ReadAllBytes(photo).SequenceEqual(new byte[] { 7 }), "bulk cleaner followed its second route into thumbnails");
        }
        finally { temp.Delete(true); }
    }

    private static Task TestCaptureDisclosesDisplayAndClipboard()
    {
        var root = Path.Combine(FindWorkspaceRoot(), "Nikkiward");
        var text = File.ReadAllText(Path.Combine(root, "Web/pages/settings/sections/Screenshot.js"));
        Assert(text.Contains("截取当前显示器") && text.Contains("所有可见内容") &&
            text.Contains("其他窗口") && text.Contains("复制到剪贴板"), "display capture disclosure");
        return Task.CompletedTask;
    }

    private sealed class Resource : IDisposable
    {
        public int Closes { get; private set; }
        public void Dispose() => Closes++;
    }

    private static Task TestHandoff(string outcome)
    {
        var frame = new Resource();
        var target = new Resource();
        var session = new Resource();
        using var cancel = new CancellationTokenSource();
        var result = new TaskCompletionSource<Resource>();
        using var registration = cancel.Token.Register(() => result.TrySetCanceled(cancel.Token));
        try
        {
            Nikkiward.Services.ScreenshotFrameHandoff.Draw(frame, _ => target, _ => session,
                (_, _) =>
                {
                    if (outcome == "error") throw new IOException();
                    if (outcome == "cancelled") cancel.Cancel();
                },
                value =>
                {
                    Assert(session.Closes == 1, "drawing must close before delivery");
                    return outcome != "rejected" && result.TrySetResult(value);
                });
            Assert(outcome != "error", "drawing error must propagate");
        }
        catch (IOException) when (outcome == "error") { }

        if (outcome == "success")
        {
            Assert(result.Task.IsCompletedSuccessfully && target.Closes == 0, "caller owns target");
            result.Task.Result.Dispose();
        }
        Assert(frame.Closes == 1 && target.Closes == 1 && session.Closes == 1, "exactly one close");
        return Task.CompletedTask;
    }

    private static Task DisplayIdsUseWindowDisplayArea()
    {
        var root = FindWorkspaceRoot();
        var source = File.ReadAllText(
            Path.Combine(root, "Nikkiward", "Services", "GameScreenshotService.cs"));

        Assert(
            source.Contains("Win32Interop.GetWindowIdFromWindow", StringComparison.Ordinal),
            "screenshot capture must resolve a WinUI window id");
        Assert(
            source.Contains("DisplayArea.GetFromWindowId", StringComparison.Ordinal),
            "screenshot capture must resolve the display through DisplayArea");
        Assert(
            source.Contains("GraphicsCaptureItem.TryCreateFromDisplayId", StringComparison.Ordinal),
            "display test capture must use the resolved display id");
        Assert(
            !source.Contains("MonitorFromWindow", StringComparison.Ordinal),
            "screenshot capture must not cast HMONITOR to DisplayId");
        return Task.CompletedTask;
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Nikkiward", "Nikkiward.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Workspace root not found.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
