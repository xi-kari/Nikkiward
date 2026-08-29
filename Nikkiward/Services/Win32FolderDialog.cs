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
    private const uint FosPickFolders = 0x20;
    private const uint FosForceFileSystem = 0x40;
    private const uint SigdnFileSysPath = 0x80058000;
    private const int HResultCancelled = unchecked((int)0x800704C7);

    public static string? PickFolder(nint ownerWindowHandle, string title)
    {
        IFileDialog? dialog = null;
        try
        {
            dialog = (IFileDialog)new FileOpenDialogRcw();
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FosPickFolders | FosForceFileSystem);
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

    // Vtable order mirrors IModalWindow + IFileDialog exactly; unused members
    // are declared only to keep the slots aligned.
    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig]
        int Show(nint parent);

        void SetFileTypes(uint cFileTypes, nint rgFilterSpec);

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
