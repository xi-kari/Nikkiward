using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;

/// <summary>
/// Characterization tests for the launch path. These lock the behaviour
/// documented in LAUNCH_CONTRACT.md. A failure here is a regression in the
/// launch mechanism, not an outdated test.
/// </summary>
internal static class LaunchContractTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("frozen contract keeps the exact -skiplauncher argument list", ArgumentListIsFrozen),
        ("frozen contract runs xstarter from the launcher root", WorkingDirectoryIsLauncherRoot),
        ("frozen contract keeps the static execution gate closed", ExecutionGateStaysClosed),
        ("closed execution gate still synthesizes a transient plan", ClosedGateSynthesizesPlan),
        ("inconsistent gate state fails closed", InconsistentGateFailsClosed),
        ("an allowed gate without a plan fails closed", AllowedGateWithoutPlanFailsClosed),
        ("argument drift is rejected", ArgumentDriftRejected),
        ("a foreign contract id is rejected", ForeignContractRejected),
        ("an incomplete component receipt is rejected", IncompleteReceiptRejected),
        ("a duplicated component receipt is rejected", DuplicateReceiptRejected),
        ("a missing profile is rejected before any verification", MissingProfileRejected),
        ("Start refuses a preparation that never succeeded", StartRefusesUnpreparedPlan),
        ("process binding keeps the current profile game paths", ProcessBindingCarriesOnlyGamePaths),
        ("process binding requires the root process identity", ProcessBindingRejectsMissingRootIdentity),
        ("the catalog only resolves the exact launcher version directory", CatalogVersionIsExact),
        ("no component requirement omits its identity anchors", ComponentAnchorsPresent),
        ("Bilibili config discovery preserves its independent identity", BilibiliConfigDiscoveryPreservesIdentity),
        ("Bilibili manual parent selection resolves InfinityNikkiBili", BilibiliManualParentSelectionResolvesGameRoot),
        ("Bilibili marker mismatch fails closed", BilibiliMarkerMismatchFailsClosed),
        ("Bilibili complete installs remain providerless", BilibiliCompleteInstallHasNoProvider),
    ];

    private const string LauncherVersion = LaunchProviderCatalog.CnLauncherVersion;

    private static async Task ArgumentListIsFrozen()
    {
        var contract = LaunchProviderCatalog.CnWindows131;
        AssertTrue(
            contract.ArgumentList.SequenceEqual(["-skiplauncher"], StringComparer.Ordinal),
            "the frozen argument list must be exactly [\"-skiplauncher\"]");
        AssertTrue(
            !contract.ArgumentList.Any(a => a.Contains("_SD", StringComparison.OrdinalIgnoreCase)),
            "the SteamOS _SD argument must never appear");
        AssertEqual(
            LaunchProviderCatalog.CnWindows131ArgumentPresetId,
            contract.ArgumentPresetId,
            "argument preset id");

        // The synthesized plan must carry the same list through to Start.
        using var layout = FakeInstall.Create();
        var harness = await RunPrepareAsync(ClosedGatePreflight(layout), layout);
        AssertTrue(harness.Succeeded, $"preparation should succeed, got {harness.FailureCode}");
        AssertTrue(
            harness.Plan!.ArgumentList.SequenceEqual(["-skiplauncher"], StringComparer.Ordinal),
            "the synthesized plan must keep the frozen argument list");
    }

    private static async Task WorkingDirectoryIsLauncherRoot()
    {
        var contract = LaunchProviderCatalog.CnWindows131;
        AssertEqual("LauncherRoot", contract.WorkingDirectoryRole, "working directory role");
        AssertEqual(
            Path.Combine(LauncherVersion, "xstarter.exe"),
            contract.BackendRelativeExecutablePath,
            "backend relative executable path");

        using var layout = FakeInstall.Create();
        var harness = await RunPrepareAsync(ClosedGatePreflight(layout), layout);
        AssertTrue(harness.Succeeded, $"preparation should succeed, got {harness.FailureCode}");

        // Working directory is the launcher root, NOT the directory holding xstarter.exe.
        AssertEqual(layout.LauncherRoot, harness.Plan!.WorkingDirectory, "plan working directory");
        AssertTrue(
            !PathEquals(harness.Plan.WorkingDirectory, Path.Combine(layout.LauncherRoot, LauncherVersion)),
            "the working directory must not be the versioned backend directory");
    }

    private static Task ExecutionGateStaysClosed()
    {
        AssertTrue(
            !LaunchProviderCatalog.CnWindows131.ExecutionEnabled,
            "the frozen contract must keep ExecutionEnabled=false; flipping it breaks PrepareAsync");
        return Task.CompletedTask;
    }

    private static async Task ClosedGateSynthesizesPlan()
    {
        using var layout = FakeInstall.Create();
        var preflight = ClosedGatePreflight(layout);
        AssertTrue(preflight.Plan is null, "the static verifier must not supply a plan");

        var harness = await RunPrepareAsync(preflight, layout);
        AssertTrue(harness.Succeeded, $"a closed gate must still prepare, got {harness.FailureCode}");
        AssertTrue(harness.Plan is not null, "a transient plan must be synthesized");
        AssertEqual(
            Path.Combine(layout.LauncherRoot, LauncherVersion, "xstarter.exe"),
            harness.Plan!.ProviderExecutablePath,
            "synthesized provider path");
        AssertEqual(5, harness.ObservedProcessPaths.Count, "observed component receipt count");
    }

    private static async Task InconsistentGateFailsClosed()
    {
        using var layout = FakeInstall.Create();

        // Closed gate but the verifier also handed back a plan: contradictory.
        var withPlan = ClosedGatePreflight(layout) with
        {
            Plan = new LaunchPlan { ProviderId = LaunchProviderCatalog.CnWindows131ContractId },
        };
        var a = await RunPrepareAsync(withPlan, layout);
        AssertFailure(a, "Preflight.ExecutionStateMismatch");

        // Closed gate reported under the wrong failure code.
        var wrongCode = ClosedGatePreflight(layout) with
        {
            FailureCode = LaunchPreflightFailureCode.ArtifactHashMismatch,
        };
        var b = await RunPrepareAsync(wrongCode, layout);
        AssertFailure(b, "Preflight.ExecutionStateMismatch");
    }

    private static async Task AllowedGateWithoutPlanFailsClosed()
    {
        using var layout = FakeInstall.Create();
        var preflight = ClosedGatePreflight(layout) with
        {
            ExecutionAllowed = true,
            FailureCode = LaunchPreflightFailureCode.None,
            Plan = null,
        };
        var result = await RunPrepareAsync(preflight, layout);
        AssertFailure(result, "Preflight.ExecutionStateMismatch");
    }

    private static async Task ArgumentDriftRejected()
    {
        using var layout = FakeInstall.Create();
        var candidate = layout.BuildCandidate(provider => provider with
        {
            ArgumentList = ["-skiplauncher", "-extra"],
        });
        var result = await new OfficialAssistedLaunchCoordinator(
                new StubVerifier(ClosedGatePreflight(layout)))
            .PrepareAsync(candidate);
        AssertFailure(result, "Preflight.ContractDrift");
    }

    private static async Task ForeignContractRejected()
    {
        using var layout = FakeInstall.Create();
        var candidate = layout.BuildCandidate(provider => provider with
        {
            ProviderId = "SomeOtherContract",
        });
        var result = await new OfficialAssistedLaunchCoordinator(
                new StubVerifier(ClosedGatePreflight(layout)))
            .PrepareAsync(candidate);
        AssertFailure(result, "Preflight.ProfileMismatch");
    }

    private static async Task IncompleteReceiptRejected()
    {
        using var layout = FakeInstall.Create();
        var preflight = ClosedGatePreflight(layout);
        var trimmed = preflight with
        {
            Components = preflight.Components
                .Where(c => c.ComponentId != "anti-cheat-artifact")
                .ToArray(),
        };
        var result = await RunPrepareAsync(trimmed, layout);
        AssertFailure(result, "Preflight.ComponentReceiptIncomplete");

        // A component present but not passing is equally unacceptable.
        var failed = preflight with
        {
            Components = preflight.Components
                .Select(c => c.ComponentId == "game-client" ? c with { Passed = false } : c)
                .ToArray(),
        };
        AssertFailure(await RunPrepareAsync(failed, layout), "Preflight.ComponentReceiptIncomplete");
    }

    private static async Task DuplicateReceiptRejected()
    {
        using var layout = FakeInstall.Create();
        var preflight = ClosedGatePreflight(layout);
        var duplicated = preflight with
        {
            Components = preflight.Components
                .Concat([preflight.Components.First(c => c.ComponentId == "game-client")])
                .ToArray(),
        };
        AssertFailure(await RunPrepareAsync(duplicated, layout), "Preflight.ComponentReceiptIncomplete");
    }

    private static async Task MissingProfileRejected()
    {
        var coordinator = new OfficialAssistedLaunchCoordinator(new ThrowingVerifier());
        AssertFailure(await coordinator.PrepareAsync(null), "Preflight.ProfileUnavailable");
        AssertFailure(
            await coordinator.PrepareAsync(new InstallationProfileCandidate { ProfileId = "x" }),
            "Preflight.ProfileUnavailable");
    }

    private static Task StartRefusesUnpreparedPlan()
    {
        var coordinator = new OfficialAssistedLaunchCoordinator(new ThrowingVerifier());
        var receipt = coordinator.Start(new OfficialAssistedLaunchPreparation
        {
            Succeeded = false,
            FailureCode = "Preflight.ProfileUnavailable",
        });
        AssertTrue(!receipt.StartRequested, "no process may be requested without a preparation");
        AssertEqual("Runtime.PreparationMissing", receipt.FailureCode, "receipt failure code");
        AssertTrue(receipt.RootProcessId is null, "no pid may be reported");
        return Task.CompletedTask;
    }

    private static async Task ProcessBindingCarriesOnlyGamePaths()
    {
        using var layout = FakeInstall.Create();
        var preparation = await RunPrepareAsync(ClosedGatePreflight(layout), layout);
        var coordinator = new OfficialAssistedLaunchCoordinator(new StubVerifier(ClosedGatePreflight(layout)));
        var receipt = new OfficialAssistedLaunchReceipt
        {
            AttemptId = Guid.NewGuid(),
            RequestedAtUtc = DateTimeOffset.UtcNow,
            StartRequested = true,
            RootProcessId = 12345,
            RootProcessStartTimeUtc = DateTimeOffset.UtcNow,
        };

        AssertTrue(coordinator.TryBind(preparation, receipt, out var binding), "a complete receipt should bind");
        AssertEqual(2, binding.GameProcessPaths.Count, "bound game process path count");
        AssertEqual(
            Path.GetFullPath(preparation.Plan!.ProviderExecutablePath),
            Path.GetFullPath(binding.RootExecutablePath),
            "bound root executable path");
        AssertTrue(
            binding.RunningProcessPath.EndsWith("X6Game-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase),
            "the official running path must be Shipping");
        AssertEqual(0, binding.AuxiliaryProcessPaths.Count, "official binding auxiliary path count");
        AssertTrue(
            binding.GameProcessPaths.All(path =>
                path.EndsWith("InfinityNikki.exe", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("X6Game-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase)),
            "binding must exclude launcher, xstarter, and anti-cheat paths");
    }

    private static async Task ProcessBindingRejectsMissingRootIdentity()
    {
        using var layout = FakeInstall.Create();
        var preparation = await RunPrepareAsync(ClosedGatePreflight(layout), layout);
        var coordinator = new OfficialAssistedLaunchCoordinator(new StubVerifier(ClosedGatePreflight(layout)));
        var receipt = new OfficialAssistedLaunchReceipt
        {
            AttemptId = Guid.NewGuid(),
            RequestedAtUtc = DateTimeOffset.UtcNow,
            StartRequested = true,
            RootProcessId = 12345,
        };

        AssertTrue(
            !coordinator.TryBind(preparation, receipt, out _),
            "a binding without the root start identity must fail closed");
    }

    private static Task CatalogVersionIsExact()
    {
        AssertTrue(
            LaunchProviderCatalog.TryGet(DistributionChannel.Official, LauncherVersion, out _),
            "the exact launcher version must resolve");
        foreach (var near in new[] { "1.3.2", "1.3", "1.3.1.0", "1.4.0", "" })
        {
            AssertTrue(
                !LaunchProviderCatalog.TryGet(DistributionChannel.Official, near, out _),
                $"version '{near}' must not resolve — see LAUNCH_CONTRACT.md §5 when the launcher updates");
        }

        foreach (var channel in new[]
                 {
                     DistributionChannel.Bilibili,
                     DistributionChannel.Steam,
                     DistributionChannel.Epic,
                     DistributionChannel.Unknown,
                 })
        {
            AssertTrue(
                !LaunchProviderCatalog.TryGet(channel, LauncherVersion, out _),
                $"channel {channel} must not resolve a provider");
        }

        return Task.CompletedTask;
    }

    private static Task ComponentAnchorsPresent()
    {
        var contract = LaunchProviderCatalog.CnWindows131;
        AssertEqual(5, contract.RequiredComponents.Count, "required component count");
        foreach (var component in contract.RequiredComponents)
        {
            AssertTrue(
                component.ExpectedSha256.Length == 64,
                $"{component.ComponentId} must pin a 64-hex SHA-256");
            AssertTrue(
                !string.IsNullOrWhiteSpace(component.ExpectedSignerThumbprint),
                $"{component.ComponentId} must pin a signer thumbprint");
            AssertTrue(
                component.ExpectedSignature is AuthenticodeSignatureStatus.Valid,
                $"{component.ComponentId} must require a valid signature");
        }

        AssertEqual("InfinityNikki Launcher", contract.ProductMarker!.ExpectedName!, "product marker name");
        return Task.CompletedTask;
    }

    private static async Task BilibiliConfigDiscoveryPreservesIdentity()
    {
        using var fixture = BilibiliInstallFixture.Create("InfinityNikkiBili Launcher");
        var builder = new WindowsInstallationProfileBuilder(
            new BilibiliPathSource([]),
            fixture.LocalAppData);

        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Bilibili,
        });
        var candidate = result.SelectableCandidate;

        AssertTrue(candidate is not null, "Bilibili config should produce a selectable candidate");
        AssertEqual(fixture.LauncherRoot, candidate!.LauncherRootPath, "Bilibili launcher root");
        AssertEqual(fixture.GameRoot, candidate.GameRootPath, "Bilibili game root");
        AssertEqual(ProfileDiscoverySource.LauncherConfig, candidate.DiscoverySource, "Bilibili discovery source");
        AssertEqual(RegionFamily.MainlandChina, candidate.Identity.RegionFamily, "Bilibili region family");
        AssertEqual(DistributionChannel.Bilibili, candidate.Identity.DistributionChannel, "Bilibili distribution");
        AssertEqual(AccountAuthority.Bilibili, candidate.Identity.AccountAuthority, "Bilibili account authority");
        AssertEqual("Bilibili", candidate.Profile!.Channel, "Bilibili launch profile channel");
    }

    private static async Task BilibiliManualParentSelectionResolvesGameRoot()
    {
        using var fixture = BilibiliInstallFixture.Create("InfinityNikkiBili Launcher");
        var selectedParent = Path.Combine(fixture.Root, "selected-parent");
        var nestedGameRoot = Path.Combine(selectedParent, "InfinityNikkiBili");
        CopyDirectory(fixture.GameRoot, nestedGameRoot);

        var builder = new WindowsInstallationProfileBuilder(
            new BilibiliPathSource([]),
            fixture.LocalAppData);
        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Bilibili,
            ManualGameRootPath = selectedParent,
            ManualLauncherRootPath = fixture.LauncherRoot,
            AllowAutomaticDiscovery = false,
        });
        var candidate = result.SelectableCandidate;

        AssertTrue(candidate is not null, "Bilibili manual parent should produce a selectable candidate");
        AssertEqual(nestedGameRoot, candidate!.GameRootPath, "Bilibili nested game root");
        AssertEqual(ProfileDiscoverySource.ManualSelection, candidate.DiscoverySource, "Bilibili manual discovery source");
    }

    private static async Task BilibiliMarkerMismatchFailsClosed()
    {
        using var fixture = BilibiliInstallFixture.Create("InfinityNikki Launcher");
        var builder = new WindowsInstallationProfileBuilder(
            new BilibiliPathSource([fixture.LauncherRoot]),
            fixture.LocalAppData);

        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Bilibili,
        });
        var candidate = result.Candidates.Single();

        AssertEqual(InstallationCandidateState.Unsupported, candidate.State, "Bilibili marker mismatch state");
        AssertEqual(ProfileBuildFailureCode.ChannelMarkerMismatch, candidate.FailureCode, "Bilibili marker mismatch code");
        AssertTrue(candidate.Provider is null, "Bilibili marker mismatch must not create a provider");
    }

    private static async Task BilibiliCompleteInstallHasNoProvider()
    {
        using var fixture = BilibiliInstallFixture.Create("InfinityNikkiBili Launcher");
        var builder = new WindowsInstallationProfileBuilder(
            new BilibiliPathSource([fixture.LauncherRoot]),
            fixture.LocalAppData);

        var result = await builder.BuildAsync(new ProfileBuildRequest
        {
            Channel = DistributionChannel.Bilibili,
        });
        var candidate = result.SelectableCandidate;

        AssertTrue(candidate is not null, "complete Bilibili layout should remain selectable for diagnostics");
        AssertEqual(InstallationCandidateState.Candidate, candidate!.State, "Bilibili candidate state");
        AssertEqual(LaunchCapability.NotVerified, candidate.Profile!.Capability, "Bilibili capability");
        AssertEqual(ProfileBuildFailureCode.ProviderContractUnavailable, candidate.FailureCode, "Bilibili provider status");
        AssertTrue(candidate.Provider is null, "Bilibili must not reuse the official provider binding");
    }

    // ---- harness ----------------------------------------------------------

    // Both helpers require the caller's live layout: the coordinator probes every
    // path on disk, so receipts must outlive the call.
    private static Task<OfficialAssistedLaunchPreparation> RunPrepareAsync(
        LaunchPreflightResult preflight,
        FakeInstall layout) =>
        new OfficialAssistedLaunchCoordinator(new StubVerifier(preflight))
            .PrepareAsync(layout.BuildCandidate());

    private static LaunchPreflightResult ClosedGatePreflight(FakeInstall layout) => new()
    {
        StaticIdentityPassed = true,
        ExecutionAllowed = false,
        FailureCode = LaunchPreflightFailureCode.ExecutionGateClosed,
        Contract = LaunchProviderCatalog.CnWindows131,
        Plan = null,
        Components = layout.ComponentReceipts(),
    };

    private sealed record BilibiliPathSource(
        IReadOnlyList<string> BilibiliRoots) : IWindowsInstallationPathSource
    {
        public IReadOnlyList<string> GetOfficialLauncherRootCandidates() => Array.Empty<string>();

        public IReadOnlyList<string> GetBilibiliLauncherRootCandidates() => BilibiliRoots;

        public IReadOnlyList<string> GetSteamRootCandidates() => Array.Empty<string>();
    }

    private sealed class BilibiliInstallFixture : IDisposable
    {
        private BilibiliInstallFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"Nikkiward-Bilibili-{Guid.NewGuid():N}");
            LauncherRoot = Path.Combine(Root, "InfinityNikkiBili Launcher");
            GameRoot = Path.Combine(LauncherRoot, "InfinityNikkiBili");
            LocalAppData = Path.Combine(Root, "LocalAppData");
        }

        public string Root { get; }

        public string LauncherRoot { get; }

        public string GameRoot { get; }

        public string LocalAppData { get; }

        public static BilibiliInstallFixture Create(string markerName)
        {
            var fixture = new BilibiliInstallFixture();
            Touch(Path.Combine(fixture.LauncherRoot, "launcher.exe"));
            Touch(Path.Combine(fixture.LauncherRoot, LauncherVersion, "xstarter.exe"));
            Touch(Path.Combine(fixture.GameRoot, "InfinityNikki.exe"));
            Touch(Path.Combine(
                fixture.GameRoot,
                "X6Game",
                "Binaries",
                "Win64",
                "X6Game-Win64-Shipping.exe"));
            Touch(Path.Combine(
                fixture.GameRoot,
                "X6Game",
                "Binaries",
                "Win64",
                "AntiCheatExpert",
                "ACE-Service64.exe"));
            File.WriteAllText(
                Path.Combine(fixture.GameRoot, "product.db"),
                $"{{\"name\":\"{markerName}\",\"version\":2828}}");

            var configDirectory = Path.Combine(fixture.LocalAppData, "InfinityNikkiBili Launcher");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(
                Path.Combine(configDirectory, "config.ini"),
                $"[Download]{Environment.NewLine}gameDir={fixture.GameRoot}{Environment.NewLine}");
            return fixture;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static void Touch(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [0x4E, 0x49, 0x4B, 0x4B, 0x49]);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private sealed class StubVerifier(LaunchPreflightResult result) : ILaunchPreflightVerifier
    {
        public Task<LaunchPreflightResult> VerifyAsync(
            InstallationProfileCandidate candidate,
            CancellationToken cancellationToken = default) => Task.FromResult(result);

        public Task<LaunchPreflightResult> VerifyAsync(
            string launcherRootPath,
            string gameRootPath,
            string contractId,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class ThrowingVerifier : ILaunchPreflightVerifier
    {
        public Task<LaunchPreflightResult> VerifyAsync(
            InstallationProfileCandidate candidate,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the verifier must not run for an unusable profile");

        public Task<LaunchPreflightResult> VerifyAsync(
            string launcherRootPath,
            string gameRootPath,
            string contractId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the verifier must not run for an unusable profile");
    }

    private static bool PathEquals(string left, string right) => string.Equals(
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
        StringComparison.OrdinalIgnoreCase);

    private static void AssertTrue(bool condition, string because)
    {
        if (!condition)
        {
            throw new InvalidOperationException(because);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string what)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{what}: expected '{expected}', got '{actual}'");
        }
    }

    private static void AssertFailure(OfficialAssistedLaunchPreparation result, string expectedCode)
    {
        AssertTrue(!result.Succeeded, $"preparation must fail with {expectedCode}");
        AssertEqual(expectedCode, result.FailureCode, "failure code");
        AssertTrue(result.Plan is null, "a failed preparation must not carry a plan");
    }
}
