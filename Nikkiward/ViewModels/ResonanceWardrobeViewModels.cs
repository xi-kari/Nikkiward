using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Nikkiward.ViewModels;

public sealed class ResonanceBannerCardViewModel
{
    public ResonanceBannerCardViewModel(ResonanceBannerSnapshot banner)
    {
        ArgumentNullException.ThrowIfNull(banner);
        PoolId = banner.PoolId ?? banner.PatchTitle;
        PatchTitle = banner.PatchTitle;
        OutfitTitle = string.IsNullOrWhiteSpace(banner.OutfitTitle)
            ? banner.PoolName ?? banner.PatchTitle
            : banner.OutfitTitle;
        AveragePulls = string.IsNullOrWhiteSpace(banner.AveragePulls)
            ? "暂无数据"
            : banner.AveragePulls;
        TotalPulls = string.IsNullOrWhiteSpace(banner.TotalPulls)
            ? "暂无数据"
            : banner.TotalPulls;
        CompletionText = string.IsNullOrWhiteSpace(banner.CompletionText)
            ? $"{banner.Items.Count(item => item.ObtainCount > 0)}/{banner.Items.Count}"
            : banner.CompletionText;
        RemainingText = banner.RemainingText;
        PreviewSource = PreviewImageSource.Create(banner.CoverPreviewUri);
        var rarity = banner.Rarity ?? banner.Items
            .Select(item => item.Rarity ?? 0)
            .DefaultIfEmpty()
            .Max();
        RarityStars = rarity > 0 ? new string('★', rarity) : string.Empty;
        Slots = banner.Items
            .OrderBy(item => item.SlotIndex)
            .Select(item => new ResonanceSlotCardViewModel(item))
            .ToArray();
    }

    public string PoolId { get; }

    public string PatchTitle { get; }

    public string OutfitTitle { get; }

    public string AveragePulls { get; }

    public string TotalPulls { get; }

    public string CompletionText { get; }

    public string RemainingText { get; }

    public ImageSource? PreviewSource { get; }

    public string RarityStars { get; }

    public IReadOnlyList<ResonanceSlotCardViewModel> Slots { get; }

    public string AutomationName => $"{PatchTitle} {OutfitTitle} 共鸣衣橱";

    public Visibility ImageVisibility => PreviewSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility PlaceholderVisibility => PreviewSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;
}

public sealed class ResonanceSlotCardViewModel
{
    public ResonanceSlotCardViewModel(ResonanceItemSnapshot item)
    {
        PreviewSource = PreviewImageSource.Create(item.PreviewUri);
        CountText = item.ObtainCount.ToString();
        Label = string.IsNullOrWhiteSpace(item.ItemName)
            ? $"服装槽位 {item.SlotIndex + 1}"
            : item.ItemName;
        OwnedOpacity = item.ObtainCount > 0 ? 1d : 0.52d;
        CountBackgroundOpacity = item.ObtainCount > 0 ? 1d : 0.72d;
    }

    public ImageSource? PreviewSource { get; }

    public string CountText { get; }

    public string Label { get; }

    public double OwnedOpacity { get; }

    public double CountBackgroundOpacity { get; }
}
