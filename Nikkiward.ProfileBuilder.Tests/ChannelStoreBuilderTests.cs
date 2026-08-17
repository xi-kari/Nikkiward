using System.Text;
using Nikkiward.Models;
using Nikkiward.Services;

internal static class ChannelStoreBuilderTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("channel store dry-run requires all three channels", MissingChannelIsRejected),
        ("channel store materializes three roots from one shared object layer", BuildsAndRollsBackThreeChannelStore),
    ];

    private static async Task MissingChannelIsRejected()
    {
        using var fixture = new StoreFixture();
        var builder = new WindowsChannelStoreBuilder();
        var plan = await builder.CreatePlanAsync(new ChannelStoreBuildRequest
        {
            Candidates = [fixture.OfficialCandidate, fixture.SteamCandidate],
            StoreRootPath = fixture.StoreRoot,
        });
        Assert(!plan.CanExecute, "a two-channel store must fail closed");
        AssertEqual(ChannelStoreFailureCode.MissingChannel, plan.FailureCode, "missing channel code");
        Assert(!Directory.Exists(fixture.StoreRoot), "dry-run rejection must not create the store root");
    }

    private static async Task BuildsAndRollsBackThreeChannelStore()
    {
        using var fixture = new StoreFixture();
        var builder = new WindowsChannelStoreBuilder();
        var plan = await builder.CreatePlanAsync(new ChannelStoreBuildRequest
        {
            Candidates =
            [
                fixture.OfficialCandidate,
                fixture.BilibiliCandidate,
                fixture.SteamCandidate,
            ],
            StoreRootPath = fixture.StoreRoot,
        });

        Assert(plan.CanExecute, $"three-channel plan should pass: {plan.FailureCode} {plan.FailureDetail}");
        AssertEqual(3, plan.Variants.Count, "variant count");
        Assert(plan.HardLinkBytes > 0, "same-volume shared files should plan hard links");
        Assert(!Directory.Exists(fixture.StoreRoot), "dry-run must not write the store root");
        var longOverlayImport = plan.Imports.Single(item =>
            string.Equals(
                item.SourcePath,
                Path.Combine(fixture.BilibiliRoot, StoreFixture.LongOverlayRelativePath),
                StringComparison.OrdinalIgnoreCase));
        AssertEqual(
            ChannelStoreImportAction.CreateHardLink,
            longOverlayImport.Action,
            "long overlay import action");
        Assert(longOverlayImport.SourcePath.Length > 260, "long overlay source must exceed MAX_PATH");
        Assert(longOverlayImport.DestinationPath.Length > 260, "long overlay import target must exceed MAX_PATH");

        var receipt = await builder.BuildAsync(plan, plan.PlanSha256);
        Assert(receipt.Succeeded, $"channel store build should pass: {receipt.FailureCode} {receipt.FailureDetail}");
        AssertEqual(3, receipt.Variants.Count, "materialized variant count");
        var official = plan.Variants.Single(item => item.Definition.VariantId == GameVariantId.MainlandOfficial);
        var bilibili = plan.Variants.Single(item => item.Definition.VariantId == GameVariantId.MainlandBilibili);
        var steam = plan.Variants.Single(item => item.Definition.VariantId == GameVariantId.GlobalSteam);
        Assert(!steam.UsesExistingTarget, "Steam must materialize inside the channel store");
        AssertPath(
            Path.Combine(fixture.StoreRoot, "profiles", "global-steam", "InfinityNikki"),
            steam.TargetGameRootPath,
            "Steam target root");
        AssertPath(
            Path.Combine(fixture.StoreRoot, "runtimes", "cn-bilibili", "1.3.1", "xstarter.exe"),
            bilibili.TargetXStarterPath,
            "Bilibili portable xstarter");
        Assert(File.Exists(official.TargetXStarterPath), "official portable xstarter should exist");
        Assert(File.Exists(bilibili.TargetXStarterPath), "Bilibili portable xstarter should exist");
        Assert(File.Exists(steam.TargetXStarterPath), "Steam portable xstarter should exist");
        Assert(File.Exists(Path.Combine(bilibili.TargetLauncherRootPath, "launcher.exe")), "Bilibili runtime should preserve launcher identity");
        Assert(!File.Exists(Path.Combine(bilibili.TargetLauncherRootPath, "uninst.exe")), "portable runtime must omit the uninstaller");
        AssertEqual(6, receipt.SourceRootsEligibleForManualCleanup.Count, "cleanup source root count");

        var officialEngine = Path.Combine(official.TargetGameRootPath, "Engine", "common.bin");
        var bilibiliEngine = Path.Combine(bilibili.TargetGameRootPath, "Engine", "common.bin");
        var steamEngine = Path.Combine(steam.TargetGameRootPath, "Engine", "common.bin");
        Assert(File.Exists(officialEngine), "official shared engine file should exist");
        Assert(File.Exists(bilibiliEngine), "Bilibili shared engine file should exist");
        Assert(File.Exists(steamEngine), "Steam shared engine file should exist");
        Assert(WindowsHardLinkIdentity.AreSameFile(officialEngine, steamEngine), "official and Steam targets should share physical identity");
        Assert(WindowsHardLinkIdentity.AreSameFile(bilibiliEngine, steamEngine), "Bilibili and Steam targets should share physical identity");

        var officialPak = Path.Combine(official.TargetGameRootPath, "X6Game", "Saved", "Paks", "same.ucas");
        var steamPak = Path.Combine(steam.TargetGameRootPath, "X6Game", "Content", "Paks", "same.ucas");
        Assert(WindowsHardLinkIdentity.AreSameFile(officialPak, steamPak), "different package layouts should share one physical file");
        var longSharedTarget = Path.Combine(official.TargetGameRootPath, StoreFixture.LongSharedRelativePath);
        var longOverlayTarget = Path.Combine(bilibili.TargetGameRootPath, StoreFixture.LongOverlayRelativePath);
        Assert(longSharedTarget.Length > 260, "shared Store target must exceed MAX_PATH");
        Assert(longOverlayTarget.Length > 260, "overlay Store target must exceed MAX_PATH");
        Assert(File.Exists(longSharedTarget), "long shared Store target should exist");
        Assert(File.Exists(longOverlayTarget), "long overlay Store target should exist");
        Assert(File.Exists(longOverlayImport.DestinationPath), "long overlay import should exist");
        Assert(
            WindowsHardLinkIdentity.AreSameFile(
                longOverlayImport.SourcePath,
                longOverlayImport.DestinationPath),
            "long overlay import should share the source file identity");
        Assert(
            WindowsHardLinkIdentity.AreSameFile(
                longSharedTarget,
                Path.Combine(steam.TargetGameRootPath, StoreFixture.LongSharedRelativePath)),
            "long shared Store targets should share physical identity");
        Assert(!File.Exists(Path.Combine(official.TargetGameRootPath, "X6Game", "Saved", "Logs", "session.log")), "mutable session data must not migrate");
        Assert(!File.Exists(Path.Combine(official.TargetGameRootPath, "DownloadCache", "update.part")), "download cache must not migrate");
        Assert(!File.Exists(Path.Combine(official.TargetGameRootPath, "NikkiGallery", "node_modules", "tool.js")), "local gallery tool must not migrate");
        Assert(
            official.Manifest.Entries.All(entry =>
                !entry.TargetRelativePath.StartsWith("DownloadCache", StringComparison.OrdinalIgnoreCase) &&
                !entry.TargetRelativePath.StartsWith("NikkiGallery", StringComparison.OrdinalIgnoreCase)),
            "excluded top-level tools and caches must not enter the manifest");
        Assert(File.Exists(Path.Combine(plan.StoreRootPath, "manifests", "cn-official.json")), "official manifest should persist");
        Assert(File.Exists(Path.Combine(plan.StoreRootPath, "receipts", receipt.ReceiptId + ".json")), "build receipt should persist");
        var storedBilibili = new ChannelStoreProfileSettings
        {
            ProfileId = fixture.BilibiliCandidate.ProfileId,
            DistributionChannel = DistributionChannel.Bilibili,
            GameRootPath = bilibili.TargetGameRootPath,
            LauncherRootPath = bilibili.TargetLauncherRootPath,
            XStarterPath = bilibili.TargetXStarterPath,
        };
        var storeSettings = new ChannelStoreSettings
        {
            StoreRootPath = plan.StoreRootPath,
            LastReceiptId = receipt.ReceiptId,
            LastPlanSha256 = plan.PlanSha256,
            Profiles = [storedBilibili],
        };
        Assert(ChannelStoreReceiptVerifier.Verify(storeSettings, storedBilibili), "Bilibili store receipt should verify");
        Assert(
            !ChannelStoreReceiptVerifier.Verify(
                storeSettings with { LastPlanSha256 = new string('0', 64) },
                storedBilibili),
            "tampered store plan hash must be rejected");

        fixture.DeleteSources();
        Assert(File.Exists(officialEngine), "official target must survive source deletion");
        Assert(File.Exists(bilibiliEngine), "Bilibili target must survive source deletion");
        Assert(File.Exists(steamEngine), "Steam target must survive source deletion");
        Assert(File.Exists(bilibili.TargetXStarterPath), "Bilibili runtime must survive source deletion");
        var portableBilibili = MoveCandidateToStore(fixture.BilibiliCandidate, bilibili);
        var bilibiliLaunchPlan = new WindowsChannelEntryLauncher().CreatePlan(portableBilibili);
        Assert(bilibiliLaunchPlan.CanLaunch, $"portable Bilibili launch plan should pass: {bilibiliLaunchPlan.FailureCode}");
        AssertPath(bilibili.TargetXStarterPath, bilibiliLaunchPlan.FileName, "portable Bilibili launch executable");
        AssertPath(bilibili.TargetLauncherRootPath, bilibiliLaunchPlan.WorkingDirectory!, "portable Bilibili launch working directory");
        AssertEqual("-skiplauncher", bilibiliLaunchPlan.ArgumentList.Single(), "portable Bilibili launch argument");
        var portableSteam = MoveCandidateToStore(fixture.SteamCandidate, steam);
        var steamLaunchPlan = new WindowsChannelEntryLauncher().CreatePlan(portableSteam);
        Assert(steamLaunchPlan.CanLaunch, $"portable Steam launch plan should pass: {steamLaunchPlan.FailureCode}");
        AssertPath(steam.TargetXStarterPath, steamLaunchPlan.FileName, "portable Steam launch executable");
        AssertPath(steam.TargetLauncherRootPath, steamLaunchPlan.WorkingDirectory!, "portable Steam launch working directory");
        AssertEqual("-skiplauncher", steamLaunchPlan.ArgumentList.Single(), "portable Steam launch argument");

        var rollback = await builder.RollbackAsync(receipt);
        Assert(rollback, "channel store rollback should succeed");
        Assert(!File.Exists(officialEngine), "official materialized file should be removed by rollback");
        Assert(!File.Exists(bilibiliEngine), "Bilibili materialized file should be removed by rollback");
        Assert(!File.Exists(steamEngine), "Steam materialized file should be removed by rollback");
        Assert(!File.Exists(bilibili.TargetXStarterPath), "Bilibili runtime should be removed by rollback");
        Assert(!File.Exists(longSharedTarget), "long shared Store target should be removed by rollback");
        Assert(!File.Exists(longOverlayTarget), "long overlay Store target should be removed by rollback");
        Assert(!File.Exists(longOverlayImport.DestinationPath), "long overlay import should be removed by rollback");
    }

    private static InstallationProfileCandidate MoveCandidateToStore(
        InstallationProfileCandidate candidate,
        ChannelStoreVariantPlan target)
    {
        var profile = candidate.Profile! with
        {
            GameRootPath = target.TargetGameRootPath,
            LauncherPath = Path.Combine(target.TargetLauncherRootPath, "launcher.exe"),
            XStarterPath = target.TargetXStarterPath,
            GameExecutablePath = Path.Combine(target.TargetGameRootPath, "InfinityNikki.exe"),
            ShippingExecutablePath = Path.Combine(
                target.TargetGameRootPath,
                "X6Game",
                "Binaries",
                "Win64",
                "X6Game-Win64-Shipping.exe"),
        };
        return candidate with
        {
            DiscoverySource = ProfileDiscoverySource.ChannelStoreReceipt,
            LauncherRootPath = target.TargetLauncherRootPath,
            GameRootPath = target.TargetGameRootPath,
            Profile = profile,
            SteamManifest = null,
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
        }
    }

    private static void AssertPath(string expected, string actual, string message) =>
        AssertEqual(
            Path.GetFullPath(expected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(actual).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            message);

    private sealed class StoreFixture : IDisposable
    {
        private static readonly byte[] SharedBytes = Encoding.UTF8.GetBytes("shared-across-three-channels");
        private static readonly byte[] PackageBytes = Encoding.UTF8.GetBytes("same-package-different-layout");
        private static readonly byte[] LongSharedBytes = Encoding.UTF8.GetBytes("long-path-shared-across-three-channels");
        private static readonly string LongDirectory = Path.Combine(
            "X6Game",
            "Plugins",
            "long-path-segment-00-xxxxxxxxxxxxxxxxxxxx",
            "long-path-segment-01-xxxxxxxxxxxxxxxxxxxx",
            "long-path-segment-02-xxxxxxxxxxxxxxxxxxxx",
            "long-path-segment-03-xxxxxxxxxxxxxxxxxxxx",
            "long-path-segment-04-xxxxxxxxxxxxxxxxxxxx");

        public static string LongSharedRelativePath { get; } =
            Path.Combine(LongDirectory, "shared-long-path.bin");

        public static string LongOverlayRelativePath { get; } =
            Path.Combine(LongDirectory, "channel-long-path.bin");

        public StoreFixture()
        {
            Root = Path.Combine("E:\\", $"Nikkiward-StoreTest-{Guid.NewGuid():N}");
            OfficialRoot = Path.Combine(Root, "official-source");
            BilibiliRoot = Path.Combine(Root, "bilibili-source");
            SteamRoot = Path.Combine(Root, "steam-source");
            OfficialLauncherRoot = Path.Combine(Root, "official-launcher");
            BilibiliLauncherRoot = Path.Combine(Root, "bilibili-launcher");
            SteamLauncherRoot = Path.Combine(Root, "steam-launcher");
            StoreRoot = Path.Combine(Root, "store");
            CreateRoot(OfficialRoot, "InfinityNikki Launcher", "official", steamLayout: false);
            CreateRoot(BilibiliRoot, "InfinityNikkiBili Launcher", "bilibili", steamLayout: false);
            CreateRoot(SteamRoot, "InfinityNikkiSteam Launcher", "steam", steamLayout: true);
            CreateLauncherRoot(OfficialLauncherRoot, "official");
            CreateLauncherRoot(BilibiliLauncherRoot, "bilibili");
            CreateLauncherRoot(SteamLauncherRoot, "steam");
            OfficialCandidate = CreateCandidate(
                DistributionChannel.Official,
                RegionFamily.MainlandChina,
                AccountAuthority.Papergames,
                OfficialRoot,
                OfficialLauncherRoot);
            BilibiliCandidate = CreateCandidate(
                DistributionChannel.Bilibili,
                RegionFamily.MainlandChina,
                AccountAuthority.Bilibili,
                BilibiliRoot,
                BilibiliLauncherRoot);
            SteamCandidate = CreateCandidate(
                DistributionChannel.Steam,
                RegionFamily.Overseas,
                AccountAuthority.Steam,
                SteamRoot,
                SteamLauncherRoot);
        }

        public string Root { get; }

        public string OfficialRoot { get; }

        public string BilibiliRoot { get; }

        public string SteamRoot { get; }

        public string OfficialLauncherRoot { get; }

        public string BilibiliLauncherRoot { get; }

        public string SteamLauncherRoot { get; }

        public string StoreRoot { get; }

        public InstallationProfileCandidate OfficialCandidate { get; }

        public InstallationProfileCandidate BilibiliCandidate { get; }

        public InstallationProfileCandidate SteamCandidate { get; }

        public void DeleteSources()
        {
            foreach (var path in new[]
                     {
                         OfficialRoot,
                         BilibiliRoot,
                         SteamRoot,
                         OfficialLauncherRoot,
                         BilibiliLauncherRoot,
                         SteamLauncherRoot,
                     })
            {
                Directory.Delete(path, recursive: true);
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }

        private static void CreateRoot(
            string root,
            string markerName,
            string privateValue,
            bool steamLayout)
        {
            Write(Path.Combine(root, "Engine", "common.bin"), SharedBytes);
            Write(Path.Combine(root, LongSharedRelativePath), LongSharedBytes);
            Write(
                Path.Combine(root, LongOverlayRelativePath),
                Encoding.UTF8.GetBytes(privateValue + "-long-path"));
            var pakPath = steamLayout
                ? Path.Combine(root, "X6Game", "Content", "Paks", "same.ucas")
                : Path.Combine(root, "X6Game", "Saved", "Paks", "same.ucas");
            Write(pakPath, PackageBytes);
            Write(Path.Combine(root, "InfinityNikki.exe"), Encoding.UTF8.GetBytes(privateValue + "-bootstrap"));
            Write(
                Path.Combine(root, "X6Game", "Binaries", "Win64", "X6Game-Win64-Shipping.exe"),
                Encoding.UTF8.GetBytes(privateValue + "-shipping"));
            Write(
                Path.Combine(root, "X6Game", "Saved", "Logs", "session.log"),
                Encoding.UTF8.GetBytes("do-not-migrate"));
            Write(
                Path.Combine(root, "DownloadCache", "update.part"),
                Encoding.UTF8.GetBytes("do-not-migrate"));
            Write(
                Path.Combine(root, "NikkiGallery", "node_modules", "tool.js"),
                Encoding.UTF8.GetBytes("do-not-migrate"));
            File.WriteAllText(
                Path.Combine(root, "product.db"),
                $"{{\"name\":\"{markerName}\",\"version\":2828}}");
        }

        private static InstallationProfileCandidate CreateCandidate(
            DistributionChannel channel,
            RegionFamily region,
            AccountAuthority authority,
            string gameRoot,
            string launcherRoot)
        {
            var profile = new LaunchProfile
            {
                ProfileId = "fixture-" + channel,
                DisplayName = channel.ToString(),
                Channel = channel.ToString(),
                GameRootPath = gameRoot,
                LauncherPath = Path.Combine(launcherRoot, "launcher.exe"),
                XStarterPath = Path.Combine(launcherRoot, "1.3.1", "xstarter.exe"),
                GameExecutablePath = Path.Combine(gameRoot, "InfinityNikki.exe"),
                ShippingExecutablePath = Path.Combine(gameRoot, "X6Game", "Binaries", "Win64", "X6Game-Win64-Shipping.exe"),
            };
            return new InstallationProfileCandidate
            {
                ProfileId = profile.ProfileId,
                DisplayName = profile.DisplayName,
                Identity = new ProfileIdentity
                {
                    RegionFamily = region,
                    DistributionChannel = channel,
                    AccountAuthority = authority,
                    SteamAppId = channel is DistributionChannel.Steam ? "3164330" : null,
                    SteamBuildId = channel is DistributionChannel.Steam ? "24603829" : null,
                },
                State = InstallationCandidateState.Candidate,
                GameRootPath = gameRoot,
                LauncherRootPath = launcherRoot,
                Profile = profile,
            };
        }

        private static void CreateLauncherRoot(string root, string channel)
        {
            Write(Path.Combine(root, "launcher.exe"), Encoding.UTF8.GetBytes(channel + "-launcher"));
            Write(Path.Combine(root, "vcruntime140.dll"), Encoding.UTF8.GetBytes("root-runtime"));
            Write(Path.Combine(root, "uninst.exe"), Encoding.UTF8.GetBytes("must-not-copy"));
            Write(Path.Combine(root, "1.3.1", "xstarter.exe"), Encoding.UTF8.GetBytes(channel + "-xstarter"));
            Write(Path.Combine(root, "1.3.1", "common-runtime.dll"), Encoding.UTF8.GetBytes("shared-runtime"));
            Write(Path.Combine(root, "1.3.1", "channel-runtime.dll"), Encoding.UTF8.GetBytes(channel));
        }

        private static void Write(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }
}
