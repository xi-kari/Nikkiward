using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Nikkiward.Features.Gallery;
using Windows.Graphics.Imaging;

namespace Nikkiward.ViewModels;

public enum GallerySortMode
{
    NewestFirst,
    OldestFirst,
    NameAscending,
    CategoryAscending,
}

public enum GalleryViewMode
{
    All,
    Favorites,
}

public sealed class GalleryViewModel : INotifyPropertyChanged
{
    /// <summary>
    /// <c>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS</c>, not in the BCL enum.
    /// </summary>
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    /// <summary>
    /// <c>FILE_ATTRIBUTE_RECALL_ON_OPEN</c>, not in the BCL enum.
    /// </summary>
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;

    private static readonly IReadOnlyDictionary<string, string> CategoryNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NikkiPhotos_HighQuality"] = "高清拍摄",
            ["NikkiPhotos_LowQuality"] = "低清副本",
            ["MagazinePhotos"] = "杂志照片",
            ["ClockInPhoto"] = "打卡照片",
            ["CloudPhotos"] = "云端照片",
            ["CloudPhotos_LowQuality"] = "云端低清",
            ["ScreenShot"] = "系统截图",
            ["Collage"] = "拼贴照片",
            ["CollagePhoto"] = "拼贴照片",
        };

    private readonly List<GalleryPhotoItemViewModel> _allPhotos = [];
    private bool _isBusy;
    private bool _isInitialized;
    private bool _rootUnavailable;
    private string _rootPath = "尚未载入图库目录";
    private string _statusText = "首次打开相册时会只读扫描当前 profile 的 GamePlayPhotos。";
    private string _searchQuery = string.Empty;
    private string _selectedCategoryId = GalleryCategoryItemViewModel.AllCategoryId;
    private GallerySortMode _sortMode = GallerySortMode.NewestFirst;
    private GalleryViewMode _viewMode;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GalleryPhotoItemViewModel> Photos { get; } = [];

    public ObservableCollection<GalleryDateGroupViewModel> DateGroups { get; } = [];

    public ObservableCollection<GalleryCategoryItemViewModel> Categories { get; } =
    [
        new(GalleryCategoryItemViewModel.AllCategoryId, "全部", 0),
    ];

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool IsInitialized
    {
        get => _isInitialized;
        private set => SetField(ref _isInitialized, value);
    }

    public string RootPath
    {
        get => _rootPath;
        private set => SetField(ref _rootPath, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string PhotoCountText => _viewMode == GalleryViewMode.Favorites
        ? $"显示 {Photos.Count:N0} 张收藏 · 共 {StarredCount:N0} 张 · 只读"
        : $"显示 {Photos.Count:N0} 张 · 已索引 {_allPhotos.Count:N0} 张 · 只读";

    /// <summary>
    /// The scanned root, or null when there is nothing to watch. Distinct from
    /// <see cref="RootPath"/>, which carries placeholder prose when the gallery is
    /// unavailable and so must never be handed to a file watcher.
    /// </summary>
    public string? WatchableRootPath { get; private set; }

    public GallerySortMode SortMode => _sortMode;

    public GalleryViewMode ViewMode => _viewMode;

    public int StarredCount => _allPhotos.Count(photo => photo.IsStarred);

    public string EmptyStateTitle => _rootUnavailable
        ? "截图文件夹不存在"
        : _allPhotos.Count == 0
            ? "当前目录中没有可显示的图片"
            : _viewMode == GalleryViewMode.Favorites && StarredCount == 0
                ? "还没有收藏照片"
            : "没有符合当前筛选条件的图片";

    public string EmptyStateDetail => _rootUnavailable
        ? "请选择 GamePlayPhotos 或其他本地图片目录；Nikkiward 只读浏览，不会移动、删除或改写文件。"
        : _allPhotos.Count == 0
            ? "目录可访问，但其中没有受支持的 PNG、JPG、JPEG、WebP 或 BMP 图片。"
            : _viewMode == GalleryViewMode.Favorites && StarredCount == 0
                ? "在相册缩略图或预览页点击星标后，照片会出现在这里。"
            : "尝试清空搜索词或切换到“全部”分类。";

    public Visibility EmptyStateVisibility => Photos.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility GridVisibility => Photos.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public async Task LoadDefaultAsync(
        string gameRootPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameRootPath) || !Directory.Exists(gameRootPath))
        {
            SetUnavailable(
                "当前 profile 没有可访问的游戏安装根。请选择一个本地图片目录。",
                gameRootPath);
            return;
        }

        var defaultRoot = Path.Combine(
            gameRootPath,
            "X6Game",
            "Saved",
            "GamePlayPhotos");

        if (!Directory.Exists(defaultRoot))
        {
            SetUnavailable(
                "当前安装根下没有发现 X6Game\\Saved\\GamePlayPhotos。请选择图库目录。",
                defaultRoot);
            return;
        }

        await LoadAsync(defaultRoot, cancellationToken);
    }

    public async Task LoadAsync(
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(rootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            SetUnavailable($"图库路径无效：{ex.GetType().Name}", rootPath);
            return;
        }

        if (!Directory.Exists(normalizedRoot))
        {
            SetUnavailable("所选图库目录不存在或当前不可访问。", normalizedRoot);
            return;
        }

        IsBusy = true;
        _rootUnavailable = false;
        RootPath = normalizedRoot;
        WatchableRootPath = normalizedRoot;
        StatusText = "正在只读枚举图片与文件元数据…";

        try
        {
            var scanned = await Task.Run(
                () => ScanRoot(normalizedRoot, cancellationToken),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            _allPhotos.Clear();
            _allPhotos.AddRange(scanned);
            _selectedCategoryId = GalleryCategoryItemViewModel.AllCategoryId;
            _searchQuery = string.Empty;
            _sortMode = GallerySortMode.NewestFirst;

            RebuildCategories();
            ApplyFilterAndSort();

            IsInitialized = true;
            StatusText = scanned.Count == 0
                ? "扫描完成；目录中没有受支持的 PNG、JPG、JPEG、WebP 或 BMP 图片。"
                : $"扫描完成 · {scanned.Count:N0} 张图片 · 不读取游戏进程、账号状态或令牌";
        }
        catch (OperationCanceledException)
        {
            StatusText = "图库扫描已取消。";
            throw;
        }
        catch (Exception ex)
        {
            _allPhotos.Clear();
            Photos.Clear();
            DateGroups.Clear();
            RebuildCategories();
            IsInitialized = true;
            StatusText = $"图库扫描失败：{ex.GetType().Name}: {RedactUserPath(ex.Message)}";
            NotifyCollectionStateChanged();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SetSearchQuery(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.Equals(_searchQuery, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _searchQuery = normalized;
        ApplyFilterAndSort();
    }

    public void SetCategory(string? categoryId)
    {
        var normalized = string.IsNullOrWhiteSpace(categoryId)
            ? GalleryCategoryItemViewModel.AllCategoryId
            : categoryId;

        if (string.Equals(_selectedCategoryId, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _selectedCategoryId = normalized;
        ApplyFilterAndSort();
    }

    public void SetSortMode(GallerySortMode sortMode)
    {
        if (_sortMode == sortMode)
        {
            return;
        }

        _sortMode = sortMode;
        OnPropertyChanged(nameof(SortMode));
        ApplyFilterAndSort();
    }

    public void SetViewMode(GalleryViewMode viewMode)
    {
        if (_viewMode == viewMode)
        {
            return;
        }

        _viewMode = viewMode;
        OnPropertyChanged(nameof(ViewMode));
        ApplyFilterAndSort();
    }

    public void ApplyStarredPaths(IReadOnlySet<string> starredPaths)
    {
        ArgumentNullException.ThrowIfNull(starredPaths);
        foreach (var photo in _allPhotos)
        {
            photo.SetStarred(starredPaths.Contains(photo.StarKey));
        }

        ApplyFilterAndSort();
    }

    public void ApplyProtectedFavorites(
        IReadOnlyList<GalleryProtectedFavorite> protectedFavorites)
    {
        ArgumentNullException.ThrowIfNull(protectedFavorites);
        var protectedByPath = protectedFavorites
            .GroupBy(
                item => GalleryAnnotationStore.NormalizeRelativePath(item.Entry.RelativePath),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.Entry.ProtectedAtUtc).First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var photo in _allPhotos)
        {
            if (protectedByPath.TryGetValue(photo.StarKey, out var protectedFavorite))
            {
                photo.SetProtectedCopy(
                    protectedFavorite.ProtectedPath,
                    isUsingProtectedCopy: false);
            }
            else
            {
                photo.SetProtectedCopy(null, isUsingProtectedCopy: false);
            }
        }

        var existingPaths = _allPhotos
            .Select(photo => photo.StarKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var protectedFavorite in protectedFavorites.Where(item => item.IsUsingProtectedCopy))
        {
            var entry = protectedFavorite.Entry;
            var starKey = GalleryAnnotationStore.NormalizeRelativePath(entry.RelativePath);
            if (!existingPaths.Add(starKey))
            {
                continue;
            }

            var fileName = Path.GetFileName(entry.OriginalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = Path.GetFileName(entry.RelativePath);
            }

            var (categoryId, categoryName) = ResolveCategory(entry.RelativePath);
            var fallback = new GalleryPhotoItemViewModel(
                protectedFavorite.ProtectedPath,
                entry.RelativePath,
                fileName,
                categoryId,
                categoryName,
                entry.OriginalLength,
                entry.OriginalLastWriteTimeUtc.UtcDateTime);
            fallback.SetStarred(true);
            fallback.SetProtectedCopy(
                protectedFavorite.ProtectedPath,
                isUsingProtectedCopy: true);
            _allPhotos.Add(fallback);
        }

        RebuildCategories();
        ApplyFilterAndSort();
    }

    public void ApplyDefaultFavorites(
        IReadOnlyList<GalleryDefaultFavorite> defaultFavorites)
    {
        ArgumentNullException.ThrowIfNull(defaultFavorites);
        _allPhotos.RemoveAll(photo => string.Equals(
            photo.AnnotationScopeId,
            GalleryDefaultFavoriteSeedService.ScopeId,
            StringComparison.Ordinal));

        foreach (var favorite in defaultFavorites)
        {
            var fileName = Path.GetFileName(favorite.FilePath);
            var photo = new GalleryPhotoItemViewModel(
                favorite.FilePath,
                favorite.RelativePath,
                fileName,
                "DefaultFavorites",
                "默认收藏",
                favorite.FileSizeBytes,
                favorite.LastWriteTimeUtc,
                favorite.ScopeId);
            photo.SetStarred(true);
            _allPhotos.Add(photo);
        }

        RebuildCategories();
        ApplyFilterAndSort();
    }

    public void SetStarred(GalleryPhotoItemViewModel photo, bool isStarred)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (!_allPhotos.Contains(photo))
        {
            return;
        }

        photo.SetStarred(isStarred);
        ApplyFilterAndSort();
    }

    private static List<GalleryPhotoItemViewModel> ScanRoot(
        string rootPath,
        CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
        };

        var items = new List<GalleryPhotoItemViewModel>();
        foreach (var file in new DirectoryInfo(rootPath).EnumerateFiles("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!GalleryFileTypes.IsSupported(file.Extension))
            {
                continue;
            }

            if (IsCloudPlaceholder(file.Attributes))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(rootPath, file.FullName);
            var (categoryId, categoryName) = ResolveCategory(relativePath);
            items.Add(new GalleryPhotoItemViewModel(
                file.FullName,
                relativePath,
                file.Name,
                categoryId,
                categoryName,
                file.Length,
                GalleryTimestamp.Resolve(file.Name, file.LastWriteTimeUtc)));
        }

        return items;
    }

    /// <summary>
    /// Skips OneDrive-style dehydrated files. Reading one triggers a download, so
    /// a recursive scan over a synced folder would silently pull gigabytes.
    /// </summary>
    private static bool IsCloudPlaceholder(FileAttributes attributes) =>
        (attributes & (RecallOnDataAccess | RecallOnOpen)) != 0;

    private static (string Id, string DisplayName) ResolveCategory(string relativePath)
    {
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (CategoryNames.TryGetValue(segment, out var displayName))
            {
                return (segment, displayName);
            }
        }

        return relativePath.EndsWith("PendingUp.jpeg", StringComparison.OrdinalIgnoreCase)
            ? ("PendingUp", "待上传")
            : ("Other", "其他");
    }

    private void RebuildCategories()
    {
        Categories.Clear();
        Categories.Add(new GalleryCategoryItemViewModel(
            GalleryCategoryItemViewModel.AllCategoryId,
            "全部",
            _allPhotos.Count));

        foreach (var group in _allPhotos
                     .GroupBy(photo => new { photo.CategoryId, photo.CategoryName })
                     .OrderBy(group => group.Key.CategoryName, StringComparer.CurrentCulture))
        {
            Categories.Add(new GalleryCategoryItemViewModel(
                group.Key.CategoryId,
                group.Key.CategoryName,
                group.Count()));
        }
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<GalleryPhotoItemViewModel> query = _allPhotos;

        if (_viewMode == GalleryViewMode.Favorites)
        {
            query = query.Where(photo => photo.IsStarred);
        }

        if (!string.Equals(
                _selectedCategoryId,
                GalleryCategoryItemViewModel.AllCategoryId,
                StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(photo => string.Equals(
                photo.CategoryId,
                _selectedCategoryId,
                StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            query = query.Where(photo =>
                photo.FileName.Contains(_searchQuery, StringComparison.CurrentCultureIgnoreCase) ||
                photo.CategoryName.Contains(_searchQuery, StringComparison.CurrentCultureIgnoreCase) ||
                photo.RelativePath.Contains(_searchQuery, StringComparison.CurrentCultureIgnoreCase));
        }

        query = _sortMode switch
        {
            GallerySortMode.OldestFirst => query
                .OrderBy(photo => photo.LastWriteTimeUtc.ToLocalTime().Date)
                .ThenBy(photo => photo.LastWriteTimeUtc)
                .ThenBy(photo => photo.FileName, StringComparer.CurrentCultureIgnoreCase),
            GallerySortMode.NameAscending => query
                .OrderByDescending(photo => photo.LastWriteTimeUtc.ToLocalTime().Date)
                .ThenBy(photo => photo.FileName, StringComparer.CurrentCultureIgnoreCase),
            GallerySortMode.CategoryAscending => query
                .OrderByDescending(photo => photo.LastWriteTimeUtc.ToLocalTime().Date)
                .ThenBy(photo => photo.CategoryName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(photo => photo.FileName, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(photo => photo.LastWriteTimeUtc),
            _ => query
                .OrderByDescending(photo => photo.LastWriteTimeUtc.ToLocalTime().Date)
                .ThenByDescending(photo => photo.LastWriteTimeUtc)
                .ThenBy(photo => photo.FileName, StringComparer.CurrentCultureIgnoreCase),
        };

        Photos.Clear();
        var orderedPhotos = query.ToList();
        foreach (var photo in orderedPhotos)
        {
            Photos.Add(photo);
        }

        RebuildDateGroups(orderedPhotos);

        NotifyCollectionStateChanged();
    }

    private void RebuildDateGroups(IReadOnlyList<GalleryPhotoItemViewModel> orderedPhotos)
    {
        DateGroups.Clear();

        foreach (var group in orderedPhotos
                     .GroupBy(photo => photo.LastWriteTimeUtc.ToLocalTime().Date)
                     .OrderBy(group => group.Key,
                         _sortMode == GallerySortMode.OldestFirst
                             ? Comparer<DateTime>.Default
                             : Comparer<DateTime>.Create((left, right) => right.CompareTo(left))))
        {
            var dateGroup = new GalleryDateGroupViewModel(group.Key);
            foreach (var photo in group)
            {
                dateGroup.Items.Add(photo);
            }

            DateGroups.Add(dateGroup);
        }
    }

    private void SetUnavailable(string status, string? rootPath)
    {
        _rootUnavailable = true;
        WatchableRootPath = null;
        _allPhotos.Clear();
        Photos.Clear();
        DateGroups.Clear();
        RootPath = string.IsNullOrWhiteSpace(rootPath) ? "尚未载入图库目录" : rootPath;
        StatusText = status;
        IsInitialized = true;
        RebuildCategories();
        NotifyCollectionStateChanged();
    }

    private void NotifyCollectionStateChanged()
    {
        OnPropertyChanged(nameof(PhotoCountText));
        OnPropertyChanged(nameof(StarredCount));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDetail));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(GridVisibility));
    }

    private static string RedactUserPath(string value)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userProfile)
            ? value
            : value.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class GalleryPhotoItemViewModel : INotifyPropertyChanged
{
    private ImageSource? _thumbnailSource;
    private bool _thumbnailRequested;
    private bool _isStarred;
    private ImageSource? _cardSource;
    private double _cardAspectRatio = GalleryFavoriteCardLayoutProjection.DefaultAspectRatio;
    private int _cardAspectRatioLoadVersion;
    private string? _protectedFilePath;
    private bool _isUsingProtectedCopy;

    public GalleryPhotoItemViewModel(
        string filePath,
        string relativePath,
        string fileName,
        string categoryId,
        string categoryName,
        long fileSizeBytes,
        DateTime lastWriteTimeUtc,
        string? annotationScopeId = null)
    {
        FilePath = filePath;
        RelativePath = relativePath;
        StarKey = GalleryAnnotationStore.NormalizeRelativePath(relativePath);
        FileName = fileName;
        CategoryId = categoryId;
        CategoryName = categoryName;
        FileSizeBytes = fileSizeBytes;
        LastWriteTimeUtc = DateTime.SpecifyKind(lastWriteTimeUtc, DateTimeKind.Utc);
        AnnotationScopeId = annotationScopeId;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FilePath { get; }

    public string RelativePath { get; }

    public string StarKey { get; }

    public string? AnnotationScopeId { get; }

    public string FileName { get; }

    public string CategoryId { get; }

    public string CategoryName { get; }

    public long FileSizeBytes { get; }

    /// <summary>
    /// Capture time in UTC, from the file name when it carries one, otherwise the
    /// file system write time. See <see cref="GalleryTimestamp"/>.
    /// </summary>
    public DateTime LastWriteTimeUtc { get; }

    public string ModifiedText => LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string FileSizeText => FormatBytes(FileSizeBytes);

    public string MetadataText => $"{CategoryName} · {ModifiedText} · {FileSizeText}";

    public string HoverTimeText => LastWriteTimeUtc.ToLocalTime().ToString("HH:mm");

    public string AccessibleName => HasProtectedCopy
        ? $"{FileName}，{CategoryName}，{ModifiedText}，{ProtectionStatusText}"
        : $"{FileName}，{CategoryName}，{ModifiedText}";

    public bool IsStarred => _isStarred;

    public Visibility StarredVisibility => _isStarred
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string StarCommandText => _isStarred ? "取消收藏" : "添加到收藏";

    public string StarGlyph => _isStarred ? "\uE735" : "\uE734";

    public ImageSource CardSource => _isStarred
        ? _cardSource ??= CreateFullResolutionSource(ResolveFavoriteCardPath(FilePath))
        : ThumbnailSource;

    public double CardAspectRatio => _cardAspectRatio;

    public string? ProtectedFilePath => _protectedFilePath;

    public bool HasProtectedCopy => !string.IsNullOrWhiteSpace(_protectedFilePath);

    public bool IsUsingProtectedCopy => _isUsingProtectedCopy;

    public Visibility ProtectedVisibility => HasProtectedCopy
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ProtectionStatusText => _isUsingProtectedCopy
        ? "原图不可用，正在使用本地保护副本"
        : HasProtectedCopy
            ? "本地保护副本已就绪"
            : "尚未创建本地保护副本";

    public string UidText
    {
        get
        {
            var segments = FilePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            return segments.FirstOrDefault(segment =>
                segment.Length is >= 6 and <= 12 &&
                segment.All(char.IsAsciiDigit)) ?? "未识别";
        }
    }

    /// <summary>
    /// Binds to the full image on first read, then swaps in the cached thumbnail
    /// once it is on disk. Returning the original immediately keeps the grid from
    /// showing holes while the cache warms.
    /// </summary>
    public ImageSource ThumbnailSource
    {
        get
        {
            if (!_thumbnailRequested)
            {
                _thumbnailRequested = true;
                _ = LoadCachedThumbnailAsync();
            }

            return _thumbnailSource ??= CreateThumbnail(FilePath);
        }
    }

    private async Task LoadCachedThumbnailAsync()
    {
        try
        {
            // Task.Run keeps the hash, decode and encode off the UI thread; the
            // continuation resumes on it, which BitmapImage requires.
            var cachePath = await Task.Run(
                () => GalleryThumbnailCache.TryGetThumbnailPathAsync(FilePath));
            if (string.IsNullOrEmpty(cachePath))
            {
                return;
            }

            _thumbnailSource = CreateThumbnail(cachePath);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailSource)));
        }
        catch (Exception)
        {
            // The full-size fallback is already bound; a cache miss is not worth
            // surfacing to the user.
        }
    }

    public void SetStarred(bool isStarred)
    {
        if (_isStarred == isStarred)
        {
            return;
        }

        _isStarred = isStarred;
        _cardSource = null;
        var aspectRatioLoadVersion = ++_cardAspectRatioLoadVersion;
        SetCardAspectRatio(GalleryFavoriteCardLayoutProjection.DefaultAspectRatio);
        if (isStarred)
        {
            _ = LoadCardAspectRatioAsync(aspectRatioLoadVersion);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStarred)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardSource)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StarredVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StarCommandText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StarGlyph)));
    }

    public void SetProtectedCopy(string? protectedFilePath, bool isUsingProtectedCopy)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(protectedFilePath)
            ? null
            : Path.GetFullPath(protectedFilePath);
        var normalizedUsingState = normalizedPath is not null && isUsingProtectedCopy;
        if (string.Equals(
                _protectedFilePath,
                normalizedPath,
                StringComparison.OrdinalIgnoreCase) &&
            _isUsingProtectedCopy == normalizedUsingState)
        {
            return;
        }

        _protectedFilePath = normalizedPath;
        _isUsingProtectedCopy = normalizedUsingState;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProtectedFilePath)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasProtectedCopy)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUsingProtectedCopy)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProtectedVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProtectionStatusText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessibleName)));
    }

    private static BitmapImage CreateThumbnail(string filePath)
    {
        var image = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Logical,
            DecodePixelWidth = 360,
            UriSource = new Uri(filePath, UriKind.Absolute),
        };
        return image;
    }

    private static BitmapImage CreateFullResolutionSource(string filePath)
    {
        return new BitmapImage
        {
            CreateOptions = BitmapCreateOptions.IgnoreImageCache,
            UriSource = new Uri(filePath, UriKind.Absolute),
        };
    }

    private async Task LoadCardAspectRatioAsync(int loadVersion)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(
                ResolveFavoriteCardPath(FilePath));
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            if (!_isStarred || loadVersion != _cardAspectRatioLoadVersion)
            {
                return;
            }

            SetCardAspectRatio(
                decoder.OrientedPixelWidth / (double)decoder.OrientedPixelHeight);
        }
        catch (Exception)
        {
        }
    }

    private void SetCardAspectRatio(double aspectRatio)
    {
        var normalized = double.IsFinite(aspectRatio) && aspectRatio > 0d
            ? aspectRatio
            : GalleryFavoriteCardLayoutProjection.DefaultAspectRatio;
        if (Math.Abs(_cardAspectRatio - normalized) < 0.000001d)
        {
            return;
        }

        _cardAspectRatio = normalized;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CardAspectRatio)));
    }

    private static string ResolveFavoriteCardPath(string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (!stem.EndsWith("_Low", StringComparison.OrdinalIgnoreCase))
        {
            return filePath;
        }

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return filePath;
        }

        var candidate = Path.Combine(
            directory,
            stem[..^4] + Path.GetExtension(filePath));
        return File.Exists(candidate) ? candidate : filePath;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)bytes;
        var index = 0;

        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.##} {units[index]}";
    }
}

public sealed class GalleryCategoryItemViewModel
{
    public const string AllCategoryId = "__all__";

    public GalleryCategoryItemViewModel(string categoryId, string displayName, int count)
    {
        CategoryId = categoryId;
        DisplayName = displayName;
        Count = count;
    }

    public string CategoryId { get; }

    public string DisplayName { get; }

    public int Count { get; }

    public string Label => $"{DisplayName}  {Count:N0}";
}

public sealed class GalleryDateGroupViewModel
{
    public GalleryDateGroupViewModel(DateTime date)
    {
        Date = date.Date;
        HeaderText = Date.ToString("yyyy年M月d日");
    }

    public DateTime Date { get; }

    public string HeaderText { get; }

    public ObservableCollection<GalleryPhotoItemViewModel> Items { get; } = [];

    public string PhotoCountText => $"{Items.Count:N0} 张照片";

    public string AccessibleName => $"{HeaderText}，{PhotoCountText}";
}
