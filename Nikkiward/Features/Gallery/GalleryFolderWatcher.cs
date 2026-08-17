using Microsoft.UI.Dispatching;

namespace Nikkiward.Features.Gallery;

/// <summary>
/// Watches the gallery root so new captures appear without a manual refresh.
/// </summary>
/// <remarks>
/// Modelled on Starward 0.18.1 (MIT, Copyright (c) 2023 Scighost), with three
/// corrections: subdirectories are included, because Infinity Nikki writes into
/// per-profile subfolders; <c>Renamed</c> is handled, because capture tools write
/// a temp file and rename it; and the extension filter is applied in managed code
/// so <c>.jpeg</c> is covered. Callbacks are coalesced onto the UI thread — an
/// unhandled exception in a raw watcher callback kills the process.
/// </remarks>
public sealed class GalleryFolderWatcher : IDisposable
{
    /// <summary>
    /// Collapses a burst of writes into one reload, and gives a capture tool time
    /// to finish writing before the scan reopens the file.
    /// </summary>
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(900);

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Action _onChanged;
    private readonly Lock _gate = new();

    private FileSystemWatcher? _watcher;
    private DispatcherQueueTimer? _timer;
    private bool _disposed;

    public GalleryFolderWatcher(DispatcherQueue dispatcherQueue, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(onChanged);
        _dispatcherQueue = dispatcherQueue;
        _onChanged = onChanged;
    }

    /// <summary>
    /// Points the watcher at <paramref name="rootPath"/>, replacing any previous
    /// target. A path that cannot be watched is not an error; the gallery simply
    /// keeps working with manual refresh.
    /// </summary>
    public void Watch(string? rootPath)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            DisposeWatcher();

            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(rootPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName |
                                   NotifyFilters.Size |
                                   NotifyFilters.LastWrite,
                };

                watcher.Created += OnWatcherEvent;
                watcher.Changed += OnWatcherEvent;
                watcher.Deleted += OnWatcherEvent;
                watcher.Renamed += OnWatcherEvent;
                watcher.Error += OnWatcherError;
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch (Exception ex) when (ex is ArgumentException
                or IOException
                or UnauthorizedAccessException
                or PlatformNotSupportedException)
            {
                DisposeWatcher();
            }
        }
    }

    public void Stop() => Watch(null);

    private void OnWatcherEvent(object sender, FileSystemEventArgs e)
    {
        // Runs on a thread pool thread. Anything that throws here would be
        // unhandled, so the body stays trivial and the real work is dispatched.
        try
        {
            if (!IsRelevant(e))
            {
                return;
            }

            ScheduleReload();
        }
        catch (Exception)
        {
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Buffer overflow or the folder went away. Re-scan once so the gallery
        // does not sit on a stale index.
        try
        {
            ScheduleReload();
        }
        catch (Exception)
        {
        }
    }

    private static bool IsRelevant(FileSystemEventArgs e)
    {
        var extension = Path.GetExtension(e.Name);
        if (string.IsNullOrEmpty(extension))
        {
            // A rename out of a temp extension arrives with the new name, so an
            // extensionless entry is a folder change and still worth a re-scan.
            return true;
        }

        return GalleryFileTypes.IsSupported(extension);
    }

    private void ScheduleReload()
    {
        if (!_dispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed)
            {
                return;
            }

            _timer ??= CreateTimer();
            // Restarting an already-running timer is what coalesces the burst.
            _timer.Stop();
            _timer.Start();
        }))
        {
            // The UI thread is gone; nothing left to refresh.
        }
    }

    private DispatcherQueueTimer CreateTimer()
    {
        var timer = _dispatcherQueue.CreateTimer();
        timer.Interval = CoalesceWindow;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _onChanged();
            }
            catch (Exception)
            {
            }
        };
        return timer;
    }

    private void DisposeWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnWatcherEvent;
            _watcher.Changed -= OnWatcherEvent;
            _watcher.Deleted -= OnWatcherEvent;
            _watcher.Renamed -= OnWatcherEvent;
            _watcher.Error -= OnWatcherError;
            _watcher.Dispose();
        }
        catch (Exception)
        {
        }
        finally
        {
            _watcher = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeWatcher();
        }

        _dispatcherQueue.TryEnqueue(() => _timer?.Stop());
    }
}
