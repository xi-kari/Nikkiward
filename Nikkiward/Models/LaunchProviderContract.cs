using System.Text.RegularExpressions;

namespace Nikkiward.Models;

/// <summary>
/// One trusted publisher identity. A component passes the signer gate when the
/// leaf certificate thumbprint appears in <see cref="Thumbprints"/>, or — so a
/// routine certificate rotation does not brick every install — when the
/// certificate subject carries the pinned CommonName (and SerialNumber, when
/// one is pinned). Subject matching is whole-value equality, never a substring
/// match, and only runs after WinVerifyTrust reported a valid chain.
/// </summary>
public sealed record TrustedSignerRequirement
{
    public string DisplayName { get; init; } = string.Empty;

    public IReadOnlyList<string> Thumbprints { get; init; } = Array.Empty<string>();

    public string? SubjectCommonName { get; init; }

    /// <summary>
    /// The subject serialNumber attribute (OID 2.5.4.5). For CN publishers this
    /// is the unified social credit code, which survives certificate renewals.
    /// </summary>
    public string? SubjectSerialNumber { get; init; }
}

/// <summary>
/// Structural + publisher identity requirement for one component. Since the
/// adaptive contract (ContractVersion 2) this deliberately pins no hash and no
/// file version: exact-content pinning broke every install on every game patch
/// day. Observed hash/version values are still recorded on the receipts for
/// diagnostics.
/// </summary>
public sealed record BinaryIdentityRequirement
{
    public string ComponentId { get; init; } = string.Empty;

    public string RootRole { get; init; } = "LauncherRoot";

    public string RelativePath { get; init; } = string.Empty;

    public AuthenticodeSignatureStatus ExpectedSignature { get; init; } =
        AuthenticodeSignatureStatus.Valid;

    public IReadOnlyList<TrustedSignerRequirement> TrustedSigners { get; init; } =
        Array.Empty<TrustedSignerRequirement>();

    /// <summary>
    /// Version-resource OriginalFilename pin, used only for the launcher-side
    /// binaries Nikkiward itself elevates or spawns from. This is a build
    /// constant (not a per-version value), and it stops a *different* trusted
    /// publisher binary from being renamed into the elevated slot. The game
    /// executables stay unpinned: InfinityNikki.exe ships no version resource.
    /// </summary>
    public string? ExpectedOriginalFilename { get; init; }
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
    /// <summary>
    /// Historical id, frozen on purpose: the coordinator, the preflight
    /// verifier and the launch button all gate on exact equality with this
    /// value. The contract adapts to any launcher version directory since
    /// ContractVersion 2; only the name still says "131".
    /// </summary>
    public const string CnWindows131ContractId = "OfficialXStarterSkipLauncherCn131";

    public const string CnWindows131ArgumentPresetId = "cn-win-xstarter-skiplauncher-v1";

    /// <summary>
    /// Fallback version directory for callers that carry no version context
    /// (legacy 3-argument verifier calls, test fixtures). Discovery always
    /// passes the version directory it actually found on disk.
    /// </summary>
    public const string CnLauncherVersion = "1.3.1";

    // Same shape as WindowsInstallationProfileBuilder.VersionDirectoryPattern:
    // exactly three numeric segments; prerelease suffixes and 4-part versions
    // never appear as xstarter directories and stay rejected.
    private static readonly Regex CnVersionDirectoryPattern = new(
        "^\\d+\\.\\d+\\.\\d+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Signs launcher.exe, xstarter.exe, InfinityNikki.exe and the shipping
    /// exe. The serial number is the unified social credit code of
    /// 苏州暖暖 (Papergames), stable across certificate renewals.
    /// </summary>
    public static TrustedSignerRequirement PapergamesSigner { get; } = new()
    {
        DisplayName = "Suzhou Nikki Co.,Ltd.",
        // 6850080C… is the 2026-10 renewal (valid to 2028-01) that signs launcher 1.3.2;
        // same subject, serial number and DigiCert G4 code-signing CA as 6A57615C….
        Thumbprints = ["6A57615C8DBED53A8BE5FF2533535AAEEAA1015A", "6850080CB455A1BEF3462DA49073B6D3A25E6D86"],
        SubjectCommonName = "Suzhou Nikki Co.,Ltd.",
        SubjectSerialNumber = "91320594076301379M",
    };

    /// <summary>Signs the AntiCheatExpert service artifact.</summary>
    public static TrustedSignerRequirement AntiCheatExpertSigner { get; } = new()
    {
        DisplayName = "ACEVILLE PTE LTD",
        Thumbprints = ["30D53B8278ADEDA1FF20520CDCCCBB5766141466"],
        SubjectCommonName = "ACEVILLE PTE LTD",
    };

    public static LaunchProviderContract CnWindows131 { get; } = CreateCnWindows(CnLauncherVersion);

    public static bool IsCnVersionDirectoryName(string? versionDirectoryName) =>
        !string.IsNullOrWhiteSpace(versionDirectoryName) &&
        CnVersionDirectoryPattern.IsMatch(versionDirectoryName);

    /// <summary>
    /// Materializes the CN contract for the version directory discovery found
    /// on disk. Everything except the two version-derived relative paths is a
    /// frozen constant; see LAUNCH_CONTRACT.md §6 before changing any of it.
    /// </summary>
    public static LaunchProviderContract CreateCnWindows(string versionDirectoryName)
    {
        if (!IsCnVersionDirectoryName(versionDirectoryName))
        {
            throw new ArgumentException(
                $"'{versionDirectoryName}' is not a three-part numeric version directory name.",
                nameof(versionDirectoryName));
        }

        var backendRelativePath = Path.Combine(versionDirectoryName, "xstarter.exe");
        return new LaunchProviderContract
        {
            ContractId = CnWindows131ContractId,
            ContractVersion = 2,
            RegionFamily = RegionFamily.MainlandChina,
            DistributionChannel = DistributionChannel.Official,
            Platform = "WindowsNative",
            BackendRelativeExecutablePath = backendRelativePath,
            WorkingDirectoryRole = "LauncherRoot",
            ArgumentPresetId = CnWindows131ArgumentPresetId,
            ArgumentList = new[] { "-skiplauncher" },
            MaximumCapability = LaunchCapability.OfficialAssisted,
            ExecutionEnabled = false,
            RequiredComponents = CreateComponentRequirements(backendRelativePath),
            ProductMarker = new ProductMarkerRequirement
            {
                RootRole = "GameRoot",
                RelativePath = "product.db",
                ExpectedName = "InfinityNikki Launcher",
            },
        };
    }

    internal static IReadOnlyList<BinaryIdentityRequirement> CreateComponentRequirements(string backendRelativePath) =>
        new[]
        {
            new BinaryIdentityRequirement
            {
                ComponentId = "official-launcher",
                RelativePath = "launcher.exe",
                TrustedSigners = [PapergamesSigner],
                ExpectedOriginalFilename = "launcher.exe",
            },
            new BinaryIdentityRequirement
            {
                ComponentId = "official-backend",
                RelativePath = backendRelativePath,
                TrustedSigners = [PapergamesSigner],
                ExpectedOriginalFilename = "xstarter.exe",
            },
            new BinaryIdentityRequirement
            {
                ComponentId = "game-bootstrap",
                RootRole = "GameRoot",
                RelativePath = "InfinityNikki.exe",
                TrustedSigners = [PapergamesSigner],
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
                TrustedSigners = [PapergamesSigner],
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
                TrustedSigners = [AntiCheatExpertSigner],
            },
        };

    public static bool TryGet(
        DistributionChannel channel,
        string versionDirectoryName,
        out LaunchProviderContract contract)
    {
        if (channel is DistributionChannel.Official &&
            IsCnVersionDirectoryName(versionDirectoryName))
        {
            contract = CreateCnWindows(versionDirectoryName);
            return true;
        }

        contract = null!;
        return false;
    }
}
