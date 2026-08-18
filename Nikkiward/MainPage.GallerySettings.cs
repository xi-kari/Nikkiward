using Microsoft.UI.Xaml;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.Settings;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private readonly NikkiGalleryToolRegistry _nikkiGalleryToolRegistry = new();
    private readonly GalleryAnnotationStore _galleryAnnotationStore = new();
    private readonly GalleryFavoriteProtectionService _galleryFavoriteProtectionService = new();
    private GalleryThumbnailCacheStatistics? _galleryCacheStatistics;
    private GalleryFavoriteProtectionOverview? _galleryProtectionOverview;
    private NikkiGalleryToolState? _nikkiGalleryToolState;
    private bool _gallerySettingsOperationInProgress;
    private int _gallerySettingsRefreshInProgress;

    private void ApplyGallerySettingsPageState(SettingsPage page)
    {
        var profileId = ViewModel.SelectedProfileId;
        var customRoot = ViewModel.GalleryRootPath;
        var defaultRoot = ResolveDefaultGalleryRoot(ViewModel.GameRootPath);
        var effectiveRoot = customRoot ?? defaultRoot;
        var cache = _galleryCacheStatistics;
        var protection = _galleryProtectionOverview;
        var tool = _nikkiGalleryToolState;
        var busy = _gallerySettingsOperationInProgress;

        page.ApplyGalleryState(new GallerySettingsViewState(
            string.IsNullOrWhiteSpace(profileId)
                ? "未选择 Profile"
                : $"{ViewModel.ProfileDisplayName} · {profileId}",
            effectiveRoot ?? "尚未配置",
            customRoot is not null
                ? "自定义目录"
                : defaultRoot is null
                    ? "当前 Profile 没有可用的默认目录"
                    : "当前 Profile 默认目录",
            CanChooseRoot: !busy && !string.IsNullOrWhiteSpace(profileId),
            CanResetRoot: !busy && customRoot is not null,
            CanOpenGallery: !busy,
            ProtectionEnabled: protection?.Preferences.IsEnabled ?? true,
            ProtectionStatusText: protection is null
                ? "正在读取"
                : FormatProtectionStatus(protection),
            ProtectionPathText: protection?.Preferences.ActiveRootPath ?? "正在读取",
            CanChangeProtection: !busy,
            CanOpenProtectionRoot: !busy &&
                protection is not null &&
                !protection.UnavailableRootPaths.Contains(
                    protection.Preferences.ActiveRootPath,
                    StringComparer.OrdinalIgnoreCase),
            CanVerifyProtection: !busy && protection?.Statistics.EntryCount > 0,
            CanCleanProtection: !busy &&
                !string.IsNullOrWhiteSpace(profileId) &&
                protection?.Statistics.EntryCount > 0,
            CacheStatusText: cache is null
                ? "正在读取"
                : cache.IsAvailable
                    ? $"{cache.FileCount:N0} 个缩略图 · {FormatGalleryBytes(cache.TotalBytes)}"
                    : "缓存目录不可用",
            CachePathText: GalleryThumbnailCache.FolderPath ?? "尚未生成",
            CanRefreshCache: !busy,
            CanClearCache: !busy && cache is { FileCount: > 0 },
            NikkiGalleryStatusText: tool?.StatusText ?? "正在读取",
            NikkiGalleryPathText: tool?.ExecutablePath ?? "尚未关联",
            CanRegisterNikkiGallery: !busy,
            CanOpenNikkiGallery: !busy && tool?.IsAvailable is true,
            CanDisconnectNikkiGallery: !busy && tool?.IsRegistered is true,
            IsBusy: busy));
    }

    private async Task RefreshGallerySettingsStateAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _gallerySettingsRefreshInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            await LoadGallerySettingsStateAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError(
                $"相册设置读取失败：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _gallerySettingsRefreshInProgress, 0);
            if (_hostedSettingsPage is { } page)
            {
                ApplyGallerySettingsPageState(page);
            }
        }
    }

    private async Task LoadGallerySettingsStateAsync(CancellationToken cancellationToken)
    {
        var cacheTask = GalleryThumbnailCache.GetStatisticsAsync(cancellationToken);
        var protectionTask = _galleryFavoriteProtectionService.GetOverviewAsync(
            verify: false,
            cancellationToken);
        var toolTask = _nikkiGalleryToolRegistry.GetStateAsync(cancellationToken);
        await Task.WhenAll(cacheTask, protectionTask, toolTask);
        _galleryCacheStatistics = await cacheTask;
        _galleryProtectionOverview = await protectionTask;
        _nikkiGalleryToolState = await toolTask;
    }

    private async Task RunGallerySettingsOperationAsync(
        Func<CancellationToken, Task> operation,
        string failurePrefix)
    {
        if (_gallerySettingsOperationInProgress)
        {
            return;
        }

        _gallerySettingsOperationInProgress = true;
        if (_hostedSettingsPage is { } page)
        {
            ApplyGallerySettingsPageState(page);
        }

        var cancellationToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
        try
        {
            await operation(cancellationToken);
            await LoadGallerySettingsStateAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError(
                $"{failurePrefix}：{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _gallerySettingsOperationInProgress = false;
            if (_hostedSettingsPage is { } currentPage)
            {
                ApplyGallerySettingsPageState(currentPage);
            }
        }
    }

    private async void OnSettingsGalleryRootChooseRequested(object? sender, EventArgs e)
    {
        var profileId = ViewModel.SelectedProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        var picker = new FolderPicker
        {
            CommitButtonText = "选择图库目录",
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
        };
        picker.FileTypeFilter.Add("*");
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null || string.IsNullOrWhiteSpace(folder.Path))
        {
            return;
        }

        await RunGallerySettingsOperationAsync(
            cancellationToken => ViewModel.SaveGalleryRootAsync(
                profileId,
                folder.Path,
                cancellationToken),
            "图库目录保存失败");
    }

    private async void OnSettingsGalleryRootResetRequested(object? sender, EventArgs e)
    {
        var profileId = ViewModel.SelectedProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        await RunGallerySettingsOperationAsync(
            cancellationToken => ViewModel.ResetGalleryRootAsync(profileId, cancellationToken),
            "图库目录恢复失败");
    }

    private async void OnSettingsGalleryOpenRequested(object? sender, EventArgs e)
    {
        SetShellNavigationSelection(GalleryNavigationItem);
        await ShowGalleryAsync(returnToSettings: true);
    }

    private async void OnSettingsGalleryProtectionEnabledChanged(
        object? sender,
        GalleryProtectionEnabledChangedEventArgs e)
    {
        await RunGallerySettingsOperationAsync(
            cancellationToken => _galleryFavoriteProtectionService.SetEnabledAsync(
                e.Enabled,
                cancellationToken),
            "收藏保护设置保存失败");
    }

    private async void OnSettingsGalleryProtectionRootChooseRequested(
        object? sender,
        EventArgs e)
    {
        var picker = new FolderPicker
        {
            CommitButtonText = "选择收藏保护目录",
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
        };
        picker.FileTypeFilter.Add("*");
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null || string.IsNullOrWhiteSpace(folder.Path))
        {
            return;
        }

        await RunGallerySettingsOperationAsync(
            cancellationToken => _galleryFavoriteProtectionService.SetActiveRootAsync(
                folder.Path,
                cancellationToken),
            "收藏保护目录保存失败");
    }

    private async void OnSettingsGalleryProtectionRootOpenRequested(
        object? sender,
        EventArgs e)
    {
        await RunGallerySettingsOperationAsync(
            async cancellationToken =>
            {
                var preferences = await _galleryFavoriteProtectionService.GetPreferencesAsync(
                    cancellationToken);
                Directory.CreateDirectory(preferences.ActiveRootPath);
                var folder = await StorageFolder.GetFolderFromPathAsync(preferences.ActiveRootPath);
                if (!await Launcher.LaunchFolderAsync(folder))
                {
                    throw new InvalidOperationException("The favorite protection folder was not opened.");
                }
            },
            "收藏保护目录打开失败");
    }

    private async void OnSettingsGalleryProtectionVerifyRequested(
        object? sender,
        EventArgs e)
    {
        await RunGallerySettingsOperationAsync(
            async cancellationToken =>
            {
                _galleryProtectionOverview = await _galleryFavoriteProtectionService.VerifyAsync(
                    cancellationToken);
            },
            "收藏保护校验失败");
    }

    private async void OnSettingsGalleryProtectionCleanRequested(
        object? sender,
        EventArgs e)
    {
        var profileId = ViewModel.SelectedProfileId;
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return;
        }

        var effectiveRoot = ViewModel.GalleryRootPath ?? ResolveDefaultGalleryRoot(ViewModel.GameRootPath);
        var scopeId = GalleryAnnotationStore.CreateScopeId(profileId, effectiveRoot);
        await RunGallerySettingsOperationAsync(
            async cancellationToken =>
            {
                var starred = await _galleryAnnotationStore.LoadStarredAsync(
                    scopeId,
                    cancellationToken);
                _ = await _galleryFavoriteProtectionService.CleanUnstarredAsync(
                    scopeId,
                    starred,
                    cancellationToken);
            },
            "未收藏保护副本清理失败");
    }

    private async void OnSettingsGalleryCacheRefreshRequested(object? sender, EventArgs e)
    {
        await RunGallerySettingsOperationAsync(
            async cancellationToken =>
            {
                _galleryCacheStatistics = await GalleryThumbnailCache.GetStatisticsAsync(cancellationToken);
            },
            "缓存统计刷新失败");
    }

    private async void OnSettingsGalleryCacheClearRequested(object? sender, EventArgs e)
    {
        await RunGallerySettingsOperationAsync(
            async cancellationToken =>
            {
                _ = await GalleryThumbnailCache.ClearAsync(cancellationToken);
            },
            "缩略图缓存清理失败");
    }

    private async void OnSettingsNikkiGalleryRegisterRequested(object? sender, EventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".exe");
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is null || string.IsNullOrWhiteSpace(file.Path))
        {
            return;
        }

        await RunGallerySettingsOperationAsync(
            async cancellationToken =>
            {
                _nikkiGalleryToolState = await _nikkiGalleryToolRegistry.RegisterAsync(
                    file.Path,
                    cancellationToken);
            },
            "NikkiGallery 关联失败");
    }

    private async void OnSettingsNikkiGalleryOpenRequested(object? sender, EventArgs e)
    {
        await RunGallerySettingsOperationAsync(
            async cancellationToken =>
            {
                if (!await _nikkiGalleryToolRegistry.LaunchAsync(cancellationToken))
                {
                    throw new InvalidOperationException("NikkiGallery process was not created.");
                }
            },
            "NikkiGallery 打开失败");
    }

    private async void OnSettingsNikkiGalleryDisconnectRequested(object? sender, EventArgs e)
    {
        await RunGallerySettingsOperationAsync(
            cancellationToken => _nikkiGalleryToolRegistry.DisconnectAsync(cancellationToken),
            "NikkiGallery 解除关联失败");
    }

    private static string? ResolveDefaultGalleryRoot(string gameRootPath)
    {
        if (string.IsNullOrWhiteSpace(gameRootPath) || !Path.IsPathRooted(gameRootPath))
        {
            return null;
        }

        try
        {
            return Path.Combine(
                Path.GetFullPath(gameRootPath),
                "X6Game",
                "Saved",
                "GamePlayPhotos");
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    private static string FormatGalleryBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)Math.Max(0, bytes);
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {units[index]}";
    }

    private static string FormatProtectionStatus(GalleryFavoriteProtectionOverview overview)
    {
        var statistics = overview.Statistics;
        if (!overview.Preferences.IsEnabled)
        {
            return $"已关闭 · 已保留 {statistics.EntryCount:N0} 条保护记录";
        }

        if (overview.UnavailableRootPaths.Count > 0)
        {
            return $"{overview.UnavailableRootPaths.Count:N0} 个保护目录不可用 · 已读取 {statistics.EntryCount:N0} 条记录";
        }

        var problems = statistics.ObjectMissingCount + statistics.ObjectCorruptCount;
        var parts = new List<string>
        {
            $"{statistics.EntryCount:N0} 条记录",
            $"{statistics.UniqueObjectCount:N0} 个保护对象",
            FormatGalleryBytes(statistics.ProtectedBytes),
        };
        if (statistics.OriginalMissingCount > 0)
        {
            parts.Add($"{statistics.OriginalMissingCount:N0} 张原图缺失");
        }
        if (statistics.OriginalChangedCount > 0)
        {
            parts.Add($"{statistics.OriginalChangedCount:N0} 张原图已变化");
        }
        if (problems > 0)
        {
            parts.Add($"{problems:N0} 个副本异常");
        }

        return string.Join(" · ", parts);
    }
}
