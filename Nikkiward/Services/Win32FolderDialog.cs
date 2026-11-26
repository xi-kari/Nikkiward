using System.Runtime.InteropServices;

namespace Nikkiward.Services;

/// <summary>
/// Win32 IFileOpenDialog in folder-pick mode. The WinRT FolderPicker throws
/// COMException in an elevated process — and some players insist on running
/// launchers as administrator — so this is the fallback path that keeps
/// "选择游戏目录" working there. Must be called on the UI (STA) thread.
/// </summary>
internal static class Win32FolderDialog
{
    private const uint FosStrictFileTypes = 0x4;
    private const uint FosNoTestFileCreate = 0x10000;
    private const uint FosOverwritePrompt = 0x2;
    private const uint FosPickFolders = 0x20;
    private const uint FosForceFileSystem = 0x40;
    private const uint SigdnFileSysPath = 0x80058000;
    private const int HResultCancelled = unchecked((int)0x800704C7);

    public static string? PickFolder(nint ownerWindowHandle, string title) =>
        Pick(ownerWindowHandle, title, null);

    internal static string? PickJsonSavePath(nint ownerWindowHandle, string suggestedName) =>
        Pick(ownerWindowHandle, "导出心愿共鸣记录", suggestedName);

    private static string? Pick(nint ownerWindowHandle, string title, string? suggestedName)
    {
        IFileDialog? dialog = null;
        try
        {
            dialog = suggestedName is null ? (IFileDialog)new FileOpenDialogRcw() : (IFileDialog)new FileSaveDialogRcw();
            dialog.GetOptions(out var options);
            if (suggestedName is null)
                options |= FosPickFolders;
            else
            {
                options |= FosNoTestFileCreate | FosOverwritePrompt | FosStrictFileTypes;
                dialog.SetFileTypes(1, [new FilterSpec { Name = "JSON", Pattern = "*.json" }]);
                dialog.SetDefaultExtension("json");
                dialog.SetFileName(suggestedName);
            }
            dialog.SetOptions(options | FosForceFileSystem);
            dialog.SetTitle(title);

            var hr = dialog.Show(ownerWindowHandle);
            if (hr == HResultCancelled)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(hr);

            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(SigdnFileSysPath, out var pathPointer);
                try
                {
                    var path = Marshal.PtrToStringUni(pathPointer);
                    return string.IsNullOrWhiteSpace(path) ? null : path;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pathPointer);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        finally
        {
            if (dialog is not null)
            {
                Marshal.ReleaseComObject(dialog);
            }
        }
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRcw
    {
    }

    [ComImport]
    [Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B")]
    private class FileSaveDialogRcw
    {
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
        [MarshalAs(UnmanagedType.LPWStr)] public string Pattern;
    }

    // Vtable order mirrors IModalWindow + IFileDialog exactly; unused members
    // are declared only to keep the slots aligned.
    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(nint parent);

        void SetFileTypes(uint cFileTypes, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] FilterSpec[] rgFilterSpec);

        void SetFileTypeIndex(uint iFileType);

        void GetFileTypeIndex(out uint piFileType);

        void Advise(nint pfde, out uint pdwCookie);

        void Unadvise(uint dwCookie);

        void SetOptions(uint fos);

        void GetOptions(out uint pfos);

        void SetDefaultFolder(IShellItem psi);

        void SetFolder(IShellItem psi);

        void GetFolder(out IShellItem ppsi);

        void GetCurrentSelection(out IShellItem ppsi);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

        void GetResult(out IShellItem ppsi);

        void AddPlace(IShellItem psi, int fdap);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

        void Close(int hr);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(nint pFilter);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);

        void GetParent(out IShellItem ppsi);

        void GetDisplayName(uint sigdnName, out nint ppszName);

        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        void Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
