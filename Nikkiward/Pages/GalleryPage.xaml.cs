using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Concurrent;
using System.Text.Json;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.Shell;
using Nikkiward.Models;
using Nikkiward.ViewModels;

namespace Nikkiward.Pages;

public sealed record GalleryNavigationContext(
    string? ProfileId,
    string? GameRootPath,
    string? PersistedGalleryRootPath,
    CancellationToken HostCancellationToken,
    GalleryViewMode ViewMode = GalleryViewMode.All,
    AppearanceSettings? AppearanceSettings = null);

public sealed partial class GalleryPage : PageBase
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private readonly GalleryAnnotationStore _annotationStore = new();
    private readonly GalleryFavoriteProtectionService _favoriteProtectionService = new();
    private readonly GalleryDefaultFavoriteSeedService _defaultFavoriteSeedService = new();
    private readonly GalleryFolderWatcher _folderWatcher;
    private readonly Nuan5GalleryMetadataService _metadataService = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _favoriteOperationGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _favoriteOperationCancellations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _favoriteDesiredStates =
        new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _loadCancellation;
    private long _galleryGeneration;
    private string? _profileGameRootPath;
    private string? _profileId;
    private string? _manualGalleryRootPath;
    private string? _persistedGalleryRootPath;
    private string _annotationScopeId = GalleryAnnotationStore.CreateScopeId(null, null);
    private GalleryViewMode _viewMode;
    private int _galleryPreviewIndex = -1;
    private AppearanceSettings _appearanceSettings = new();
    private bool _surfaceActive = true;
    private bool _galleryGridLayoutRefreshQueued;
    private bool _xamlInitialized;

    public GalleryViewModel ViewModel { get; } = new();

    private GridView ActiveGalleryGridView => _viewMode == GalleryViewMode.Favorites
        ? GalleryFavoriteGridView
        : GalleryGridView;

    public override string PageTitle => _viewMode == GalleryViewMode.Favorites
        ? "收藏"
        : "相册";

    public override UIElement? CommandBarContent =>
        _xamlInitialized ? GalleryCommandBar : null;

    public GalleryPage()
    {
        InitializeComponent();
        _xamlInitialized = true;
        _folderWatcher = new GalleryFolderWatcher(DispatcherQueue, OnGalleryFolderChanged);
        GalleryPreview.MetadataLoader = _metadataService.ReadAsync;
        GalleryPreview.CloseRequested += OnGalleryPreviewCloseRequested;
        GalleryPreview.CopyRequested += OnGalleryPreviewCopyRequested;
        GalleryPreview.StarRequested += OnGalleryPreviewStarRequested;
        GalleryPreview.PreviousRequested += OnGalleryPreviewPreviousRequested;
        GalleryPreview.NextRequested += OnGalleryPreviewNextRequested;
        Unloaded += OnPageUnloaded;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is GalleryNavigationContext context)
        {
            ApplyAppearanceSettings(context.AppearanceSettings ?? new AppearanceSettings());
            SetSurfaceActive(true);
            _ = LoadForProfileAsync(
                context.ProfileId,
                context.GameRootPath,
                context.PersistedGalleryRootPath,
                context.HostCancellationToken,
                context.ViewMode,
                forceReload: false);
        }
    }

    public async Task LoadForProfileAsync(
        string? profileId,
        string? gameRootPath,
        string? persistedGalleryRootPath,
        CancellationToken hostCancellationToken,
        GalleryViewMode viewMode = GalleryViewMode.All,
        bool forceReload = false)
    {
        if (!forceReload &&
            string.Equals(
                _profileGameRootPath,
                gameRootPath,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_profileId, profileId, StringComparison.Ordinal) &&
            string.Equals(
                _persistedGalleryRootPath,
                persistedGalleryRootPath,
                StringComparison.OrdinalIgnoreCase) &&
            ViewModel.IsInitialized)
        {
            _folderWatcher.Watch(ViewModel.WatchableRootPath);
            SetViewMode(viewMode);
            return;
        }

        await _loadGate.WaitAsync();
        try
        {
            if (!forceReload &&
                string.Equals(
                    _profileGameRootPath,
                    gameRootPath,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_profileId, profileId, StringComparison.Ordinal) &&
                string.Equals(
                    _persistedGalleryRootPath,
                    persistedGalleryRootPath,
                    StringComparison.OrdinalIgnoreCase) &&
                ViewModel.IsInitialized)
            {
                _folderWatcher.Watch(ViewModel.WatchableRootPath);
                SetViewMode(viewMode);
                return;
            }

            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            CancelFavoriteOperations();
            Interlocked.Increment(ref _galleryGeneration);
            _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                hostCancellationToken);
            var cancellationToken = _loadCancellation.Token;
            _profileId = profileId;
            _profileGameRootPath = gameRootPath;
            _persistedGalleryRootPath = persistedGalleryRootPath;
            _manualGalleryRootPath = string.IsNullOrWhiteSpace(persistedGalleryRootPath)
                ? null
                : persistedGalleryRootPath;
            CloseGalleryPreview();

            try
            {
                if (!string.IsNullOrWhiteSpace(_manualGalleryRootPath))
                {
                    await ViewModel.LoadAsync(_manualGalleryRootPath, cancellationToken);
                }
                else
                {
                    await ViewModel.LoadDefaultAsync(gameRootPath ?? string.Empty, cancellationToken);
                }
                await LoadStarredStateAsync(cancellationToken);
                SetViewMode(viewMode);
                ResetGalleryControls();
                _folderWatcher.Watch(ViewModel.WatchableRootPath);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                await ShowDialogAsync(
                    "相册初始化失败",
                    $"只读扫描未完成：{ex.GetType().Name}");
            }
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public void ClosePreview()
    {
        CloseGalleryPreview();
    }

    public void ApplyAppearanceSettings(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _appearanceSettings = settings;
        RefreshGalleryCardPresentation();
    }

    public void SetSurfaceActive(bool active)
    {
        if (_surfaceActive == active)
        {
            return;
        }

        _surfaceActive = active;
        RefreshGalleryCardPresentation();
    }

    private bool IsFavoriteCardEffectEnabled =>
        _surfaceActive &&
        _viewMode == GalleryViewMode.Favorites &&
        _appearanceSettings.Background.HolographicCardEnabled;

    private void SetViewMode(GalleryViewMode viewMode)
    {
        _viewMode = viewMode;
        ViewModel.SetViewMode(viewMode);
        GalleryStandardGridHost.Visibility = viewMode == GalleryViewMode.All
            ? Visibility.Visible
            : Visibility.Collapsed;
        GalleryFavoriteGridHost.Visibility = viewMode == GalleryViewMode.Favorites
            ? Visibility.Visible
            : Visibility.Collapsed;
        GalleryTitleText.Text = viewMode == GalleryViewMode.Favorites ? "收藏" : "相册";
        GallerySearchBox.PlaceholderText = viewMode == GalleryViewMode.Favorites
            ? "搜索收藏"
            : "搜索照片";
        QueueGalleryGridLayoutRefresh();
        RefreshGalleryCardPresentation();
    }

    private void OnGalleryGridLoaded(object sender, RoutedEventArgs e) =>
        QueueGalleryGridLayoutRefresh();

    private void OnGalleryGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_viewMode == GalleryViewMode.Favorites)
        {
            ApplyGalleryGridLayout();
        }
    }

    private void QueueGalleryGridLayoutRefresh()
    {
        if (_galleryGridLayoutRefreshQueued || DispatcherQueue is null)
        {
            return;
        }

        _galleryGridLayoutRefreshQueued = DispatcherQueue.TryEnqueue(() =>
        {
            _galleryGridLayoutRefreshQueued = false;
            ApplyGalleryGridLayout();
        });
    }

    private void ApplyGalleryGridLayout()
    {
        if (_viewMode == GalleryViewMode.Favorites &&
            GalleryFavoriteGridView.ItemsPanelRoot is GalleryFavoriteJustifiedPanel favoritePanel)
        {
            favoritePanel.InvalidateMeasure();
        }
    }

    private async Task LoadStarredStateAsync(CancellationToken cancellationToken)
    {
        _annotationScopeId = GalleryAnnotationStore.CreateScopeId(
            _profileId,
            ViewModel.WatchableRootPath);
        var starredPaths = await _annotationStore.LoadStarredAsync(
            _annotationScopeId,
            cancellationToken);
        ViewModel.ApplyStarredPaths(starredPaths);
        try
        {
            var protectedFavorites = await _favoriteProtectionService.GetProtectedFavoritesAsync(
                _annotationScopeId,
                starredPaths,
                cancellationToken);
            ViewModel.ApplyProtectedFavorites(protectedFavorites);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidDataException
            or JsonException)
        {
            ViewModel.ApplyProtectedFavorites([]);
        }

        try
        {
            var defaultFavorites = await _defaultFavoriteSeedService.EnsureSeededAsync(
                _annotationStore,
                cancellationToken);
            ViewModel.ApplyDefaultFavorites(defaultFavorites);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidDataException)
        {
            ViewModel.ApplyDefaultFavorites([]);
        }
    }

    private async void OnRefreshGalleryClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            await ReloadCurrentRootAsync(preserveControls: false);
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("图库刷新失败", $"只读扫描未完成：{ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Re-scans whichever root is active. <paramref name="preserveControls"/> keeps
    /// the search text and category selection, which matters for a watcher-driven
    /// reload: resetting them under the user would look like a bug.
    /// </summary>
    private async Task ReloadCurrentRootAsync(bool preserveControls)
    {
        try
        {
            var cancellationToken = _loadCancellation?.Token ?? CancellationToken.None;
            if (!string.IsNullOrWhiteSpace(_manualGalleryRootPath))
            {
                await ViewModel.LoadAsync(_manualGalleryRootPath, cancellationToken);
            }
            else
            {
                await ViewModel.LoadDefaultAsync(
                    _profileGameRootPath ?? string.Empty,
                    cancellationToken);
            }
            await LoadStarredStateAsync(cancellationToken);
            ViewModel.SetViewMode(_viewMode);

            if (preserveControls)
            {
                RestoreGalleryControls();
            }
            else
            {
                ResetGalleryControls();
            }

            _folderWatcher.Watch(ViewModel.WatchableRootPath);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnGallerySearchTextChanged(
        AutoSuggestBox sender,
        AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SetSearchQuery(sender.Text);
    }

    private void OnGalleryCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GalleryCategoryListView.SelectedItem is GalleryCategoryItemViewModel category)
        {
            ViewModel.SetCategory(category.CategoryId);
        }
    }

    private void OnGallerySortClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string tag } &&
            Enum.TryParse<GallerySortMode>(tag, out var sortMode))
        {
            ViewModel.SetSortMode(sortMode);
        }
    }

    private void ResetGalleryControls()
    {
        GallerySearchBox.Text = string.Empty;
        GalleryCategoryListView.SelectedIndex = ViewModel.Categories.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Re-applies the search text and category after a background reload. A reload
    /// rebuilds the category list, so the previous selection must be matched by id
    /// rather than by index.
    /// </summary>
    private void RestoreGalleryControls()
    {
        var searchText = GallerySearchBox.Text;
        var previousCategoryId =
            (GalleryCategoryListView.SelectedItem as GalleryCategoryItemViewModel)?.CategoryId;

        var restoredIndex = 0;
        if (!string.IsNullOrEmpty(previousCategoryId))
        {
            for (var index = 0; index < ViewModel.Categories.Count; index++)
            {
                if (string.Equals(
                        ViewModel.Categories[index].CategoryId,
                        previousCategoryId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    restoredIndex = index;
                    break;
                }
            }
        }

        GalleryCategoryListView.SelectedIndex =
            ViewModel.Categories.Count > 0 ? restoredIndex : -1;

        if (!string.IsNullOrEmpty(searchText))
        {
            ViewModel.SetSearchQuery(searchText);
        }
    }

    private async Task ShowDialogAsync(string title, string content)
    {
        await _dialogGate.WaitAsync();
        try
        {
            if (XamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "知道了",
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
        finally
        {
            _dialogGate.Release();
        }
    }
}
