using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Nikkiward.Features.Gallery;
using Microsoft.UI.Xaml.Navigation;
using Nikkiward.Features.Launcher.Skins;
using Nikkiward.Features.Shell;
using Nikkiward.Models;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Launcher;

public sealed partial class LauncherPage : Page
{
    private HomeSkin? _skin;
    private GalleryFolderWatcher? _watcher;
    private CancellationTokenSource? _scan;
    private (string? Profile, string? Root)? _scope;
    internal DateTime[]? AlbumTimes { get; private set; }
    public ObservableCollection<GalleryPhotoItemViewModel> RecentPhotos { get; } = [];
    public event EventHandler<GalleryPhotoItemViewModel>? PhotoRequested;
    public MainPageViewModel ViewModel { get; private set; } = null!;
    public FrameworkElement OnArtHost => SkinHost;

    public LauncherPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Activate();
        Unloaded += (_, _) => Deactivate();
    }

    public event EventHandler? DownloadStatusRequested;

    public event EventHandler? PlayTimeRequested;

    public event EventHandler? OfficialFlowRequested;

    public event EventHandler? CloseGameRequested;

    public event EventHandler? JournalRequested;

    public event EventHandler? GalleryRequested;

    public event EventHandler? BackgroundResetRequested;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = ((LauncherNavigationContext)e.Parameter).ViewModel;
        Activate();
    }

    private void Activate()
    {
        if (_watcher != null || ViewModel == null) return;
        ApplyAppearanceSettings(ViewModel.AppearanceSettings);
        WebHome?.SetAway(false);
        _watcher = new GalleryFolderWatcher(DispatcherQueue, () => RefreshRecentPhotos(force: true));
        ViewModel.PropertyChanged += OnModelChanged;
        RefreshRecentPhotos();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e) => Deactivate();

    private void Deactivate()
    {
        if (_watcher == null) return;
        ViewModel.PropertyChanged -= OnModelChanged;
        _scan?.Cancel();
        _watcher.Dispose();
        _watcher = null;
        _scope = null;
        // Keep the home page (and its recent photos) loaded but suspended; MainPage releases it when the window goes away.
        WebHome?.SetAway(true);
    }

    internal void ReleaseHome() => ReleaseSkin();

    private DateTime _photosDay = DateTime.Today;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.GalleryRootPath) or nameof(ViewModel.SelectedProfileId) or nameof(ViewModel.GameRootPath))
            RefreshRecentPhotos();
        else if (e.PropertyName is nameof(ViewModel.HomeCheckTime) or nameof(ViewModel.LastUpdatedText) && _photosDay != DateTime.Today)
        {
            _photosDay = DateTime.Today;
            RefreshRecentPhotos(force: true);
        }
    }

    private async void RefreshRecentPhotos(bool force = false)
    {
        var root = ViewModel.GalleryRootPath ?? (Path.IsPathFullyQualified(ViewModel.GameRootPath)
            ? GalleryFiles.DefaultRoot(ViewModel.GameRootPath) : null);
        var profile = ViewModel.SelectedProfileId;
        if (_scope != (profile, root))
        {
            _scope = (profile, root);
            AlbumTimes = null;
            RecentPhotos.Clear();
            _watcher?.Watch(root);
        }
        else if (!force) return;
        _scan?.Cancel();
        if (root == null) return;
        using var scan = new CancellationTokenSource();
        _scan = scan;
        try
        {
            var items = await Task.Run(() =>
            {
                var scope = GalleryAnnotationStore.CreateScopeId(profile, root);
                var files = GalleryFiles.NewestFirst(root, scan.Token).ToArray();
                return (Times: files.Select(f => GalleryTimestamp.Resolve(f.Name, f.LastWriteTimeUtc)).Order().ToArray(),
                    Recent: files.Take(4).Select(file => GalleryPhotoItemViewModel.CreatePhoto(file, root, scope)).ToArray());
            }, scan.Token);
            if (scan.IsCancellationRequested) return;
            RecentPhotos.Clear();
            AlbumTimes = items.Times;
            foreach (var item in items.Recent) RecentPhotos.Add(item);
        }
        catch (OperationCanceledException) when (scan.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!scan.IsCancellationRequested) { AlbumTimes = null; RecentPhotos.Clear(); }
        }
        finally
        {
            if (_scan == scan) _scan = null;
        }
    }

    private void ReleaseSkin()
    {
        if (SkinHost.Content is IDisposable disposable) disposable.Dispose();
        SkinHost.Content = null;
        _skin = null;
    }

    public void ApplyAppearanceSettings(AppearanceSettings settings)
    {
        var skin = HomeSkins.Resolve(settings.HomeSkin);
        if (skin == _skin) return;
        ReleaseSkin();
        ViewModel.HomeFallback = "";
        UserControl view;
        try { view = skin.Create(); }
        catch (Exception ex)
        {
            UseCinema($"{ex.GetType().Name}: {ex.Message}");
            return;
        }
        view.DataContext = ViewModel;
        SkinHost.Content = view;
        _skin = skin;
        SkinChanged?.Invoke(this, EventArgs.Empty);
    }

    internal bool IsSafeMode => SkinHost.Content is CinemaView;
    internal WebHomeView? WebHome => SkinHost.Content as WebHomeView;
    internal event EventHandler? SkinChanged;
    internal event EventHandler<string>? NavigationRequested;

    internal void UseCinema(string reason)
    {
        ViewModel.HomeFallback = string.Join(Environment.NewLine, System.Text.RegularExpressions.Regex.Replace(
            MainPageViewModel.RedactUiText(reason), @"(?<=[。.!！])\s+(?=\S)", Environment.NewLine)
            .Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct());
        System.Diagnostics.Debug.WriteLine(ViewModel.HomeFallback);
        ReleaseSkin();
        var cinema = new CinemaView(this) { DataContext = ViewModel };
        SkinHost.Content = cinema;
        _skin = HomeSkins.Resolve(ViewModel.AppearanceSettings.HomeSkin);
        SkinChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void RetryWebHome()
    {
        _skin = null;
        ApplyAppearanceSettings(ViewModel.AppearanceSettings);
    }

    internal void InvokeAction(string name, GalleryPhotoItemViewModel? photo = null)
    {
#if DEBUG
        if (HomePreview.Active) return;
#endif
        if (name == "photo" && photo != null)
        {
            if (RecentPhotos.Contains(photo)) PhotoRequested?.Invoke(this, photo);
            return;
        }
        if (name.StartsWith("nav-", StringComparison.Ordinal))
        {
            NavigationRequested?.Invoke(this, name[4..]);
            return;
        }
        var action = name switch
        {
            "launch" when ViewModel.HomeLaunchEnabled => OfficialFlowRequested,
            "close" when ViewModel.HomeCloseEnabled => CloseGameRequested,
            "gallery" => GalleryRequested,
            "journal" => JournalRequested,
            "reset" => BackgroundResetRequested,
            "download" => DownloadStatusRequested,
            "time" => PlayTimeRequested,
            _ => null
        };
        action?.Invoke(this, EventArgs.Empty);
    }
}
