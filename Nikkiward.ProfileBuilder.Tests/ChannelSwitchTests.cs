using System.Diagnostics;
using Nikkiward.Models;
using Nikkiward.Services;

internal static class ChannelSwitchTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("official activation changes only gameDir and rolls back", OfficialActivationRoundTrips),
        ("activation leaves an already selected gameDir untouched", MatchingActivationDoesNotRewriteConfig),
        ("Bilibili activation uses its independent launcher config", BilibiliActivationUsesIndependentConfig),
        ("portable Bilibili activation creates and rolls back config", PortableBilibiliActivationCreatesConfig),
        ("activation rejects a marker from another channel", ActivationRejectsMarkerMismatch),
        ("Steam activation uses its independent launcher config", SteamActivationUsesIndependentConfig),
        ("portable Steam activation creates and rolls back config", PortableSteamActivationCreatesConfig),
        ("Bilibili launch plan binds the selected xstarter", BilibiliLaunchPlanBindsSelectedXStarter),
        ("Bilibili launch rejects an unbound xstarter", BilibiliLaunchRejectsUnboundXStarter),
        ("Bilibili launch submits only the direct xstarter plan", BilibiliLaunchSubmitsDirectXStarterPlan),
        ("Bilibili direct launch binds only the current game processes", BilibiliDirectLaunchCreatesProcessBinding),
        ("Steam launch plan binds the selected xstarter", SteamLaunchPlanBindsSelectedXStarter),
        ("Steam launch submits only the direct xstarter plan", SteamLaunchSubmitsDirectXStarterPlan),
        ("Steam direct launch binds only its current game processes", SteamDirectLaunchCreatesProcessBinding),
        ("external process binding rejects a mismatched receipt", ExternalProcessBindingRejectsMismatch),
    ];

    private static async Task OfficialActivationRoundTrips()
    {
        using var fixture = new ActivationFixture();
        var oldRoot = fixture.CreateGameRoot("old-official", "InfinityNikki Launcher");
        var targetRoot = fixture.CreateGameRoot("target-official", "InfinityNikki Launcher");
        var configPath = fixture.WriteConfig("InfinityNikki Launcher", oldRoot);
        var service = new WindowsChannelActivationService(fixture.LocalAppData);
        var request = new ChannelActivationRequest
        {
            Candidate = fixture.CreateCandidate(DistributionChannel.Official, oldRoot),
            TargetGameRootPath = targetRoot,
        };

        var plan = await service.CreatePlanAsync(request);
        Assert(plan.CanActivate, $"official activation plan should pass: {plan.FailureCode}");
        var receipt = await service.ActivateAsync(request, plan.PlanSha256);
        Assert(receipt.Succeeded, $"official activation should pass: {receipt.FailureCode}");
        Assert(receipt.ConfigChanged, "official activation should update config.ini");
        AssertPath(targetRoot, LauncherConfigReader.TryReadGameDirectory(configPath), "activated official root");
        var activatedText = File.ReadAllText(configPath);
        Assert(activatedText.Contains("PaperLauncherToken=preserve-me", StringComparison.Ordinal), "unrelated config values must remain");
        AssertEqual(1, activatedText.Split("gameDir=", StringSplitOptions.None).Length - 1, "gameDir line count");

        var rollback = await service.RollbackAsync(receipt);
        Assert(rollback.Succeeded, $"official activation rollback should pass: {rollback.FailureCode}");
        AssertPath(oldRoot, LauncherConfigReader.TryReadGameDirectory(configPath), "restored official root");
    }

    private static async Task BilibiliActivationUsesIndependentConfig()
    {
        using var fixture = new ActivationFixture();
        var officialRoot = fixture.CreateGameRoot("official", "InfinityNikki Launcher");
        var oldBilibiliRoot = fixture.CreateGameRoot("old-bilibili", "InfinityNikkiBili Launcher");
        var targetBilibiliRoot = fixture.CreateGameRoot("target-bilibili", "InfinityNikkiBili Launcher");
        var officialConfig = fixture.WriteConfig("InfinityNikki Launcher", officialRoot);
        var bilibiliConfig = fixture.WriteConfig("InfinityNikkiBili Launcher", oldBilibiliRoot);
        var service = new WindowsChannelActivationService(fixture.LocalAppData);
        var request = new ChannelActivationRequest
        {
            Candidate = fixture.CreateCandidate(DistributionChannel.Bilibili, oldBilibiliRoot),
            TargetGameRootPath = targetBilibiliRoot,
        };

        var plan = await service.CreatePlanAsync(request);
        Assert(plan.CanActivate, $"Bilibili activation plan should pass: {plan.FailureCode}");
        var receipt = await service.ActivateAsync(request, plan.PlanSha256);
        Assert(receipt.Succeeded, $"Bilibili activation should pass: {receipt.FailureCode}");
        AssertPath(targetBilibiliRoot, LauncherConfigReader.TryReadGameDirectory(bilibiliConfig), "activated Bilibili root");
        AssertPath(officialRoot, LauncherConfigReader.TryReadGameDirectory(officialConfig), "official config must remain unchanged");
    }

    private static async Task PortableBilibiliActivationCreatesConfig()
    {
        using var fixture = new ActivationFixture();
        var targetRoot = fixture.CreateGameRoot("portable-bilibili", "InfinityNikkiBili Launcher");
        var candidate = fixture.CreateCandidate(DistributionChannel.Bilibili, targetRoot) with
        {
            DiscoverySource = ProfileDiscoverySource.ChannelStoreReceipt,
        };
        var service = new WindowsChannelActivationService(fixture.LocalAppData);
        var request = new ChannelActivationRequest
        {
            Candidate = candidate,
            TargetGameRootPath = targetRoot,
        };

        var plan = await service.CreatePlanAsync(request);
        Assert(plan.CanActivate, $"portable Bilibili activation should plan: {plan.FailureCode}");
        Assert(plan.CreatesLauncherConfig, "portable Bilibili plan must create a minimal config");
        var receipt = await service.ActivateAsync(request, plan.PlanSha256);
        Assert(receipt.Succeeded, $"portable Bilibili activation should succeed: {receipt.FailureDetail}");
        Assert(receipt.LauncherConfigCreated, "portable Bilibili receipt must record config creation");
        Assert(File.Exists(receipt.LauncherConfigPath), "portable Bilibili config should exist");
        var config = await File.ReadAllTextAsync(receipt.LauncherConfigPath!);
        AssertEqual($"gameDir={targetRoot.Replace('\\', '/')}\r\n", config, "portable Bilibili config content");
        Assert(!config.Contains("Token", StringComparison.OrdinalIgnoreCase), "portable config must not contain credentials");

        var rollback = await service.RollbackAsync(receipt);
        Assert(rollback.Succeeded, $"portable Bilibili config rollback should succeed: {rollback.FailureDetail}");
        Assert(!File.Exists(receipt.LauncherConfigPath), "portable Bilibili config should be removed by rollback");
    }

    private static async Task MatchingActivationDoesNotRewriteConfig()
    {
        using var fixture = new ActivationFixture();
        var root = fixture.CreateGameRoot("selected-official", "InfinityNikki Launcher");
        var configPath = fixture.WriteConfig("InfinityNikki Launcher", root);
        var before = File.GetLastWriteTimeUtc(configPath);
        var service = new WindowsChannelActivationService(fixture.LocalAppData);
        var request = new ChannelActivationRequest
        {
            Candidate = fixture.CreateCandidate(DistributionChannel.Official, root),
            TargetGameRootPath = root,
        };

        var plan = await service.CreatePlanAsync(request);
        var receipt = await service.ActivateAsync(request, plan.PlanSha256);

        Assert(receipt.Succeeded, $"matching activation should pass: {receipt.FailureCode}");
        Assert(!receipt.ConfigChanged, "matching activation must not rewrite config.ini");
        AssertEqual(before, File.GetLastWriteTimeUtc(configPath), "config write time");
        AssertPath(root, LauncherConfigReader.TryReadGameDirectory(configPath), "matching root");
    }

    private static async Task ActivationRejectsMarkerMismatch()
    {
        using var fixture = new ActivationFixture();
        var oldRoot = fixture.CreateGameRoot("old", "InfinityNikki Launcher");
        var wrongTarget = fixture.CreateGameRoot("wrong", "InfinityNikkiBili Launcher");
        fixture.WriteConfig("InfinityNikki Launcher", oldRoot);
        var service = new WindowsChannelActivationService(fixture.LocalAppData);
        var plan = await service.CreatePlanAsync(new ChannelActivationRequest
        {
            Candidate = fixture.CreateCandidate(DistributionChannel.Official, oldRoot),
            TargetGameRootPath = wrongTarget,
        });

        Assert(!plan.CanActivate, "cross-channel marker must be rejected");
        AssertEqual(ChannelActivationFailureCode.MarkerMismatch, plan.FailureCode, "marker mismatch code");
    }

    private static async Task SteamActivationUsesIndependentConfig()
    {
        using var fixture = new ActivationFixture();
        var officialRoot = fixture.CreateGameRoot("official", "InfinityNikki Launcher");
        var bilibiliRoot = fixture.CreateGameRoot("bilibili", "InfinityNikkiBili Launcher");
        var oldSteamRoot = fixture.CreateGameRoot("old-steam", "InfinityNikkiSteam Launcher");
        var targetSteamRoot = fixture.CreateGameRoot("target-steam", "InfinityNikkiSteam Launcher");
        var officialConfig = fixture.WriteConfig("InfinityNikki Launcher", officialRoot);
        var bilibiliConfig = fixture.WriteConfig("InfinityNikkiBili Launcher", bilibiliRoot);
        var steamConfig = fixture.WriteConfig("InfinityNikkiSteam Launcher", oldSteamRoot);
        var appManifest = Path.Combine(fixture.Root, "steamapps", "appmanifest_3164330.acf");
        Directory.CreateDirectory(Path.GetDirectoryName(appManifest)!);
        File.WriteAllText(appManifest, "\"AppState\"\r\n{\r\n    \"appid\" \"3164330\"\r\n}\r\n");
        var officialConfigBefore = File.ReadAllBytes(officialConfig);
        var bilibiliConfigBefore = File.ReadAllBytes(bilibiliConfig);
        var appManifestBefore = File.ReadAllBytes(appManifest);
        var service = new WindowsChannelActivationService(fixture.LocalAppData);
        var request = new ChannelActivationRequest
        {
            Candidate = fixture.CreateCandidate(DistributionChannel.Steam, targetSteamRoot) with
            {
                DiscoverySource = ProfileDiscoverySource.ChannelStoreReceipt,
                SteamManifest = null,
            },
            TargetGameRootPath = targetSteamRoot,
        };

        var plan = await service.CreatePlanAsync(request);
        Assert(plan.CanActivate, $"Steam activation plan should pass: {plan.FailureCode}");
        AssertPath(steamConfig, plan.LauncherConfigPath, "Steam launcher config path");
        AssertPath(oldSteamRoot, plan.PreviousGameRootPath, "Steam previous game root");
        var receipt = await service.ActivateAsync(request, plan.PlanSha256);
        Assert(receipt.Succeeded, $"Steam activation should pass: {receipt.FailureDetail}");
        Assert(receipt.ConfigChanged, "Steam activation should update config.ini");
        AssertPath(targetSteamRoot, LauncherConfigReader.TryReadGameDirectory(steamConfig), "activated Steam root");
        Assert(officialConfigBefore.SequenceEqual(File.ReadAllBytes(officialConfig)), "official config must remain unchanged");
        Assert(bilibiliConfigBefore.SequenceEqual(File.ReadAllBytes(bilibiliConfig)), "Bilibili config must remain unchanged");
        Assert(appManifestBefore.SequenceEqual(File.ReadAllBytes(appManifest)), "Steam appmanifest must remain unchanged");

        var rollback = await service.RollbackAsync(receipt);
        Assert(rollback.Succeeded, $"Steam activation rollback should pass: {rollback.FailureDetail}");
        AssertPath(oldSteamRoot, LauncherConfigReader.TryReadGameDirectory(steamConfig), "restored Steam root");
        Assert(appManifestBefore.SequenceEqual(File.ReadAllBytes(appManifest)), "Steam appmanifest must remain unchanged after rollback");
    }

    private static async Task PortableSteamActivationCreatesConfig()
    {
        using var fixture = new ActivationFixture();
        var targetRoot = fixture.CreateGameRoot("portable-steam", "InfinityNikkiSteam Launcher");
        var candidate = fixture.CreateCandidate(DistributionChannel.Steam, targetRoot) with
        {
            DiscoverySource = ProfileDiscoverySource.ChannelStoreReceipt,
            SteamManifest = null,
        };
        var service = new WindowsChannelActivationService(fixture.LocalAppData);
        var request = new ChannelActivationRequest
        {
            Candidate = candidate,
            TargetGameRootPath = targetRoot,
        };

        var plan = await service.CreatePlanAsync(request);
        Assert(plan.CanActivate, $"portable Steam activation should plan: {plan.FailureCode}");
        Assert(plan.CreatesLauncherConfig, "portable Steam plan must create a minimal config");
        var receipt = await service.ActivateAsync(request, plan.PlanSha256);
        Assert(receipt.Succeeded, $"portable Steam activation should succeed: {receipt.FailureDetail}");
        Assert(receipt.LauncherConfigCreated, "portable Steam receipt must record config creation");
        Assert(File.Exists(receipt.LauncherConfigPath), "portable Steam config should exist");
        var config = await File.ReadAllTextAsync(receipt.LauncherConfigPath!);
        AssertEqual($"gameDir={targetRoot.Replace('\\', '/')}\r\n", config, "portable Steam config content");
        Assert(!config.Contains("Token", StringComparison.OrdinalIgnoreCase), "portable Steam config must not contain credentials");

        var rollback = await service.RollbackAsync(receipt);
        Assert(rollback.Succeeded, $"portable Steam config rollback should succeed: {rollback.FailureDetail}");
        Assert(!File.Exists(receipt.LauncherConfigPath), "portable Steam config should be removed by rollback");
    }

    private static Task BilibiliLaunchPlanBindsSelectedXStarter()
    {
        using var fixture = new ActivationFixture();
        var bilibiliRoot = fixture.CreateGameRoot("bilibili", "InfinityNikkiBili Launcher");
        var bilibiliCandidate = fixture.CreateCandidate(DistributionChannel.Bilibili, bilibiliRoot);
        fixture.AddXStarter(bilibiliCandidate, "1.4.0");
        var launcher = new WindowsChannelEntryLauncher();
        var bilibiliPlan = launcher.CreatePlan(bilibiliCandidate);
        Assert(bilibiliPlan.CanLaunch, $"Bilibili direct plan should pass: {bilibiliPlan.FailureCode}");
        AssertEqual(ChannelLaunchEntryKind.BilibiliXStarterDirect, bilibiliPlan.EntryKind, "Bilibili entry kind");
        AssertPath(bilibiliCandidate.Profile!.XStarterPath, bilibiliPlan.FileName, "Bilibili xstarter path");
        AssertPath(bilibiliCandidate.LauncherRootPath, bilibiliPlan.WorkingDirectory, "Bilibili working directory");
        AssertEqual(1, bilibiliPlan.ArgumentList.Count, "Bilibili argument count");
        AssertEqual("-skiplauncher", bilibiliPlan.ArgumentList[0], "Bilibili argument");
        Assert(bilibiliPlan.RequiresElevation, "Bilibili xstarter manifest requires elevation");
        AssertEqual("xstarter.exe", Path.GetFileName(bilibiliPlan.FileName), "Bilibili executable name");
        Assert(!string.Equals(bilibiliPlan.FileName, bilibiliCandidate.Profile.LauncherPath, StringComparison.OrdinalIgnoreCase), "Bilibili plan must not use launcher.exe");
        Assert(!bilibiliPlan.FileName.StartsWith("steam://", StringComparison.OrdinalIgnoreCase), "Bilibili plan must not use a Steam URI");
        Assert(!bilibiliPlan.ArgumentList.Any(argument => argument.Contains("SkipLauncherTokenCheck", StringComparison.OrdinalIgnoreCase)), "Bilibili plan must not use the SteamOS argument");
        Assert(VariantHash.IsSha256(bilibiliPlan.PlanSha256), "Bilibili plan digest");
        return Task.CompletedTask;
    }

    private static Task BilibiliLaunchRejectsUnboundXStarter()
    {
        using var fixture = new ActivationFixture();
        var gameRoot = fixture.CreateGameRoot("bilibili-invalid", "InfinityNikkiBili Launcher");
        var candidate = fixture.CreateCandidate(DistributionChannel.Bilibili, gameRoot);
        var launcher = new WindowsChannelEntryLauncher();

        var missing = candidate with
        {
            Profile = candidate.Profile! with
            {
                XStarterPath = Path.Combine(candidate.LauncherRootPath!, "1.3.2", "xstarter.exe"),
            },
        };
        var missingPlan = launcher.CreatePlan(missing);
        Assert(!missingPlan.CanLaunch, "missing Bilibili xstarter must fail closed");
        AssertEqual(ChannelLaunchFailureCode.LauncherMissing, missingPlan.FailureCode, "missing xstarter code");

        var outsidePath = fixture.CreateOutsideXStarter();
        var outside = candidate with
        {
            Profile = candidate.Profile! with { XStarterPath = outsidePath },
        };
        var outsidePlan = launcher.CreatePlan(outside);
        Assert(!outsidePlan.CanLaunch, "outside Bilibili xstarter must fail closed");
        AssertEqual(ChannelLaunchFailureCode.LauncherMissing, outsidePlan.FailureCode, "outside xstarter code");

        var invalidVersionPath = fixture.AddXStarter(candidate, "current");
        var invalidVersion = candidate with
        {
            Profile = candidate.Profile! with { XStarterPath = invalidVersionPath },
        };
        var invalidVersionPlan = launcher.CreatePlan(invalidVersion);
        Assert(!invalidVersionPlan.CanLaunch, "non-version Bilibili xstarter must fail closed");
        AssertEqual(ChannelLaunchFailureCode.LauncherMissing, invalidVersionPlan.FailureCode, "non-version xstarter code");
        return Task.CompletedTask;
    }

    private static async Task BilibiliLaunchSubmitsDirectXStarterPlan()
    {
        using var fixture = new ActivationFixture();
        var gameRoot = fixture.CreateGameRoot("bilibili-submit", "InfinityNikkiBili Launcher");
        var candidate = fixture.CreateCandidate(DistributionChannel.Bilibili, gameRoot);
        var processStarter = new RecordingChannelProcessStarter(4242);
        var launcher = new WindowsChannelEntryLauncher(processStarter);
        var plan = launcher.CreatePlan(candidate);

        var changedReceipt = await launcher.LaunchAsync(candidate, new string('0', 64));
        Assert(!changedReceipt.Succeeded, "changed Bilibili plan must not launch");
        AssertEqual(ChannelLaunchFailureCode.PlanChanged, changedReceipt.FailureCode, "changed Bilibili plan code");
        AssertEqual(0, processStarter.CallCount, "changed plan process count");

        var receipt = await launcher.LaunchAsync(candidate, plan.PlanSha256);
        Assert(receipt.Succeeded, $"Bilibili direct submission should succeed: {receipt.FailureDetail}");
        AssertEqual(1, processStarter.CallCount, "Bilibili process count");
        AssertEqual(4242, receipt.SubmittedProcessId, "Bilibili submitted process id");
        Assert(receipt.AttemptId != Guid.Empty, "Bilibili receipt attempt id");
        AssertEqual(processStarter.StartTimeUtc, receipt.SubmittedProcessStartTimeUtc, "Bilibili process start time");
        var startInfo = processStarter.LastStartInfo ?? throw new InvalidOperationException("Bilibili start info was not captured");
        AssertPath(candidate.Profile!.XStarterPath, startInfo.FileName, "submitted Bilibili xstarter");
        AssertPath(candidate.LauncherRootPath, startInfo.WorkingDirectory, "submitted Bilibili working directory");
        Assert(startInfo.UseShellExecute, "Bilibili elevation must use Windows shell execution");
        AssertEqual("runas", startInfo.Verb, "Bilibili elevation verb");
        AssertEqual(string.Empty, startInfo.Arguments, "Bilibili concatenated arguments");
        AssertEqual(1, startInfo.ArgumentList.Count, "submitted Bilibili argument count");
        AssertEqual("-skiplauncher", startInfo.ArgumentList[0], "submitted Bilibili argument");
    }

    private static async Task BilibiliDirectLaunchCreatesProcessBinding()
    {
        using var fixture = new ActivationFixture();
        var gameRoot = fixture.CreateGameRoot("bilibili-binding", "InfinityNikkiBili Launcher");
        var candidate = fixture.CreateCandidate(DistributionChannel.Bilibili, gameRoot);
        var processStarter = new RecordingChannelProcessStarter(4242);
        var launcher = new WindowsChannelEntryLauncher(processStarter);
        var plan = launcher.CreatePlan(candidate);
        var receipt = await launcher.LaunchAsync(candidate, plan.PlanSha256);

        Assert(
            Nikkiward.ViewModels.ExternalChannelProcessBindingFactory.TryCreate(
                candidate,
                receipt,
                out var binding),
            "a complete Bilibili receipt should create a process binding");
        AssertEqual(candidate.ProfileId, binding.ProfileId, "Bilibili binding profile id");
        AssertEqual(4242, binding.RootProcessId, "Bilibili binding root process id");
        AssertEqual(processStarter.StartTimeUtc, binding.RootProcessStartTimeUtc, "Bilibili binding start time");
        AssertPath(candidate.Profile!.XStarterPath, binding.RootExecutablePath, "Bilibili binding root path");
        AssertEqual(2, binding.GameProcessPaths.Count, "Bilibili binding game path count");
        Assert(
            binding.GameProcessPaths.All(path =>
                path.EndsWith("InfinityNikki.exe", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("X6Game-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase)),
            "the Bilibili binding must exclude launcher, xstarter, and SDK helpers");
        AssertPath(candidate.Profile.ShippingExecutablePath, binding.RunningProcessPath, "Bilibili running process path");
        AssertEqual(2, binding.AuxiliaryProcessPaths.Count, "Bilibili auxiliary path count");
        Assert(
            binding.AuxiliaryProcessPaths.Any(path =>
                path.EndsWith("PCGamePlatform.exe", StringComparison.OrdinalIgnoreCase)),
            "the Bilibili binding must include PCGamePlatform");
        Assert(
            binding.AuxiliaryProcessPaths.Any(path =>
                path.EndsWith("game_security_protection.exe", StringComparison.OrdinalIgnoreCase)),
            "the Bilibili binding must include game security protection");
    }

    private static Task SteamLaunchPlanBindsSelectedXStarter()
    {
        using var fixture = new ActivationFixture();
        var steamRoot = fixture.CreateGameRoot("steam", "InfinityNikkiSteam Launcher");
        var steamCandidate = fixture.CreateCandidate(DistributionChannel.Steam, steamRoot);
        var launcher = new WindowsChannelEntryLauncher();
        var steamPlan = launcher.CreatePlan(steamCandidate);
        Assert(steamPlan.CanLaunch, $"Steam direct plan should pass: {steamPlan.FailureCode}");
        AssertEqual(ChannelLaunchEntryKind.SteamXStarterDirect, steamPlan.EntryKind, "Steam entry kind");
        AssertPath(steamCandidate.Profile!.XStarterPath, steamPlan.FileName, "Steam xstarter path");
        AssertPath(steamCandidate.LauncherRootPath, steamPlan.WorkingDirectory, "Steam working directory");
        AssertEqual(1, steamPlan.ArgumentList.Count, "Steam argument count");
        AssertEqual("-skiplauncher", steamPlan.ArgumentList[0], "Steam argument");
        Assert(steamPlan.RequiresElevation, "Steam xstarter manifest requires elevation");
        AssertEqual("xstarter.exe", Path.GetFileName(steamPlan.FileName), "Steam executable name");
        Assert(!string.Equals(steamPlan.FileName, steamCandidate.Profile.LauncherPath, StringComparison.OrdinalIgnoreCase), "Steam plan must not use launcher.exe");
        Assert(!steamPlan.FileName.StartsWith("steam://", StringComparison.OrdinalIgnoreCase), "Steam plan must not use a Steam URI");
        Assert(!steamPlan.ArgumentList.Any(argument => argument.Contains("SkipLauncherTokenCheck", StringComparison.OrdinalIgnoreCase)), "Steam plan must not use the SteamOS argument");
        Assert(VariantHash.IsSha256(steamPlan.PlanSha256), "Steam plan digest");
        return Task.CompletedTask;
    }

    private static async Task SteamLaunchSubmitsDirectXStarterPlan()
    {
        using var fixture = new ActivationFixture();
        var steamRoot = fixture.CreateGameRoot("steam-submit", "InfinityNikkiSteam Launcher");
        var steamCandidate = fixture.CreateCandidate(DistributionChannel.Steam, steamRoot);
        var processStarter = new RecordingChannelProcessStarter(4242);
        var launcher = new WindowsChannelEntryLauncher(processStarter);
        var steamPlan = launcher.CreatePlan(steamCandidate);

        var changedReceipt = await launcher.LaunchAsync(steamCandidate, new string('0', 64));
        Assert(!changedReceipt.Succeeded, "changed Steam plan must not launch");
        AssertEqual(ChannelLaunchFailureCode.PlanChanged, changedReceipt.FailureCode, "changed Steam plan code");
        AssertEqual(0, processStarter.CallCount, "changed Steam plan process count");

        var receipt = await launcher.LaunchAsync(steamCandidate, steamPlan.PlanSha256);
        Assert(receipt.Succeeded, $"Steam direct submission should succeed: {receipt.FailureDetail}");
        AssertEqual(1, processStarter.CallCount, "Steam process count");
        AssertEqual(4242, receipt.SubmittedProcessId, "Steam submitted process id");
        Assert(receipt.AttemptId != Guid.Empty, "Steam receipt attempt id");
        AssertEqual(processStarter.StartTimeUtc, receipt.SubmittedProcessStartTimeUtc, "Steam process start time");
        var startInfo = processStarter.LastStartInfo ?? throw new InvalidOperationException("Steam start info was not captured");
        AssertPath(steamCandidate.Profile!.XStarterPath, startInfo.FileName, "submitted Steam xstarter");
        AssertPath(steamCandidate.LauncherRootPath, startInfo.WorkingDirectory, "submitted Steam working directory");
        Assert(startInfo.UseShellExecute, "Steam elevation must use Windows shell execution");
        AssertEqual("runas", startInfo.Verb, "Steam elevation verb");
        AssertEqual(string.Empty, startInfo.Arguments, "Steam concatenated arguments");
        AssertEqual(1, startInfo.ArgumentList.Count, "submitted Steam argument count");
        AssertEqual("-skiplauncher", startInfo.ArgumentList[0], "submitted Steam argument");
    }

    private static async Task SteamDirectLaunchCreatesProcessBinding()
    {
        using var fixture = new ActivationFixture();
        var gameRoot = fixture.CreateGameRoot("steam-binding", "InfinityNikkiSteam Launcher");
        var candidate = fixture.CreateCandidate(DistributionChannel.Steam, gameRoot);
        var processStarter = new RecordingChannelProcessStarter(4242);
        var launcher = new WindowsChannelEntryLauncher(processStarter);
        var plan = launcher.CreatePlan(candidate);
        var receipt = await launcher.LaunchAsync(candidate, plan.PlanSha256);

        Assert(
            Nikkiward.ViewModels.ExternalChannelProcessBindingFactory.TryCreate(
                candidate,
                receipt,
                out var binding),
            "a complete Steam receipt should create a process binding");
        AssertPath(candidate.Profile!.XStarterPath, binding.RootExecutablePath, "Steam binding root path");
        AssertPath(candidate.Profile.ShippingExecutablePath, binding.RunningProcessPath, "Steam running process path");
        AssertEqual(2, binding.GameProcessPaths.Count, "Steam binding game path count");
        AssertEqual(0, binding.AuxiliaryProcessPaths.Count, "Steam binding auxiliary path count");
    }

    private static Task ExternalProcessBindingRejectsMismatch()
    {
        using var fixture = new ActivationFixture();
        var gameRoot = fixture.CreateGameRoot("bilibili-mismatch", "InfinityNikkiBili Launcher");
        var candidate = fixture.CreateCandidate(DistributionChannel.Bilibili, gameRoot);
        var receipt = new ChannelLaunchReceipt
        {
            AttemptId = Guid.NewGuid(),
            Succeeded = true,
            DistributionChannel = DistributionChannel.Steam,
            ProfileId = candidate.ProfileId,
            SubmittedProcessId = 4242,
            SubmittedProcessStartTimeUtc = DateTimeOffset.UtcNow,
        };

        Assert(
            !Nikkiward.ViewModels.ExternalChannelProcessBindingFactory.TryCreate(
                candidate,
                receipt,
                out _),
            "a receipt from another channel must not bind");

        var foreignRoot = fixture.CreateGameRoot("foreign-profile", "InfinityNikkiBili Launcher");
        var foreignPathCandidate = candidate with
        {
            Profile = candidate.Profile! with
            {
                GameExecutablePath = Path.Combine(foreignRoot, "InfinityNikki.exe"),
            },
        };
        var matchingReceipt = receipt with
        {
            DistributionChannel = DistributionChannel.Bilibili,
        };
        Assert(
            !Nikkiward.ViewModels.ExternalChannelProcessBindingFactory.TryCreate(
                foreignPathCandidate,
                matchingReceipt,
                out _),
            "a game executable outside the selected root must not bind");

        var platformPath = Path.Combine(
            gameRoot,
            "X6Game",
            "Plugins",
            "PaperSDK",
            "PSDKChannelBili",
            "Source",
            "ThirdParty",
            "PSDKChannelBiliLibrary",
            "x64",
            "Release",
            "BLPlatform64",
            "PCGamePlatform.exe");
        File.Delete(platformPath);
        Assert(
            !Nikkiward.ViewModels.ExternalChannelProcessBindingFactory.TryCreate(
                candidate,
                matchingReceipt,
                out _),
            "a Bilibili binding without its platform helper must fail closed");
        return Task.CompletedTask;
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

    private static void AssertPath(string? expected, string? actual, string message)
    {
        Assert(expected is not null && actual is not null, $"{message}: path is null");
        AssertEqual(
            Path.GetFullPath(expected!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(actual!).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            message);
    }

    private sealed class ActivationFixture : IDisposable
    {
        public ActivationFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"Nikkiward-ChannelSwitch-{Guid.NewGuid():N}");
            LocalAppData = Path.Combine(Root, "local-app-data");
            Directory.CreateDirectory(LocalAppData);
        }

        public string Root { get; }

        public string LocalAppData { get; }

        public string CreateGameRoot(string name, string markerName)
        {
            var root = Path.Combine(Root, name);
            Touch(Path.Combine(root, "InfinityNikki.exe"));
            Touch(Path.Combine(root, "X6Game", "Binaries", "Win64", "X6Game-Win64-Shipping.exe"));
            if (string.Equals(markerName, "InfinityNikkiBili Launcher", StringComparison.Ordinal))
            {
                var platformRoot = Path.Combine(
                    root,
                    "X6Game",
                    "Plugins",
                    "PaperSDK",
                    "PSDKChannelBili",
                    "Source",
                    "ThirdParty",
                    "PSDKChannelBiliLibrary",
                    "x64",
                    "Release",
                    "BLPlatform64");
                Touch(Path.Combine(platformRoot, "PCGamePlatform.exe"));
                Touch(Path.Combine(platformRoot, "game_security_protection.exe"));
            }
            File.WriteAllText(Path.Combine(root, "product.db"), $"{{\"name\":\"{markerName}\",\"version\":2828}}");
            return root;
        }

        public string WriteConfig(string launcherDirectoryName, string gameRoot)
        {
            var directory = Path.Combine(LocalAppData, launcherDirectoryName);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "config.ini");
            File.WriteAllText(
                path,
                $"PaperLauncherToken=preserve-me\r\ngameDir={gameRoot.Replace('\\', '/')}\r\nother=value\r\n");
            return path;
        }

        public InstallationProfileCandidate CreateCandidate(
            DistributionChannel channel,
            string gameRoot)
        {
            var launcherRoot = Path.Combine(Root, $"launcher-{channel}");
            var launcherPath = Path.Combine(launcherRoot, "launcher.exe");
            var xstarterPath = Path.Combine(launcherRoot, "1.3.1", "xstarter.exe");
            Touch(launcherPath);
            Touch(xstarterPath);
            var identity = channel switch
            {
                DistributionChannel.Official => new ProfileIdentity
                {
                    RegionFamily = RegionFamily.MainlandChina,
                    DistributionChannel = channel,
                    AccountAuthority = AccountAuthority.Papergames,
                },
                DistributionChannel.Bilibili => new ProfileIdentity
                {
                    RegionFamily = RegionFamily.MainlandChina,
                    DistributionChannel = channel,
                    AccountAuthority = AccountAuthority.Bilibili,
                },
                DistributionChannel.Steam => new ProfileIdentity
                {
                    RegionFamily = RegionFamily.Overseas,
                    DistributionChannel = channel,
                    AccountAuthority = AccountAuthority.Steam,
                    SteamAppId = "3164330",
                },
                _ => new ProfileIdentity(),
            };
            var profile = new LaunchProfile
            {
                ProfileId = $"test-{channel}",
                DisplayName = channel.ToString(),
                Channel = channel.ToString(),
                GameRootPath = gameRoot,
                LauncherPath = launcherPath,
                XStarterPath = xstarterPath,
                GameExecutablePath = Path.Combine(gameRoot, "InfinityNikki.exe"),
                ShippingExecutablePath = Path.Combine(gameRoot, "X6Game", "Binaries", "Win64", "X6Game-Win64-Shipping.exe"),
            };
            return new InstallationProfileCandidate
            {
                ProfileId = profile.ProfileId,
                DisplayName = profile.DisplayName,
                Identity = identity,
                State = InstallationCandidateState.Candidate,
                LauncherRootPath = launcherRoot,
                GameRootPath = gameRoot,
                Profile = profile,
            };
        }

        public string AddXStarter(InstallationProfileCandidate candidate, string version)
        {
            var path = Path.Combine(candidate.LauncherRootPath!, version, "xstarter.exe");
            Touch(path);
            return path;
        }

        public string CreateOutsideXStarter()
        {
            var path = Path.Combine(Root, "outside", "1.3.1", "xstarter.exe");
            Touch(path);
            return path;
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

        private static void Touch(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, []);
        }
    }

    private sealed class RecordingChannelProcessStarter(int? processId) : IChannelProcessStarter
    {
        public DateTimeOffset StartTimeUtc { get; } =
            new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);

        public int CallCount { get; private set; }

        public ProcessStartInfo? LastStartInfo { get; private set; }

        public ChannelProcessStartResult? Start(ProcessStartInfo startInfo)
        {
            CallCount++;
            LastStartInfo = startInfo;
            return processId is int id
                ? new ChannelProcessStartResult(id, StartTimeUtc)
                : null;
        }
    }
}
