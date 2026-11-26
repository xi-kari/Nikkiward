using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Nikkiward.Services;

internal sealed class AtomicFile : IDisposable
{
    private const uint MoveFileReplaceExisting = 0x1;
    private const uint MoveFileWriteThrough = 0x8;
    private readonly string _destination;
    private readonly string _temporaryPath;

    public AtomicFile(string destination)
    {
        _destination = destination;
        _temporaryPath = $"{destination}.{Guid.NewGuid():N}.tmp";
        Stream = new FileStream(_temporaryPath, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 64 * 1024, FileOptions.Asynchronous);
    }

    public FileStream Stream { get; }

    public void Commit(bool overwrite, CancellationToken cancellationToken = default)
    {
        Stream.Flush(flushToDisk: true);
        Stream.Dispose();
        cancellationToken.ThrowIfCancellationRequested();
        // The recovery record's directory entry must be durable before changing the registry.
        var flags = overwrite ? MoveFileReplaceExisting | MoveFileWriteThrough : MoveFileWriteThrough;
        if (!MoveFileEx(WindowsHardLink.ToExtendedWindowsPath(_temporaryPath),
                        WindowsHardLink.ToExtendedWindowsPath(_destination), flags))
        {
            throw new IOException("无法发布临时文件。", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    public void Dispose()
    {
        Stream.Dispose();
        File.Delete(_temporaryPath);
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingPath, string newPath, uint flags);
}
