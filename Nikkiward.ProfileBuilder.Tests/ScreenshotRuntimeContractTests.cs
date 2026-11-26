internal static class ScreenshotRuntimeContractTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
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
            using (var output = new Nikkiward.Services.ScreenshotOutput())
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
