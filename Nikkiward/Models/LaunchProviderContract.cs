namespace Nikkiward.Models;

public sealed record BinaryIdentityRequirement
{
    public string ComponentId { get; init; } = string.Empty;

    public string RootRole { get; init; } = "LauncherRoot";

    public string RelativePath { get; init; } = string.Empty;

    public string ExpectedSha256 { get; init; } = string.Empty;

    public string? ExpectedFileVersion { get; init; }

    public string? ExpectedProductVersion { get; init; }

    public AuthenticodeSignatureStatus ExpectedSignature { get; init; } =
        AuthenticodeSignatureStatus.Valid;

    public string? ExpectedSignerThumbprint { get; init; }
}

public sealed record ProductMarkerRequirement
{
    public string RootRole { get; init; } = "GameRoot";

    public string RelativePath { get; init; } = string.Empty;

    public string? ExpectedName { get; init; }

    public string? ExpectedLauncherVersion { get; init; }

    public string? ExpectedAdPlatformId { get; init; }
}

/// <summary>
/// Immutable description of an official entry point. User settings may select
/// a contract, but cannot alter any of its paths or arguments.
/// </summary>
public sealed record LaunchProviderContract
{
    public string ContractId { get; init; } = string.Empty;

    public int ContractVersion { get; init; }

    public RegionFamily RegionFamily { get; init; } = RegionFamily.Unknown;

    public DistributionChannel DistributionChannel { get; init; } = DistributionChannel.Unknown;

    public string Platform { get; init; } = "WindowsNative";

    public string BackendRelativeExecutablePath { get; init; } = string.Empty;

    public string WorkingDirectoryRole { get; init; } = "LauncherRoot";

    public string ArgumentPresetId { get; init; } = string.Empty;

    public IReadOnlyList<string> ArgumentList { get; init; } = Array.Empty<string>();

    public IReadOnlyList<BinaryIdentityRequirement> RequiredComponents { get; init; } =
        Array.Empty<BinaryIdentityRequirement>();

    public ProductMarkerRequirement? ProductMarker { get; init; }

    public LaunchCapability MaximumCapability { get; init; } = LaunchCapability.OfficialAssisted;

    public bool ExecutionEnabled { get; init; }
}

public static class LaunchProviderCatalog
{
    public const string CnWindows131ContractId = "OfficialXStarterSkipLauncherCn131";

    public const string CnWindows131ArgumentPresetId = "cn-win-xstarter-skiplauncher-v1";

    public const string CnLauncherVersion = "1.3.1";

    public static LaunchProviderContract CnWindows131 { get; } = new()
    {
        ContractId = CnWindows131ContractId,
        ContractVersion = 1,
        RegionFamily = RegionFamily.MainlandChina,
        DistributionChannel = DistributionChannel.Official,
        Platform = "WindowsNative",
        BackendRelativeExecutablePath = Path.Combine(CnLauncherVersion, "xstarter.exe"),
        WorkingDirectoryRole = "LauncherRoot",
        ArgumentPresetId = CnWindows131ArgumentPresetId,
        ArgumentList = new[] { "-skiplauncher" },
        MaximumCapability = LaunchCapability.OfficialAssisted,
        ExecutionEnabled = false,
        RequiredComponents = new[]
        {
            new BinaryIdentityRequirement
            {
                ComponentId = "official-launcher",
                RelativePath = "launcher.exe",
                ExpectedSha256 = "8CB1A9BB25EBB1F0173BDF7E7BCB57992FF0BBB206BBDDA87B9D66FA90EFD0B6",
                ExpectedFileVersion = "1.3.1",
                ExpectedProductVersion = "1.3.1",
                ExpectedSignerThumbprint = "6A57615C8DBED53A8BE5FF2533535AAEEAA1015A",
            },
            new BinaryIdentityRequirement
            {
                ComponentId = "official-backend",
                RelativePath = Path.Combine(CnLauncherVersion, "xstarter.exe"),
                ExpectedSha256 = "56E26684ACC9121330BA43B674A1B340493D318F677F4AB6B951A19A2D00CEBF",
                ExpectedFileVersion = "1.3.1",
                ExpectedProductVersion = "1.3.1",
                ExpectedSignerThumbprint = "6A57615C8DBED53A8BE5FF2533535AAEEAA1015A",
            },
            new BinaryIdentityRequirement
            {
                ComponentId = "game-bootstrap",
                RootRole = "GameRoot",
                RelativePath = "InfinityNikki.exe",
                ExpectedSha256 = "A0372297E3887C312145468878501108D977890983590DEFBBFE95DEEC3ACB8D",
                ExpectedSignerThumbprint = "6A57615C8DBED53A8BE5FF2533535AAEEAA1015A",
            },
            new BinaryIdentityRequirement
            {
                ComponentId = "game-client",
                RootRole = "GameRoot",
                RelativePath = Path.Combine(
                    "X6Game",
                    "Binaries",
                    "Win64",
                    "X6Game-Win64-Shipping.exe"),
                ExpectedSha256 = "4BDE53DAD10F8DCB68C7588CE832B8508B8CD206FB55924D0F15FB4A3BF215F4",
                ExpectedFileVersion = "2,8,1,2828",
                ExpectedProductVersion = "UE5-CL-0",
                ExpectedSignerThumbprint = "6A57615C8DBED53A8BE5FF2533535AAEEAA1015A",
            },
            new BinaryIdentityRequirement
            {
                ComponentId = "anti-cheat-artifact",
                RootRole = "GameRoot",
                RelativePath = Path.Combine(
                    "X6Game",
                    "Binaries",
                    "Win64",
                    "AntiCheatExpert",
                    "ACE-Service64.exe"),
                ExpectedSha256 = "8B13CF6329F21A97C8AE5934975CF7F91A84B68E802BA70E4525E7418224E7D5",
                ExpectedFileVersion = "24.0.2510.212",
                ExpectedProductVersion = "24.0.2510.212",
                ExpectedSignerThumbprint = "30D53B8278ADEDA1FF20520CDCCCBB5766141466",
            },
        },
        ProductMarker = new ProductMarkerRequirement
        {
            RootRole = "GameRoot",
            RelativePath = "product.db",
            ExpectedName = "InfinityNikki Launcher",
        },
    };

    public static bool TryGet(
        DistributionChannel channel,
        string versionDirectoryName,
        out LaunchProviderContract contract)
    {
        if (channel is DistributionChannel.Official &&
            string.Equals(versionDirectoryName, CnLauncherVersion, StringComparison.OrdinalIgnoreCase))
        {
            contract = CnWindows131;
            return true;
        }

        contract = null!;
        return false;
    }
}
