using System.Collections.ObjectModel;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nikkiward.Features.Shell;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Journal;

public sealed partial class JournalSnapshotPanel : UserControl
{
    private const double WideBreakpoint = 1040;

    public ObservableCollection<JournalTaskViewModel> ScheduleTasks { get; } = [];

    public ObservableCollection<JournalExploreGroupViewModel> ExploreGroups { get; } = [];

    public ObservableCollection<JournalRecordViewModel> NotebookRecords { get; } = [];

    public ObservableCollection<JournalRecordViewModel> BlessingRecords { get; } = [];

    public ObservableCollection<JournalRecordViewModel> CrownRecords { get; } = [];

    public ObservableCollection<JournalStatViewModel> WishStats { get; } = [];

    public ObservableCollection<JournalWardrobePreviewViewModel> WardrobePreviews { get; } = [];

    public ObservableCollection<JournalResourceGroupViewModel> ResourceGroups { get; } = [];

    public JournalSnapshotPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    public bool ApplySnapshot(JournalSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ClearCollections();
        var resources = snapshot.Resources
            .Where(resource => !string.IsNullOrWhiteSpace(resource.PreviewUri))
            .ToArray();

        PopulateSchedule(snapshot, resources);
        PopulateExploration(snapshot, resources);
        PopulateNotebook(snapshot, resources);
        PopulateBlessing(snapshot, resources);
        PopulateCrown(snapshot, resources);
        PopulateWish(snapshot);
        PopulateWardrobe(snapshot, resources);

        LastSyncedText.Text = snapshot.CapturedAtUtc.ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss");
        var hasContent = HasContent(snapshot);
        Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
        ScheduleSection.Visibility = ScheduleTasks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExplorationSection.Visibility = ExploreGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotebookPanel.Visibility = NotebookRecords.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BlessingSection.Visibility = BlessingRecords.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CrownSection.Visibility = CrownRecords.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WishPanel.Visibility = WishStats.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WardrobeSection.Visibility = WardrobePreviews.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResourceSection.Visibility = Visibility.Collapsed;
        return hasContent;
    }

    public void ResetState()
    {
        ClearCollections();
        LastSyncedText.Text = string.Empty;
        Visibility = Visibility.Collapsed;
    }

    private void PopulateSchedule(
        JournalSnapshot snapshot,
        IReadOnlyList<JournalResourceSnapshot> resources)
    {
        var section = FindSection(snapshot, "anchor:schedule-note");
        foreach (var block in (section?.Blocks ?? [])
                     .Where(block => block.Kind == "schedule-task"))
        {
            ScheduleTasks.Add(new JournalTaskViewModel(
                block.Label ?? "日程",
                block.Value ?? string.Empty,
                FormatProgress(block),
                block.Status ?? string.Empty));
        }
    }

    private void PopulateExploration(
        JournalSnapshot snapshot,
        IReadOnlyList<JournalResourceSnapshot> resources)
    {
        var section = FindSection(snapshot, "anchor:exploration-overview");
        if (section is null)
        {
            return;
        }

        var itemBlocks = section.Blocks
            .Where(block => block.Kind == "explore-item")
            .OrderBy(block => block.Order)
            .ToArray();
        var groups = section.Blocks
            .Where(block => block.Kind == "explore-group")
            .OrderBy(block => block.Order)
            .ToArray();
        foreach (var group in groups)
        {
            var name = group.Label ?? "探索区域";
            var items = itemBlocks
                .Where(block => string.Equals(block.ParentKey, name, StringComparison.Ordinal))
                .Select(block => new JournalExploreItemViewModel(
                    block.Label ?? "收集物",
                    FormatProgress(block),
                    block.Status ?? string.Empty,
                    ResolvePreviewUri(resources, block.ResourceUrl)))
                .ToArray();
            ExploreGroups.Add(new JournalExploreGroupViewModel(
                name,
                group.Status ?? string.Empty,
                items));
        }
    }

    private void PopulateNotebook(
        JournalSnapshot snapshot,
        IReadOnlyList<JournalResourceSnapshot> resources)
    {
        var section = FindSection(snapshot, "anchor:inspiration-sketches");
        foreach (var block in (section?.Blocks ?? [])
                     .Where(block => block.Kind is "notebook-summary" or "notebook-record")
                     .OrderBy(block => block.Kind == "notebook-summary" ? 0 : 1)
                     .ThenBy(block => block.Order))
        {
            NotebookRecords.Add(new JournalRecordViewModel(
                block.Label ?? "札记",
                block.Value ?? string.Empty,
                block.Kind,
                ResolvePreviewUri(resources, block.ResourceUrl)));
        }
    }

    private void PopulateBlessing(
        JournalSnapshot snapshot,
        IReadOnlyList<JournalResourceSnapshot> resources)
    {
        var section = FindSection(snapshot, "anchor:blessing-sparkle");
        foreach (var block in (section?.Blocks ?? [])
                     .Where(block => block.Kind.StartsWith("blessing-", StringComparison.Ordinal))
                     .OrderBy(block => block.Order))
        {
            BlessingRecords.Add(new JournalRecordViewModel(
                block.Label ?? "祝福素材",
                block.Value ?? string.Empty,
                block.Kind,
                ResolvePreviewUri(resources, block.ResourceUrl)));
        }
    }

    private void PopulateCrown(
        JournalSnapshot snapshot,
        IReadOnlyList<JournalResourceSnapshot> resources)
    {
        var section = FindSection(snapshot, "anchor:miracle-crown");
        foreach (var block in (section?.Blocks ?? [])
                     .Where(block => block.Kind == "crown-stat")
                     .OrderBy(block => block.Order))
        {
            CrownRecords.Add(new JournalRecordViewModel(
                block.Label ?? "奇迹之冠",
                block.Value ?? string.Empty,
                block.Kind,
                ResolvePreviewUri(resources, block.ResourceUrl)));
        }
    }

    private void PopulateWish(JournalSnapshot snapshot)
    {
        var section = FindSection(snapshot, "anchor:wish-resonance");
        foreach (var block in (section?.Blocks ?? [])
                     .Where(block => block.Kind is "wish-stat" or "wish-set-ratio")
                     .OrderBy(block => block.Order))
        {
            WishStats.Add(new JournalStatViewModel(
                block.Label ?? "共鸣统计",
                block.Value ?? "暂无数据",
                block.Kind == "wish-set-ratio" ? "集齐进度" : string.Empty));
        }
    }

    private void PopulateWardrobe(
        JournalSnapshot snapshot,
        IReadOnlyList<JournalResourceSnapshot> resources)
    {
        var section = FindSection(snapshot, "anchor:resonance-wardrobe");
        foreach (var block in (section?.Blocks ?? [])
                     .Where(block => block.Kind == "wardrobe-card")
                     .OrderBy(block => block.Order))
        {
            var parts = (block.Source ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries);
            WardrobePreviews.Add(new JournalWardrobePreviewViewModel(
                parts.Length >= 3 ? parts[^2] : "共鸣衣橱",
                block.Label ?? "套装",
                block.Value ?? string.Empty,
                block.Status ?? FormatProgress(block),
                block.Unit ?? string.Empty,
                ResolvePreviewUri(resources, block.ResourceUrl)));
        }
    }

    private void PopulateResourceGroups(
        JournalSnapshot snapshot,
        IReadOnlyList<JournalResourceSnapshot> resources)
    {
        var definitions = new[]
        {
            (Key: "anchor:exploration-overview", Title: "探索资源"),
            (Key: "anchor:inspiration-sketches", Title: "札记资源"),
            (Key: "anchor:blessing-sparkle", Title: "祝福闪光"),
            (Key: "anchor:wish-resonance", Title: "共鸣统计资源"),
            (Key: "anchor:resonance-wardrobe", Title: "共鸣衣橱资源"),
            (Key: "anchor:miracle-crown", Title: "奇迹之冠资源"),
        };
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            var matches = resources
                .Where(resource => resource.Source?.Contains(
                    definition.Key,
                    StringComparison.OrdinalIgnoreCase) == true)
                .DistinctBy(resource => resource.Url, StringComparer.OrdinalIgnoreCase)
                .Select(resource => CreateResourceThumb(resource, assigned))
                .Where(item => item is not null)
                .Cast<JournalResourceThumbViewModel>()
                .ToArray();
            if (matches.Length > 0)
            {
                ResourceGroups.Add(new JournalResourceGroupViewModel(
                    definition.Title,
                    $"{matches.Length} 项",
                    matches));
            }
        }

        var remaining = resources
            .Where(resource => !assigned.Contains(resource.Url))
            .DistinctBy(resource => resource.Url, StringComparer.OrdinalIgnoreCase)
            .Select(resource => CreateResourceThumb(resource, assigned))
            .Where(item => item is not null)
            .Cast<JournalResourceThumbViewModel>()
            .ToArray();
        if (remaining.Length > 0)
        {
            ResourceGroups.Add(new JournalResourceGroupViewModel(
                "页面装饰与公共素材",
                $"{remaining.Length} 项",
                remaining));
        }
    }

    private static JournalResourceThumbViewModel? CreateResourceThumb(
        JournalResourceSnapshot resource,
        ISet<string> assigned)
    {
        if (string.IsNullOrWhiteSpace(resource.PreviewUri) || !assigned.Add(resource.Url))
        {
            return null;
        }

        return new JournalResourceThumbViewModel(
            resource.AltText ?? resource.Role ?? "网页素材",
            resource.PreviewUri,
            resource.Role ?? "image");
    }

    private static JournalSectionSnapshot? FindSection(JournalSnapshot snapshot, string key) =>
        snapshot.Sections.FirstOrDefault(section =>
            section.SectionKey.Equals(key, StringComparison.OrdinalIgnoreCase));

    private static string ResolvePreviewUri(
        IEnumerable<JournalResourceSnapshot> resources,
        string? url) =>
        string.IsNullOrWhiteSpace(url)
            ? string.Empty
            : resources.FirstOrDefault(resource =>
                resource.Url.Equals(url, StringComparison.OrdinalIgnoreCase))?.PreviewUri ?? string.Empty;

    private static string FormatProgress(JournalContentBlockSnapshot block)
    {
        if (!string.IsNullOrWhiteSpace(block.Current) && !string.IsNullOrWhiteSpace(block.Total))
        {
            return $"{block.Current}/{block.Total}";
        }

        return block.Value ?? string.Empty;
    }

    private static bool HasContent(JournalSnapshot snapshot) =>
        !string.IsNullOrWhiteSpace(snapshot.GameHours) ||
        !string.IsNullOrWhiteSpace(snapshot.LoginDays) ||
        !string.IsNullOrWhiteSpace(snapshot.OutfitCount) ||
        !string.IsNullOrWhiteSpace(snapshot.MomoCloakCount) ||
        !string.IsNullOrWhiteSpace(snapshot.SketchCount) ||
        snapshot.Sections.Count > 0;

    private void ClearCollections()
    {
        ScheduleTasks.Clear();
        ExploreGroups.Clear();
        NotebookRecords.Clear();
        BlessingRecords.Clear();
        CrownRecords.Clear();
        WishStats.Clear();
        WardrobePreviews.Clear();
        ResourceGroups.Clear();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => ApplyResponsiveLayout(ActualWidth);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout(e.NewSize.Width);

    private void OnImageCardPointerEntered(object sender, PointerRoutedEventArgs e) =>
        SetInteractiveScale(sender, AppearanceRuntimeValues.ReadScale("HoverScale"));

    private void OnImageCardPointerExited(object sender, PointerRoutedEventArgs e) =>
        SetInteractiveScale(sender, 1f);

    private void OnImageCardPointerPressed(object sender, PointerRoutedEventArgs e) =>
        SetInteractiveScale(sender, AppearanceRuntimeValues.ReadScale("PressScale"));

    private void OnImageCardPointerReleased(object sender, PointerRoutedEventArgs e) =>
        SetInteractiveScale(sender, AppearanceRuntimeValues.ReadScale("HoverScale"));

    private static void SetInteractiveScale(object sender, float scale)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        element.CenterPoint = new Vector3(
            (float)(element.ActualWidth / 2),
            (float)(element.ActualHeight / 2),
            0f);
        element.Scale = new Vector3(scale, scale, 1f);
    }

    private void ApplyResponsiveLayout(double width)
    {
        var wide = width >= WideBreakpoint;
        JournalDetailGrid.ColumnDefinitions[0].Width = wide
            ? new GridLength(7, GridUnitType.Star)
            : new GridLength(1, GridUnitType.Star);
        JournalDetailGrid.ColumnDefinitions[1].Width = wide
            ? new GridLength(5, GridUnitType.Star)
            : new GridLength(0);
        Grid.SetColumn(NotebookPanel, 0);
        Grid.SetColumn(WishPanel, wide ? 1 : 0);
        Grid.SetRow(WishPanel, wide ? 0 : 1);
    }
}
