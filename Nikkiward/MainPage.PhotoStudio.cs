using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nikkiward.Features.Background;
using Nikkiward.Features.Gallery;
using Nikkiward.ViewModels;
using Windows.Storage.Pickers;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PhotoStudioView? _studio;
    internal string StudioOrigin => _webGallery != null ? "相册" : "首页";
    internal string StudioSkin => Features.Launcher.Skins.HomeSkins.Resolve(ViewModel.AppearanceSettings.HomeSkin).Id.ToLowerInvariant();
    internal string StudioTone => ViewModel.HomeArtKey switch
    {
        Models.AppearanceBackgroundPresets.Preset1Source => "dark",
        Models.AppearanceBackgroundPresets.Preset2Source => "light",
        _ => HomeBackdrop.IsReady && HomeBackdrop.PreferredTheme == ArtPreferredTheme.Light ? "light" : "dark"
    };

    internal static MainPage FromElement(DependencyObject element)
    {
        while (element is not MainPage) element = VisualTreeHelper.GetParent(element);
        return (MainPage)element;
    }

    internal void OpenStudio(IEnumerable<GalleryPhotoItemViewModel> list, GalleryPhotoItemViewModel photo, bool compare = false)
    {
        if (_studio != null) return;
        var view = _studio = new PhotoStudioView(this, list, photo, compare);
        ContentFrame.Visibility = Visibility.Collapsed;
        RootGrid.Children.Add(view);
        SyncLauncherChrome();
    }

    internal void CloseStudio(PhotoStudioView studio)
    {
        _studio = null;
        RootGrid.Children.Remove(studio);
        ContentFrame.Visibility = Visibility.Visible;
        SyncLauncherChrome();
    }

    internal async Task ImportStudioBackgroundAsync(string path, string originalName, CancellationToken token)
    {
        var imported = await _wallpaperAssetImporter.ImportStillAsync(path, token);
        if (!imported.Validation.IsUsable || imported.ImportedPath == null)
            throw new IOException(imported.Validation.RejectReason);
        await CommitStillBackgroundAsync(imported.ImportedPath, originalName, token);
    }

    internal async Task ChooseStudioExportsAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        if (await picker.PickSingleFolderAsync() is not { } folder) return;
        try
        {
            await GalleryProtection.ChangeGalleryDirectoriesAsync(() =>
            {
                PhotoStudioStore.CheckDestination(folder.Path, ViewModel.GalleryReadOnlyRoots);
                PhotoStudioStore.CheckDestination(PhotoStudioView.Store.Root, ViewModel.GalleryReadOnlyRoots);
                PhotoStudioView.Store.ExportRoot = folder.Path;
                return Task.CompletedTask;
            }, _lifetimeCancellation?.Token ?? CancellationToken.None);
            CurrentWebPage?.Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { await TryShowDialogAsync("无法设置导出目录", ex.Message); }
    }

    internal async Task StarStudioPhotoAsync(GalleryPhotoItemViewModel photo, bool value, CancellationToken token)
    {
        _webGallery?.CheckStarStorage();
        if (await GalleryFavoriteChange.SetAsync(new(), GalleryProtection, photo.AnnotationScopeId, photo.RelativePath,
            photo.FilePath, value, async (message, button) =>
            {
                var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "取消这张照片的收藏？", Content = message,
                    PrimaryButtonText = button, CloseButtonText = "保留", DefaultButton = ContentDialogButton.Close };
                return await dialog.ShowAsync() == ContentDialogResult.Primary;
            }, token)) photo.SetStarred(value);
        _webGallery?.ScheduleRefresh();
    }
}
