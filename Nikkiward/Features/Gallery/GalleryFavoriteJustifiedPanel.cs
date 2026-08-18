using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.ViewModels;
using Windows.Foundation;

namespace Nikkiward.Features.Gallery;

public sealed class GalleryFavoriteJustifiedPanel : Panel
{
    private readonly HashSet<GalleryPhotoItemViewModel> _trackedItems = [];

    public GalleryFavoriteJustifiedPanel()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var availableWidth = ResolveAvailableWidth(availableSize.Width);
        if (availableWidth <= GalleryFavoriteCardLayoutProjection.ItemSpacing)
        {
            return new Size(Math.Max(0d, availableWidth), 0d);
        }

        var items = ReadItems();
        SyncTrackedItems(items);
        var layout = GalleryFavoriteCardLayoutProjection.Project(
            availableWidth - GalleryFavoriteCardLayoutProjection.ItemSpacing,
            items.Select(item => item?.CardAspectRatio ??
                GalleryFavoriteCardLayoutProjection.DefaultAspectRatio).ToArray());

        foreach (var placement in layout.Placements)
        {
            Children[placement.ItemIndex].Measure(new Size(
                placement.Width + GalleryFavoriteCardLayoutProjection.ItemSpacing,
                placement.Height + GalleryFavoriteCardLayoutProjection.ItemSpacing));
        }

        return new Size(
            availableWidth,
            layout.ContentHeight + GalleryFavoriteCardLayoutProjection.ItemSpacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var availableWidth = ResolveAvailableWidth(finalSize.Width);
        if (availableWidth <= GalleryFavoriteCardLayoutProjection.ItemSpacing)
        {
            return finalSize;
        }

        var items = ReadItems();
        var layout = GalleryFavoriteCardLayoutProjection.Project(
            availableWidth - GalleryFavoriteCardLayoutProjection.ItemSpacing,
            items.Select(item => item?.CardAspectRatio ??
                GalleryFavoriteCardLayoutProjection.DefaultAspectRatio).ToArray());

        foreach (var placement in layout.Placements)
        {
            Children[placement.ItemIndex].Arrange(new Rect(
                placement.X,
                placement.Y,
                placement.Width + GalleryFavoriteCardLayoutProjection.ItemSpacing,
                placement.Height + GalleryFavoriteCardLayoutProjection.ItemSpacing));
        }

        return finalSize;
    }

    private GalleryPhotoItemViewModel?[] ReadItems()
    {
        var items = new GalleryPhotoItemViewModel?[Children.Count];
        for (var index = 0; index < Children.Count; index++)
        {
            items[index] = ResolveItem(Children[index]);
        }

        return items;
    }

    private void SyncTrackedItems(IEnumerable<GalleryPhotoItemViewModel?> items)
    {
        var currentItems = items
            .Where(item => item is not null)
            .Cast<GalleryPhotoItemViewModel>()
            .ToHashSet();
        foreach (var removed in _trackedItems.Except(currentItems).ToArray())
        {
            removed.PropertyChanged -= OnItemPropertyChanged;
            _trackedItems.Remove(removed);
        }

        foreach (var added in currentItems.Except(_trackedItems))
        {
            added.PropertyChanged += OnItemPropertyChanged;
            _trackedItems.Add(added);
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GalleryPhotoItemViewModel.CardAspectRatio))
        {
            return;
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            InvalidateMeasure();
        }
        else
        {
            _ = DispatcherQueue.TryEnqueue(InvalidateMeasure);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => InvalidateMeasure();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        foreach (var item in _trackedItems)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        _trackedItems.Clear();
    }

    private static GalleryPhotoItemViewModel? ResolveItem(UIElement child)
    {
        if (child is ContentControl { Content: GalleryPhotoItemViewModel contentItem })
        {
            return contentItem;
        }

        return (child as FrameworkElement)?.DataContext as GalleryPhotoItemViewModel;
    }

    private double ResolveAvailableWidth(double width)
    {
        if (double.IsFinite(width) && width > 0d)
        {
            return width;
        }

        return double.IsFinite(ActualWidth) && ActualWidth > 0d ? ActualWidth : 0d;
    }
}
