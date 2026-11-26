using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Nikkiward.Services;

internal static class PhotoDirectoryPolicy
{
    internal static string ScreenshotFolder(string folder, IEnumerable<string> roots) =>
        CheckFolder(folder, roots, VariantPathPolicy.IsSameOrDescendant,
            "截图目录位于游戏或图库目录中。请到「设置 → 游戏截图」选择其他目录。");

    internal static string ThumbnailFolder(string folder, IEnumerable<string> roots) =>
        CheckFolder(folder, roots, VariantPathPolicy.PathsOverlap,
            "缩略图缓存与游戏、图库、工坊导出或收藏保护目录重叠，已停止清理。");

    private static string CheckFolder(string folder, IEnumerable<string> roots, Func<string, string, bool> conflicts, string message)
    {
        var actual = Resolve(folder);
        foreach (var root in roots)
        {
            if (conflicts(folder, root) || conflicts(actual, Resolve(root)))
                throw new IOException(message);
        }
        return actual;
    }

    private static string Resolve(string folder)
    {
        if (!Path.IsPathFullyQualified(folder)) throw new IOException($"无法核实目录：{folder}");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        using var handle = CreateFileW(path, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open,
            0x02000000 /* FILE_FLAG_BACKUP_SEMANTICS */, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error is 2 or 3)
            {
                if (new DirectoryInfo(path).LinkTarget != null) throw new IOException($"目录链接的目标不可用：{path}");
                var parent = Path.GetDirectoryName(path);
                return parent == null ? path : Path.Combine(Resolve(parent), Path.GetFileName(path));
            }
            throw new IOException($"无法核实目录：{path}", new Win32Exception(error));
        }

        var buffer = new StringBuilder(512);
        var size = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (size >= buffer.Capacity)
        {
            buffer.EnsureCapacity(checked((int)size + 1));
            size = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        }
        if (size == 0 || size >= buffer.Capacity) throw new IOException($"无法解析目录的实际位置：{path}");
        return buffer.ToString();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, IntPtr security,
        FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
}
