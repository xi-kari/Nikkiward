using System.Runtime.InteropServices;

namespace Nikkiward.Services;

public sealed record HotkeyRegistrationResult(bool Succeeded, string Message)
{
    public static HotkeyRegistrationResult Success(string message) => new(true, message);

    public static HotkeyRegistrationResult Failure(string message) => new(false, message);
}

public sealed class NativeWindowActivationChangedEventArgs(bool isActive) : EventArgs
{
    public bool IsActive { get; } = isActive;
}

public sealed class NativeWindowRuntime : IDisposable
{
    private const int ShowWindowHotkeyId = 0x4E11;
    private const int ScreenshotHotkeyId = 0x4E12;
    private const uint EventSystemForeground = 0x0003;
    private const uint WmActivateApp = 0x001C;
    private const uint WmHotkey = 0x0312;
    private const uint WmAppTray = 0x8000 + 0x42;
    private const uint WmAppForegroundSync = 0x8000 + 0x43;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmContextMenu = 0x007B;
    private const uint TrayIconId = 1;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x00000010;
    private const uint LoadDefaultSize = 0x00000040;
    private const uint MfString = 0x00000000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint ShowMenuCommand = 1;
    private const uint ExitMenuCommand = 2;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const uint ErrorHotkeyAlreadyRegistered = 1409;

    private readonly IntPtr _windowHandle;
    private readonly SubclassProcedure _subclassProcedure;
    private readonly WinEventProcedure _winEventProcedure;
    private readonly UIntPtr _subclassId = new(0x4E494B4B);
    private IntPtr _foregroundWinEventHook;
    private HotkeyGesture? _showWindowGesture;
    private HotkeyGesture? _screenshotGesture;
    private IntPtr _trayIconHandle;
    private bool _ownsTrayIconHandle;
    private bool _trayVisible;
    private bool _applicationActive;
    private int _disposeState;
    private bool _disposed;

    public event EventHandler? ShowWindowRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? ScreenshotRequested;

    public event EventHandler<NativeWindowActivationChangedEventArgs>? ActivationChanged;

    public bool IsApplicationActive
    {
        get
        {
            _applicationActive = IsCurrentProcessForeground();
            return _applicationActive;
        }
    }

    public NativeWindowRuntime(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        _windowHandle = windowHandle;
        _applicationActive = IsCurrentProcessForeground();
        _subclassProcedure = WindowSubclassProcedure;
        _winEventProcedure = OnForegroundWinEvent;
        if (!SetWindowSubclass(
                _windowHandle,
                _subclassProcedure,
                _subclassId,
                UIntPtr.Zero))
        {
            throw new InvalidOperationException(
                $"Window message subclass registration failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        _foregroundWinEventHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _winEventProcedure,
            0,
            0,
            0);
        if (_foregroundWinEventHook == IntPtr.Zero)
        {
            RemoveWindowSubclass(_windowHandle, _subclassProcedure, _subclassId);
            throw new InvalidOperationException(
                $"Foreground event hook registration failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        _applicationActive = IsCurrentProcessForeground();
    }

    public HotkeyRegistrationResult ApplyHotkeys(
        string showWindowHotkey,
        string screenshotHotkey)
    {
        ThrowIfDisposed();
        if (!HotkeyGesture.TryParse(showWindowHotkey, out var showGesture, out var showError))
        {
            return HotkeyRegistrationResult.Failure($"显示主窗口快捷键无效：{showError}");
        }

        if (!HotkeyGesture.TryParse(screenshotHotkey, out var screenshotGesture, out var screenshotError))
        {
            return HotkeyRegistrationResult.Failure($"游戏截图快捷键无效：{screenshotError}");
        }

        if (showGesture == screenshotGesture)
        {
            return HotkeyRegistrationResult.Failure("显示主窗口与游戏截图不能使用同一个快捷键。");
        }

        var previousShowGesture = _showWindowGesture;
        var previousScreenshotGesture = _screenshotGesture;
        UnregisterHotkeys();

        if (!TryRegisterHotkey(ShowWindowHotkeyId, showGesture, out var showRegistrationError))
        {
            RestoreHotkeys(previousShowGesture, previousScreenshotGesture);
            return HotkeyRegistrationResult.Failure(
                FormatRegistrationFailure(showGesture.DisplayText, showRegistrationError));
        }

        if (!TryRegisterHotkey(
                ScreenshotHotkeyId,
                screenshotGesture,
                out var screenshotRegistrationError))
        {
            UnregisterHotKey(_windowHandle, ShowWindowHotkeyId);
            RestoreHotkeys(previousShowGesture, previousScreenshotGesture);
            return HotkeyRegistrationResult.Failure(
                FormatRegistrationFailure(
                    screenshotGesture.DisplayText,
                    screenshotRegistrationError));
        }

        _showWindowGesture = showGesture;
        _screenshotGesture = screenshotGesture;
        return HotkeyRegistrationResult.Success(
            $"快捷键已注册：显示主窗口 {showGesture.DisplayText}；游戏截图 {screenshotGesture.DisplayText}");
    }

    public void SetTrayEnabled(bool enabled)
    {
        ThrowIfDisposed();
        if (enabled == _trayVisible)
        {
            return;
        }

        if (enabled)
        {
            AddTrayIcon();
        }
        else
        {
            RemoveTrayIcon();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _disposed = true;
        if (_foregroundWinEventHook != IntPtr.Zero)
        {
            _ = UnhookWinEvent(_foregroundWinEventHook);
            _foregroundWinEventHook = IntPtr.Zero;
        }

        UnregisterHotkeys();
        RemoveTrayIcon();
        RemoveWindowSubclass(_windowHandle, _subclassProcedure, _subclassId);
        GC.SuppressFinalize(this);
    }

    private IntPtr WindowSubclassProcedure(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData)
    {
        if (message is WmActivateApp or WmAppForegroundSync)
        {
            SetApplicationActive(IsCurrentProcessForeground());
        }
        else if (message == WmHotkey)
        {
            switch (unchecked((int)wParam.ToUInt64()))
            {
                case ShowWindowHotkeyId:
                    ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                    return IntPtr.Zero;
                case ScreenshotHotkeyId:
                    ScreenshotRequested?.Invoke(this, EventArgs.Empty);
                    return IntPtr.Zero;
            }
        }
        else if (message == WmAppTray)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64()) & 0xFFFF;
            if (mouseMessage is WmLButtonUp or WmLButtonDoubleClick)
            {
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
            }

            if (mouseMessage is WmRButtonUp or WmContextMenu)
            {
                ShowTrayMenu();
                return IntPtr.Zero;
            }
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private void OnForegroundWinEvent(
        IntPtr winEventHook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime)
    {
        if (Volatile.Read(ref _disposeState) == 0)
        {
            _ = PostMessage(
                _windowHandle,
                WmAppForegroundSync,
                UIntPtr.Zero,
                IntPtr.Zero);
        }
    }

    private void SetApplicationActive(bool isActive)
    {
        if (_applicationActive == isActive)
        {
            return;
        }

        _applicationActive = isActive;
        ActivationChanged?.Invoke(
            this,
            new NativeWindowActivationChangedEventArgs(isActive));
    }

    private static bool IsCurrentProcessForeground()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foregroundWindow, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    private void AddTrayIcon()
    {
        var iconPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "NikkiwardIcon.ico");
        _trayIconHandle = LoadImage(
            IntPtr.Zero,
            iconPath,
            ImageIcon,
            0,
            0,
            LoadFromFile | LoadDefaultSize);
        _ownsTrayIconHandle = _trayIconHandle != IntPtr.Zero;
        if (_trayIconHandle == IntPtr.Zero)
        {
            _trayIconHandle = LoadIcon(IntPtr.Zero, new IntPtr(32512));
        }

        var data = CreateTrayData();
        if (!ShellNotifyIcon(NimAdd, ref data))
        {
            DestroyLoadedTrayIcon();
            throw new InvalidOperationException(
                $"System tray icon registration failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        data.UnionTimeoutOrVersion = NotifyIconVersion4;
        _ = ShellNotifyIcon(NimSetVersion, ref data);
        _trayVisible = true;
    }

    private void RemoveTrayIcon()
    {
        if (_trayVisible)
        {
            var data = CreateTrayData();
            _ = ShellNotifyIcon(NimDelete, ref data);
            _trayVisible = false;
        }

        DestroyLoadedTrayIcon();
    }

    private NotifyIconData CreateTrayData() => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = _windowHandle,
        Id = TrayIconId,
        Flags = NifMessage | NifIcon | NifTip,
        CallbackMessage = WmAppTray,
        IconHandle = _trayIconHandle,
        Tip = "Nikkiward",
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private void ShowTrayMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = AppendMenu(menu, MfString, ShowMenuCommand, "显示 Nikkiward");
            _ = AppendMenu(menu, MfString, ExitMenuCommand, "退出");
            _ = GetCursorPos(out var point);
            _ = SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenuEx(
                menu,
                TpmRightButton | TpmReturnCommand,
                point.X,
                point.Y,
                _windowHandle,
                IntPtr.Zero);
            if (command == ShowMenuCommand)
            {
                ShowWindowRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (command == ExitMenuCommand)
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    private void DestroyLoadedTrayIcon()
    {
        if (_trayIconHandle != IntPtr.Zero)
        {
            if (_ownsTrayIconHandle)
            {
                _ = DestroyIcon(_trayIconHandle);
            }

            _trayIconHandle = IntPtr.Zero;
            _ownsTrayIconHandle = false;
        }
    }

    private void UnregisterHotkeys()
    {
        _ = UnregisterHotKey(_windowHandle, ShowWindowHotkeyId);
        _ = UnregisterHotKey(_windowHandle, ScreenshotHotkeyId);
        _showWindowGesture = null;
        _screenshotGesture = null;
    }

    private void RestoreHotkeys(
        HotkeyGesture? showWindowGesture,
        HotkeyGesture? screenshotGesture)
    {
        _showWindowGesture = null;
        _screenshotGesture = null;
        if (showWindowGesture is not null &&
            TryRegisterHotkey(ShowWindowHotkeyId, showWindowGesture, out _))
        {
            _showWindowGesture = showWindowGesture;
        }

        if (screenshotGesture is not null &&
            TryRegisterHotkey(ScreenshotHotkeyId, screenshotGesture, out _))
        {
            _screenshotGesture = screenshotGesture;
        }
    }

    private bool TryRegisterHotkey(
        int id,
        HotkeyGesture gesture,
        out int error)
    {
        if (RegisterHotKey(
                _windowHandle,
                id,
                gesture.Modifiers | ModNoRepeat,
                gesture.VirtualKey))
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }

    private static string FormatRegistrationFailure(string hotkey, int error) =>
        error == ErrorHotkeyAlreadyRegistered
            ? $"快捷键 {hotkey} 已被其他应用占用。"
            : $"快捷键 {hotkey} 注册失败（Win32 {error}）。";

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record HotkeyGesture(uint Modifiers, uint VirtualKey, string DisplayText)
    {
        public static bool TryParse(
            string? value,
            out HotkeyGesture gesture,
            out string error)
        {
            gesture = null!;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "不能为空";
                return false;
            }

            var tokens = value.Split(
                '+',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                error = "未找到按键";
                return false;
            }

            uint modifiers = 0;
            string? keyToken = null;
            foreach (var token in tokens)
            {
                switch (token.ToUpperInvariant())
                {
                    case "ALT":
                        modifiers |= ModAlt;
                        break;
                    case "CTRL":
                    case "CONTROL":
                        modifiers |= ModControl;
                        break;
                    case "SHIFT":
                        modifiers |= ModShift;
                        break;
                    case "WIN":
                    case "WINDOWS":
                        modifiers |= ModWin;
                        break;
                    default:
                        if (keyToken is not null)
                        {
                            error = "只能包含一个非修饰键";
                            return false;
                        }

                        keyToken = token;
                        break;
                }
            }

            if (keyToken is null || !TryGetVirtualKey(keyToken, out var virtualKey, out var keyName))
            {
                error = $"不支持按键 {keyToken ?? "<空>"}";
                return false;
            }

            var displayParts = new List<string>(5);
            if ((modifiers & ModControl) != 0)
            {
                displayParts.Add("Ctrl");
            }

            if ((modifiers & ModAlt) != 0)
            {
                displayParts.Add("Alt");
            }

            if ((modifiers & ModShift) != 0)
            {
                displayParts.Add("Shift");
            }

            if ((modifiers & ModWin) != 0)
            {
                displayParts.Add("Win");
            }

            displayParts.Add(keyName);
            gesture = new HotkeyGesture(
                modifiers,
                virtualKey,
                string.Join('+', displayParts));
            return true;
        }

        private static bool TryGetVirtualKey(
            string token,
            out uint virtualKey,
            out string displayName)
        {
            var normalized = token.Trim().ToUpperInvariant();
            if (normalized.Length == 1 &&
                (char.IsAsciiLetterUpper(normalized[0]) || char.IsAsciiDigit(normalized[0])))
            {
                virtualKey = normalized[0];
                displayName = normalized;
                return true;
            }

            if (normalized.Length >= 2 &&
                normalized[0] == 'F' &&
                int.TryParse(normalized.AsSpan(1), out var functionKey) &&
                functionKey is >= 1 and <= 24)
            {
                virtualKey = checked((uint)(0x70 + functionKey - 1));
                displayName = $"F{functionKey}";
                return true;
            }

            var key = normalized switch
            {
                "BACKSPACE" => (0x08u, "Backspace"),
                "TAB" => (0x09u, "Tab"),
                "ENTER" or "RETURN" => (0x0Du, "Enter"),
                "ESC" or "ESCAPE" => (0x1Bu, "Esc"),
                "SPACE" => (0x20u, "Space"),
                "PAGEUP" or "PGUP" => (0x21u, "PageUp"),
                "PAGEDOWN" or "PGDN" => (0x22u, "PageDown"),
                "END" => (0x23u, "End"),
                "HOME" => (0x24u, "Home"),
                "LEFT" => (0x25u, "Left"),
                "UP" => (0x26u, "Up"),
                "RIGHT" => (0x27u, "Right"),
                "DOWN" => (0x28u, "Down"),
                "INSERT" => (0x2Du, "Insert"),
                "DELETE" or "DEL" => (0x2Eu, "Delete"),
                _ => (0u, string.Empty),
            };
            virtualKey = key.Item1;
            displayName = key.Item2;
            return virtualKey != 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint UnionTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIconHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private delegate IntPtr SubclassProcedure(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr referenceData);

    private delegate void WinEventProcedure(
        IntPtr winEventHook,
        uint eventType,
        IntPtr windowHandle,
        int objectId,
        int childId,
        uint eventThreadId,
        uint eventTime);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(
        IntPtr windowHandle,
        SubclassProcedure subclassProcedure,
        UIntPtr subclassId,
        UIntPtr referenceData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(
        IntPtr windowHandle,
        SubclassProcedure subclassProcedure,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        IntPtr moduleHandle,
        WinEventProcedure winEventProcedure,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr winEventHook);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr instance,
        string name,
        uint type,
        int desiredWidth,
        int desiredHeight,
        uint loadFlags);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(
        IntPtr menu,
        uint flags,
        uint itemId,
        string text);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        IntPtr windowHandle,
        IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
