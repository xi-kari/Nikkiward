using Nikkiward.Models;
using Nikkiward.Services;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--emit-contract", StringComparer.Ordinal))
        {
            return await ContractRefreshTool.RunAsync().ConfigureAwait(false);
        }

        var tests = new (string Name, Func<Task> Run)[]
        {
            ("config reader accepts only gameDir", TestConfigReader),
            ("Steam library VDF enumerates every path", TestSteamLibraryReader),
            ("Steam depot parser preserves manifest and size associations", TestSteamInstalledDepotReader),
            ("Steam manifest parser records install state and depots", TestSteamManifestReader),
            ("CN automatic discovery builds the frozen provider binding", TestChinaAutomaticBuild),
            ("CN manual parent selection resolves the nested game root", TestChinaManualBuild),
            ("unsupported CN version cannot create a provider", TestUnsupportedVersion),
            ("channel marker mismatch fails closed", TestChannelMarkerMismatch),
            ("Steam downloading state never creates a provider", TestSteamDownloading),
            ("complete Steam Windows install still has no unverified provider", TestSteamComplete),
            ("Steam manual nested game root resolves its library manifest", TestSteamManualNestedGameRoot),
            ("provider catalog never contains the SteamOS _SD argument", TestNoSdArgument),
            ("preflight rejects an invalid provider contract", TestPreflightInvalidContract),
            ("preflight rejects artifact hash drift", TestPreflightHashDrift),
        };

        tests = tests
            .Concat(LaunchContractTests.All)
            .Concat(VariantMaterializerTests.All)
            .Concat(ChannelSwitchTests.All)
            .Concat(ChannelStoreBuilderTests.All)
            .Concat(BackdropTests.All)
            .Concat(MotionBackgroundTests.All)
            .Concat(MotionRuntimeHardeningContractTests.All)
            .Concat(DiagnosticTests.All)
            .Concat(UserSettingsTests.All)
            .Concat(AppearanceSettingsTests.All)
            .Concat(ApplicationSettingsTests.All)
            .Concat(AppearanceRuntimeContractTests.All)
            .Concat(CardBorderGlowProjectionTests.All)
            .Concat(HolographicBackdropProjectionTests.All)
            .Concat(AuthorProfileDepthProjectionTests.All)
            .Concat(GalleryIntegrationTests.All)
            .Concat(GalleryFavoriteCardLayoutTests.All)
            .Concat(GalleryMetadataTests.All)
            .Concat(ScreenshotRuntimeContractTests.All)
            .Concat(JournalDomainTests.All)
            .Concat(JournalSnapshotTests.All)
            .Concat(WishHistoryDomainTests.All)
            .Concat(UpdateReleaseTests.All)
            .Concat(AboutSettingsContractTests.All)
            .ToArray();

        if (args.Contains("--current-machine", StringComparer.Ordinal))
        {
            tests = tests
                .Append(("current machine discovery remains fail-closed", TestCurrentMachine))
                .Append(("current machine static preflight passes but execution stays closed", TestCurrentMachinePreflight))
                .Append(("current Steam install exposes the direct xstarter plan", TestCurrentSteamInstall))
                .ToArray();
        }

        var failures = new List<string>();
        foreach (var test in tests)
        {
            try
            {
                await test.Run().ConfigureAwait(false);
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failures.Add($"FAIL {test.Name}: {ex.Message}");
                Console.WriteLine(failures[^1]);
            }
        }

        Console.WriteLine($"RESULT total={tests.Length} passed={tests.Length - failures.Count} failed={failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }

    private static Task TestConfigReader()
    {
        const string text = """
            PaperLauncherToken=must-not-be-read
            game_path=C:\\wrong\\guess
            gameDir="D:\\Games\\InfinityNikki"
            PaperStartupToken=must-not-be-read
            """;

        var result = LauncherConfigReader.TryReadGameDirectoryText(text);
        AssertEqual("D:\\Games\\InfinityNikki", result, "gameDir should be the only accepted key");
        return Task.CompletedTask;
    }

    private static Task TestSteamLibraryReader()
    {
        const string text = """
            "libraryfolders"
            {
                "0"
                {
                    "path" "C:\\\\Program Files (x86)\\\\Steam"
                }
                "1"
                {
                    "path" "E:\\\\SteamLibrary"
                }
            }
            """;

        var paths = SteamLibraryVdfReader.ReadLibraryPaths(text);
        AssertEqual(2, paths.Count, "all library paths must be retained");
        Assert(paths.Any(path => path.EndsWith("Steam", StringComparison.OrdinalIgnoreCase)), "Steam root missing");
        Assert(paths.Any(path => path.EndsWith("SteamLibrary", StringComparison.OrdinalIgnoreCase)), "secondary library missing");
        return Task.CompletedTask;
    }

    private static Task TestSteamManifestReader()
    {
        using var fixture = new TempFixture();
        var manifestPath = Path.Combine(fixture.Root, "appmanifest_3164330.acf");
        File.WriteAllText(
            manifestPath,
            """
            "AppState"
            {
                "appid" "3164330"
                "StateFlags" "1026"
                "installdir" "Infinity Nikki"
                "SizeOnDisk" "126464332199"
                "buildid" "24603829"
                "InstalledDepots"
                {
                    "3164332"
                    {
                        "manifest" "1181472662926303513"
                        "size" "126464332199"
                    }
                }
            }
            """);

        var evidence = SteamManifestReader.TryRead(
            manifestPath,
            Path.Combine(fixture.Root, "common", "Infinity Nikki"),
            Path.Combine(fixture.Root, "downloading", "3164330"));

        Assert(evidence is not null, "manifest should parse");
        AssertEqual("3164330", evidence!.AppId, "app id");
        AssertEqual("1026", evidence.StateFlags, "state flags");
        AssertEqual(126464332199L, evidence.SizeOnDisk, "size on disk");
        AssertEqual(1, evidence.InstalledDepotIds.Count, "installed depot count");
        AssertEqual("3164332", evidence.InstalledDepotIds[0], "installed depot id");
        AssertEqual("3164332", evidence.DepotId, "primary depot id");
        AssertEqual("1181472662926303513", evidence.ManifestId, "primary manifest id");
        return Task.CompletedTask;
    }

    private static Task TestSteamInstalledDepotReader()
    {
        const string nested = """
            "AppState"
            {
                "InstalledDepots"
                {
                    "3164331"
                    {
                        "manifest" "1111111111111111111"
                        "size" "42"
                    }
                    "3164332"
                    {
                        "manifest" "2222222222222222222"
                        "size" "84"
                    }
                }
            }
            """;

        var depots = SteamKeyValueReader.ReadInstalledDepots(nested);
        AssertEqual(2, depots.Count, "nested depot count");
        AssertEqual("3164331", depots[0].DepotId, "first nested depot id");
        AssertEqual("1111111111111111111", depots[0].ManifestId, "first nested manifest association");
        AssertEqual(42L, depots[0].SizeInBytes, "first nested size association");
        AssertEqual("3164332", depots[1].DepotId, "second nested depot id");
        AssertEqual("2222222222222222222", depots[1].ManifestId, "second nested manifest association");
        AssertEqual(84L, depots[1].SizeInBytes, "second nested size association");

        const string legacy = """
            "InstalledDepots"
            {
                "3164332" "legacy-manifest"
            }
            """;
        var legacyDepots = SteamKeyValueReader.ReadInstalledDepots(legacy);
        AssertEqual(1, legacyDepots.Count, "legacy depot count");
        AssertEqual("3164332", legacyDepots[0].DepotId, "legacy depot id");
        AssertEqual("legacy-manifest", legacyDepots[0].ManifestId, "legacy manifest value");
        AssertEqual<long?>(null, legacyDepots[0].SizeInBytes, "legacy size remains unknown");
        return Task.CompletedTask;
    }

    private static async Task TestChinaAutomaticBuild()
    {
        using var fixture = CreateChinaFixture("1.3.1", "InfinityNikki Launcher");
        var source = new FakePathSource([fixture.LauncherRoot], []);
        var builder = new WindowsInstallationProfileBuilder(source, fixture.LocalAppData);

        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Official,
        });

        var candidate = result.SelectableCandidate;
        Assert(candidate is not null, "CN candidate should be selectable");
        Assert(candidate!.Provider is not null, "CN provider binding should exist");
        AssertEqual(
            fixture.LauncherRoot,
            candidate.Provider!.WorkingDirectory,
            "working directory must be launcher root");
        AssertEqual(
            Path.Combine(fixture.LauncherRoot, "1.3.1", "xstarter.exe"),
            candidate.Provider.BackendExecutablePath,
            "backend path");
        AssertEqual(1, candidate.Provider.ArgumentList.Count, "argument count");
        AssertEqual("-skiplauncher", candidate.Provider.ArgumentList[0], "argument preset");
        Assert(!candidate.Provider.ExecutionEnabled, "execution gate must remain closed");
        AssertEqual(LaunchCapability.NotVerified, candidate.Profile!.Capability, "profile capability");
        AssertEqual(ProfileDiscoverySource.LauncherConfig, candidate.DiscoverySource, "discovery source");
    }

    private static async Task TestChinaManualBuild()
    {
        using var fixture = CreateChinaFixture("1.3.1", "InfinityNikki Launcher");
        var parent = Path.Combine(fixture.Root, "selected-parent");
        var nested = Path.Combine(parent, "InfinityNikki");
        Directory.CreateDirectory(parent);
        CopyDirectory(fixture.GameRoot, nested);

        var source = new FakePathSource([fixture.LauncherRoot], []);
        var builder = new WindowsInstallationProfileBuilder(source, fixture.LocalAppData);
        var result = await builder.DiscoverFromManualGameRootAsync(parent, fixture.LauncherRoot);
        var candidate = result.SelectableCandidate;

        Assert(candidate is not null, "manual parent selection should find nested root");
        AssertEqual(nested, candidate!.GameRootPath, "nested game root");
        AssertEqual(ProfileDiscoverySource.ManualSelection, candidate.DiscoverySource, "manual source");
    }

    private static async Task TestUnsupportedVersion()
    {
        using var fixture = CreateChinaFixture("1.3.2", "InfinityNikki Launcher");
        var builder = new WindowsInstallationProfileBuilder(
            new FakePathSource([fixture.LauncherRoot], []),
            fixture.LocalAppData);
        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Official,
        });

        var candidate = result.Candidates.Single();
        AssertEqual(InstallationCandidateState.Unsupported, candidate.State, "unsupported version state");
        AssertEqual(ProfileBuildFailureCode.UnsupportedVersion, candidate.FailureCode, "unsupported version code");
        Assert(candidate.Provider is null, "unsupported version must not create provider");
    }

    private static async Task TestChannelMarkerMismatch()
    {
        using var fixture = CreateChinaFixture("1.3.1", "InfinityNikkiSteam Launcher");
        var builder = new WindowsInstallationProfileBuilder(
            new FakePathSource([fixture.LauncherRoot], []),
            fixture.LocalAppData);
        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Official,
        });

        var candidate = result.SelectableCandidate;
        Assert(candidate is null, "cross-channel marker must not be selectable");
        AssertEqual(ProfileBuildFailureCode.ChannelMarkerMismatch, result.Candidates.Single().FailureCode, "marker code");
    }

    private static async Task TestSteamDownloading()
    {
        using var fixture = CreateSteamFixture(complete: false);
        var builder = new WindowsInstallationProfileBuilder(
            new FakePathSource([], [fixture.SteamRoot]),
            fixture.LocalAppData);
        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Steam,
        });

        var candidate = result.Candidates.Single(item => item.SteamManifest is not null);
        AssertEqual(InstallationCandidateState.Downloading, candidate.State, "Steam state");
        AssertEqual(ProfileBuildFailureCode.SteamNotReady, candidate.FailureCode, "Steam failure code");
        Assert(candidate.Provider is null, "Steam downloading must not create provider");
        Assert(!candidate.GameRootPath!.Contains("downloading", StringComparison.OrdinalIgnoreCase), "staging must not become game root");
    }

    private static async Task TestSteamComplete()
    {
        using var fixture = CreateSteamFixture(complete: true);
        var builder = new WindowsInstallationProfileBuilder(
            new FakePathSource([], [fixture.SteamRoot]),
            fixture.LocalAppData);
        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Steam,
        });

        var candidate = result.Candidates.Single(item => item.SteamManifest is not null);
        AssertEqual(InstallationCandidateState.Candidate, candidate.State, "complete Steam state");
        AssertEqual(ProfileBuildFailureCode.ProviderContractUnavailable, candidate.FailureCode, "Steam provider code");
        AssertEqual(RegionFamily.Overseas, candidate.Identity.RegionFamily, "Steam region family");
        AssertEqual("Steam Global", candidate.Profile!.Channel, "Steam profile channel");
        AssertEqual(
            Path.Combine(fixture.SteamRoot, "steamapps", "common", "Infinity Nikki", "InfinityNikki"),
            candidate.GameRootPath,
            "Steam game root semantics");
        Assert(candidate.Provider is null, "Steam Windows provider remains unverified");
    }

    private static async Task TestSteamManualNestedGameRoot()
    {
        using var fixture = CreateSteamFixture(complete: true);
        var gameRoot = Path.Combine(
            fixture.SteamRoot,
            "steamapps",
            "common",
            "Infinity Nikki",
            "InfinityNikki");
        var builder = new WindowsInstallationProfileBuilder(
            new FakePathSource([], []),
            fixture.LocalAppData);
        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Steam,
            ManualGameRootPath = gameRoot,
            AllowAutomaticDiscovery = false,
        });

        var candidate = result.Candidates.Single(item => item.SteamManifest is not null);
        AssertEqual(InstallationCandidateState.Candidate, candidate.State, "manual Steam state");
        AssertEqual(gameRoot, candidate.GameRootPath, "manual Steam game root");
        AssertEqual(
            Path.Combine(fixture.SteamRoot, "steamapps", "appmanifest_3164330.acf"),
            candidate.SteamManifest!.ManifestPath,
            "manual Steam manifest path");
    }

    private static Task TestNoSdArgument()
    {
        Assert(
            LaunchProviderCatalog.CnWindows131.ArgumentList.SequenceEqual(["-skiplauncher"]),
            "CN catalog argument must be exact");
        Assert(
            LaunchProviderCatalog.CnWindows131.ArgumentList.All(argument =>
                !argument.Contains("SkipLauncherTokenCheck_SD", StringComparison.OrdinalIgnoreCase)),
            "SteamOS argument must never enter CN catalog");
        return Task.CompletedTask;
    }

    private static async Task TestPreflightInvalidContract()
    {
        using var fixture = CreateChinaFixture("1.3.1", "InfinityNikki Launcher");
        var verifier = new WindowsLaunchPreflightVerifier();
        var result = await verifier.VerifyAsync(
            fixture.LauncherRoot,
            fixture.GameRoot,
            "UnapprovedContract");

        Assert(!result.StaticIdentityPassed, "invalid contract must fail static preflight");
        Assert(!result.ExecutionAllowed, "invalid contract must close execution");
        AssertEqual(
            LaunchPreflightFailureCode.InvalidContract,
            result.FailureCode,
            "invalid contract failure code");
        Assert(result.Plan is null, "invalid contract must not expose a launch plan");
    }

    private static async Task TestPreflightHashDrift()
    {
        using var fixture = CreateChinaFixture("1.3.1", "InfinityNikki Launcher");
        var verifier = new WindowsLaunchPreflightVerifier();
        var result = await verifier.VerifyAsync(
            fixture.LauncherRoot,
            fixture.GameRoot,
            LaunchProviderCatalog.CnWindows131ContractId);

        Assert(!result.StaticIdentityPassed, "fixture artifact identity must fail against the frozen hash");
        Assert(!result.ExecutionAllowed, "hash drift must close execution");
        AssertEqual(
            LaunchPreflightFailureCode.ArtifactHashMismatch,
            result.FailureCode,
            "hash drift failure code");
        Assert(result.Plan is null, "hash drift must not expose a launch plan");
    }

    private static async Task TestCurrentMachine()
    {
        var builder = new WindowsInstallationProfileBuilder();
        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Official,
        });

        Assert(result.Candidates.Count > 0, "current machine should produce a diagnostic candidate or an explicit failure");
        var cn = result.Candidates.FirstOrDefault(candidate => candidate.Provider is not null);
        Assert(cn is not null, "current CN installation should produce the known 1.3.1 provider candidate");
        AssertEqual(
            @"C:\InfinityNikki Launcher",
            cn!.Provider!.WorkingDirectory,
            "current CN working directory");
        AssertEqual(
            @"C:\InfinityNikki Launcher\1.3.1\xstarter.exe",
            cn.Provider.BackendExecutablePath,
            "current CN backend path");
        Assert(
            result.Candidates.All(candidate => candidate.Provider is null ||
                candidate.Provider.ArgumentList.SequenceEqual(["-skiplauncher"])),
            "current machine provider arguments must remain fixed");
    }

    private static async Task TestCurrentMachinePreflight()
    {
        var builder = new WindowsInstallationProfileBuilder();
        var discovered = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Official,
        });
        var candidate = discovered.SelectableCandidate;
        Assert(candidate is not null, "current CN candidate required for preflight");

        var verifier = new WindowsLaunchPreflightVerifier();
        var result = await verifier.VerifyAsync(candidate!);
        Assert(result.StaticIdentityPassed, result.FailureDetail ?? "static preflight should pass");
        Assert(!result.ExecutionAllowed, "execution must remain disabled by contract");
        AssertEqual(LaunchPreflightFailureCode.ExecutionGateClosed, result.FailureCode, "execution gate code");
        Assert(result.Plan is null, "closed gate must not expose a launch plan");
    }

    private static async Task TestCurrentSteamInstall()
    {
        var builder = new WindowsInstallationProfileBuilder();
        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Steam,
        });
        var installation = result.Candidates.FirstOrDefault(candidate =>
            candidate.SteamManifest?.ManifestPath.EndsWith(
                @"steamapps\appmanifest_3164330.acf",
                StringComparison.OrdinalIgnoreCase) == true);
        Assert(installation is not null, "current Steam manifest should be discovered");
        AssertEqual(InstallationCandidateState.Candidate, installation!.State, "current Steam install state");
        AssertEqual(
            ProfileBuildFailureCode.ProviderContractUnavailable,
            installation.FailureCode,
            "current Steam provider failure code");
        Assert(installation.Provider is null, "current Steam install must not get an unverified provider");
        var directPlan = new WindowsChannelEntryLauncher().CreatePlan(installation);
        Assert(directPlan.CanLaunch, $"current Steam direct plan should pass: {directPlan.FailureDetail}");
        AssertEqual(ChannelLaunchEntryKind.SteamXStarterDirect, directPlan.EntryKind, "current Steam entry kind");
        AssertEqual(installation.Profile!.XStarterPath, directPlan.FileName, "current Steam xstarter path");
        AssertEqual(installation.LauncherRootPath, directPlan.WorkingDirectory, "current Steam working directory");
        Assert(directPlan.ArgumentList.SequenceEqual(["-skiplauncher"]), "current Steam direct argument");
    }

    private static ChinaFixture CreateChinaFixture(string version, string markerName)
    {
        var fixture = new ChinaFixture();
        Directory.CreateDirectory(fixture.LauncherRoot);
        Directory.CreateDirectory(Path.Combine(fixture.LauncherRoot, version));
        Directory.CreateDirectory(fixture.GameRoot);
        Touch(Path.Combine(fixture.LauncherRoot, "launcher.exe"));
        Touch(Path.Combine(fixture.LauncherRoot, version, "xstarter.exe"));
        Touch(Path.Combine(fixture.GameRoot, "InfinityNikki.exe"));
        Touch(Path.Combine(fixture.GameRoot, "X6Game", "Binaries", "Win64", "X6Game-Win64-Shipping.exe"));
        Touch(Path.Combine(fixture.GameRoot, "X6Game", "Binaries", "Win64", "AntiCheatExpert", "ACE-Service64.exe"));
        File.WriteAllText(
            Path.Combine(fixture.GameRoot, "product.db"),
            $"{{\"name\":\"{markerName}\",\"version\":2828}}");
        Directory.CreateDirectory(Path.Combine(fixture.LocalAppData, "InfinityNikki Launcher"));
        File.WriteAllText(
            Path.Combine(fixture.LocalAppData, "InfinityNikki Launcher", "config.ini"),
            $"gameDir=\"{fixture.GameRoot}\"\nPaperLauncherToken=redacted\n");
        return fixture;
    }

    private static SteamFixture CreateSteamFixture(bool complete)
    {
        var fixture = new SteamFixture();
        var steamApps = Path.Combine(fixture.SteamRoot, "steamapps");
        Directory.CreateDirectory(steamApps);
        Directory.CreateDirectory(Path.Combine(steamApps, "downloading", "3164330"));
        var commonRoot = Path.Combine(steamApps, "common", "Infinity Nikki");
        if (complete)
        {
            Directory.CreateDirectory(commonRoot);
            Touch(Path.Combine(commonRoot, "launcher.exe"));
            Touch(Path.Combine(commonRoot, "1.3.1", "xstarter.exe"));
            var gameRoot = Path.Combine(commonRoot, "InfinityNikki");
            Touch(Path.Combine(gameRoot, "InfinityNikki.exe"));
            Touch(Path.Combine(gameRoot, "X6Game", "Binaries", "Win64", "X6Game-Win64-Shipping.exe"));
            File.WriteAllText(
                Path.Combine(gameRoot, "product.db"),
                "{\"name\":\"InfinityNikkiSteam Launcher\"}");
        }

        var depots = complete
            ? "                \"InstalledDepots\"\n                {\n                    \"3164332\"\n                    {\n                        \"manifest\" \"1181472662926303513\"\n                        \"size\" \"123\"\n                    }\n                }"
            : "                \"InstalledDepots\"\n                {\n                }";
        var size = complete ? "123" : "0";
        var build = complete ? "24603829" : "0";
        File.WriteAllText(
            Path.Combine(steamApps, "appmanifest_3164330.acf"),
            $"\"AppState\"\n{{\n    \"appid\" \"3164330\"\n    \"StateFlags\" \"{(complete ? "4" : "1026")}\"\n    \"installdir\" \"Infinity Nikki\"\n    \"SizeOnDisk\" \"{size}\"\n    \"buildid\" \"{build}\"\n{depots}\n}}\n");
        return fixture;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
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
            throw new InvalidOperationException($"{message}: expected '{expected}', actual '{actual}'.");
        }
    }

    private sealed class FakePathSource(
        IReadOnlyList<string> officialRoots,
        IReadOnlyList<string> steamRoots) : IWindowsInstallationPathSource
    {
        public IReadOnlyList<string> GetOfficialLauncherRootCandidates() => officialRoots;

        public IReadOnlyList<string> GetSteamRootCandidates() => steamRoots;
    }

    private class TempFixture : IDisposable
    {
        public TempFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "NikkiwardProfileBuilder", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string LocalAppData => Path.Combine(Root, "localappdata");

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class ChinaFixture : TempFixture
    {
        public string LauncherRoot => Path.Combine(Root, "launcher");

        public string GameRoot => Path.Combine(Root, "game", "InfinityNikki");

    }

    private sealed class SteamFixture : TempFixture
    {
        public string SteamRoot => Path.Combine(Root, "SteamLibrary");
    }
}
