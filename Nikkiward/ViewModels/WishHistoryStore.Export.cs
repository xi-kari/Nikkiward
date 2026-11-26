using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Nikkiward.Features.Gallery;
using Nikkiward.Services;

namespace Nikkiward.ViewModels;

public sealed partial class WishHistoryStore
{
    private const uint VolumeNameGuid = 1;
    private const uint VolumeNameNt = 2;

    internal async Task ExportFileAsync(string path, IReadOnlyList<string> protectedRoots)
    {
        PhotoStudioStore.CheckDestination(path, protectedRoots);
        path = Path.GetFullPath(path);
        var name = Path.GetFileName(path);
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("请选择有效的 JSON 文件名。");

        using var parent = OpenDirectory(Path.GetDirectoryName(path)!, FileShare.ReadWrite)
            ?? throw new DirectoryNotFoundException("所选导出目录不存在。");
        var physicalDirectory = FinalPath(parent, VolumeNameNt);
        foreach (var root in protectedRoots)
        {
            using var directory = OpenDirectory(root, FileShare.ReadWrite | FileShare.Delete);
            if (directory != null && VariantPathPolicy.IsSameOrDescendant(physicalDirectory, FinalPath(directory, VolumeNameNt)))
                throw new IOException("请选择图库和游戏目录以外的本地导出目录。");
        }

        var target = Path.Combine(FinalPath(parent, VolumeNameGuid), name);
        var exists = File.Exists(target);
        if (name.Equals(Path.GetFileName(HistoryPath), StringComparison.OrdinalIgnoreCase))
        {
            using var historyDirectory = OpenDirectory(RootPath, FileShare.ReadWrite | FileShare.Delete);
            if (historyDirectory != null && physicalDirectory.Equals(FinalPath(historyDirectory, VolumeNameNt), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("请选择历史数据库以外的导出位置。");
        }
        if (exists && File.Exists(HistoryPath) && WindowsHardLinkIdentity.AreSameFile(target, HistoryPath))
            throw new InvalidDataException("请选择历史数据库以外的导出位置。");

        using var file = new AtomicFile(target);
        await ExportAsync(file.Stream);
        file.Commit(overwrite: exists);
    }

    private static SafeFileHandle? OpenDirectory(string path, FileShare share)
    {
        const uint backupSemantics = 0x02000000;
        var handle = CreateFileW(WindowsHardLink.ToExtendedWindowsPath(path), 0, share, 0, FileMode.Open, backupSemantics, 0);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        if (error is 2 or 3) return null;
        throw new IOException("无法核实导出目录与游戏、图库目录的位置。", new Win32Exception(error));
    }

    private static string FinalPath(SafeFileHandle handle, uint volumeName)
    {
        var buffer = new StringBuilder(260);
        while (true)
        {
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, volumeName);
            if (length == 0)
                throw new IOException("无法解析目录的实际位置，请选择本地导出目录。", new Win32Exception(Marshal.GetLastPInvokeError()));
            if (length < buffer.Capacity) return buffer.ToString();
            buffer.EnsureCapacity(checked((int)length + 1));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, nint security,
        FileMode disposition, uint flags, nint template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
}
