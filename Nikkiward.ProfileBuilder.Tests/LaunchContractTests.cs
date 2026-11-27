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
        ("manual launch applies the window preference once per confirmed run", AfterLaunchWindowOnce),
        ("cancelled, failed and timed out launches keep the window", AfterLaunchWindowFailures),
        ("registry paths preserve spaces and executable boundaries", RegistryPaths),
        ("discovery distinguishes pending, empty and selection states", DiscoveryStates),
        ("found installations retain their discovery failure", FoundInstallFailure),
        ("runtime refusal locks launch and survives diagnostic export", RuntimeRefusal),
        ("an unreadable running xstarter fails closed as ObserverUnavailable", UnreadableXStarterFailsClosed),
        ("a clean observation releases a refused baseline", RefusedBaselineClears),
        ("channel probes for one root retain the matching installation failure", MixedChannelDiscovery),
        ("an empty Steam library is distinct from a partial installation", EmptySteamLibrary),
        ("game file versions require four numeric parts", GameVersions),
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
        ("the catalog adapts to any discovered launcher version directory", CatalogResolvesDiscoveredVersions),
        ("no component requirement omits its trusted signer anchors", ComponentAnchorsPresent),
        ("an updated launcher version still prepares with unknown hashes", UpdatedVersionStillPrepares),
        ("the trust gate passes trusted signers regardless of file content", TrustGatePassesTrustedSigner),
        ("the trust gate survives a certificate rotation via subject identity", TrustGateSurvivesCertificateRotation),
        ("an untrusted signer fails closed as SignerMismatch", UntrustedSignerFailsClosed),
        ("an unreadable signer identity fails closed as SignerMismatch", UnreadableSignerIdentityFailsClosed),
        ("Start re-verifies the backend identity before elevation", StartRefusesDriftedBackend),
        ("Bilibili config discovery preserves its independent identity", BilibiliConfigDiscoveryPreservesIdentity),
        ("Bilibili manual parent selection resolves InfinityNikkiBili", BilibiliManualParentSelectionResolvesGameRoot),
        ("Bilibili marker mismatch fails closed", BilibiliMarkerMismatchFailsClosed),
        ("Bilibili complete installs remain providerless", BilibiliCompleteInstallHasNoProvider),
    ];

    private static Task AfterLaunchWindowOnce()
    {
        var policy = new AfterLaunchWindowPolicy();
        var binding = new OfficialAssistedProcessBinding { AttemptId = Guid.NewGuid(), RootProcessStartTimeUtc = DateTimeOffset.UtcNow };
        var observed = binding.RootProcessStartTimeUtc.AddSeconds(3);
        foreach (var preference in Enum.GetValues<AfterLaunchBehavior>())
        {
            binding = binding with { AttemptId = Guid.NewGuid() };
            AssertEqual(AfterLaunchBehavior.KeepWindow, policy.Observe(binding, LaunchButtonState.Running, observed, preference), "no click");
            policy.Begin(binding.AttemptId);
            AssertEqual(AfterLaunchBehavior.KeepWindow, policy.Observe(binding, LaunchButtonState.Launching, null, preference), "pending");
            AssertEqual(preference, policy.Observe(binding, LaunchButtonState.Running, observed, preference), "first confirmation");
            AssertEqual(AfterLaunchBehavior.KeepWindow, policy.Observe(binding, LaunchButtonState.Running, observed, AfterLaunchBehavior.HideToTray), "already consumed");
        }
        return Task.CompletedTask;
    }

    private static Task AfterLaunchWindowFailures()
    {
        var policy = new AfterLaunchWindowPolicy();
        var binding = new OfficialAssistedProcessBinding { AttemptId = Guid.NewGuid(), RootProcessStartTimeUtc = DateTimeOffset.UtcNow };
        foreach (var state in new[] { LaunchButtonState.Ready, LaunchButtonState.Blocked, LaunchButtonState.CleanupRequired })
        {
            policy.Begin(binding.AttemptId);
            AssertEqual(AfterLaunchBehavior.KeepWindow, policy.Observe(binding, state, null, AfterLaunchBehavior.HideToTray), "unsuccessful attempt");
            AssertEqual(AfterLaunchBehavior.KeepWindow, policy.Observe(binding, LaunchButtonState.Running, binding.RootProcessStartTimeUtc, AfterLaunchBehavior.HideToTray), "late notification");
        }
        policy.Begin(binding.AttemptId);
        AssertEqual(AfterLaunchBehavior.KeepWindow, policy.Observe(binding, LaunchButtonState.Running, binding.RootProcessStartTimeUtc.AddSeconds(46), AfterLaunchBehavior.Minimize), "first observation after deadline");
        policy.Begin(binding.AttemptId);
        AssertEqual(AfterLaunchBehavior.KeepWindow, policy.Observe(null, LaunchButtonState.Ready, null, AfterLaunchBehavior.Minimize), "no bound process after cancellation");
        policy.Begin(Guid.NewGuid());
        AssertEqual(AfterLaunchBehavior.KeepWindow, policy.Observe(binding, LaunchButtonState.Running, binding.RootProcessStartTimeUtc, AfterLaunchBehavior.Minimize), "different attempt");
        policy.Begin(binding.AttemptId);
        policy.Cancel();
        AssertEqual(AfterLaunchBehavior.KeepWindow, policy.Observe(binding, LaunchButtonState.Running, binding.RootProcessStartTimeUtc, AfterLaunchBehavior.Minimize), "close requested during observation");
        return Task.CompletedTask;
    }

    private static Task RegistryPaths()
    {
        const string launcher = @"C:\Program Files\InfinityNikki Launcher\launcher.exe";
        AssertEqual(launcher, WindowsInstallationPathSource.ParseDisplayIcon(launcher), "official display icon");
        const string root = @"C:\Program Files\游戏.exe-cache";
        const string file = root + @"\launcher.exe";
        foreach (var value in new[] { root, $"\"{root}\"" })
            AssertEqual(root, WindowsInstallationPathSource.ParseDirectory(value), "directory");
        foreach (var value in new[] { file, $"\"{file}\"", file + ",-2", $"\"{file}\",0" })
            AssertEqual(file, WindowsInstallationPathSource.ParseDisplayIcon(value), "icon");
        foreach (var value in new[] { file + " /S", $"\"{file}\" /S" })
            AssertEqual(file, WindowsInstallationPathSource.ParseUninstallCommand(value), "command");
        AssertEqual(null, WindowsInstallationPathSource.ParseDirectory(" "), "empty directory");
        AssertEqual(null, WindowsInstallationPathSource.ParseDisplayIcon(null), "empty icon");
        AssertEqual(null, WindowsInstallationPathSource.ParseUninstallCommand(""), "empty command");
        return Task.CompletedTask;
    }

    private sealed class FixedDiscovery(params InstallationProfileCandidate[] candidates) : IInstallationDiscoveryService
    {
        public Task<ProfileBuildResult> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileBuildResult { Candidates = candidates });

        public Task<ProfileBuildResult> DiscoverFromManualGameRootAsync(string selectedDirectory,
            string? selectedLauncherRoot = null, CancellationToken cancellationToken = default) => DiscoverAsync(cancellationToken);
    }

    private static async Task RuntimeRefusal()
    {
        using var layout = FakeInstall.Create();
        var candidate = layout.BuildCandidate() with { Profile = new() { ProfileId = "fake-cn-official" } };
        var components = layout.ComponentReceipts();
        components[3] = components[3] with { FilePath = Environment.ProcessPath! };
        // The game client is this test host: the fake list shows it to Prepare,
        // and the page's own runtime refresh still finds it running for real.
        var host = new FakeProcess(Path.GetFileNameWithoutExtension(Environment.ProcessPath!), Environment.ProcessId,
            Environment.ProcessPath);
        var page = new MainPageViewModel(new JsonUserSettingsStore(layout.BaseDirectory), new WindowsInstallationInspector(),
            new RedactedDiagnosticReportExporter(), new FixedDiscovery(candidate),
            new StubVerifier(ClosedGatePreflight(layout) with { Components = components }), new FakeProcesses(host));
        await page.InitializeAsync();
        AssertEqual("启动游戏", page.LaunchButtonLabel, "ready before refusal");
        var result = await page.PrepareOfficialAssistedLaunchAsync();
        AssertEqual("Preflight.BaselineDirty", result.FailureCode, "test host is the observed component");
        AssertEqual("游戏运行中", page.LaunchButtonLabel, "blocked after refusal");
        AssertTrue(!page.CanAttemptSelectedChannelLaunch && !page.CanCloseGame, "external process is never owned");
        await page.RefreshOfficialAssistedRuntimeAsync();
        AssertEqual("游戏运行中", page.LaunchButtonLabel, "still running");
        var report = await page.ExportDiagnosticsAsync(layout.BaseDirectory);
        var content = await File.ReadAllTextAsync(report.TextFilePath!);
        AssertTrue(content.Contains(result.FailureCode) && content.Contains(result.Detail), "export records code and reason");
    }

    private static async Task UnreadableXStarterFailsClosed()
    {
        // The official launcher runs xstarter elevated, e.g. while it updates the
        // game. A normal-rights process cannot read that path, so it cannot tell
        // whose xstarter is running: Prepare must refuse instead of assuming a
        // clean baseline.
        using var layout = FakeInstall.Create();
        var elevated = new FakeProcess("xstarter", 4242, ExecutablePath: null);
        var result = await RunPrepareAsync(ClosedGatePreflight(layout), layout, new FakeProcesses(elevated));
        AssertFailure(result, "Preflight.ObserverUnavailable");
        AssertEqual("无法读取活动 xstarter 进程的精确路径；未创建新的 provider。", result.Detail, "refusal detail");
    }

    private static async Task RefusedBaselineClears()
    {
        using var layout = FakeInstall.Create();
        var candidate = layout.BuildCandidate() with { Profile = new() { ProfileId = "fake-cn-official" } };
        var page = new MainPageViewModel(new JsonUserSettingsStore(layout.BaseDirectory), new WindowsInstallationInspector(),
            new RedactedDiagnosticReportExporter(), new FixedDiscovery(candidate), new StubVerifier(ClosedGatePreflight(layout)));
        await page.InitializeAsync();
        foreach (var code in new[] { "Preflight.BaselineDirty", "Preflight.ObserverUnavailable" })
        {
            const string detail = "测试目标组件的运行检查被拒绝。";
            var before = DateTimeOffset.UtcNow;
            page.RecordRuntimeRefusal(code, detail, [Path.Combine(layout.GameRoot, "exited-" + Guid.NewGuid() + ".exe")]);
            AssertEqual(code == "Preflight.BaselineDirty" ? "游戏运行中" : "启动条件不满足", page.LaunchButtonLabel, "refused");
            AssertEqual(detail, page.LaunchButtonDetail, "refusal detail");
            await page.RefreshAsync();
            AssertTrue(!page.CanAttemptSelectedChannelLaunch, "static refresh cannot unlock runtime refusal");
            await page.RefreshOfficialAssistedRuntimeAsync();
            AssertEqual("启动游戏", page.LaunchButtonLabel, "clean observation");
            AssertTrue(page.CanAttemptSelectedChannelLaunch, "click enabled");
            var report = await page.ExportDiagnosticsAsync(layout.BaseDirectory);
            var content = await File.ReadAllTextAsync(report.TextFilePath!);
            var match = System.Text.RegularExpressions.Regex.Match(content, @"最近运行检查拒绝（([^）]+)）：");
            AssertTrue(match.Success && DateTimeOffset.TryParse(match.Groups[1].Value, out var when) &&
                when >= before && when <= DateTimeOffset.UtcNow && content.Contains(code) && content.Contains(detail),
                "last refusal retains its time, code and detail after recovery");
        }
    }

    private static async Task FoundInstallFailure()
    {
        using var layout = FakeInstall.Create();
        var incomplete = layout.BuildCandidate() with
        {
            State = InstallationCandidateState.Incomplete, FailureCode = ProfileBuildFailureCode.LayoutMismatch,
            MissingComponents = ["InfinityNikki.exe"],
        };
        foreach (var (candidate, label, detail) in new[]
        {
            (incomplete with { State = InstallationCandidateState.Unsupported, FailureCode = ProfileBuildFailureCode.ChannelMarkerMismatch },
                "渠道身份不符", "product.db"),
            (incomplete, "安装不完整", "InfinityNikki.exe"),
        })
        {
            var page = new MainPageViewModel(new JsonUserSettingsStore(layout.BaseDirectory), new WindowsInstallationInspector(),
                new RedactedDiagnosticReportExporter(), new FixedDiscovery(candidate));
            await page.InitializeAsync();
            AssertEqual(label, page.LaunchButtonLabel, "found installation");
            AssertTrue(page.LaunchButtonDetail.Contains(detail), "specific discovery reason");
        }
    }

    private static async Task MixedChannelDiscovery()
    {
        using var layout = FakeInstall.Create();
        var candidate = layout.BuildCandidate();
        var wrongChannel = candidate with { State = InstallationCandidateState.Unsupported, FailureCode = ProfileBuildFailureCode.ChannelMarkerMismatch };
        var incomplete = candidate with { State = InstallationCandidateState.Incomplete, FailureCode = ProfileBuildFailureCode.LayoutMismatch,
            MissingComponents = ["X6Game-Win64-Shipping.exe"] };
        var page = new MainPageViewModel(new JsonUserSettingsStore(layout.BaseDirectory), new WindowsInstallationInspector(),
            new RedactedDiagnosticReportExporter(), new FixedDiscovery(wrongChannel, incomplete));
        await page.InitializeAsync();
        AssertEqual("安装不完整", page.LaunchButtonLabel, "same root recognized by its own channel");
        AssertTrue(page.LaunchButtonDetail.Contains("X6Game-Win64-Shipping.exe"), "matching channel reports the missing file");
    }

    private static Task EmptySteamLibrary()
    {
        using var layout = FakeInstall.Create();
        var commonRoot = Path.Combine(layout.BaseDirectory, "steamapps", "common", "Infinity Nikki");
        var candidate = new InstallationProfileCandidate
        {
            State = InstallationCandidateState.Incomplete, FailureCode = ProfileBuildFailureCode.SteamManifestMissing,
            LauncherRootPath = commonRoot,
        };
        LaunchButtonState Project(InstallationProfileCandidate item) =>
            LaunchDiscoveryDisplay.WithoutSelection(false, new() { Candidates = [item] }, out _);
        AssertEqual(LaunchButtonState.NotInstalled, Project(candidate), "empty Steam library");
        AssertEqual(LaunchButtonState.Incomplete, Project(candidate with { State = InstallationCandidateState.Downloading }), "staging exists");
        Directory.CreateDirectory(commonRoot);
        AssertEqual(LaunchButtonState.Incomplete, Project(candidate), "installation exists without its manifest");
        return Task.CompletedTask;
    }

    private static Task DiscoveryStates()
    {
        var empty = new ProfileBuildResult();
        var candidate = new InstallationProfileCandidate { State = InstallationCandidateState.Candidate, Profile = new() };
        var multiple = empty with { Candidates = [candidate, candidate with { ProfileId = "steam" }] };
        var ambiguous = empty with { Candidates = [new() { FailureCode = ProfileBuildFailureCode.AmbiguousGameRoot }] };
        AssertEqual(LaunchButtonState.Checking, LaunchDiscoveryDisplay.WithoutSelection(true, empty, out _), "pending");
        AssertEqual(LaunchButtonState.NotInstalled, LaunchDiscoveryDisplay.WithoutSelection(false, empty, out _), "empty");
        AssertEqual(LaunchButtonState.SelectionRequired, LaunchDiscoveryDisplay.WithoutSelection(false, multiple, out _), "multiple");
        AssertEqual(LaunchButtonState.SelectionRequired, LaunchDiscoveryDisplay.WithoutSelection(false, ambiguous, out _), "ambiguous");
        AssertEqual(LaunchButtonState.Blocked, LaunchDiscoveryDisplay.WithoutSelection(false, null, out _), "discovery failed");
        AssertEqual("检查安装中", LaunchButtonState.Checking.Label(), "pending label");
        AssertEqual("未发现安装", LaunchButtonState.NotInstalled.Label(), "empty label");
        AssertEqual("需要选择安装", LaunchButtonState.SelectionRequired.Label(), "selection label");
        return Task.CompletedTask;
    }

    private static Task GameVersions()
    {
        AssertEqual("2.8.1（构建 2828）", LaunchDiscoveryDisplay.FormatGameVersion("2,8,1,2828"), "version");
        foreach (var value in new[] { null, "", "2.8.1", "2,x,1,2828", "2.8.1.-2", "2,8.1,2828", "2.8.1.+2" })
            AssertEqual("未知", LaunchDiscoveryDisplay.FormatGameVersion(value), "invalid version");
        return Task.CompletedTask;
    }

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
            Path.Combine(layout.LauncherRoot, layout.Version, "xstarter.exe"),
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

    private static Task CatalogResolvesDiscoveredVersions()
    {
        // Adaptive since ContractVersion 2: any three-part numeric version
        // directory resolves, and both version-derived relative paths follow
        // the requested directory. Everything else stays frozen.
        foreach (var version in new[] { LauncherVersion, "1.3.2", "1.4.0", "12.0.3" })
        {
            AssertTrue(
                LaunchProviderCatalog.TryGet(DistributionChannel.Official, version, out var contract),
                $"version '{version}' must resolve adaptively");
            AssertEqual(
                Path.Combine(version, "xstarter.exe"),
                contract.BackendRelativeExecutablePath,
                $"backend relative path for {version}");
            AssertEqual(
                Path.Combine(version, "xstarter.exe"),
                contract.RequiredComponents.Single(c => c.ComponentId == "official-backend").RelativePath,
                $"official-backend relative path for {version}");
            AssertEqual(
                LaunchProviderCatalog.CnWindows131ContractId,
                contract.ContractId,
                "the contract id must stay frozen across versions");
            AssertEqual(
                LaunchProviderCatalog.CnWindows131ArgumentPresetId,
                contract.ArgumentPresetId,
                "the argument preset id must stay frozen across versions");
            AssertTrue(
                contract.ArgumentList.SequenceEqual(["-skiplauncher"], StringComparer.Ordinal),
                "the argument list must stay frozen across versions");
            AssertTrue(!contract.ExecutionEnabled, "the execution gate must stay closed across versions");
        }

        foreach (var malformed in new[] { "1.3", "1.3.1.0", "", "1.4.0-beta", "v1.4.0" })
        {
            AssertTrue(
                !LaunchProviderCatalog.TryGet(DistributionChannel.Official, malformed, out _),
                $"malformed version '{malformed}' must not resolve");
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
                component.TrustedSigners.Count > 0,
                $"{component.ComponentId} must pin at least one trusted signer");
            foreach (var signer in component.TrustedSigners)
            {
                AssertTrue(
                    signer.Thumbprints.All(thumbprint => thumbprint.Length == 40),
                    $"{component.ComponentId} signer thumbprints must be 40-hex SHA-1 values");
                AssertTrue(
                    signer.Thumbprints.Count > 0 || !string.IsNullOrWhiteSpace(signer.SubjectCommonName),
                    $"{component.ComponentId} signer must anchor a thumbprint or a subject CN");
            }

            AssertTrue(
                component.ExpectedSignature is AuthenticodeSignatureStatus.Valid,
                $"{component.ComponentId} must require a valid signature");
        }

        // The rotation anchor: Papergames components must survive a certificate
        // renewal via whole-value subject identity, not just the thumbprint.
        var papergames = LaunchProviderCatalog.PapergamesSigner;
        AssertTrue(
            !string.IsNullOrWhiteSpace(papergames.SubjectCommonName) &&
            !string.IsNullOrWhiteSpace(papergames.SubjectSerialNumber),
            "the Papergames signer must pin subject CN and serialNumber for rotation survival");
        AssertEqual(
            "xstarter.exe",
            contract.RequiredComponents.Single(c => c.ComponentId == "official-backend").ExpectedOriginalFilename,
            "the elevated backend must pin its OriginalFilename");
        AssertTrue(
            contract.RequiredComponents
                .Single(c => c.ComponentId == "anti-cheat-artifact")
                .TrustedSigners.All(s => s.SubjectCommonName == LaunchProviderCatalog.AntiCheatExpertSigner.SubjectCommonName),
            "the anti-cheat artifact must trust only the AntiCheatExpert signer");

        AssertEqual("InfinityNikki Launcher", contract.ProductMarker!.ExpectedName!, "product marker name");
        return Task.CompletedTask;
    }

    private static async Task UpdatedVersionStillPrepares()
    {
        // The regression this whole redesign exists for: a machine whose game
        // and launcher already updated (unknown hashes, new version directory)
        // must still prepare a launch without a code change.
        using var layout = FakeInstall.Create("1.4.0");
        var harness = await RunPrepareAsync(ClosedGatePreflight(layout), layout);
        AssertTrue(harness.Succeeded, $"an updated install must prepare, got {harness.FailureCode}: {harness.Detail}");
        AssertEqual(
            Path.Combine(layout.LauncherRoot, "1.4.0", "xstarter.exe"),
            harness.Plan!.ProviderExecutablePath,
            "updated provider path");
        AssertEqual(layout.LauncherRoot, harness.Plan.WorkingDirectory, "updated plan working directory");
        AssertTrue(
            harness.Plan.ArgumentList.SequenceEqual(["-skiplauncher"], StringComparer.Ordinal),
            "the frozen argument list must survive the version change");
    }

    private static async Task TrustGatePassesTrustedSigner()
    {
        using var layout = FakeInstall.Create("1.4.0");
        var verifier = new WindowsLaunchPreflightVerifier(new FakeIdentityProbe(KnownThumbprintIdentity));
        var result = await verifier.VerifyAsync(layout.BuildCandidate());

        AssertTrue(result.StaticIdentityPassed, result.FailureDetail ?? "trusted signers must pass the static gate");
        AssertTrue(!result.ExecutionAllowed, "the execution gate must stay closed");
        AssertEqual(LaunchPreflightFailureCode.ExecutionGateClosed, result.FailureCode, "gate code");
        AssertTrue(result.Plan is null, "a closed gate must not expose a plan");
        AssertEqual(
            Path.Combine("1.4.0", "xstarter.exe"),
            result.Contract!.BackendRelativeExecutablePath,
            "the verifier must materialize the contract for the candidate's version directory");
        AssertEqual(5, result.Components.Count(component => component.Passed), "passing component receipts");
    }

    private static async Task TrustGateSurvivesCertificateRotation()
    {
        // Unknown thumbprints, same legal identity: the subject match keeps the
        // gate open after the publisher renews its certificate.
        using var layout = FakeInstall.Create();
        var verifier = new WindowsLaunchPreflightVerifier(new FakeIdentityProbe(RotatedCertificateIdentity));
        var result = await verifier.VerifyAsync(layout.BuildCandidate());

        AssertTrue(result.StaticIdentityPassed, result.FailureDetail ?? "a rotated certificate with the pinned subject must pass");
        AssertEqual(LaunchPreflightFailureCode.ExecutionGateClosed, result.FailureCode, "gate code after rotation");
    }

    private static async Task UntrustedSignerFailsClosed()
    {
        using var layout = FakeInstall.Create();
        var verifier = new WindowsLaunchPreflightVerifier(new FakeIdentityProbe(_ =>
            new SignerIdentity("0000000000000000000000000000000000000000", "Untrusted Corp", null)));
        var result = await verifier.VerifyAsync(layout.BuildCandidate());

        AssertTrue(!result.StaticIdentityPassed, "an untrusted signer must fail the static gate");
        AssertTrue(!result.ExecutionAllowed, "an untrusted signer must close execution");
        AssertEqual(LaunchPreflightFailureCode.SignerMismatch, result.FailureCode, "untrusted signer failure code");
        AssertTrue(result.Plan is null, "an untrusted signer must not expose a plan");
    }

    private static async Task UnreadableSignerIdentityFailsClosed()
    {
        // Valid WinVerifyTrust status but the leaf certificate cannot be read:
        // attribution is impossible, so the gate must not open.
        using var layout = FakeInstall.Create();
        var verifier = new WindowsLaunchPreflightVerifier(new FakeIdentityProbe(_ => null));
        var result = await verifier.VerifyAsync(layout.BuildCandidate());

        AssertTrue(!result.StaticIdentityPassed, "an unreadable signer identity must fail the static gate");
        AssertEqual(LaunchPreflightFailureCode.SignerMismatch, result.FailureCode, "unreadable signer failure code");
        AssertTrue(result.Plan is null, "an unreadable signer identity must not expose a plan");
    }

    private static async Task StartRefusesDriftedBackend()
    {
        // Prepare succeeds against canned receipts, but the stub xstarter on
        // disk carries no Authenticode signature: the last-instant identity
        // re-check inside Start (which runs the production probe while holding
        // a write/delete-denying handle) must refuse to elevate it.
        using var layout = FakeInstall.Create();
        var preparation = await RunPrepareAsync(ClosedGatePreflight(layout), layout);
        AssertTrue(preparation.Succeeded, $"preparation should succeed, got {preparation.FailureCode}");

        var coordinator = new OfficialAssistedLaunchCoordinator(new StubVerifier(ClosedGatePreflight(layout)));
        var receipt = coordinator.Start(preparation);

        AssertTrue(!receipt.StartRequested, "a drifted backend must never reach Process.Start");
        AssertEqual("Runtime.BackendIdentityDrift", receipt.FailureCode, "drifted backend failure code");
        AssertTrue(receipt.RootProcessId is null, "no pid may be reported for a refused elevation");
    }

    private static SignerIdentity? KnownThumbprintIdentity(string filePath) =>
        filePath.EndsWith("ACE-Service64.exe", StringComparison.OrdinalIgnoreCase)
            ? new SignerIdentity(
                LaunchProviderCatalog.AntiCheatExpertSigner.Thumbprints[0],
                LaunchProviderCatalog.AntiCheatExpertSigner.SubjectCommonName,
                null)
            : new SignerIdentity(
                LaunchProviderCatalog.PapergamesSigner.Thumbprints[0],
                LaunchProviderCatalog.PapergamesSigner.SubjectCommonName,
                LaunchProviderCatalog.PapergamesSigner.SubjectSerialNumber);

    private static SignerIdentity? RotatedCertificateIdentity(string filePath) =>
        filePath.EndsWith("ACE-Service64.exe", StringComparison.OrdinalIgnoreCase)
            ? new SignerIdentity(
                "1111111111111111111111111111111111111111",
                LaunchProviderCatalog.AntiCheatExpertSigner.SubjectCommonName,
                null)
            : new SignerIdentity(
                "2222222222222222222222222222222222222222",
                LaunchProviderCatalog.PapergamesSigner.SubjectCommonName,
                LaunchProviderCatalog.PapergamesSigner.SubjectSerialNumber);

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
        FakeInstall layout,
        FakeProcesses? running = null) =>
        new OfficialAssistedLaunchCoordinator(new StubVerifier(preflight), running ?? new FakeProcesses())
            .PrepareAsync(layout.BuildCandidate());

    private static LaunchPreflightResult ClosedGatePreflight(FakeInstall layout) => new()
    {
        StaticIdentityPassed = true,
        ExecutionAllowed = false,
        FailureCode = LaunchPreflightFailureCode.ExecutionGateClosed,
        Contract = layout.Contract,
        Plan = null,
        Components = layout.ComponentReceipts(),
    };

    /// <summary>
    /// Drives the real verifier against stub files: signature status and signer
    /// identity come from the delegate, everything else (existence, TOCTOU
    /// stability, path containment, marker) runs for real. OriginalFilename is
    /// answered with the on-disk file name, which satisfies the launcher-side
    /// pins for a well-formed layout.
    /// </summary>
    private sealed class FakeIdentityProbe(Func<string, SignerIdentity?> readIdentity) : IAuthenticodeIdentityProbe
    {
        public AuthenticodeVerification VerifySignature(string filePath) =>
            new(AuthenticodeSignatureStatus.Valid, "0x00000000");

        public SignerIdentity? ReadSignerIdentity(string filePath) => readIdentity(filePath);

        public string? ReadOriginalFilename(string filePath) => Path.GetFileName(filePath);
    }

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

    /// <summary>
    /// Stands in for this machine's process list in the clean-baseline check, so
    /// an official launcher running on the test machine never decides a test.
    /// </summary>
    private sealed class FakeProcesses(params FakeProcess[] running) : IBaselineProcessObserver
    {
        public IEnumerable<IBaselineProcess> GetProcessesByName(string processName) =>
            running.Where(process => string.Equals(process.Name, processName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A running process. A null path is one this host may not open: reading it
    /// fails with access denied, as for an elevated xstarter.
    /// </summary>
    private sealed record FakeProcess(string Name, int Id, string? ExecutablePath) : IBaselineProcess
    {
        public bool HasExited => false;

        public string? MainModuleFileName => ExecutablePath ?? throw new System.ComponentModel.Win32Exception(5);

        public void Dispose()
        {
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
            string? versionDirectoryName = null,
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
            string? versionDirectoryName = null,
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
