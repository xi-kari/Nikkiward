using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Display;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Nikkiward.Models;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;

namespace Nikkiward.Services;

public sealed record ScreenshotCaptureResult(
    bool Succeeded,
    string Message,
    string? PrimaryFilePath = null,
    string? SdrFilePath = null,
    bool CapturedHdr = false)
{
    public string? ClipboardFilePath => SdrFilePath ?? PrimaryFilePath;
}

public sealed class GameScreenshotService
{
    private static readonly string[] GameProcessNames =
    [
        "X6Game-Win64-Shipping",
        "InfinityNikki",
    ];

    private static readonly bool SupportsBorderControl =
        ApiInformation.IsPropertyPresent(
            "Windows.Graphics.Capture.GraphicsCaptureSession",
            "IsBorderRequired");

    private static readonly bool SupportsCursorControl =
        ApiInformation.IsPropertyPresent(
            "Windows.Graphics.Capture.GraphicsCaptureSession",
            "IsCursorCaptureEnabled");

    private readonly SemaphoreSlim _captureGate = new(1, 1);

    public async Task<ScreenshotCaptureResult> CaptureGameAsync(
        ScreenshotSettings settings,
        string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        var target = FindGameWindow();
        if (target is null)
        {
            return new ScreenshotCaptureResult(
                false,
                "未找到正在运行且未最小化的无限暖暖游戏窗口。");
        }

        return await CaptureWindowAsync(
            target.Value.WindowHandle,
            target.Value.FilePrefix,
            settings,
            destinationFolder,
            cancellationToken);
    }

    public async Task<ScreenshotCaptureResult> CaptureTestAsync(
        IntPtr ownerWindowHandle,
        ScreenshotSettings settings,
        string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetDisplayId(ownerWindowHandle, out var displayId))
        {
            return new ScreenshotCaptureResult(false, "未找到 Nikkiward 所在的显示器。");
        }

        await _captureGate.WaitAsync(cancellationToken);
        try
        {
            using var displayInformation = DisplayInformation.CreateForDisplayId(
                displayId);
            var colorInfo = displayInformation.GetAdvancedColorInfo();
            var requestedFormat = IsHdr(colorInfo)
                ? DirectXPixelFormat.R16G16B16A16Float
                : DirectXPixelFormat.R8G8B8A8UIntNormalized;
            var captureItem = GraphicsCaptureItem.TryCreateFromDisplayId(
                new Windows.Graphics.DisplayId(displayId.Value));
            if (captureItem is null)
            {
                return new ScreenshotCaptureResult(false, "当前系统无法创建显示器截图源。");
            }

            using var bitmap = await CaptureFrameAsync(
                captureItem,
                requestedFormat,
                cancellationToken);
            return await SaveCapturedBitmapAsync(
                bitmap,
                "Nikkiward_Test",
                settings,
                destinationFolder,
                (float)Math.Max(80, colorInfo.MaxLuminanceInNits),
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ScreenshotCaptureResult(false, "测试截图等待画面超时。");
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private async Task<ScreenshotCaptureResult> CaptureWindowAsync(
        IntPtr windowHandle,
        string filePrefix,
        ScreenshotSettings settings,
        string destinationFolder,
        CancellationToken cancellationToken)
    {
        if (IsIconic(windowHandle))
        {
            return new ScreenshotCaptureResult(false, "游戏窗口已最小化，无法截图。");
        }

        await _captureGate.WaitAsync(cancellationToken);
        try
        {
            if (!TryGetDisplayId(windowHandle, out var displayId))
            {
                return new ScreenshotCaptureResult(false, "未找到游戏所在的显示器。");
            }

            using var displayInformation = DisplayInformation.CreateForDisplayId(
                displayId);
            var colorInfo = displayInformation.GetAdvancedColorInfo();
            var requestedFormat = IsHdr(colorInfo)
                ? DirectXPixelFormat.R16G16B16A16Float
                : DirectXPixelFormat.R8G8B8A8UIntNormalized;
            var captureItem = GraphicsCaptureItem.TryCreateFromWindowId(
                new Windows.UI.WindowId(unchecked((ulong)windowHandle.ToInt64())));
            if (captureItem is null)
            {
                return new ScreenshotCaptureResult(false, "当前系统无法创建游戏窗口截图源。");
            }

            using var bitmap = await CaptureFrameAsync(
                captureItem,
                requestedFormat,
                cancellationToken);
            return await SaveCapturedBitmapAsync(
                bitmap,
                filePrefix,
                settings,
                destinationFolder,
                (float)Math.Max(80, colorInfo.MaxLuminanceInNits),
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ScreenshotCaptureResult(false, "游戏截图等待画面超时。");
        }
        finally
        {
            _captureGate.Release();
        }
    }

    private static async Task<CanvasRenderTarget> CaptureFrameAsync(
        GraphicsCaptureItem captureItem,
        DirectXPixelFormat pixelFormat,
        CancellationToken cancellationToken)
    {
        if (captureItem.Size.Width <= 0 || captureItem.Size.Height <= 0)
        {
            throw new InvalidOperationException("The capture target has an empty surface.");
        }

        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            CanvasDevice.GetSharedDevice(),
            pixelFormat,
            2,
            captureItem.Size);
        using var session = framePool.CreateCaptureSession(captureItem);
#pragma warning disable CA1416
        if (SupportsBorderControl)
        {
            session.IsBorderRequired = false;
        }

        if (SupportsCursorControl)
        {
            session.IsCursorCaptureEnabled = false;
        }
#pragma warning restore CA1416

        var completion = new TaskCompletionSource<CanvasRenderTarget>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        framePool.FrameArrived += OnFrameArrived;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var registration = timeout.Token.Register(() =>
            completion.TrySetCanceled(timeout.Token));
        session.StartCapture();

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            framePool.FrameArrived -= OnFrameArrived;
        }

        void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null ||
                frame.ContentSize.Width <= 0 ||
                frame.ContentSize.Height <= 0)
            {
                return;
            }

            CanvasRenderTarget? target = null;
            try
            {
                using var source = CanvasBitmap.CreateFromDirect3D11Surface(
                    CanvasDevice.GetSharedDevice(),
                    frame.Surface,
                    96);
                target = new CanvasRenderTarget(
                    CanvasDevice.GetSharedDevice(),
                    frame.ContentSize.Width,
                    frame.ContentSize.Height,
                    96,
                    source.Format,
                    CanvasAlphaMode.Premultiplied);
                using var drawingSession = target.CreateDrawingSession();
                drawingSession.Clear(Microsoft.UI.Colors.Transparent);
                drawingSession.DrawImage(source);
                if (!completion.TrySetResult(target))
                {
                    target.Dispose();
                }

                target = null;
            }
            catch (Exception ex)
            {
                target?.Dispose();
                completion.TrySetException(ex);
            }
        }
    }

    private static async Task<ScreenshotCaptureResult> SaveCapturedBitmapAsync(
        CanvasRenderTarget bitmap,
        string filePrefix,
        ScreenshotSettings settings,
        string destinationFolder,
        float maxLuminance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationFolder);
        var frameTime = DateTimeOffset.Now;
        var capturedHdr = bitmap.Format is DirectXPixelFormat.R16G16B16A16Float;
        var extension = capturedHdr
            ? settings.Format is ScreenshotImageFormat.JpegXl ? ".jxl" : ".avif"
            : settings.Format switch
            {
                ScreenshotImageFormat.Avif => ".avif",
                ScreenshotImageFormat.JpegXl => ".jxl",
                _ => ".png",
            };
        var quality = settings.Quality switch
        {
            ScreenshotImageQuality.Medium => 80,
            ScreenshotImageQuality.Lossless => 100,
            _ => 90,
        };
        var xmpData = BuildXmpMetadata(frameTime);
        var primaryPath = CreateUniqueFilePath(
            destinationFolder,
            filePrefix,
            frameTime,
            extension);
        await using (var stream = new FileStream(
            primaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (!capturedHdr)
            {
                await ScreenshotImageEncoder.EncodeSdrAsync(
                    bitmap,
                    stream,
                    extension,
                    quality,
                    settings.EnableColorManagement,
                    xmpData);
            }
            else if (extension == ".jxl")
            {
                await ScreenshotImageEncoder.EncodeHdrJpegXlAsync(
                    bitmap,
                    stream,
                    quality,
                    maxLuminance,
                    xmpData);
            }
            else
            {
                await ScreenshotImageEncoder.EncodeHdrAvifAsync(
                    bitmap,
                    stream,
                    quality,
                    xmpData);
            }
        }

        string? sdrPath = null;
        if (capturedHdr && settings.AutoConvertHdrToSdr)
        {
            var sdrPixels = ScreenshotImageEncoder.ToneMapHdrToSdr(bitmap);
            using var sdrBitmap = CanvasBitmap.CreateFromBytes(
                CanvasDevice.GetSharedDevice(),
                sdrPixels,
                (int)bitmap.SizeInPixels.Width,
                (int)bitmap.SizeInPixels.Height,
                DirectXPixelFormat.R8G8B8A8UIntNormalized,
                96);
            sdrPath = CreateUniqueFilePath(
                destinationFolder,
                filePrefix + "_SDR",
                frameTime,
                ".png");
            await using var sdrStream = new FileStream(
                sdrPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await ScreenshotImageEncoder.EncodeSdrAsync(
                sdrBitmap,
                sdrStream,
                ".png",
                quality,
                settings.EnableColorManagement,
                xmpData);
        }

        var message = capturedHdr
            ? sdrPath is null
                ? $"HDR 截图已保存：{primaryPath}"
                : $"HDR 与 SDR 截图已保存：{primaryPath}；{sdrPath}"
            : $"截图已保存：{primaryPath}";
        return new ScreenshotCaptureResult(
            true,
            message,
            primaryPath,
            sdrPath,
            capturedHdr);
    }

    private static (IntPtr WindowHandle, string FilePrefix)? FindGameWindow()
    {
        foreach (var processName in GameProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var windowHandle = process.MainWindowHandle;
                        if (windowHandle != IntPtr.Zero &&
                            IsWindowVisible(windowHandle) &&
                            !IsIconic(windowHandle))
                        {
                            return (windowHandle, SanitizeFileName(process.ProcessName));
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                    {
                    }
                }
            }
        }

        return null;
    }

    private static bool IsHdr(DisplayAdvancedColorInfo colorInfo) =>
        colorInfo.CurrentAdvancedColorKind is DisplayAdvancedColorKind.HighDynamicRange;

    private static string CreateUniqueFilePath(
        string folder,
        string prefix,
        DateTimeOffset frameTime,
        string extension)
    {
        var safePrefix = SanitizeFileName(prefix);
        var baseName = $"{safePrefix}_{frameTime:yyyyMMdd_HHmmssff}";
        var candidate = Path.Combine(folder, baseName + extension);
        for (var suffix = 2; File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(folder, $"{baseName}_{suffix}{extension}");
        }

        return candidate;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidCharacters.Contains(character) ? '_' : character);
        }

        return builder.Length == 0 ? "Nikkiward" : builder.ToString();
    }

    private static byte[] BuildXmpMetadata(DateTimeOffset frameTime) =>
        Encoding.UTF8.GetBytes(
            $"<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"><xmp:CreatorTool>Nikkiward</xmp:CreatorTool><xmp:CreateDate>{frameTime:yyyy-MM-ddTHH:mm:sszzz}</xmp:CreateDate></rdf:Description></rdf:RDF></x:xmpmeta>");

    private static bool TryGetDisplayId(
        IntPtr windowHandle,
        out Microsoft.UI.DisplayId displayId)
    {
        displayId = default;
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
            if (windowId.Value == 0)
            {
                return false;
            }

            displayId = DisplayArea.GetFromWindowId(
                windowId,
                DisplayAreaFallback.Nearest).DisplayId;
            return displayId.Value != 0;
        }
        catch (Exception ex) when (ex is ArgumentException or COMException or InvalidOperationException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);
}
