internal static class ScreenshotRuntimeContractTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("screenshot display ids resolve through the window display area", DisplayIdsUseWindowDisplayArea),
    ];

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
