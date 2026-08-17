using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.Shell;
using Nikkiward.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace Nikkiward.Pages;

public sealed partial class GalleryPage
{
    private const string GalleryPreviewAnimationKey = AppearanceRuntimeValues.GalleryPreviewAnimationKey;
    private GalleryPhotoItemViewModel? _returnFocusPhoto;

    private async void OnGalleryItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GalleryPhotoItemViewModel photo)
        {
            PrepareGalleryPreviewAnimation(photo);
            await ShowGalleryPhotoAsync(photo);
        }
    }

    private void OnGalleryItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        SetGalleryItemHoverState(sender, true);
    }

    private void OnGalleryItemPointerExited(object sender, PointerRoutedEventArgs e)
    {
        SetGalleryItemHoverState(sender, false);
    }

    private void OnGalleryItemVisualLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement root)
        {
            return;
        }

        if (root.FindName("GalleryThumbnail") is UIElement thumbnail)
        {
            AppearanceRuntimeValues.ApplyScaleTransition(thumbnail);
        }

        if (root.FindName("GalleryImageInfo") is UIElement overlay)
        {
            AppearanceRuntimeValues.ApplyOpacityTransition(overlay);
        }
    }

    private static void SetGalleryItemHoverState(object sender, bool isPointerOver)
    {
        if (sender is not FrameworkElement root)
        {
            return;
        }

        if (root.FindName("GalleryImageInfo") is UIElement overlay)
        {
            overlay.Opacity = isPointerOver ? 1 : 0;
        }

        if (root.FindName("GalleryThumbnail") is UIElement thumbnail)
        {
            var scale = isPointerOver
                ? AppearanceRuntimeValues.ReadScale("HoverScale")
                : 1f;
            thumbnail.Scale = new System.Numerics.Vector3(scale, scale, 1f);
        }
    }

    private async void OnGalleryCopyTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: GalleryPhotoItemViewModel photo } button)
        {
            var originalContent = button.Content;
            try
            {
                await CopyGalleryPhotoToClipboardAsync(photo);
                button.Content = new FontIcon { FontSize = 16, Glyph = "\uE8FB" };
                await Task.Delay(1200);
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("图片复制失败", $"复制未完成：{ex.GetType().Name}");
            }
            finally
            {
                button.Content = originalContent;
            }
        }
    }

    private async void OnGalleryStarTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: GalleryPhotoItemViewModel photo })
        {
            await ToggleGalleryStarAsync(photo);
        }
    }

    private async void OnGalleryPreviewMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: GalleryPhotoItemViewModel photo })
        {
            PrepareGalleryPreviewAnimation(photo);
            await ShowGalleryPhotoAsync(photo);
        }
    }

    private async void OnGalleryCopyMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: GalleryPhotoItemViewModel photo })
        {
            try
            {
                await CopyGalleryPhotoToClipboardAsync(photo);
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("图片复制失败", $"复制未完成：{ex.GetType().Name}");
            }
        }
    }

    private async void OnGalleryRevealMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: GalleryPhotoItemViewModel photo } ||
            !File.Exists(photo.FilePath))
        {
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(photo.FilePath);
            var folder = await file.GetParentAsync();
            var options = new FolderLauncherOptions();
            options.ItemsToSelect.Add(file);
            await Launcher.LaunchFolderAsync(folder, options);
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("打开文件夹失败", $"资源管理器未打开：{ex.GetType().Name}");
        }
    }

    private async void OnGalleryStarMenuClicked(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: GalleryPhotoItemViewModel photo })
        {
            await ToggleGalleryStarAsync(photo);
        }
    }

    private async Task ShowGalleryPhotoAsync(GalleryPhotoItemViewModel photo)
    {
        if (!File.Exists(photo.FilePath))
        {
            await ShowDialogAsync("图片预览失败", "图片文件已不存在。");
            return;
        }

        var index = ViewModel.Photos.IndexOf(photo);
        if (index < 0)
        {
            await ShowDialogAsync("图片预览失败", "当前筛选集合中没有该图片。");
            return;
        }

        ShowGalleryPreviewAt(index);
    }

    private void ShowGalleryPreviewAt(int index)
    {
        if (index < 0 || index >= ViewModel.Photos.Count)
        {
            return;
        }

        var photo = ViewModel.Photos[index];
        if (!File.Exists(photo.FilePath))
        {
            GalleryPreview.SetStatus("图片文件已不存在");
            return;
        }

        _galleryPreviewIndex = index;
        _returnFocusPhoto = photo;
        GalleryPreview.ShowPhoto(
            photo,
            index,
            ViewModel.Photos.Count,
            GalleryPreviewAnimationKey);
    }

    private GalleryPhotoItemViewModel? GetCurrentGalleryPreviewPhoto() =>
        _galleryPreviewIndex >= 0 && _galleryPreviewIndex < ViewModel.Photos.Count
            ? ViewModel.Photos[_galleryPreviewIndex]
            : null;

    private void MoveGalleryPreview(int offset)
    {
        var nextIndex = _galleryPreviewIndex + offset;
        if (nextIndex >= 0 && nextIndex < ViewModel.Photos.Count)
        {
            ShowGalleryPreviewAt(nextIndex);
        }
    }

    private void CloseGalleryPreview()
    {
        if (!GalleryPreview.IsOpen)
        {
            _returnFocusPhoto = null;
            _galleryPreviewIndex = -1;
            TryStartPendingGalleryRefresh();
            return;
        }

        var returnFocusPhoto = _returnFocusPhoto;
        _returnFocusPhoto = null;
        _galleryPreviewIndex = -1;
        GalleryPreview.ClosePreview();
        if (returnFocusPhoto is not null)
        {
            DispatcherQueue.TryEnqueue(() => FocusGalleryPhoto(returnFocusPhoto));
        }

        TryStartPendingGalleryRefresh();
    }

    private void PrepareGalleryPreviewAnimation(GalleryPhotoItemViewModel photo)
    {
        if (FindGalleryThumbnail(photo) is { } thumbnail)
        {
            PrepareConnectedAnimation(GalleryPreviewAnimationKey, thumbnail);
        }
    }

    private UIElement? FindGalleryThumbnail(GalleryPhotoItemViewModel photo)
    {
        if (GalleryGridView.ContainerFromItem(photo) is not GridViewItem container ||
            container.ContentTemplateRoot is not FrameworkElement templateRoot)
        {
            return null;
        }

        return templateRoot.FindName("GalleryThumbnail") as UIElement;
    }

    private void FocusGalleryPhoto(GalleryPhotoItemViewModel photo)
    {
        if (GalleryGridView.ContainerFromItem(photo) is GridViewItem container)
        {
            GalleryGridView.ScrollIntoView(photo, ScrollIntoViewAlignment.Leading);
            _ = container.Focus(FocusState.Programmatic);
        }
    }

    private void OnGalleryPreviewCloseRequested(object? sender, EventArgs e) =>
        CloseGalleryPreview();

    private void OnGalleryPreviewPreviousRequested(object? sender, EventArgs e) =>
        MoveGalleryPreview(-1);

    private void OnGalleryPreviewNextRequested(object? sender, EventArgs e) =>
        MoveGalleryPreview(1);

    private async void OnGalleryPreviewCopyRequested(object? sender, EventArgs e)
    {
        var photo = GetCurrentGalleryPreviewPhoto();
        if (photo is null)
        {
            return;
        }

        try
        {
            await CopyGalleryPhotoToClipboardAsync(photo);
            GalleryPreview.SetStatus("已复制图片");
            await Task.Delay(1200);
            if (ReferenceEquals(photo, GetCurrentGalleryPreviewPhoto()))
            {
                GalleryPreview.SetStatus(string.Empty);
            }
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("图片复制失败", $"复制未完成：{ex.GetType().Name}");
        }
    }

    private async void OnGalleryPreviewStarRequested(object? sender, EventArgs e)
    {
        var photo = GetCurrentGalleryPreviewPhoto();
        if (photo is not null)
        {
            await ToggleGalleryStarAsync(photo);
        }
    }

    private async Task ToggleGalleryStarAsync(GalleryPhotoItemViewModel photo)
    {
        var isStarred = !photo.IsStarred;
        try
        {
            await _annotationStore.SetStarredAsync(
                _annotationScopeId,
                photo.RelativePath,
                isStarred,
                _loadCancellation?.Token ?? CancellationToken.None);
            ViewModel.SetStarred(photo, isStarred);
            GalleryPreview.UpdateStarState(isStarred);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("收藏更新失败", $"星标未保存：{ex.GetType().Name}");
            return;
        }

        if (isStarred)
        {
            GalleryPreview.SetStatus("正在创建本地保护副本…");
            try
            {
                var protection = await _favoriteProtectionService.ProtectAsync(
                    _annotationScopeId,
                    photo.RelativePath,
                    photo.FilePath,
                    CancellationToken.None);
                if (protection.Entry is not null && protection.ProtectedPath is not null)
                {
                    photo.SetProtectedCopy(
                        protection.ProtectedPath,
                        isUsingProtectedCopy: false);
                    GalleryPreview.SetStatus("已收藏 · 本地保护副本已就绪");
                }
                else
                {
                    GalleryPreview.SetStatus("已收藏 · 本地保护已关闭");
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                GalleryPreview.SetStatus("已收藏 · 本地保护副本创建失败");
                await ShowDialogAsync(
                    "照片已收藏",
                    $"星标已保存，但本地保护副本未创建：{ex.GetType().Name}");
            }
        }
        else if (_viewMode == GalleryViewMode.Favorites)
        {
            CloseGalleryPreview();
        }
    }

    private static async Task CopyGalleryPhotoToClipboardAsync(
        GalleryPhotoItemViewModel photo)
    {
        var file = await StorageFile.GetFileFromPathAsync(photo.FilePath);
        var package = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy,
        };
        package.SetStorageItems([file]);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }
}
