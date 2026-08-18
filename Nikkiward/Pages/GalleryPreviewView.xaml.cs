using System.Diagnostics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Nikkiward.Features.Background;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.Shell;
using Nikkiward.ViewModels;
using Windows.System;
using Windows.UI.Core;

namespace Nikkiward.Pages;

public sealed partial class GalleryPreviewView : UserControl
{
    private const int PhotoPaletteDecodeWidth = 64;
    private const int PhotoPaletteSampleSize = 16;

    private readonly ArtPaletteAnalyzer _photoPaletteAnalyzer = new();
    private CancellationTokenSource? _photoThemeCancellation;
    private CancellationTokenSource? _metadataCancellation;
    private long _photoThemeGeneration;
    private string? _connectedAnimationKey;

    public event EventHandler? CloseRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? StarRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler? NextRequested;

    public Func<string, CancellationToken, Task<GalleryPhotoMetadata>>? MetadataLoader { get; set; }

    public GalleryPreviewView()
    {
        InitializeComponent();
        Unloaded += OnGalleryPreviewUnloaded;
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    public void ShowPhoto(
        GalleryPhotoItemViewModel photo,
        int index,
        int totalCount,
        string? connectedAnimationKey = null)
    {
        _connectedAnimationKey = connectedAnimationKey;
        GalleryPreviewImage.Source = new BitmapImage(
            new Uri(photo.FilePath, UriKind.Absolute));
        GalleryPreviewFileNameText.Text = photo.FileName;
        GalleryPreviewMetadataText.Text = photo.MetadataText;
        GalleryPreviewIndexText.Text = $"{index + 1:N0} / {totalCount:N0}";
        GalleryPreviewPreviousButton.IsEnabled = index > 0;
        GalleryPreviewNextButton.IsEnabled = index + 1 < totalCount;
        GalleryPreviewStatusText.Text = photo.HasProtectedCopy
            ? photo.ProtectionStatusText
            : string.Empty;
        GalleryPreviewZoomText.Text = "100%";
        GalleryInfoFileNameText.Text = photo.FileName;
        GalleryInfoCategoryText.Text = photo.CategoryName;
        GalleryInfoMetadataText.Text = $"{photo.ModifiedText} · {photo.FileSizeText}";
        GalleryInfoDimensionsText.Text = "分辨率：正在读取";
        GalleryInfoUidText.Text = $"UID：{photo.UidText}";
        GalleryInfoPathText.Text = photo.RelativePath;
        GalleryInfoCameraText.Text = "正在读取…";
        GalleryInfoFilterText.Text = string.Empty;
        GalleryInfoOutfitText.Text = string.Empty;
        GalleryInfoLocationText.Text = string.Empty;
        GalleryInfoTasksText.Text = string.Empty;
        UpdateStarState(photo.IsStarred);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            GalleryPreviewImage,
            photo.AccessibleName);

        GalleryInfoPanel.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Visible;
        GalleryPreviewScrollViewer.ChangeView(0, 0, 1f, true);
        GalleryInfoPanel.RequestedTheme = ActualTheme;
        StartMetadataLoad(photo.FilePath);
        StartPhotoThemeAnalysis(photo.FilePath);
        _ = GalleryPreviewCloseButton.Focus(FocusState.Programmatic);
    }

    public void ClosePreview()
    {
        CancelPhotoThemeAnalysis(resetTheme: true);
        CancelMetadataLoad();
        GalleryPreviewImage.Source = null;
        GalleryPreviewFileNameText.Text = string.Empty;
        GalleryPreviewIndexText.Text = string.Empty;
        GalleryPreviewMetadataText.Text = string.Empty;
        GalleryPreviewStatusText.Text = string.Empty;
        GalleryPreviewPreviousButton.IsEnabled = false;
        GalleryPreviewNextButton.IsEnabled = false;
        GalleryInfoPanel.Visibility = Visibility.Collapsed;
        Visibility = Visibility.Collapsed;
        _connectedAnimationKey = null;
    }

    public void SetStatus(string value)
    {
        GalleryPreviewStatusText.Text = value;
    }

    public void UpdateStarState(bool isStarred)
    {
        GalleryPreviewStarIcon.Glyph = isStarred ? "\uE735" : "\uE734";
        var label = isStarred ? "取消收藏" : "添加到收藏";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            GalleryPreviewStarButton,
            label);
        ToolTipService.SetToolTip(GalleryPreviewStarButton, label);
    }

    private void StartPhotoThemeAnalysis(string filePath)
    {
        var generation = Interlocked.Increment(ref _photoThemeGeneration);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(
            ref _photoThemeCancellation,
            cancellation);
        previous?.Cancel();

        GalleryPreviewSurface.RequestedTheme = ElementTheme.Dark;
        _ = ApplyPhotoThemeAsync(filePath, generation, cancellation);
    }

    private async Task ApplyPhotoThemeAsync(
        string filePath,
        long generation,
        CancellationTokenSource cancellation)
    {
        var cancellationToken = cancellation.Token;

        try
        {
            var file = await ArtDecoder.TryResolveAsync(filePath);
            cancellationToken.ThrowIfCancellationRequested();

            if (file is null ||
                generation != Volatile.Read(ref _photoThemeGeneration))
            {
                ApplyPhotoThemeFallback(generation);
                return;
            }

            var decoded = await ArtDecoder.DecodeScaledAsync(
                file,
                PhotoPaletteDecodeWidth,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (decoded is null ||
                generation != Volatile.Read(ref _photoThemeGeneration))
            {
                ApplyPhotoThemeFallback(generation);
                return;
            }

            var preferredTheme = await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sample = decoded.Downsample(
                        PhotoPaletteSampleSize,
                        PhotoPaletteSampleSize);
                    var analysis = _photoPaletteAnalyzer.Analyze(sample, string.Empty);
                    cancellationToken.ThrowIfCancellationRequested();
                    return analysis.PreferredTheme;
                },
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (generation != Volatile.Read(ref _photoThemeGeneration) ||
                !IsOpen)
            {
                return;
            }

            GalleryPreviewSurface.RequestedTheme =
                preferredTheme == ArtPreferredTheme.Dark
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ApplyPhotoThemeFallback(generation);
            Debug.WriteLine(
                $"Gallery preview theme analysis failed: {ex.GetType().Name}");
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _photoThemeCancellation,
                null,
                cancellation);
            cancellation.Dispose();
        }
    }

    private void CancelPhotoThemeAnalysis(bool resetTheme)
    {
        Interlocked.Increment(ref _photoThemeGeneration);
        var cancellation = Interlocked.Exchange(
            ref _photoThemeCancellation,
            null);
        cancellation?.Cancel();

        if (resetTheme)
        {
            GalleryPreviewSurface.RequestedTheme = ElementTheme.Dark;
            GalleryInfoPanel.RequestedTheme = ElementTheme.Default;
        }
    }

    private void ApplyPhotoThemeFallback(long generation)
    {
        if (generation == Volatile.Read(ref _photoThemeGeneration) && IsOpen)
        {
            GalleryPreviewSurface.RequestedTheme = ElementTheme.Dark;
        }
    }

    private void OnGalleryPreviewUnloaded(object sender, RoutedEventArgs e)
    {
        CancelPhotoThemeAnalysis(resetTheme: true);
        CancelMetadataLoad();
    }

    private void OnGalleryPreviewPreviousClicked(object sender, RoutedEventArgs e) =>
        PreviousRequested?.Invoke(this, EventArgs.Empty);

    private void OnGalleryPreviewNextClicked(object sender, RoutedEventArgs e) =>
        NextRequested?.Invoke(this, EventArgs.Empty);

    private void OnGalleryPreviewCloseClicked(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnGalleryPreviewCopyClicked(object sender, RoutedEventArgs e) =>
        CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnGalleryPreviewStarClicked(object sender, RoutedEventArgs e) =>
        StarRequested?.Invoke(this, EventArgs.Empty);

    private void OnGalleryPreviewInfoClicked(object sender, RoutedEventArgs e)
    {
        ToggleInfoPanel();
    }

    private void OnGalleryPreviewFitClicked(object sender, RoutedEventArgs e) =>
        GalleryPreviewScrollViewer.ChangeView(0, 0, 1f);

    private void OnGalleryPreviewViewportSizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        if (IsOpen && GalleryPreviewScrollViewer.ZoomFactor <= 1.001f)
        {
            GalleryPreviewScrollViewer.ChangeView(0, 0, 1f, true);
        }
    }

    private void OnGalleryPreviewZoomOutClicked(object sender, RoutedEventArgs e)
    {
        var zoom = Math.Clamp(GalleryPreviewScrollViewer.ZoomFactor - 0.25f, 0.25f, 5f);
        GalleryPreviewScrollViewer.ChangeView(null, null, zoom);
    }

    private void OnGalleryPreviewZoomInClicked(object sender, RoutedEventArgs e)
    {
        var zoom = Math.Clamp(GalleryPreviewScrollViewer.ZoomFactor + 0.25f, 0.25f, 5f);
        GalleryPreviewScrollViewer.ChangeView(null, null, zoom);
    }

    private void OnGalleryPreviewViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        GalleryPreviewZoomText.Text = $"{GalleryPreviewScrollViewer.ZoomFactor:P0}";
    }

    private void OnGalleryPreviewImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _connectedAnimationKey = null;
        GalleryPreviewStatusText.Text = "图片载入失败";
    }

    private void OnGalleryPreviewImageOpened(object sender, RoutedEventArgs e)
    {
        if (GalleryPreviewImage.Source is BitmapImage image &&
            image.PixelWidth > 0 &&
            image.PixelHeight > 0)
        {
            GalleryInfoDimensionsText.Text =
                $"分辨率：{image.PixelWidth:N0} × {image.PixelHeight:N0}";
        }

        if (string.IsNullOrWhiteSpace(_connectedAnimationKey))
        {
            return;
        }

        var key = _connectedAnimationKey;
        _connectedAnimationKey = null;
        if (AppearanceRuntimeValues.IsMotionEnabled("MotionArt"))
        {
            ConnectedAnimationService.GetForCurrentView()
                .GetAnimation(key)
                ?.TryStart(GalleryPreviewImage);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!IsOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case VirtualKey.Left:
                PreviousRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                NextRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case VirtualKey.Number0 when InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(CoreVirtualKeyStates.Down):
                GalleryPreviewScrollViewer.ChangeView(0, 0, 1f);
                e.Handled = true;
                break;
            case VirtualKey.I when InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(CoreVirtualKeyStates.Down):
                ToggleInfoPanel();
                e.Handled = true;
                break;
        }
    }

    private void ToggleInfoPanel()
    {
        GalleryInfoPanel.Visibility = GalleryInfoPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
