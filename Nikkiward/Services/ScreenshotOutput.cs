namespace Nikkiward.Services;

internal sealed class ScreenshotOutput(Func<IReadOnlyList<string>> readOnlyRoots) : IDisposable
{
    private readonly List<(string Temporary, string Final)> _files = [];

    public async Task StageAsync(string path, Func<Stream, Task> encode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var folder = PhotoDirectoryPolicy.ScreenshotFolder(Path.GetDirectoryName(path)!, readOnlyRoots());
        Directory.CreateDirectory(folder);
        path = Path.Combine(folder, Path.GetFileName(path));
        var temporary = Path.Combine(folder, $"{Guid.NewGuid():N}.tmp");
        await using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        _files.Add((temporary, path));
        await encode(stream);
    }

    public void Publish(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        foreach (var (temporary, path) in _files)
        {
            PhotoDirectoryPolicy.ScreenshotFolder(Path.GetDirectoryName(path)!, readOnlyRoots());
            File.Move(temporary, path);
        }
    }

    public void Dispose()
    {
        foreach (var (temporary, _) in _files)
        {
            File.Delete(temporary);
        }
    }
}
