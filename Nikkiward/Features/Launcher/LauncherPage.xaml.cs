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

public sealed partial class LauncherPage : PageBase
{
    private HomeSkin? _skin;
    private GalleryFolderWatcher? _watcher;
    private CancellationTokenSource? _scan;
    private (string? Profile, string? Root)? _scope;
    public ObservableCollection<GalleryPhotoItemViewModel> RecentPhotos { get; } = [];
    public event EventHandler<GalleryPhotoItemViewModel>? PhotoRequested;
    public MainPageViewModel ViewModel { get; private set; } = null!;
    public FrameworkElement OnArtHost => SkinHost;
    public event EventHandler? StatusRequested;
    public event EventHandler? BackgroundRequested;

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

    public event EventHandler? LaunchSettingsRequested;

    public event EventHandler? JournalRequested;

    public event EventHandler? GalleryRequested;

    public event EventHandler? BackgroundResetRequested;

    public event EventHandler? ProfileRequested;

    protected override void OnEntering(NavigationEventArgs e)
    {
        ViewModel = ((LauncherNavigationContext)e.Parameter).ViewModel;
        Activate();
    }

    private void Activate()
    {
        if (_watcher != null || ViewModel == null) return;
        ApplyAppearanceSettings(ViewModel.AppearanceSettings);
        _watcher = new GalleryFolderWatcher(DispatcherQueue, () => RefreshRecentPhotos(force: true));
        ViewModel.PropertyChanged += OnModelChanged;
        RefreshRecentPhotos();
    }

    protected override void OnExiting(NavigationEventArgs e) => Deactivate();

    private void Deactivate()
    {
        if (_watcher == null) return;
        ViewModel.PropertyChanged -= OnModelChanged;
        _scan?.Cancel();
        _watcher?.Dispose();
        _watcher = null;
        _scope = null;
        RecentPhotos.Clear();
        ReleaseSkin();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModel.GalleryRootPath) or nameof(ViewModel.SelectedProfileId) or nameof(ViewModel.GameRootPath))
            RefreshRecentPhotos();
    }

    private async void RefreshRecentPhotos(bool force = false)
    {
        var root = ViewModel.GalleryRootPath ?? (Path.IsPathFullyQualified(ViewModel.GameRootPath)
            ? GalleryFiles.DefaultRoot(ViewModel.GameRootPath) : null);
        var profile = ViewModel.SelectedProfileId;
        if (_scope != (profile, root))
        {
            _scope = (profile, root);
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
                return GalleryFiles.Recent(root, scan.Token)
                    .Select(file => GalleryViewModel.CreatePhoto(file, root, scope)).ToArray();
            }, scan.Token);
            if (scan.IsCancellationRequested) return;
            RecentPhotos.Clear();
            foreach (var item in items) RecentPhotos.Add(item);
        }
        catch (OperationCanceledException) when (scan.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (!scan.IsCancellationRequested) RecentPhotos.Clear();
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
        var view = skin.Create();
        view.DataContext = ViewModel;
        SkinHost.Content = view;
        _skin = skin;
    }

    internal void InvokeAction(Button button)
    {
#if DEBUG
        if (HomePreview.Active) return;
#endif
        if (Equals(button.Tag, "photo") && button.DataContext is GalleryPhotoItemViewModel photo)
        {
            if (RecentPhotos.Contains(photo)) PhotoRequested?.Invoke(this, photo);
            return;
        }
        var action = button.Tag switch
        {
            "launch" => OfficialFlowRequested,
            "close" => CloseGameRequested,
            "settings" => LaunchSettingsRequested,
            "profile" => ProfileRequested,
            "gallery" => GalleryRequested,
            "journal" => JournalRequested,
            "background" => BackgroundRequested,
            "reset" => BackgroundResetRequested,
            "status" => StatusRequested,
            "download" => DownloadStatusRequested,
            "time" => PlayTimeRequested,
            _ => null
        };
        action?.Invoke(button, EventArgs.Empty);
    }
}
