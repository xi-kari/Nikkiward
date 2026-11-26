using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nikkiward.Features.Background;
using Nikkiward.Features.Gallery;
using Nikkiward.Pages;
using Nikkiward.ViewModels;
using Windows.Storage.Pickers;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PhotoStudioView? _studio;
    internal string StudioOrigin => ContentFrame.Content is GalleryPage ? "相册" : "首页";
    internal string StudioSkin => Features.Launcher.Skins.HomeSkins.Resolve(ViewModel.AppearanceSettings.HomeSkin).Id.ToLowerInvariant();
    internal string StudioTone => ViewModel.HomeArtKey switch
    {
        Models.AppearanceBackgroundPresets.Preset1Source => "dark",
        Models.AppearanceBackgroundPresets.Preset2Source => "light",
        _ => HomeBackdrop.IsReady && HomeBackdrop.PreferredTheme == ArtPreferredTheme.Light ? "light" : "dark"
    };

    internal static MainPage StudioShell(DependencyObject element)
    {
        while (element is not MainPage) element = VisualTreeHelper.GetParent(element);
        return (MainPage)element;
    }

    internal void OpenStudio(IEnumerable<GalleryPhotoItemViewModel> list, GalleryPhotoItemViewModel photo)
    {
        if (_studio != null) return;
        var view = _studio = new PhotoStudioView(this, list, photo);
        ContentFrame.Visibility = Visibility.Collapsed;
        ShellNavigation.Visibility = Visibility.Collapsed;
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

    internal Task ImportStudioBackgroundAsync(string path, CancellationToken token) =>
        ImportWallpaperAsync(path, Path.GetFileName(path), WallpaperImportMode.HolographicCard, token);

    internal async Task ChooseStudioExportsAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
        if (await picker.PickSingleFolderAsync() is not { } folder) return;
        try
        {
            PhotoStudioStore.CheckDestination(folder.Path, ViewModel.GalleryReadOnlyRoots);
            PhotoStudioStore.CheckDestination(PhotoStudioView.Store.Root, ViewModel.GalleryReadOnlyRoots);
            PhotoStudioView.Store.ExportRoot = folder.Path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { await TryShowDialogAsync("无法设置导出目录", ex.Message); }
    }

    internal async Task StarStudioPhotoAsync(GalleryPhotoItemViewModel photo, bool value, CancellationToken token)
    {
        if (ContentFrame.Content is GalleryPage gallery)
        {
            if (photo.IsStarred != value) await gallery.ToggleGalleryStarAsync(photo);
            return;
        }
        if (value)
        {
            await new GalleryAnnotationStore().SetStarredAsync(photo.AnnotationScopeId, photo.RelativePath, true, token);
            photo.SetStarred(true);
            await GalleryProtection.ProtectAsync(photo.AnnotationScopeId, photo.RelativePath, photo.FilePath, token);
        }
        else
        {
            var pending = await GalleryProtection.TryUnstarAsync(photo.AnnotationScopeId, photo.RelativePath, token, approved: null);
            if (pending.Count > 0)
            {
                var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "取消收藏？",
                    Content = "原图无法核对，取消收藏可能删除仅存的本地保护副本。", PrimaryButtonText = "取消收藏", CloseButtonText = "保留" };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                if ((await GalleryProtection.TryUnstarAsync(photo.AnnotationScopeId, photo.RelativePath, token, pending)).Count > 0)
                    throw new IOException("保护副本已更改，请重试。");
            }
            photo.SetStarred(false);
        }
    }
}
