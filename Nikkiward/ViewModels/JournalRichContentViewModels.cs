using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Nikkiward.ViewModels;

public sealed class JournalTaskViewModel
{
    public JournalTaskViewModel(string title, string detail, string progress, string status)
    {
        Title = title;
        Detail = detail;
        Progress = progress;
        Status = status;
    }

    public string Title { get; }

    public string Detail { get; }

    public string Progress { get; }

    public string Status { get; }
}

public sealed class JournalExploreItemViewModel
{
    public JournalExploreItemViewModel(string name, string progress, string status, string previewUri)
    {
        Name = name;
        Progress = progress;
        Status = status;
        PreviewSource = PreviewImageSource.Create(previewUri);
    }

    public string Name { get; }

    public string Progress { get; }

    public string Status { get; }

    public ImageSource? PreviewSource { get; }

    public Visibility ImageVisibility => PreviewSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility PlaceholderVisibility => PreviewSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;
}

public sealed class JournalExploreGroupViewModel
{
    public JournalExploreGroupViewModel(
        string name,
        string status,
        IReadOnlyList<JournalExploreItemViewModel> items)
    {
        Name = name;
        Status = status;
        Items = items;
    }

    public string Name { get; }

    public string Status { get; }

    public IReadOnlyList<JournalExploreItemViewModel> Items { get; }
}

public sealed class JournalRecordViewModel
{
    public JournalRecordViewModel(string label, string value, string kind, string previewUri)
    {
        Label = label;
        Value = value;
        Kind = kind;
        PreviewSource = PreviewImageSource.Create(previewUri);
    }

    public string Label { get; }

    public string Value { get; }

    public string Kind { get; }

    public ImageSource? PreviewSource { get; }

    public Visibility ImageVisibility => PreviewSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility PlaceholderVisibility => PreviewSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;
}

public sealed class JournalStatViewModel
{
    public JournalStatViewModel(string label, string value, string detail)
    {
        Label = label;
        Value = value;
        Detail = detail;
    }

    public string Label { get; }

    public string Value { get; }

    public string Detail { get; }
}

public sealed class JournalWardrobePreviewViewModel
{
    public JournalWardrobePreviewViewModel(
        string patchTitle,
        string outfitTitle,
        string summary,
        string completion,
        string remaining,
        string previewUri)
    {
        PatchTitle = patchTitle;
        OutfitTitle = outfitTitle;
        Summary = summary;
        Completion = completion;
        Remaining = remaining;
        PreviewSource = PreviewImageSource.Create(previewUri);
    }

    public string PatchTitle { get; }

    public string OutfitTitle { get; }

    public string Summary { get; }

    public string Completion { get; }

    public string Remaining { get; }

    public ImageSource? PreviewSource { get; }

    public Visibility ImageVisibility => PreviewSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility PlaceholderVisibility => PreviewSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;
}

public sealed class JournalResourceThumbViewModel
{
    public JournalResourceThumbViewModel(string label, string previewUri, string role)
    {
        Label = label;
        PreviewSource = PreviewImageSource.Create(previewUri);
        Role = role;
    }

    public string Label { get; }

    public ImageSource? PreviewSource { get; }

    public string Role { get; }
}

public sealed class JournalResourceGroupViewModel
{
    public JournalResourceGroupViewModel(
        string title,
        string countText,
        IReadOnlyList<JournalResourceThumbViewModel> items)
    {
        Title = title;
        CountText = countText;
        Items = items;
    }

    public string Title { get; }

    public string CountText { get; }

    public IReadOnlyList<JournalResourceThumbViewModel> Items { get; }
}

internal static class PreviewImageSource
{
    public static ImageSource? Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return new BitmapImage(uri);
    }
}
