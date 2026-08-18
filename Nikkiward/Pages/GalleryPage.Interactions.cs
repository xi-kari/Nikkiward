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
    private const double FavoriteCardCornerRadius = 16d;
    private const double StandardCardCornerRadius = 4d;
    private static readonly string[] GalleryHoverChromeNames =
    [
        "GalleryStarButton",
        "GalleryCopyButton",
        "GalleryTimePlate",
    ];
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

        if (root.FindName("GalleryThumbnailScaleHost") is UIElement thumbnailScaleHost)
        {
            AppearanceRuntimeValues.ApplyScaleTransition(thumbnailScaleHost);
        }

        foreach (var name in GalleryHoverChromeNames)
        {
            if (root.FindName(name) is UIElement chrome)
            {
                AppearanceRuntimeValues.ApplyOpacityTransition(chrome);
            }
        }

        ApplyGalleryItemPresentation(root);
        if (_viewMode == GalleryViewMode.Favorites)
        {
            ApplyGalleryGridLayout();
        }
    }

    private void SetGalleryItemHoverState(object sender, bool isPointerOver)
    {
        if (sender is not FrameworkElement root)
        {
            return;
        }

        foreach (var name in GalleryHoverChromeNames)
        {
            if (root.FindName(name) is UIElement chrome)
            {
                chrome.Opacity = isPointerOver ? 1 : 0;
                if (name is "GalleryStarButton" or "GalleryCopyButton")
                {
                    chrome.IsHitTestVisible = isPointerOver;
                }
            }
        }

        if (root.FindName("GalleryThumbnailScaleHost") is UIElement thumbnailScaleHost)
        {
            var scale = isPointerOver && _viewMode != GalleryViewMode.Favorites
                ? AppearanceRuntimeValues.ReadScale("HoverScale")
                : 1f;
            thumbnailScaleHost.Scale = new System.Numerics.Vector3(scale, scale, 1f);
        }
    }

    private void RefreshGalleryCardPresentation()
    {
        foreach (var photo in ViewModel.Photos)
        {
            if (ActiveGalleryGridView.ContainerFromItem(photo) is GridViewItem container &&
                container.ContentTemplateRoot is FrameworkElement root)
            {
                ApplyGalleryItemPresentation(root);
            }
        }
    }

    private void ApplyGalleryItemPresentation(FrameworkElement root)
    {
        var effectEnabled = IsFavoriteCardEffectEnabled;
        var cornerRadius = _viewMode == GalleryViewMode.Favorites
            ? FavoriteCardCornerRadius
            : StandardCardCornerRadius;
        if (root is Border card)
        {
            card.CornerRadius = new CornerRadius(cornerRadius);
        }

        if (root.FindName("GalleryBorderGlow") is Nikkiward.Controls.CardBorderGlow borderGlow)
        {
            borderGlow.GlowCornerRadius = cornerRadius;
            borderGlow.ApplyMotion(_appearanceSettings.Motion);
            borderGlow.SetGlowEnabled(effectEnabled);
        }

        if (_viewMode == GalleryViewMode.Favorites &&
            root.FindName("GalleryThumbnailScaleHost") is UIElement thumbnailScaleHost)
        {
            thumbnailScaleHost.Scale = System.Numerics.Vector3.One;
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
        if (ActiveGalleryGridView.ContainerFromItem(photo) is not GridViewItem container ||
            container.ContentTemplateRoot is not FrameworkElement templateRoot)
        {
            return null;
        }

        return templateRoot.FindName("GalleryThumbnail") as UIElement;
    }

    private void FocusGalleryPhoto(GalleryPhotoItemViewModel photo)
    {
        if (ActiveGalleryGridView.ContainerFromItem(photo) is GridViewItem container)
        {
            ActiveGalleryGridView.ScrollIntoView(photo, ScrollIntoViewAlignment.Leading);
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
        var scopeId = string.IsNullOrWhiteSpace(photo.AnnotationScopeId)
            ? _annotationScopeId
            : photo.AnnotationScopeId;
        var operationKey = $"{scopeId}\0{photo.StarKey}";
        var isStarred = _favoriteDesiredStates.AddOrUpdate(
            operationKey,
            _ => !photo.IsStarred,
            (_, current) => !current);
        var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _loadCancellation?.Token ?? CancellationToken.None);
        _favoriteOperationCancellations.AddOrUpdate(
            operationKey,
            operationCancellation,
            (_, previous) =>
            {
                previous.Cancel();
                return operationCancellation;
            });
        var operationGate = _favoriteOperationGates.GetOrAdd(
            operationKey,
            static _ => new SemaphoreSlim(1, 1));
        var pageScopeId = _annotationScopeId;
        var generation = Interlocked.Read(ref _galleryGeneration);
        var gateEntered = false;
        try
        {
            await operationGate.WaitAsync(operationCancellation.Token);
            gateEntered = true;
            EnsureCurrentFavoriteOperation(
                operationKey,
                operationCancellation,
                pageScopeId,
                generation);
            await _annotationStore.SetStarredAsync(
                scopeId,
                photo.RelativePath,
                isStarred,
                operationCancellation.Token);
            EnsureCurrentFavoriteOperation(
                operationKey,
                operationCancellation,
                pageScopeId,
                generation);
            ViewModel.SetStarred(photo, isStarred);
            GalleryPreview.UpdateStarState(isStarred);

            if (isStarred)
            {
                if (string.Equals(
                        photo.AnnotationScopeId,
                        GalleryDefaultFavoriteSeedService.ScopeId,
                        StringComparison.Ordinal))
                {
                    GalleryPreview.SetStatus("已收藏");
                }
                else
                {
                    GalleryPreview.SetStatus("正在创建本地保护副本…");
                    var protection = await _favoriteProtectionService.ProtectAsync(
                        scopeId,
                        photo.RelativePath,
                        photo.FilePath,
                        operationCancellation.Token);
                    EnsureCurrentFavoriteOperation(
                        operationKey,
                        operationCancellation,
                        pageScopeId,
                        generation);
                    if (!photo.IsStarred)
                    {
                        return;
                    }

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
            }
            else
            {
                if (string.IsNullOrWhiteSpace(photo.AnnotationScopeId))
                {
                    var remainingStarredPaths = ViewModel.Photos
                        .Where(item =>
                            item.IsStarred &&
                            string.IsNullOrWhiteSpace(item.AnnotationScopeId))
                        .Select(item => item.RelativePath)
                        .ToArray();
                    await _favoriteProtectionService.CleanUnstarredAsync(
                        scopeId,
                        remainingStarredPaths,
                        operationCancellation.Token);
                }

                EnsureCurrentFavoriteOperation(
                    operationKey,
                    operationCancellation,
                    pageScopeId,
                    generation);
                photo.SetProtectedCopy(null, isUsingProtectedCopy: false);
                if (_viewMode == GalleryViewMode.Favorites)
                {
                    CloseGalleryPreview();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (isStarred && photo.IsStarred)
            {
                GalleryPreview.SetStatus("已收藏 · 本地保护副本创建失败");
                await ShowDialogAsync(
                    "照片已收藏",
                    $"星标已保存，但本地保护副本未创建：{ex.GetType().Name}");
            }
            else
            {
                await ShowDialogAsync("收藏更新失败", $"星标未保存：{ex.GetType().Name}");
            }
        }
        finally
        {
            if (gateEntered)
            {
                operationGate.Release();
            }

            if (_favoriteOperationCancellations.TryGetValue(operationKey, out var current) &&
                ReferenceEquals(current, operationCancellation))
            {
                _favoriteOperationCancellations.TryRemove(operationKey, out _);
                _favoriteDesiredStates.TryRemove(operationKey, out _);
            }

            operationCancellation.Dispose();
        }
    }

    private void EnsureCurrentFavoriteOperation(
        string operationKey,
        CancellationTokenSource operationCancellation,
        string pageScopeId,
        long generation)
    {
        operationCancellation.Token.ThrowIfCancellationRequested();
        if (generation != Interlocked.Read(ref _galleryGeneration) ||
            !string.Equals(pageScopeId, _annotationScopeId, StringComparison.Ordinal) ||
            !_favoriteOperationCancellations.TryGetValue(operationKey, out var current) ||
            !ReferenceEquals(current, operationCancellation))
        {
            throw new OperationCanceledException(operationCancellation.Token);
        }
    }

    private void CancelFavoriteOperations()
    {
        foreach (var cancellation in _favoriteOperationCancellations.Values)
        {
            cancellation.Cancel();
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
