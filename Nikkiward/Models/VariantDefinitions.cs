namespace Nikkiward.Models;

public enum GameVariantId
{
    Unknown,
    MainlandOfficial,
    MainlandBilibili,
    GlobalSteam,
}

public enum VariantLaunchAuthority
{
    Unknown,
    OfficialLauncher,
    BilibiliLauncher,
    SteamClient,
}

public sealed record VariantDefinition
{
    public required GameVariantId VariantId { get; init; }

    public required string VariantKey { get; init; }

    public required string DisplayName { get; init; }

    public required string RegionKey { get; init; }

    public required string DistributionKey { get; init; }

    public required VariantLaunchAuthority LaunchAuthority { get; init; }

    public required string ProductMarkerName { get; init; }

    public int? ProductMarkerVersion { get; init; }
}

public static class VariantDefinitionCatalog
{
    public static VariantDefinition MainlandOfficial { get; } = new()
    {
        VariantId = GameVariantId.MainlandOfficial,
        VariantKey = "cn-official",
        DisplayName = "Mainland China Official",
        RegionKey = "mainland-china",
        DistributionKey = "official",
        LaunchAuthority = VariantLaunchAuthority.OfficialLauncher,
        ProductMarkerName = "InfinityNikki Launcher",
        ProductMarkerVersion = 2828,
    };

    public static VariantDefinition MainlandBilibili { get; } = new()
    {
        VariantId = GameVariantId.MainlandBilibili,
        VariantKey = "cn-bilibili",
        DisplayName = "Mainland China Bilibili",
        RegionKey = "mainland-china",
        DistributionKey = "bilibili",
        LaunchAuthority = VariantLaunchAuthority.BilibiliLauncher,
        ProductMarkerName = "InfinityNikkiBili Launcher",
        ProductMarkerVersion = 2828,
    };

    public static VariantDefinition GlobalSteam { get; } = new()
    {
        VariantId = GameVariantId.GlobalSteam,
        VariantKey = "global-steam",
        DisplayName = "Global Steam",
        RegionKey = "global",
        DistributionKey = "steam",
        LaunchAuthority = VariantLaunchAuthority.SteamClient,
        ProductMarkerName = "InfinityNikkiSteam Launcher",
    };

    public static IReadOnlyList<VariantDefinition> All { get; } =
        Array.AsReadOnly([MainlandOfficial, MainlandBilibili, GlobalSteam]);

    public static VariantDefinition? Find(GameVariantId variantId) =>
        All.FirstOrDefault(definition => definition.VariantId == variantId);

    public static VariantDefinition? Find(string variantKey) =>
        string.IsNullOrWhiteSpace(variantKey)
            ? null
            : All.FirstOrDefault(definition =>
                string.Equals(definition.VariantKey, variantKey, StringComparison.Ordinal));
}
