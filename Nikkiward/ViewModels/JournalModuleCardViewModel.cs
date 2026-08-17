using Microsoft.UI.Xaml;

namespace Nikkiward.ViewModels;

public sealed class JournalModuleCardViewModel
{
    public JournalModuleCardViewModel(
        JournalSectionSnapshot section,
        JournalResourceSnapshot? resource,
        bool isPrimary = false)
    {
        Title = section.Title;
        SectionKey = section.SectionKey;
        SummaryText = string.IsNullOrWhiteSpace(section.Text)
            ? "同步后显示这一模块的页面数据"
            : section.Text;
        PreviewUri = resource?.PreviewUri ?? string.Empty;
        Route = section.Route ?? string.Empty;
        Metrics = section.Metrics.Take(3).ToArray();
        CardMinHeight = isPrimary ? 320 : 200;
        Glyph = section.SectionKey switch
        {
            "anchor:schedule-note" or "route:/tools/journal/schedule" => "\uE787",
            "anchor:exploration-overview" or "route:/tools/journal/exploration" => "\uE707",
            "anchor:inspiration-sketches" or "route:/tools/journal/sketch" => "\uE70B",
            "anchor:blessing-sparkle" or "route:/tools/journal/blessing" => "\uE945",
            "anchor:wish-resonance" or "route:/tools/journal/resonance" => "\uF4A5",
            "anchor:resonance-wardrobe" or "route:/tools/journal/clothespress" => "\uE8B9",
            "anchor:miracle-crown" or "route:/tools/journal/crown" => "\uE734",
            _ => "\uE8F1",
        };
    }

    public string Title { get; }

    public string SectionKey { get; }

    public string AutomationName => $"打开{Title}详情";

    public double CardMinHeight { get; }

    public string SummaryText { get; }

    public string PreviewUri { get; }

    public string Route { get; }

    public string Glyph { get; }

    public IReadOnlyList<JournalMetricSnapshot> Metrics { get; }

    public Visibility PreviewVisibility => string.IsNullOrWhiteSpace(PreviewUri)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility PlaceholderVisibility => string.IsNullOrWhiteSpace(PreviewUri)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility MetricsVisibility => Metrics.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SummaryVisibility => Metrics.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
}
