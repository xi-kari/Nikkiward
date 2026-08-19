using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Nikkiward.Services;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace Nikkiward.Features.Background;

public readonly record struct WallpaperEngineHostBounds(
    int Width,
    int Height);

public sealed record WallpaperEngineRuntimeResult(
    bool Succeeded,
    string? ErrorMessage = null)
{
    public static WallpaperEngineRuntimeResult Success() => new(true);

    public static WallpaperEngineRuntimeResult Failure(string message) =>
        new(false, message);
}

public sealed class WallpaperEngineFrameChangedEventArgs(CanvasImageSource? source) : EventArgs
{
    public CanvasImageSource? Source { get; } = source;
}

public sealed class WallpaperEngineRuntimeHost : IDisposable
{
    private const string WallpaperWindowClass = "WPEOverlappedWallpaper";
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsPopup = unchecked((long)0x80000000);
    private const long WsCaption = 0x00C00000;
    private const long WsThickFrame = 0x00040000;
    private const long WsMinimizeBox = 0x00020000;
    private const long WsMaximizeBox = 0x00010000;
    private const long WsSysMenu = 0x00080000;
    private const long WsVisible = 0x10000000;
    private const long WsDisabled = 0x08000000;
    private const long ExNoActivate = 0x08000000;
    private const long ExToolWindow = 0x00000080;
    private const uint SwHide = 0;
    private const uint SwShowNoActivate = 4;
    private const uint WmClose = 0x0010;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndBottom = new(1);
    private const int CaptureBufferCount = 2;
    private const int MaximumCaptureWidth = 1600;
    private const int MaximumCaptureHeight = 900;
    private static readonly TimeSpan WallpaperWarmupDelay =
        TimeSpan.FromMilliseconds(1000);
    private const long MinimumFrameIntervalTicks = TimeSpan.TicksPerSecond / 30;
    private const float CaptureDpi = 96f;

    private readonly object _sync = new();
    private readonly SemaphoreSlim _showGate = new(1, 1);
    private readonly IntPtr _ownerWindowHandle;
    private readonly DispatcherQueue? _ownerDispatcherQueue;
    private string _locationName = CreateLocationName();
    private readonly Func<string?> _runtimeLocator;
    private string? _runtimePath;
    private string? _packagePath;
    private IntPtr _wallpaperWindow;
    private IntPtr _captureWindow;
    private CanvasDevice? _canvasDevice;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _captureSession;
    private CanvasImageSource? _frameSource;
    private TaskCompletionSource<bool>? _firstFrame;
    private CancellationTokenSource? _showCancellation;
    private Task _closeTask = Task.CompletedTask;
    private WallpaperEngineHostBounds _bounds;
    private long _lastFrameTimestamp;
    private DispatcherQueue? _captureDispatcherQueue;
    private CanvasBitmap? _pendingFrameBitmap;
    private bool _frameDispatchQueued;
    private bool _disposed;

    private static string CreateLocationName() =>
        $"NikkiwardWallpaper-{Guid.NewGuid():N}";

    private static readonly string RuntimeLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nikkiward",
        "WallpaperRuntime.log");

    public WallpaperEngineRuntimeHost(
        IntPtr ownerWindowHandle,
        Func<string?>? runtimeLocator = null)
    {
        if (ownerWindowHandle == IntPtr.Zero)
        {
            throw new ArgumentException(
                "A valid owner window handle is required.",
                nameof(ownerWindowHandle));
        }

        _ownerWindowHandle = ownerWindowHandle;
        _ownerDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _runtimeLocator = runtimeLocator ?? FindRuntimeExecutable;
    }

    public event EventHandler<WallpaperEngineFrameChangedEventArgs>? FrameSourceChanged;

    public bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return !_disposed &&
                    _wallpaperWindow != IntPtr.Zero &&
                    _captureWindow != IntPtr.Zero &&
                    IsWindow(_wallpaperWindow) &&
                    IsWindow(_captureWindow) &&
                    _captureSession is not null;
            }
        }
    }

    public CanvasImageSource? FrameSource
    {
        get
        {
            lock (_sync)
            {
                return _frameSource;
            }
        }
    }

    public async Task<WallpaperEngineRuntimeResult> ShowAsync(
        string packagePath,
        WallpaperEngineHostBounds bounds,
        CancellationToken cancellationToken = default)
    {
        await _showGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ShowCoreAsync(
                    packagePath,
                    bounds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _showGate.Release();
        }
    }

    private async Task<WallpaperEngineRuntimeResult> ShowCoreAsync(
        string packagePath,
        WallpaperEngineHostBounds bounds,
        CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            WriteRuntimeLog("show rejected: disposed");
            return WallpaperEngineRuntimeResult.Failure("Wallpaper Engine 宿主已关闭。");
        }

        if (string.IsNullOrWhiteSpace(packagePath) ||
            !File.Exists(packagePath) ||
            !string.Equals(
                Path.GetExtension(packagePath),
                ".pkg",
                StringComparison.OrdinalIgnoreCase))
        {
            WriteRuntimeLog("show rejected: package");
            return WallpaperEngineRuntimeResult.Failure(
                "Wallpaper .pkg 文件不存在或无法读取。");
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            WriteRuntimeLog($"show rejected: bounds {bounds.Width}x{bounds.Height}");
            return WallpaperEngineRuntimeResult.Failure(
                "Wallpaper Engine 画布尺寸无效。");
        }

        var normalizedPackagePath = Path.GetFullPath(packagePath);
        var reuseActiveSession = false;
        lock (_sync)
        {
            reuseActiveSession = !_disposed &&
                _captureSession is not null &&
                _wallpaperWindow != IntPtr.Zero &&
                IsWindow(_wallpaperWindow) &&
                string.Equals(
                    _packagePath,
                    normalizedPackagePath,
                    StringComparison.OrdinalIgnoreCase);
        }

        if (reuseActiveSession)
        {
            WriteRuntimeLog("reusing active session");
            UpdateBounds(bounds);
            return WallpaperEngineRuntimeResult.Success();
        }

        Stop();
        cancellationToken.ThrowIfCancellationRequested();

        await AwaitCloseCompletionAsync(cancellationToken).ConfigureAwait(false);

        _runtimePath = _runtimeLocator();
        if (string.IsNullOrWhiteSpace(_runtimePath) ||
            !File.Exists(_runtimePath))
        {
            return WallpaperEngineRuntimeResult.Failure(
                "未找到 Wallpaper Engine 运行时，请先安装桌面版 Wallpaper Engine。");
        }

        using var showCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var showToken = showCancellation.Token;
        lock (_sync)
        {
            if (_disposed)
            {
                return WallpaperEngineRuntimeResult.Failure("Wallpaper Engine 宿主已关闭。");
            }

            _showCancellation = showCancellation;
        }

        await CleanupStaleWallpaperWindowsAsync(
                _runtimePath,
                showToken)
            .ConfigureAwait(false);
        _locationName = CreateLocationName();

        var captureBounds = ScaleCaptureBounds(bounds);
        WriteRuntimeLog($"show start bounds={bounds.Width}x{bounds.Height} capture={captureBounds.Width}x{captureBounds.Height}");
        _packagePath = normalizedPackagePath;
        _bounds = captureBounds;
        var firstFrame = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _firstFrame = firstFrame;
        }

        try
        {
            showToken.ThrowIfCancellationRequested();
            StartWallpaperCommand(_runtimePath, _packagePath, captureBounds);
            var wallpaperWindow = await WaitForWallpaperWindowAsync(showToken);
            showToken.ThrowIfCancellationRequested();
            var committed = false;
            lock (_sync)
            {
                if (ReferenceEquals(_showCancellation, showCancellation) &&
                    !_disposed)
                {
                    _wallpaperWindow = wallpaperWindow;
                    committed = true;
                }
            }

            WriteRuntimeLog($"window={wallpaperWindow}");
            if (!committed || wallpaperWindow == IntPtr.Zero)
            {
                Stop();
                if (wallpaperWindow != IntPtr.Zero)
                {
                    CloseWallpaperWindowAfterStop(
                        _runtimePath,
                        _locationName,
                        wallpaperWindow);
                }
                return WallpaperEngineRuntimeResult.Failure(
                    "Wallpaper Engine 未创建可捕获的场景窗口。");
            }

            if (!await PlaceWindowBehindOwnerAsync(
                    wallpaperWindow,
                    captureBounds,
                    showToken)
                .ConfigureAwait(false))
            {
                WriteRuntimeLog("window placement failed");
                Stop();
                return WallpaperEngineRuntimeResult.Failure(
                    "Wallpaper Engine 场景无法停靠到 Nikkiward 窗口。");
            }

            await Task.Delay(WallpaperWarmupDelay, showToken);
            showToken.ThrowIfCancellationRequested();
            var staleAfterWarmup = false;
            lock (_sync)
            {
                if (!ReferenceEquals(_showCancellation, showCancellation) ||
                    _disposed ||
                    !IsWindow(wallpaperWindow))
                {
                    staleAfterWarmup = true;
                }
                else
                {
                    _captureWindow = _wallpaperWindow;
                }
            }
            if (staleAfterWarmup)
            {
                CloseWallpaperWindowAfterStop(
                    _runtimePath!,
                    _locationName,
                    wallpaperWindow);
                return WallpaperEngineRuntimeResult.Failure(
                    "Wallpaper Engine 场景已停止。");
            }

            WriteRuntimeLog($"captureWindow={wallpaperWindow}");

            await StartCaptureWithRetryAsync(
                    wallpaperWindow,
                    captureBounds,
                    showToken)
                .ConfigureAwait(false);
            WriteRuntimeLog("capture started");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                showToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var registration = timeout.Token.Register(() =>
                firstFrame.TrySetCanceled(timeout.Token));
            if (!await firstFrame.Task.ConfigureAwait(false))
            {
                WriteRuntimeLog("first frame canceled by stop");
                return WallpaperEngineRuntimeResult.Failure(
                    "Wallpaper Engine 场景已停止。");
            }

            WriteRuntimeLog("first frame received");
            return WallpaperEngineRuntimeResult.Success();
        }
        catch (OperationCanceledException)
        {
            WriteRuntimeLog("show canceled");
            Stop();
            throw;
        }
        catch (Exception ex) when (ex is
            InvalidOperationException or
            COMException or
            ArgumentException or
            IOException or
            UnauthorizedAccessException or
            Win32Exception)
        {
            WriteRuntimeLog($"show failed {ex.GetType().Name}: {ex.Message}");
            Stop();
            return WallpaperEngineRuntimeResult.Failure(
                $"Wallpaper Engine 场景启动失败：{ex.Message}");
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_showCancellation, showCancellation))
                {
                    _showCancellation = null;
                }
            }
        }
    }

    public void UpdateBounds(WallpaperEngineHostBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var captureBounds = ScaleCaptureBounds(bounds);
        Direct3D11CaptureFramePool? framePool;
        CanvasDevice? device;
        IntPtr wallpaperWindow;
        var previousBounds = default(WallpaperEngineHostBounds);
        var dimensionsChanged = false;
        lock (_sync)
        {
            if (_disposed || !IsWindow(_wallpaperWindow))
            {
                return;
            }

            previousBounds = _bounds;
            wallpaperWindow = _wallpaperWindow;
            framePool = _framePool;
            device = _canvasDevice;
            dimensionsChanged = _bounds.Width != captureBounds.Width ||
                _bounds.Height != captureBounds.Height;
        }

        if (!PlaceWindowBehindOwner(
                wallpaperWindow,
                captureBounds,
                configureStyle: false) ||
            !dimensionsChanged)
        {
            return;
        }

        if (framePool is null || device is null)
        {
            _ = PlaceWindowBehindOwner(
                wallpaperWindow,
                previousBounds,
                configureStyle: false);
            return;
        }

        CanvasImageSource? replacement = null;
        try
        {
            replacement = new CanvasImageSource(
                device,
                captureBounds.Width,
                captureBounds.Height,
                CaptureDpi);
            framePool.Recreate(
                device,
                DirectXPixelFormat.R8G8B8A8UIntNormalized,
                CaptureBufferCount,
                new SizeInt32
                {
                    Width = captureBounds.Width,
                    Height = captureBounds.Height,
                });

            CanvasImageSource? previous = null;
            var accepted = false;
            lock (_sync)
            {
                if (!_disposed && ReferenceEquals(framePool, _framePool))
                {
                    previous = _frameSource;
                    _frameSource = replacement;
                    _bounds = captureBounds;
                    accepted = true;
                }
            }

            if (!accepted)
            {
                _ = replacement;
                return;
            }

            FrameSourceChanged?.Invoke(
                this,
                new WallpaperEngineFrameChangedEventArgs(replacement));
            _ = previous;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException)
        {
            WriteRuntimeLog($"resize failed {ex.GetType().Name}: {ex.Message}");
            _ = replacement;
            _ = PlaceWindowBehindOwner(
                wallpaperWindow,
                previousBounds,
                configureStyle: false);
        }
    }

    public void UpdatePlacement()
    {
        IntPtr wallpaperWindow;
        WallpaperEngineHostBounds bounds;
        lock (_sync)
        {
            if (_disposed || !IsWindow(_wallpaperWindow))
            {
                return;
            }

            wallpaperWindow = _wallpaperWindow;
            bounds = _bounds;
        }

        if (!PlaceWindowBehindOwner(
                wallpaperWindow,
                bounds,
                configureStyle: false))
        {
            WriteRuntimeLog("window placement refresh failed");
        }
    }

    public void Stop()
    {
        IntPtr wallpaperWindow;
        string? runtimePath;
        CancellationTokenSource? showCancellation;
        CanvasImageSource? frameSource;
        CanvasBitmap? pendingFrameBitmap;
        string locationName;
        lock (_sync)
        {
            wallpaperWindow = _wallpaperWindow;
            runtimePath = _runtimePath;
            locationName = _locationName;
            showCancellation = _showCancellation;
            _showCancellation = null;
            _wallpaperWindow = IntPtr.Zero;
            _captureWindow = IntPtr.Zero;
            _packagePath = null;
            _firstFrame?.TrySetResult(false);
            _firstFrame = null;
            if (_framePool is not null)
            {
                _framePool.FrameArrived -= OnFrameArrived;
            }
            _captureSession?.Dispose();
            _captureSession = null;
            _framePool?.Dispose();
            _framePool = null;
            _canvasDevice = null;
            _captureDispatcherQueue = null;
            pendingFrameBitmap = _pendingFrameBitmap;
            _pendingFrameBitmap = null;
            _frameDispatchQueued = false;
            frameSource = _frameSource;
            _frameSource = null;
        }

        showCancellation?.Cancel();
        pendingFrameBitmap?.Dispose();

        HideWallpaperWindow(wallpaperWindow);
        HideWallpaperWindow(FindWindow(WallpaperWindowClass, locationName));

        if (!string.IsNullOrWhiteSpace(runtimePath))
        {
            StartControlCommand(runtimePath, "closeWallpaper", locationName);
            var closeTask = Task.Run(() => CloseWallpaperWindowAfterStop(
                runtimePath,
                locationName,
                wallpaperWindow));
            lock (_sync)
            {
                _closeTask = Task.WhenAll(_closeTask, closeTask);
            }
        }

        if (frameSource is not null)
        {
            FrameSourceChanged?.Invoke(
                this,
                new WallpaperEngineFrameChangedEventArgs(null));
            _ = frameSource;
        }
    }

    private async Task AwaitCloseCompletionAsync(CancellationToken cancellationToken)
    {
        Task closeTask;
        lock (_sync)
        {
            closeTask = _closeTask;
        }

        await closeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    public static string? FindRuntimeExecutable()
    {
        foreach (var processName in new[] { "wallpaper64", "wallpaper32" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    NotSupportedException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        })
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                steamRoots.Add(Path.Combine(root, "Steam"));
            }
        }

        foreach (var steamRoot in steamRoots.ToArray())
        {
            var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
            {
                continue;
            }

            try
            {
                foreach (var library in SteamLibraryVdfReader.ReadLibraryPaths(
                    File.ReadAllText(vdfPath)))
                {
                    steamRoots.Add(library);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        foreach (var steamRoot in steamRoots)
        {
            foreach (var executableName in new[] { "wallpaper64.exe", "wallpaper32.exe" })
            {
                var candidate = Path.Combine(
                    steamRoot,
                    "steamapps",
                    "common",
                    "wallpaper_engine",
                    executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private void StartCapture(IntPtr captureWindow, WallpaperEngineHostBounds bounds)
    {
        WriteRuntimeLog($"start capture window={captureWindow} bounds={bounds.Width}x{bounds.Height}");
        var item = GraphicsCaptureItem.TryCreateFromWindowId(
            new Windows.UI.WindowId(unchecked((ulong)captureWindow.ToInt64())));
        if (item is null || item.Size.Width <= 0 || item.Size.Height <= 0)
        {
            WriteRuntimeLog("capture item invalid");
            throw new InvalidOperationException("Wallpaper Engine 场景窗口没有有效画布。");
        }

        var device = CanvasDevice.GetSharedDevice();
        WriteRuntimeLog($"capture item size={item.Size.Width}x{item.Size.Height}");
        WriteRuntimeLog("capture device ready");
        var frameSource = new CanvasImageSource(
            device,
            bounds.Width,
            bounds.Height,
            CaptureDpi);
        WriteRuntimeLog("frame source created");
        var dispatcherQueue = _ownerDispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        Direct3D11CaptureFramePool framePool;
#pragma warning disable CA1416
        if (ApiInformation.IsMethodPresent(
                "Windows.Graphics.Capture.Direct3D11CaptureFramePool",
                "CreateFreeThreaded"))
        {
            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.R8G8B8A8UIntNormalized,
                CaptureBufferCount,
                item.Size);
        }
        else
        {
            framePool = Direct3D11CaptureFramePool.Create(
                device,
                DirectXPixelFormat.R8G8B8A8UIntNormalized,
                CaptureBufferCount,
                item.Size);
        }
#pragma warning restore CA1416
        WriteRuntimeLog("frame pool created");
        var session = framePool.CreateCaptureSession(item);
        WriteRuntimeLog("frame pool/session created");
#pragma warning disable CA1416
        if (ApiInformation.IsPropertyPresent(
                "Windows.Graphics.Capture.GraphicsCaptureSession",
                "IsBorderRequired"))
        {
            session.IsBorderRequired = false;
        }

        if (ApiInformation.IsPropertyPresent(
                "Windows.Graphics.Capture.GraphicsCaptureSession",
                "IsCursorCaptureEnabled"))
        {
            session.IsCursorCaptureEnabled = false;
        }
#pragma warning restore CA1416

        framePool.FrameArrived += OnFrameArrived;
        lock (_sync)
        {
            _canvasDevice = device;
            _frameSource = frameSource;
            _framePool = framePool;
            _captureSession = session;
            _captureDispatcherQueue = dispatcherQueue;
        }

        FrameSourceChanged?.Invoke(
            this,
            new WallpaperEngineFrameChangedEventArgs(frameSource));
        session.StartCapture();
        WriteRuntimeLog("session started");
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        CanvasDevice? device;
        DispatcherQueue? dispatcherQueue;
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _framePool))
            {
                return;
            }

            device = _canvasDevice;
            dispatcherQueue = _captureDispatcherQueue;
        }

        Direct3D11CaptureFrame? frame = null;
        try
        {
            Direct3D11CaptureFrame? candidate;
            while ((candidate = sender.TryGetNextFrame()) is not null)
            {
                frame?.Dispose();
                frame = candidate;
            }

            if (frame is null ||
                frame.ContentSize.Width <= 0 ||
                frame.ContentSize.Height <= 0 ||
                device is null)
            {
                return;
            }

            var now = DateTime.UtcNow.Ticks;
            if (now - Volatile.Read(ref _lastFrameTimestamp) < MinimumFrameIntervalTicks)
            {
                return;
            }

            Volatile.Write(ref _lastFrameTimestamp, now);
            var bitmap = CanvasBitmap.CreateFromDirect3D11Surface(
                device,
                frame.Surface,
                CaptureDpi);

            if (dispatcherQueue is null)
            {
                bitmap.Dispose();
                return;
            }

            CanvasBitmap? previous;
            var accepted = false;
            var shouldQueue = false;
            lock (_sync)
            {
                previous = null;
                if (ReferenceEquals(sender, _framePool) && !_disposed)
                {
                    previous = _pendingFrameBitmap;
                    _pendingFrameBitmap = bitmap;
                    accepted = true;
                    if (!_frameDispatchQueued)
                    {
                        _frameDispatchQueued = true;
                        shouldQueue = true;
                    }
                }
            }

            if (!accepted)
            {
                bitmap.Dispose();
                return;
            }

            previous?.Dispose();
            if (shouldQueue)
            {
                QueuePendingFrameDispatch(dispatcherQueue);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException)
        {
            WriteRuntimeLog($"frame failed {ex.GetType().Name}: {ex.Message}");
            lock (_sync)
            {
                _firstFrame?.TrySetException(ex);
            }
        }
        finally
        {
            frame?.Dispose();
        }
    }

    private void ProcessPendingFrame()
    {
        CanvasBitmap? bitmap;
        CanvasImageSource? frameSource;
        TaskCompletionSource<bool>? firstFrame;
        DispatcherQueue? dispatcherQueue;
        lock (_sync)
        {
            bitmap = _pendingFrameBitmap;
            _pendingFrameBitmap = null;
            _frameDispatchQueued = false;
            frameSource = _frameSource;
            firstFrame = _firstFrame;
            dispatcherQueue = _captureDispatcherQueue;
        }

        if (bitmap is not null)
        {
            try
            {
                if (frameSource is not null)
                {
                    using (bitmap)
                    using (var drawingSession = frameSource.CreateDrawingSession(
                        Microsoft.UI.Colors.Transparent))
                    {
                        drawingSession.DrawImage(bitmap);
                    }

                    firstFrame?.TrySetResult(true);
                }
                else
                {
                    bitmap.Dispose();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException)
            {
                bitmap.Dispose();
                firstFrame?.TrySetException(ex);
            }
        }

        var shouldQueue = false;
        lock (_sync)
        {
            if (_pendingFrameBitmap is not null &&
                !_frameDispatchQueued &&
                _captureDispatcherQueue is not null)
            {
                _frameDispatchQueued = true;
                dispatcherQueue = _captureDispatcherQueue;
                shouldQueue = true;
            }
        }

        if (shouldQueue && dispatcherQueue is not null)
        {
            QueuePendingFrameDispatch(dispatcherQueue);
        }
    }

    private void QueuePendingFrameDispatch(DispatcherQueue dispatcherQueue)
    {
        if (dispatcherQueue.TryEnqueue(ProcessPendingFrame))
        {
            return;
        }

        CanvasBitmap? abandoned;
        lock (_sync)
        {
            abandoned = _pendingFrameBitmap;
            _pendingFrameBitmap = null;
            _frameDispatchQueued = false;
        }

        abandoned?.Dispose();
    }

    private async Task<bool> PlaceWindowBehindOwnerAsync(
        IntPtr wallpaperWindow,
        WallpaperEngineHostBounds bounds,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PlaceWindowBehindOwner(wallpaperWindow, bounds))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
    }

    private async Task StartCaptureWithRetryAsync(
        IntPtr wallpaperWindow,
        WallpaperEngineHostBounds bounds,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await StartCaptureOnOwnerDispatcherAsync(
                        wallpaperWindow,
                        bounds,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("没有有效画布", StringComparison.Ordinal))
            {
                lastFailure = ex;
                if (attempt == 3)
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(180), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        throw lastFailure ?? new InvalidOperationException(
            "Wallpaper Engine 场景窗口没有有效画布。");
    }

    private Task StartCaptureOnOwnerDispatcherAsync(
        IntPtr captureWindow,
        WallpaperEngineHostBounds bounds,
        CancellationToken cancellationToken)
    {
        var dispatcherQueue = _ownerDispatcherQueue;
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            StartCapture(captureWindow, bounds);
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    StartCapture(captureWindow, bounds);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Stop();
                        completion.TrySetCanceled(cancellationToken);
                        return;
                    }

                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            return Task.FromException(
                new InvalidOperationException(
                    "Wallpaper Engine 无法进入主窗口线程。"));
        }

        return completion.Task.WaitAsync(cancellationToken);
    }

    private void WaitForWallpaperWindowClosed(string locationName)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (FindWindow(WallpaperWindowClass, locationName) == IntPtr.Zero)
            {
                return;
            }

            Thread.Sleep(50);
        }
    }

    private static void CloseWallpaperWindowAfterStop(
        string runtimePath,
        string locationName,
        IntPtr knownWindow)
    {
        if (knownWindow != IntPtr.Zero && IsWindow(knownWindow))
        {
            HideWallpaperWindow(knownWindow);
        }

        var hadWindow = knownWindow != IntPtr.Zero;
        var missingPolls = 0;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var window = FindWindow(WallpaperWindowClass, locationName);
            if (window == IntPtr.Zero)
            {
                if (hadWindow && ++missingPolls >= 3)
                {
                    return;
                }

                Thread.Sleep(50);
                continue;
            }

            hadWindow = true;
            missingPolls = 0;
            HideWallpaperWindow(window);
            if (attempt == 0 || attempt % 5 == 0)
            {
                StartControlCommand(runtimePath, "closeWallpaper", locationName);
            }

            Thread.Sleep(50);
        }
    }

    private static async Task CleanupStaleWallpaperWindowsAsync(
        string runtimePath,
        CancellationToken cancellationToken)
    {
        var staleLocations = FindNikkiwardWallpaperLocations();
        if (staleLocations.Count == 0)
        {
            return;
        }

        var tasks = staleLocations.Select(location =>
            Task.Run(() =>
            {
                var window = FindWindow(WallpaperWindowClass, location);
                if (window != IntPtr.Zero && IsWindow(window))
                {
                    HideWallpaperWindow(window);
                }

                CloseWallpaperWindowAfterStop(
                runtimePath,
                location,
                window);
            }, cancellationToken)).ToArray();
        await Task.WhenAll(tasks).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void HideWallpaperWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
        {
            return;
        }

        _ = ShowWindowAsync(window, SwHide);
        _ = PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
    }

    private static IReadOnlyList<string> FindNikkiwardWallpaperLocations()
    {
        var locations = new List<string>();
        EnumWindows(
            (window, _) =>
            {
                var className = new StringBuilder(128);
                _ = GetClassName(window, className, className.Capacity);
                if (!string.Equals(
                        className.ToString(),
                        WallpaperWindowClass,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                var title = new StringBuilder(256);
                _ = GetWindowText(window, title, title.Capacity);
                if (title.ToString().StartsWith(
                        "NikkiwardWallpaper-",
                        StringComparison.Ordinal))
                {
                    locations.Add(title.ToString());
                }

                return true;
            },
            IntPtr.Zero);
        return locations;
    }

    private async Task<IntPtr> WaitForWallpaperWindowAsync(
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = FindWindow(WallpaperWindowClass, _locationName);
            if (handle != IntPtr.Zero && IsWindow(handle))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return handle;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(60), cancellationToken)
                .ConfigureAwait(false);
        }

        return IntPtr.Zero;
    }

    private static async Task<IntPtr> WaitForRenderWindowAsync(
        IntPtr wallpaperWindow,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var renderWindow = FindRenderWindow(wallpaperWindow);
            if (renderWindow != IntPtr.Zero && IsWindow(renderWindow))
            {
                return renderWindow;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken)
                .ConfigureAwait(false);
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindRenderWindow(IntPtr wallpaperWindow)
    {
        var renderWindow = IntPtr.Zero;
        EnumChildWindows(
            wallpaperWindow,
            (window, _) =>
            {
                var className = new StringBuilder(128);
                _ = GetClassName(window, className, className.Capacity);
                if (string.Equals(
                        className.ToString(),
                        "WPEDesktopDX11Window",
                        StringComparison.Ordinal))
                {
                    renderWindow = window;
                    return false;
                }

                return true;
            },
            IntPtr.Zero);
        return renderWindow;
    }

    private static WallpaperEngineHostBounds ScaleCaptureBounds(
        WallpaperEngineHostBounds bounds)
    {
        var scale = Math.Min(
            1d,
            Math.Min(
                (double)MaximumCaptureWidth / bounds.Width,
                (double)MaximumCaptureHeight / bounds.Height));
        return new WallpaperEngineHostBounds(
            Math.Max(2, MakeEven((int)Math.Round(bounds.Width * scale))),
            Math.Max(2, MakeEven((int)Math.Round(bounds.Height * scale))));
    }

    private static int MakeEven(int value) => value - (value & 1);

    internal static void WriteRuntimeLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RuntimeLogPath)!);
            File.AppendAllText(
                RuntimeLogPath,
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void StartWallpaperCommand(
        string runtimePath,
        string packagePath,
        WallpaperEngineHostBounds bounds)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = runtimePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.ArgumentList.Add("-control");
        startInfo.ArgumentList.Add("openWallpaper");
        startInfo.ArgumentList.Add("-file");
        startInfo.ArgumentList.Add(packagePath);
        startInfo.ArgumentList.Add("-playInWindow");
        startInfo.ArgumentList.Add(_locationName);
        startInfo.ArgumentList.Add("-borderless");
        startInfo.ArgumentList.Add("-width");
        startInfo.ArgumentList.Add(bounds.Width.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-height");
        startInfo.ArgumentList.Add(bounds.Height.ToString(CultureInfo.InvariantCulture));
        using var process = Process.Start(startInfo);
    }

    private static void StartControlCommand(
        string runtimePath,
        string command,
        string locationName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = runtimePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            startInfo.ArgumentList.Add("-control");
            startInfo.ArgumentList.Add(command);
            startInfo.ArgumentList.Add("-location");
            startInfo.ArgumentList.Add(locationName);
            using var process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            IOException)
        {
        }
    }

    private bool PlaceWindowBehindOwner(
        IntPtr wallpaperWindow,
        WallpaperEngineHostBounds bounds,
        bool configureStyle = true)
    {
        if (wallpaperWindow == IntPtr.Zero || !IsWindow(wallpaperWindow))
        {
            return false;
        }

        if (configureStyle)
        {
            ShowWindow(wallpaperWindow, SwHide);
        }

        if (!GetWindowRect(_ownerWindowHandle, out var ownerBounds))
        {
            return false;
        }

        if (configureStyle)
        {
            var style = GetWindowLongPtr(wallpaperWindow, GwlStyle).ToInt64();
            style &= ~(WsPopup |
                WsCaption |
                WsThickFrame |
                WsMinimizeBox |
                WsMaximizeBox |
                WsSysMenu);
            style |= WsVisible | WsDisabled;
            _ = SetWindowLongPtr(wallpaperWindow, GwlStyle, new IntPtr(style));

            var exStyle = GetWindowLongPtr(wallpaperWindow, GwlExStyle).ToInt64();
            exStyle |= ExNoActivate | ExToolWindow;
            _ = SetWindowLongPtr(wallpaperWindow, GwlExStyle, new IntPtr(exStyle));
        }

        var ownerWidth = Math.Max(1, ownerBounds.Right - ownerBounds.Left);
        var ownerHeight = Math.Max(1, ownerBounds.Bottom - ownerBounds.Top);
        var x = ownerBounds.Left + Math.Max(0, (ownerWidth - bounds.Width) / 2);
        var y = ownerBounds.Top + Math.Max(0, (ownerHeight - bounds.Height) / 2);
        var flags = SwpNoActivate | SwpFrameChanged;
        if (!configureStyle)
        {
            flags |= SwpShowWindow;
        }

        if (!SetWindowPos(
            wallpaperWindow,
            HwndBottom,
            x,
            y,
            bounds.Width,
            bounds.Height,
            flags))
        {
            return false;
        }

        if (configureStyle)
        {
            ShowWindow(wallpaperWindow, SwShowNoActivate);
        }

        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumChildWindows(
        IntPtr parentWindow,
        EnumWindowsCallback callback,
            IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr windowHandle,
        StringBuilder windowText,
        int maxCount);

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out NativeRect bounds);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(
        IntPtr windowHandle,
        int index,
        IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, uint command);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
