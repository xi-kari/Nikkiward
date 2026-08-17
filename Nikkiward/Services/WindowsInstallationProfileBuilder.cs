using System.Text.RegularExpressions;
using Nikkiward.Models;

namespace Nikkiward.Services;

public interface IInstallationProfileBuilder
{
    Task<ProfileBuildResult> BuildAsync(
        ProfileBuildRequest request,
        CancellationToken cancellationToken = default);
}

public interface IInstallationDiscoveryService
{
    Task<ProfileBuildResult> DiscoverAsync(CancellationToken cancellationToken = default);

    Task<ProfileBuildResult> DiscoverFromManualGameRootAsync(
        string selectedDirectory,
        string? selectedLauncherRoot = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds diagnostic profiles from installation evidence. It does not create a
/// ProcessStartInfo and does not launch any process.
/// </summary>
public sealed class WindowsInstallationProfileBuilder :
    IInstallationProfileBuilder,
    IInstallationDiscoveryService
{
    private const string SteamAppId = "3164330";
    private const string ChinaMarkerName = "InfinityNikki Launcher";
    private const string BilibiliMarkerName = "InfinityNikkiBili Launcher";
    private const string SteamMarkerName = "InfinityNikkiSteam Launcher";
    private static readonly Regex VersionDirectoryPattern = new(
        "^\\d+\\.\\d+\\.\\d+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IWindowsInstallationPathSource _pathSource;
    private readonly string _localApplicationDataPath;

    public WindowsInstallationProfileBuilder(
        IWindowsInstallationPathSource? pathSource = null,
        string? localApplicationDataPath = null)
    {
        _pathSource = pathSource ?? new WindowsInstallationPathSource();
        _localApplicationDataPath = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.GetFullPath(localApplicationDataPath);
    }

    public Task<ProfileBuildResult> DiscoverAsync(CancellationToken cancellationToken = default) =>
        BuildAsync(
            new ProfileBuildRequest
            {
                Channel = DistributionChannel.Unknown,
                AllowAutomaticDiscovery = true,
            },
            cancellationToken);

    public Task<ProfileBuildResult> DiscoverFromManualGameRootAsync(
        string selectedDirectory,
        string? selectedLauncherRoot = null,
        CancellationToken cancellationToken = default) =>
        BuildAsync(
            new ProfileBuildRequest
            {
                Channel = DistributionChannel.Unknown,
                ManualGameRootPath = selectedDirectory,
                ManualLauncherRootPath = selectedLauncherRoot,
                AllowAutomaticDiscovery = true,
            },
            cancellationToken);

    public async Task<ProfileBuildResult> BuildAsync(
        ProfileBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await Task.Run(
            () => BuildCore(request, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private ProfileBuildResult BuildCore(
        ProfileBuildRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return request.Channel switch
        {
            DistributionChannel.Official =>
                new ProfileBuildResult
                {
                    Candidates = BuildChinaCandidates(request, cancellationToken),
                },
            DistributionChannel.Bilibili =>
                new ProfileBuildResult
                {
                    Candidates = BuildBilibiliCandidates(request, cancellationToken),
                },
            DistributionChannel.Steam =>
                new ProfileBuildResult
                {
                    Candidates = BuildSteamCandidates(request, cancellationToken),
                },
            DistributionChannel.Unknown => BuildAllCandidates(request, cancellationToken),
            _ => new ProfileBuildResult
            {
                Candidates =
                [
                    FailureCandidate(
                        request,
                        ProfileBuildFailureCode.InvalidRequest,
                        "不支持的发行渠道。"),
                ],
            },
        };
    }

    private ProfileBuildResult BuildAllCandidates(
        ProfileBuildRequest request,
        CancellationToken cancellationToken)
    {
        var candidates = new List<InstallationProfileCandidate>();
        candidates.AddRange(BuildChinaCandidates(request with { Channel = DistributionChannel.Official }, cancellationToken));
        candidates.AddRange(BuildBilibiliCandidates(request with { Channel = DistributionChannel.Bilibili }, cancellationToken));
        candidates.AddRange(BuildSteamCandidates(request with { Channel = DistributionChannel.Steam }, cancellationToken));
        return new ProfileBuildResult { Candidates = candidates };
    }

    private IReadOnlyList<InstallationProfileCandidate> BuildChinaCandidates(
        ProfileBuildRequest request,
        CancellationToken cancellationToken)
    {
        var launcherRoots = ResolveLauncherRoots(request);
        if (launcherRoots.Count == 0)
        {
            return
            [
                FailureCandidate(
                    request,
                    ProfileBuildFailureCode.LauncherRootNotFound,
                    "未找到国服 launcher 根目录；请手动选择游戏目录和 launcher 根目录。"),
            ];
        }

        if (string.IsNullOrWhiteSpace(request.ManualLauncherRootPath) && launcherRoots.Count > 1)
        {
            var validRoots = launcherRoots.Where(HasLauncherLayout).ToArray();
            if (validRoots.Length > 1)
            {
                return
                [
                    FailureCandidate(
                        request,
                        ProfileBuildFailureCode.AmbiguousLauncherRoot,
                        "发现多个完整 launcher 根目录；未自动猜测其中一个。"),
                ];
            }

            launcherRoots = validRoots.Length == 1 ? validRoots : launcherRoots;
        }

        var gameRoots = ResolveChinaGameRoots(request, launcherRoots);
        if (gameRoots.Count == 0)
        {
            return
            [
                FailureCandidate(
                    request,
                    ProfileBuildFailureCode.GameRootNotFound,
                    "未找到国服游戏根目录；请在 Nikkiward 中选择包含 product.db 的 InfinityNikki 目录。"),
            ];
        }

        if (string.IsNullOrWhiteSpace(request.ManualGameRootPath) && gameRoots.Count > 1)
        {
            return
            [
                FailureCandidate(
                    request,
                    ProfileBuildFailureCode.AmbiguousGameRoot,
                    "发现多个候选游戏根目录；未自动合并或猜测。"),
            ];
        }

        var candidates = new List<InstallationProfileCandidate>();
        foreach (var launcherRoot in launcherRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var versions = FindVersionDirectories(launcherRoot);
            if (versions.Count == 0)
            {
                continue;
            }

            foreach (var version in versions)
            {
                foreach (var gameRoot in gameRoots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    candidates.Add(BuildChinaCandidate(
                        request,
                        launcherRoot,
                        version,
                        gameRoot));
                }
            }
        }

        return candidates.Count == 0
            ?
            [
                FailureCandidate(
                    request,
                    ProfileBuildFailureCode.BackendRootNotFound,
                    "launcher 根目录存在，但没有可识别的数字版本 xstarter 目录。"),
            ]
            : candidates;
    }

    private InstallationProfileCandidate BuildChinaCandidate(
        ProfileBuildRequest request,
        string launcherRoot,
        VersionDirectory version,
        string gameRoot)
    {
        var observedAtUtc = DateTimeOffset.UtcNow;
        var contractExists = LaunchProviderCatalog.TryGet(
            DistributionChannel.Official,
            version.Name,
            out var contract);

        var launcherPath = Path.Combine(launcherRoot, "launcher.exe");
        var xstarterPath = Path.Combine(launcherRoot, version.Name, "xstarter.exe");
        var gameExecutablePath = Path.Combine(gameRoot, "InfinityNikki.exe");
        var shippingExecutablePath = Path.Combine(
            gameRoot,
            "X6Game",
            "Binaries",
            "Win64",
            "X6Game-Win64-Shipping.exe");
        var antiCheatPath = Path.Combine(
            gameRoot,
            "X6Game",
            "Binaries",
            "Win64",
            "AntiCheatExpert",
            "ACE-Service64.exe");
        var productMarkerPath = Path.Combine(gameRoot, "product.db");
        var marker = ProductMarkerReader.TryRead(productMarkerPath);

        var missing = new[]
        {
            ("launcher.exe", launcherPath),
            ("xstarter.exe", xstarterPath),
            ("InfinityNikki.exe", gameExecutablePath),
            ("X6Game-Win64-Shipping.exe", shippingExecutablePath),
            ("ACE-Service64.exe", antiCheatPath),
            ("product.db", productMarkerPath),
        }
        .Where(item => !File.Exists(item.Item2))
        .Select(item => item.Item1)
        .ToArray();

        var profileId = string.IsNullOrWhiteSpace(request.ProfileId)
            ? $"infinity-nikki-cn-{version.Name}"
            : request.ProfileId!;
        var profile = new LaunchProfile
        {
            ProfileId = profileId,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? "无限暖暖 · 国服"
                : request.DisplayName!,
            Channel = "CN",
            GameRootPath = gameRoot,
            LauncherPath = launcherPath,
            XStarterPath = xstarterPath,
            GameExecutablePath = gameExecutablePath,
            ShippingExecutablePath = shippingExecutablePath,
            AntiCheatExecutablePath = antiCheatPath,
            Capability = LaunchCapability.NotVerified,
        };

        var discoverySource = request.ManualGameRootPath is not null ||
                              request.ManualLauncherRootPath is not null
            ? ProfileDiscoverySource.ManualSelection
            : LauncherConfigReader.TryReadGameDirectory(GetLauncherConfigPath()) is not null
                ? ProfileDiscoverySource.LauncherConfig
                : ProfileDiscoverySource.LauncherRegistry;

        if (marker is null)
        {
            return new InstallationProfileCandidate
            {
                ProfileId = profileId,
                DisplayName = profile.DisplayName,
                Identity = ChinaIdentity(),
                State = InstallationCandidateState.Incomplete,
                DiscoverySource = discoverySource,
                LauncherRootPath = launcherRoot,
                GameRootPath = gameRoot,
                Profile = profile,
                MissingComponents = missing,
                FailureCode = missing.Contains("product.db", StringComparer.OrdinalIgnoreCase)
                    ? ProfileBuildFailureCode.ChannelMarkerMissing
                    : ProfileBuildFailureCode.LayoutMismatch,
                Detail = "未能读取国服 product.db marker；不会根据目录名猜渠道。",
                ObservedAtUtc = observedAtUtc,
            };
        }

        if (!string.Equals(marker.Name, ChinaMarkerName, StringComparison.OrdinalIgnoreCase))
        {
            return new InstallationProfileCandidate
            {
                ProfileId = profileId,
                DisplayName = profile.DisplayName,
                Identity = ChinaIdentity(),
                State = InstallationCandidateState.Unsupported,
                DiscoverySource = discoverySource,
                LauncherRootPath = launcherRoot,
                GameRootPath = gameRoot,
                Profile = profile,
                MissingComponents = missing,
                FailureCode = ProfileBuildFailureCode.ChannelMarkerMismatch,
                Detail = "product.db marker 与 CN contract 不匹配；未跨渠道重用。",
                ObservedAtUtc = observedAtUtc,
            };
        }

        if (missing.Length > 0)
        {
            return new InstallationProfileCandidate
            {
                ProfileId = profileId,
                DisplayName = profile.DisplayName,
                Identity = ChinaIdentity(),
                State = InstallationCandidateState.Incomplete,
                DiscoverySource = discoverySource,
                LauncherRootPath = launcherRoot,
                GameRootPath = gameRoot,
                Profile = profile,
                MissingComponents = missing,
                FailureCode = ProfileBuildFailureCode.LayoutMismatch,
                Detail = $"缺少 {missing.Length} 个 contract 特征文件；仅生成诊断 profile。",
                ObservedAtUtc = observedAtUtc,
            };
        }

        if (!contractExists)
        {
            return new InstallationProfileCandidate
            {
                ProfileId = profileId,
                DisplayName = profile.DisplayName,
                Identity = ChinaIdentity(),
                State = InstallationCandidateState.Unsupported,
                DiscoverySource = discoverySource,
                LauncherRootPath = launcherRoot,
                GameRootPath = gameRoot,
                Profile = profile,
                FailureCode = ProfileBuildFailureCode.UnsupportedVersion,
                Detail = $"发现版本目录 {version.Name}，但没有对应的冻结 provider contract。",
                ObservedAtUtc = observedAtUtc,
            };
        }

        return new InstallationProfileCandidate
        {
            ProfileId = profileId,
            DisplayName = profile.DisplayName,
            Identity = ChinaIdentity(),
            State = InstallationCandidateState.ReadyForStaticVerification,
            DiscoverySource = discoverySource,
            LauncherRootPath = launcherRoot,
            GameRootPath = gameRoot,
            Profile = profile,
            Provider = new LaunchProviderBinding
            {
                ProviderId = contract.ContractId,
                ContractVersion = contract.ContractVersion,
                BackendExecutablePath = xstarterPath,
                WorkingDirectory = launcherRoot,
                ArgumentPresetId = contract.ArgumentPresetId,
                ArgumentList = contract.ArgumentList,
                MaximumCapability = contract.MaximumCapability,
                ExecutionEnabled = contract.ExecutionEnabled,
            },
            Detail = "profile 已由安装布局构建；仍需执行前身份 verifier，ExecutionEnabled=false。",
            ObservedAtUtc = observedAtUtc,
        };
    }

    private IReadOnlyList<InstallationProfileCandidate> BuildBilibiliCandidates(
        ProfileBuildRequest request,
        CancellationToken cancellationToken)
    {
        var launcherRoots = ResolveBilibiliLauncherRoots(request);
        if (launcherRoots.Count == 0)
        {
            return
            [
                FailureCandidate(
                    request,
                    ProfileBuildFailureCode.LauncherRootNotFound,
                    "未找到 B 服 launcher 根目录；请手动选择游戏目录和 launcher 根目录。"),
            ];
        }

        if (string.IsNullOrWhiteSpace(request.ManualLauncherRootPath) && launcherRoots.Count > 1)
        {
            var validRoots = launcherRoots.Where(HasLauncherLayout).ToArray();
            if (validRoots.Length > 1)
            {
                return
                [
                    FailureCandidate(
                        request,
                        ProfileBuildFailureCode.AmbiguousLauncherRoot,
                        "发现多个完整 B 服 launcher 根目录；未自动猜测其中一个。"),
                ];
            }

            launcherRoots = validRoots.Length == 1 ? validRoots : launcherRoots;
        }

        var gameRoots = ResolveBilibiliGameRoots(request, launcherRoots);
        if (gameRoots.Count == 0)
        {
            return
            [
                FailureCandidate(
                    request,
                    ProfileBuildFailureCode.GameRootNotFound,
                    "未找到 B 服游戏根目录；请在 Nikkiward 中选择包含 product.db 的 InfinityNikkiBili 目录。"),
            ];
        }

        if (string.IsNullOrWhiteSpace(request.ManualGameRootPath) && gameRoots.Count > 1)
        {
            return
            [
                FailureCandidate(
                    request,
                    ProfileBuildFailureCode.AmbiguousGameRoot,
                    "发现多个 B 服游戏根候选；未自动合并或猜测。"),
            ];
        }

        var candidates = new List<InstallationProfileCandidate>();
        foreach (var launcherRoot in launcherRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var versions = FindVersionDirectories(launcherRoot);
            if (versions.Count == 0)
            {
                continue;
            }

            foreach (var version in versions)
            {
                foreach (var gameRoot in gameRoots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    candidates.Add(BuildBilibiliCandidate(request, launcherRoot, version, gameRoot));
                }
            }
        }

        return candidates.Count == 0
            ?
            [
                FailureCandidate(
                    request,
                    ProfileBuildFailureCode.BackendRootNotFound,
                    "B 服 launcher 根目录存在，但没有可识别的数字版本 xstarter 目录。"),
            ]
            : candidates;
    }

    private InstallationProfileCandidate BuildBilibiliCandidate(
        ProfileBuildRequest request,
        string launcherRoot,
        VersionDirectory version,
        string gameRoot)
    {
        var observedAtUtc = DateTimeOffset.UtcNow;
        var launcherPath = Path.Combine(launcherRoot, "launcher.exe");
        var xstarterPath = Path.Combine(launcherRoot, version.Name, "xstarter.exe");
        var gameExecutablePath = Path.Combine(gameRoot, "InfinityNikki.exe");
        var shippingExecutablePath = Path.Combine(
            gameRoot,
            "X6Game",
            "Binaries",
            "Win64",
            "X6Game-Win64-Shipping.exe");
        var antiCheatPath = Path.Combine(
            gameRoot,
            "X6Game",
            "Binaries",
            "Win64",
            "AntiCheatExpert",
            "ACE-Service64.exe");
        var productMarkerPath = Path.Combine(gameRoot, "product.db");
        var marker = ProductMarkerReader.TryRead(productMarkerPath);

        var missing = new[]
        {
            ("launcher.exe", launcherPath),
            ("xstarter.exe", xstarterPath),
            ("InfinityNikki.exe", gameExecutablePath),
            ("X6Game-Win64-Shipping.exe", shippingExecutablePath),
            ("ACE-Service64.exe", antiCheatPath),
            ("product.db", productMarkerPath),
        }
        .Where(item => !File.Exists(item.Item2))
        .Select(item => item.Item1)
        .ToArray();

        var profileId = string.IsNullOrWhiteSpace(request.ProfileId)
            ? $"infinity-nikki-bilibili-{version.Name}"
            : request.ProfileId!;
        var profile = new LaunchProfile
        {
            ProfileId = profileId,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? "无限暖暖 · B服"
                : request.DisplayName!,
            Channel = "Bilibili",
            GameRootPath = gameRoot,
            LauncherPath = launcherPath,
            XStarterPath = xstarterPath,
            GameExecutablePath = gameExecutablePath,
            ShippingExecutablePath = shippingExecutablePath,
            AntiCheatExecutablePath = antiCheatPath,
            Capability = LaunchCapability.NotVerified,
        };

        var discoverySource = request.ManualGameRootPath is not null ||
                              request.ManualLauncherRootPath is not null
            ? ProfileDiscoverySource.ManualSelection
            : LauncherConfigReader.TryReadGameDirectory(GetBilibiliLauncherConfigPath()) is not null
                ? ProfileDiscoverySource.LauncherConfig
                : ProfileDiscoverySource.LauncherRegistry;

        if (marker is null)
        {
            return new InstallationProfileCandidate
            {
                ProfileId = profileId,
                DisplayName = profile.DisplayName,
                Identity = BilibiliIdentity(),
                State = InstallationCandidateState.Incomplete,
                DiscoverySource = discoverySource,
                LauncherRootPath = launcherRoot,
                GameRootPath = gameRoot,
                Profile = profile,
                MissingComponents = missing,
                FailureCode = missing.Contains("product.db", StringComparer.OrdinalIgnoreCase)
                    ? ProfileBuildFailureCode.ChannelMarkerMissing
                    : ProfileBuildFailureCode.LayoutMismatch,
                Detail = "未能读取 B 服 product.db marker；不会根据目录名猜渠道。",
                ObservedAtUtc = observedAtUtc,
            };
        }

        if (!string.Equals(marker.Name, BilibiliMarkerName, StringComparison.OrdinalIgnoreCase))
        {
            return new InstallationProfileCandidate
            {
                ProfileId = profileId,
                DisplayName = profile.DisplayName,
                Identity = BilibiliIdentity(),
                State = InstallationCandidateState.Unsupported,
                DiscoverySource = discoverySource,
                LauncherRootPath = launcherRoot,
                GameRootPath = gameRoot,
                Profile = profile,
                MissingComponents = missing,
                FailureCode = ProfileBuildFailureCode.ChannelMarkerMismatch,
                Detail = "product.db marker 与 Bilibili 渠道不匹配；未跨渠道重用。",
                ObservedAtUtc = observedAtUtc,
            };
        }

        if (missing.Length > 0)
        {
            return new InstallationProfileCandidate
            {
                ProfileId = profileId,
                DisplayName = profile.DisplayName,
                Identity = BilibiliIdentity(),
                State = InstallationCandidateState.Incomplete,
                DiscoverySource = discoverySource,
                LauncherRootPath = launcherRoot,
                GameRootPath = gameRoot,
                Profile = profile,
                MissingComponents = missing,
                FailureCode = ProfileBuildFailureCode.LayoutMismatch,
                Detail = $"缺少 {missing.Length} 个 B 服布局特征文件；仅生成诊断 profile。",
                ObservedAtUtc = observedAtUtc,
            };
        }

        return new InstallationProfileCandidate
        {
            ProfileId = profileId,
            DisplayName = profile.DisplayName,
            Identity = BilibiliIdentity(),
            State = InstallationCandidateState.Candidate,
            DiscoverySource = discoverySource,
            LauncherRootPath = launcherRoot,
            GameRootPath = gameRoot,
            Profile = profile,
            Provider = null,
            FailureCode = ProfileBuildFailureCode.ProviderContractUnavailable,
            Detail = "B 服安装已发现，但当前没有经过验证的 provider contract；ExecutionEnabled=false。",
            ObservedAtUtc = observedAtUtc,
        };
    }

    private IReadOnlyList<InstallationProfileCandidate> BuildSteamCandidates(
        ProfileBuildRequest request,
        CancellationToken cancellationToken)
    {
        var roots = ResolveSteamRoots(request);
        var candidates = new List<InstallationProfileCandidate>();

        foreach (var steamRoot in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var libraryRoot in ResolveSteamLibraries(steamRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates.Add(BuildSteamCandidate(request, libraryRoot));
            }
        }

        return candidates.Count == 0
            ?
            [
                FailureCandidate(
                    request,
                    ProfileBuildFailureCode.SteamManifestMissing,
                    "未找到 Steam appmanifest_3164330.acf；Steam profile 保持 NotReady。"),
            ]
            : candidates;
    }

    private InstallationProfileCandidate BuildSteamCandidate(
        ProfileBuildRequest request,
        string libraryRoot)
    {
        var steamApps = Path.Combine(libraryRoot, "steamapps");
        var manifestPath = Path.Combine(steamApps, $"appmanifest_{SteamAppId}.acf");
        var stagingPath = Path.Combine(steamApps, "downloading", SteamAppId);
        var manifest = SteamManifestReader.TryRead(
            manifestPath,
            string.Empty,
            stagingPath);
        var installDirectoryName = manifest?.InstallDirectoryName;
        var commonRoot = string.IsNullOrWhiteSpace(installDirectoryName)
            ? Path.Combine(steamApps, "common", "Infinity Nikki")
            : Path.Combine(steamApps, "common", installDirectoryName);
        var gameRoot = Path.Combine(commonRoot, "InfinityNikki");
        var steamVersion = FindVersionDirectories(commonRoot).FirstOrDefault();
        var normalizedManifest = manifest is null
            ? null
            : manifest with
            {
                CommonInstallPath = commonRoot,
                StagingPath = stagingPath,
                IsCompleteInstall = IsSteamInstallComplete(manifest, commonRoot, gameRoot, steamVersion),
            };

        var profileId = string.IsNullOrWhiteSpace(request.ProfileId)
            ? "infinity-nikki-global-steam-windows"
            : request.ProfileId!;
        var hasCommonRoot = Directory.Exists(commonRoot);
        var profile = new LaunchProfile
        {
            ProfileId = profileId,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? "无限暖暖 · Steam 国际服"
                : request.DisplayName!,
            Channel = "Steam Global",
            GameRootPath = gameRoot,
            LauncherPath = Path.Combine(commonRoot, "launcher.exe"),
            XStarterPath = steamVersion is null
                ? string.Empty
                : Path.Combine(steamVersion.Path, "xstarter.exe"),
            GameExecutablePath = Path.Combine(gameRoot, "InfinityNikki.exe"),
            ShippingExecutablePath = Path.Combine(
                gameRoot,
                "X6Game",
                "Binaries",
                "Win64",
                "X6Game-Win64-Shipping.exe"),
            AntiCheatExecutablePath = Path.Combine(
                gameRoot,
                "X6Game",
                "Binaries",
                "Win64",
                "AntiCheatExpert",
                "ACE-Service64.exe"),
            Capability = LaunchCapability.NotVerified,
        };

        var identity = new ProfileIdentity
        {
            RegionFamily = RegionFamily.Overseas,
            DistributionChannel = DistributionChannel.Steam,
            AccountAuthority = AccountAuthority.Steam,
            SteamAppId = normalizedManifest?.AppId ?? SteamAppId,
            SteamSubId = normalizedManifest?.SubId,
            SteamDepotId = normalizedManifest?.DepotId,
            SteamBuildId = normalizedManifest?.BuildId,
            SteamManifestId = normalizedManifest?.ManifestId,
        };

        if (normalizedManifest is null)
        {
            return new InstallationProfileCandidate
            {
                ProfileId = profileId,
                DisplayName = profile.DisplayName,
                Identity = identity,
                State = Directory.Exists(stagingPath)
                    ? InstallationCandidateState.Downloading
                    : InstallationCandidateState.Incomplete,
                DiscoverySource = ProfileDiscoverySource.SteamRegistry,
                LauncherRootPath = commonRoot,
                GameRootPath = gameRoot,
                Profile = profile,
                FailureCode = ProfileBuildFailureCode.SteamManifestMissing,
                Detail = "Steam manifest 不存在；没有把 staging 目录当作正式安装。",
                ObservedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        if (!string.Equals(normalizedManifest.AppId, SteamAppId, StringComparison.OrdinalIgnoreCase) ||
            !normalizedManifest.IsCompleteInstall)
        {
            return new InstallationProfileCandidate
            {
                ProfileId = profileId,
                DisplayName = profile.DisplayName,
                Identity = identity,
                State = InstallationCandidateState.Downloading,
                DiscoverySource = ProfileDiscoverySource.SteamLibraryManifest,
                LauncherRootPath = commonRoot,
                GameRootPath = gameRoot,
                Profile = profile,
                SteamManifest = normalizedManifest,
                FailureCode = ProfileBuildFailureCode.SteamNotReady,
                Detail = hasCommonRoot
                    ? "Steam manifest 或安装内容尚未达到完整状态。"
                    : "Steam common 目录为空或不存在；downloading staging 不是正式游戏根。",
                ObservedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        return new InstallationProfileCandidate
        {
            ProfileId = profileId,
            DisplayName = profile.DisplayName,
            Identity = identity,
            State = InstallationCandidateState.Candidate,
            DiscoverySource = ProfileDiscoverySource.SteamLibraryManifest,
            LauncherRootPath = commonRoot,
            GameRootPath = gameRoot,
            Profile = profile,
            SteamManifest = normalizedManifest,
            FailureCode = ProfileBuildFailureCode.ProviderContractUnavailable,
            Detail = "Steam 国际服 Windows 安装已发现；尚无经验证的无启动器入口，不提交 Steam URI 或 CN/_SD 参数。",
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private IReadOnlyList<string> ResolveLauncherRoots(ProfileBuildRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ManualLauncherRootPath))
        {
            return NormalizeExistingDirectories([request.ManualLauncherRootPath!]);
        }

        return request.AllowAutomaticDiscovery
            ? NormalizeExistingDirectories(_pathSource.GetOfficialLauncherRootCandidates())
            : Array.Empty<string>();
    }

    private IReadOnlyList<string> ResolveBilibiliLauncherRoots(ProfileBuildRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ManualLauncherRootPath))
        {
            return NormalizeExistingDirectories([request.ManualLauncherRootPath!]);
        }

        var roots = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ManualGameRootPath))
        {
            foreach (var gameRoot in ExpandBilibiliGameRoot(request.ManualGameRootPath!))
            {
                var parent = Directory.GetParent(gameRoot)?.FullName;
                if (parent is not null)
                {
                    roots.Add(parent);
                }
            }
        }

        if (request.AllowAutomaticDiscovery)
        {
            roots.AddRange(_pathSource.GetBilibiliLauncherRootCandidates());
            var configured = LauncherConfigReader.TryReadGameDirectory(GetBilibiliLauncherConfigPath());
            if (configured is not null)
            {
                foreach (var gameRoot in ExpandBilibiliGameRoot(configured))
                {
                    var parent = Directory.GetParent(gameRoot)?.FullName;
                    if (parent is not null)
                    {
                        roots.Add(parent);
                    }
                }
            }
        }

        return NormalizeExistingDirectories(roots);
    }

    private IReadOnlyList<string> ResolveChinaGameRoots(
        ProfileBuildRequest request,
        IReadOnlyList<string> launcherRoots)
    {
        if (!string.IsNullOrWhiteSpace(request.ManualGameRootPath))
        {
            return NormalizeExistingDirectories(ExpandGameRoot(request.ManualGameRootPath!));
        }

        var roots = new List<string>();
        if (request.AllowAutomaticDiscovery)
        {
            var configured = LauncherConfigReader.TryReadGameDirectory(GetLauncherConfigPath());
            if (configured is not null)
            {
                roots.Add(configured);
            }

            foreach (var launcherRoot in launcherRoots)
            {
                roots.Add(Path.Combine(launcherRoot, "InfinityNikki"));
            }
        }

        return NormalizeExistingDirectories(roots);
    }

    private IReadOnlyList<string> ResolveBilibiliGameRoots(
        ProfileBuildRequest request,
        IReadOnlyList<string> launcherRoots)
    {
        if (!string.IsNullOrWhiteSpace(request.ManualGameRootPath))
        {
            return NormalizeExistingDirectories(ExpandBilibiliGameRoot(request.ManualGameRootPath!));
        }

        var roots = new List<string>();
        if (request.AllowAutomaticDiscovery)
        {
            var configured = LauncherConfigReader.TryReadGameDirectory(GetBilibiliLauncherConfigPath());
            if (configured is not null)
            {
                roots.Add(configured);
            }

            foreach (var launcherRoot in launcherRoots)
            {
                roots.Add(Path.Combine(launcherRoot, "InfinityNikkiBili"));
            }
        }

        return NormalizeExistingDirectories(roots);
    }

    private IReadOnlyList<string> ResolveSteamRoots(ProfileBuildRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ManualGameRootPath))
        {
            var gameRoot = NormalizeExistingDirectories(ExpandGameRoot(request.ManualGameRootPath!)).FirstOrDefault();
            if (gameRoot is not null)
            {
                var libraryRoot = FindSteamLibraryRoot(gameRoot);
                return libraryRoot is null ? Array.Empty<string>() : [libraryRoot];
            }
        }

        return request.AllowAutomaticDiscovery
            ? NormalizeExistingDirectories(_pathSource.GetSteamRootCandidates())
            : Array.Empty<string>();
    }

    private static string? FindSteamLibraryRoot(string path)
    {
        var current = new DirectoryInfo(path);
        while (current is not null &&
               !string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
        {
            current = current.Parent;
        }

        return current?.Parent?.FullName;
    }

    private IReadOnlyList<string> ResolveSteamLibraries(string steamRoot)
    {
        var libraries = new List<string> { steamRoot };
        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath))
        {
            try
            {
                libraries.AddRange(SteamLibraryVdfReader.ReadLibraryPaths(File.ReadAllText(vdfPath)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return NormalizeExistingDirectories(libraries);
    }

    private string GetLauncherConfigPath() =>
        string.IsNullOrWhiteSpace(_localApplicationDataPath)
            ? string.Empty
            : Path.Combine(_localApplicationDataPath, "InfinityNikki Launcher", "config.ini");

    private string GetBilibiliLauncherConfigPath() =>
        string.IsNullOrWhiteSpace(_localApplicationDataPath)
            ? string.Empty
            : Path.Combine(_localApplicationDataPath, "InfinityNikkiBili Launcher", "config.ini");

    private static bool HasLauncherLayout(string root) =>
        File.Exists(Path.Combine(root, "launcher.exe")) &&
        FindVersionDirectories(root).Count > 0;

    private static IReadOnlyList<VersionDirectory> FindVersionDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<VersionDirectory>();
        }

        try
        {
            return Directory.GetDirectories(root)
                .Select(path => new VersionDirectory(Path.GetFileName(path), path))
                .Where(version => VersionDirectoryPattern.IsMatch(version.Name))
                .Where(version => File.Exists(Path.Combine(version.Path, "xstarter.exe")))
                .OrderByDescending(version => ParseVersion(version.Name))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<VersionDirectory>();
        }
    }

    private static Version ParseVersion(string value) =>
        Version.TryParse(value, out var version) ? version : new Version(0, 0, 0);

    private static IReadOnlyList<string> ExpandGameRoot(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized is null)
        {
            return Array.Empty<string>();
        }

        var candidates = new List<string> { normalized };
        var nested = Path.Combine(normalized, "InfinityNikki");
        if (Directory.Exists(nested))
        {
            candidates.Add(nested);
        }

        return candidates;
    }

    private static IReadOnlyList<string> ExpandBilibiliGameRoot(string path)
    {
        var normalized = NormalizePath(path);
        if (normalized is null)
        {
            return Array.Empty<string>();
        }

        var candidates = new List<string> { normalized };
        var nested = Path.Combine(normalized, "InfinityNikkiBili");
        if (Directory.Exists(nested))
        {
            candidates.Add(nested);
        }

        return candidates;
    }

    private static IReadOnlyList<string> NormalizeExistingDirectories(IEnumerable<string> paths) =>
        paths.Select(NormalizePath)
            .Where(path => path is not null && Directory.Exists(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim().Trim('"'))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsSteamInstallComplete(
        SteamManifestEvidence manifest,
        string commonRoot,
        string gameRoot,
        VersionDirectory? version)
    {
        var hasPositiveSize = manifest.SizeOnDisk is > 0;
        var hasBuild = !string.IsNullOrWhiteSpace(manifest.BuildId) &&
                       !string.Equals(manifest.BuildId, "0", StringComparison.OrdinalIgnoreCase);
        var hasDepot = manifest.InstalledDepotIds.Count > 0;
        var hasLayout = Directory.Exists(commonRoot) &&
                        Directory.Exists(gameRoot) &&
                        File.Exists(Path.Combine(commonRoot, "launcher.exe")) &&
                        version is not null &&
                        File.Exists(Path.Combine(version.Path, "xstarter.exe")) &&
                        File.Exists(Path.Combine(gameRoot, "product.db")) &&
                        File.Exists(Path.Combine(gameRoot, "InfinityNikki.exe")) &&
                        File.Exists(Path.Combine(
                            gameRoot,
                            "X6Game",
                            "Binaries",
                            "Win64",
                            "X6Game-Win64-Shipping.exe"));
        return hasPositiveSize && hasBuild && hasDepot && hasLayout;
    }

    private static ProfileIdentity ChinaIdentity() => new()
    {
        RegionFamily = RegionFamily.MainlandChina,
        DistributionChannel = DistributionChannel.Official,
        AccountAuthority = AccountAuthority.Papergames,
    };

    private static ProfileIdentity BilibiliIdentity() => new()
    {
        RegionFamily = RegionFamily.MainlandChina,
        DistributionChannel = DistributionChannel.Bilibili,
        AccountAuthority = AccountAuthority.Bilibili,
    };

    private static InstallationProfileCandidate FailureCandidate(
        ProfileBuildRequest request,
        ProfileBuildFailureCode failureCode,
        string detail) => new()
        {
            ProfileId = request.ProfileId ?? string.Empty,
            DisplayName = request.DisplayName ?? string.Empty,
            Identity = new ProfileIdentity
            {
                DistributionChannel = request.Channel,
            },
            State = InstallationCandidateState.NotFound,
            DiscoverySource = request.ManualGameRootPath is not null
                ? ProfileDiscoverySource.ManualSelection
                : ProfileDiscoverySource.Unknown,
            FailureCode = failureCode,
            Detail = detail,
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };

    private sealed record VersionDirectory(string Name, string Path);
}
