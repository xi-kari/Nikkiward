using System.ComponentModel;
using System.Text.Json;
using System.Runtime.InteropServices;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.Pages;
using Nikkiward.Pages;
using Nikkiward.Services;
using Nikkiward.ViewModels;
using Windows.Storage;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private WebGallery? _webGallery;
    private sealed record GalleryLibrary(string? Root, string Exports, string? Profile, GalleryIndex Index)
    {
        internal Dictionary<bool, GalleryPageSession> Pages { get; } = [];
        internal Dictionary<string, GalleryDefaultFavorite> DefaultPaths { get; } = [];
    }
    private GalleryLibrary? _galleryLibrary;

    private PageAdapter GalleryPageAdapter(bool favorites) => new("gallery",
        ["query", "clear", "next", "refresh", "open", "burst", "star", "copy", "locate", "locate-game", "plugin"],
        () =>
        {
            var root = ViewModel.GalleryRootPath ?? ResolveDefaultGalleryRoot(ViewModel.GameRootPath);
            var exports = PhotoStudioView.Store.ExportRoot;
            if (_webGallery == null || !_webGallery.Matches(root, exports, ViewModel.SelectedProfileId, favorites))
            {
                if (_galleryLibrary == null || _galleryLibrary.Root != root || _galleryLibrary.Exports != exports ||
                    _galleryLibrary.Profile != ViewModel.SelectedProfileId)
                    _galleryLibrary = new(root, exports, ViewModel.SelectedProfileId, GalleryIndex.Open(root, PhotoStudioView.Store));
                var gallery = new WebGallery(this, _galleryLibrary, favorites);
                CurrentWebPage!.RegisterImages(gallery.Thumbnail, () =>
                {
                    gallery.Dispose();
                    if (_webGallery == gallery) _webGallery = null;
                });
                _webGallery = gallery;
                gallery.Start();
            }
            return _webGallery.State();
        }, (action, message) => _webGallery?.Execute(action, message) ?? Task.CompletedTask);

    private sealed class WebGallery
    {
        private readonly MainPage _shell;
        private readonly string? _root, _profile;
        private readonly string _exports;
        private readonly bool _favorites;
        private readonly GalleryIndex _index;
        private readonly GalleryPageSession _session;
        private readonly GalleryAnnotationStore _annotations = new();
        private readonly CancellationTokenSource _life = new();
        private CancellationTokenSource _images = new();
        private readonly SemaphoreSlim _thumbnailGate = new(3);
        private readonly SemaphoreSlim _starGate = new(1);
        private readonly Dictionary<string, GalleryDefaultFavorite> _defaultPaths;
        private bool _seedsLoaded;
        private GalleryIndexPhoto[] _defaults = [];
        private (PlaySession? Current, PlaySession? Previous) _sessions;
        private IDisposable? _watch;
        private bool _running, _pending, _overlap, _protect;
        private string? _error;


        internal WebGallery(MainPage shell, GalleryLibrary library, bool favorites)
        {
            (_shell, _root, _exports, _profile, _favorites) = (shell, library.Root, library.Exports, library.Profile, favorites);
            _index = library.Index;
            _defaultPaths = library.DefaultPaths;
            if (!library.Pages.TryGetValue(favorites, out var session))
                library.Pages[favorites] = session = new(_index, favorites);
            _session = session;
            _session.BeginVisit();
            var sessions = shell.HomeSessions;
            _sessions = (sessions.Current, sessions.Previous);
        }

        internal bool Matches(string? root, string exports, string? profile, bool favorites) =>
            _root == root && _exports == exports && _profile == profile && _favorites == favorites;
        internal void Start()
        {
            _watch = _index.Watch(_shell.DispatcherQueue, ScheduleRefresh);
            _shell.ViewModel.PropertyChanged += RootChanged;
            ScheduleRefresh();
        }
        private void RootChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MainPageViewModel.GalleryRootPath) or nameof(MainPageViewModel.GameRootPath)
                or nameof(MainPageViewModel.SelectedProfileId))
                _shell.DispatcherQueue.TryEnqueue(() => { if (!_life.IsCancellationRequested) _shell.CurrentWebPage?.Refresh(); });
        }
        internal void Dispose()
        {
            _watch?.Dispose();
            _shell.ViewModel.PropertyChanged -= RootChanged;
            _life.Cancel();
            _session.CancelBuild();
            _images.Cancel();
            _images.Dispose();
        }
        internal object State() => _session.State(_running, _index.Progress, !Directory.Exists(_root), _overlap, _protect,
            _shell._photoPluginInstallation?.IsInstalled == true, _error);

        private void Publish() { if (!_life.IsCancellationRequested) _shell.CurrentWebPage?.Refresh(); }
        private void InvalidateImages()
        {
            _images.Cancel();
            _images.Dispose();
            _images = new();
        }

        internal void ScheduleRefresh()
        {
            if (_life.IsCancellationRequested) return;
            _pending = true;
            if (_running) return;
            _running = true;
            _shell.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    while (_pending && !_life.IsCancellationRequested)
                    {
                        _pending = false;
                        _error = null;
                        await RefreshAsync(_life.Token);
                    }
                }
                catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
                { _error = "相册读取失败，请检查目录权限后刷新。"; }
                finally { _running = false; Publish(); }
            });
        }

        private bool SafeFolder(string path) => GalleryIndex.CanWriteCache(path, _root, _exports);
        internal void CheckStarStorage()
        {
            if (!SafeFolder(Path.GetDirectoryName(_annotations.AnnotationFilePath)!))
                throw new IOException("收藏目录与照片目录重叠。");
        }

        private async Task RefreshAsync(CancellationToken token)
        {
            var protection = _shell.GalleryProtection;
            var prefs = await protection.GetPreferencesAsync(token);
            _protect = prefs.IsEnabled;
            _overlap = prefs.IsPausedForOverlap;
            var scope = GalleryAnnotationStore.CreateScopeId(_profile, _root);
            var exportScope = GalleryAnnotationStore.CreateScopeId(_profile, _exports);
            var safe = SafeFolder(Path.GetDirectoryName(_annotations.AnnotationFilePath)!);
            async Task<GalleryFavoriteState> Load(string? root, string id) => safe ?
                await protection.LoadGalleryAsync(_profile, root, token) :
                new(id, await _annotations.LoadStarredAsync(id, token), [], true);
            var stars = await Load(_root, scope);
            var exportStars = await Load(_exports, exportScope);
            _overlap |= stars.IsPausedForOverlap || exportStars.IsPausedForOverlap;
            var seeds = new GalleryDefaultFavoriteSeedService();
            IReadOnlyList<GalleryDefaultFavorite> seed = [];
            try
            {
                if (safe && SafeFolder(seeds.DestinationDirectoryPath)) seed = await seeds.EnsureSeededAsync(_annotations, token);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException)
            {
                _error = "默认收藏未能载入。";
            }
            token.ThrowIfCancellationRequested();
            var known = _defaultPaths.ToDictionary(p => p.Value.RelativePath, p => p.Key);
            var defaults = new List<GalleryIndexPhoto>(seed.Count);
            _defaultPaths.Clear();
            foreach (var p in seed)
            {
                var id = known.GetValueOrDefault(p.RelativePath) ?? Guid.NewGuid().ToString("N");
                _defaultPaths[id] = p;
                defaults.Add(new(id, Path.GetFileName(p.FilePath), p.RelativePath, "DefaultFavorites", "默认收藏",
                    p.LastWriteTimeUtc.ToLocalTime(), p.FileSizeBytes, new(null,null,false,null,[],null,null,[]), true,false,null));
            }
            _defaults = defaults.ToArray();
            if (!_seedsLoaded)
            {
                _seedsLoaded = true;
                await ResetAsync();
                token.ThrowIfCancellationRequested();
                Publish();
            }
            var scan = _index.RefreshAsync(stars, null, null, token: token, exportState: exportStars);
            while (!scan.IsCompleted)
            {
                if (await Task.WhenAny(scan, Task.Delay(600, token)) == scan || token.IsCancellationRequested) break;
                if (_session.Page.Matches < 80 && _session.Page.Progress.Indexed != _index.Progress.Indexed) await ResetAsync();
                Publish();
            }
            await scan;
            token.ThrowIfCancellationRequested();
            await ResetAsync();
        }

        private async Task<bool> ResetAsync()
        {
            if (!await _session.ResetAsync(_defaults, keepWindow: true, sessions: _sessions)) return false;
            InvalidateImages();
            return true;
        }

        internal async void PlayTimeChanged()
        {
            var (current, previous, _) = _shell.HomeSessions;
            if (_sessions == (current, previous)) return;
            _sessions = (current, previous);
            if (_seedsLoaded && await ResetAsync()) Publish();
        }

        private string? PathFor(GalleryIndexPhoto p) => _defaultPaths.TryGetValue(p.Id, out var seed) ? seed.FilePath : _index.ResolvePath(p.Id);
        private GalleryPhotoItemViewModel? Item(GalleryIndexPhoto p, bool memoryOnly = false)
        {
            var path = PathFor(p);
            if (path == null) return null;
            var root = p.Exported ? _exports : _root;
            var scope = _defaultPaths.ContainsKey(p.Id) ? GalleryDefaultFavoriteSeedService.ScopeId :
                GalleryAnnotationStore.CreateScopeId(_profile, root);
            var item = new GalleryPhotoItemViewModel(path, p.RelativePath, p.FileName, p.CategoryId, p.CategoryName, p.Size,
                p.LocalTime.ToUniversalTime(), scope, root) { MemoryOnlyThumbnails = memoryOnly };
            item.SetStarred(p.Favorite);
            if (p.ProtectedCopy) item.SetProtectedCopy(path, true);
            return item;
        }

        internal async Task<Stream?> Thumbnail(string version, string id)
        {
            if (_session.Find(version, id) is not { } p || PathFor(p) is not { } path) return null;
            var token = _images.Token;
            await _thumbnailGate.WaitAsync(token);
            try
            {
                var thumb = await Task.Run(async () =>
                {
                    if (VariantPathPolicy.ContainsReparsePoint(path)) return null;
                    var extension = Path.GetExtension(p.FileName);
                    bool memoryOnly = GalleryThumbnailCache.FolderPath is not { } folder || !SafeFolder(folder);
                    return await GalleryThumbnailCache.TryGetThumbnailAsync(path, extension, _root, token, memoryOnly);
                }, token);
                token.ThrowIfCancellationRequested();
                if (_session.Find(version, id) == null) return null;
                return thumb?.FilePath is { } file ? File.OpenRead(file) : thumb?.Bytes is { } bytes ? new MemoryStream(bytes) : null;
            }
            finally { _thumbnailGate.Release(); }
        }

        internal async Task Execute(string action, JsonElement message)
        {
            try { await ExecuteCore(action, message); }
            catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
            {
                _error = action == "star" ? "收藏更新未完成，请刷新查看当前收藏状态。" : "照片不可访问，请刷新相册。";
                Publish();
            }
        }

        private async Task ExecuteCore(string action, JsonElement m)
        {
            string Field(string name) => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            if (_life.IsCancellationRequested || !_session.Accept(action, m)) return;
            switch (action)
            {
                case "query": case "clear":
                    InvalidateImages();
                    if (await _session.ChangeAsync(action == "clear" ? JsonSerializer.SerializeToElement(new { }) : m, _defaults, _sessions)) Publish();
                    return;
                case "refresh": ScheduleRefresh(); Publish(); return;
                case "locate-game":
                    if (_shell.ViewModel.CanDiscover) _shell.OnChooseGameRootClicked(_shell, new Microsoft.UI.Xaml.RoutedEventArgs());
                    return;
                case "plugin":
                    if (_shell._photoPluginInstallation?.IsInstalled == true) _shell.ShowPhotoPlugin();
                    return;
                case "next":
                    if (m.TryGetProperty("offset", out var offset) && offset.ValueKind == JsonValueKind.Number && offset.TryGetInt32(out var n) && _session.Next(Field("version"), n)) Publish();
                    return;
            }
            var version = Field("version");
            var id = Field("id");
            var memoryOnly = GalleryThumbnailCache.FolderPath is not { } cache || !SafeFolder(cache);
            if (_session.Find(version, id) is not { } photo || Item(photo, memoryOnly) is not { } item) return;
            if (VariantPathPolicy.ContainsReparsePoint(item.FilePath)) throw new IOException("照片不可访问，请刷新相册。");
            switch (action)
            {
                case "open": case "burst":
                    var list = _session.StudioList(photo, action == "burst").Select(p => p.Id == id ? item : Item(p, memoryOnly)).OfType<GalleryPhotoItemViewModel>().ToArray();
                    if (list.Contains(item) && (action != "burst" || list.Length > 1))
                        _shell.OpenStudio(list, item, action == "burst");
                    break;
                case "star":
                    CheckStarStorage();
                    await _starGate.WaitAsync(_life.Token);
                    try
                    {
                        if (_session.Find(version, id) != null) await _shell.StarStudioPhotoAsync(item, !photo.Favorite, _life.Token);
                    }
                    finally { _starGate.Release(); ScheduleRefresh(); }
                    break;
                case "copy": await GalleryPage.CopyGalleryPhotoToClipboardAsync(item); break;
                case "locate":
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    var options = new FolderLauncherOptions();
                    options.ItemsToSelect.Add(file);
                    await Launcher.LaunchFolderAsync(await file.GetParentAsync(), options);
                    break;
            }
        }
    }
}
