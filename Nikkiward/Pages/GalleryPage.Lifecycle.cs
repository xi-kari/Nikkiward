using Microsoft.UI.Xaml;

namespace Nikkiward.Pages;

public sealed partial class GalleryPage
{
    private bool _galleryRefreshPending;
    private bool _galleryRefreshInProgress;

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        SetSurfaceActive(false);
        _folderWatcher.Stop();
        CancelFavoriteOperations();
    }

    private void OnGalleryFolderChanged()
    {
        _galleryRefreshPending = true;
        TryStartPendingGalleryRefresh();
    }

    private void TryStartPendingGalleryRefresh()
    {
        if (!_galleryRefreshPending ||
            _galleryRefreshInProgress ||
            _galleryPreviewIndex >= 0 ||
            ViewModel.IsBusy)
        {
            return;
        }

        _galleryRefreshPending = false;
        _galleryRefreshInProgress = true;
        _ = RefreshGalleryFromWatcherAsync();
    }

    private async Task RefreshGalleryFromWatcherAsync()
    {
        try
        {
            await ReloadCurrentRootAsync(preserveControls: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            _galleryRefreshInProgress = false;
            TryStartPendingGalleryRefresh();
        }
    }
}
