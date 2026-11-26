using Nikkiward.Models;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Launcher.Skins;

internal sealed record HomeOverlayTechnical(
    string GameRootPath,
    string ChannelText,
    string SelectedChannelLaunchRoute,
    string ManualDiscoveryStatusText,
    string LaunchCapabilityText,
    string CapabilityDetailText,
    string StaticIdentityText,
    string StaticReadinessText,
    string ProviderContractId,
    string ProviderExecutionEnabledText,
    string ManualAttemptAuthorizationText,
    string ProviderExecutablePath,
    string ProviderWorkingDirectory,
    string ProviderArgumentVector,
    string ProviderEvidenceState,
    string ProviderObservationText,
    string ExternalEvidenceText,
    string LatestControlledReceiptText,
    string ApplicationExecutionText,
    string InstallationCandidateText,
    string LifecycleText,
    string ProviderPreflightText,
    string ProviderObservationPolicyText,
    string LastUpdatedText,
    string ActivityText,
    string LastErrorText,
    string HomeFallback,
    string Signature, string LaunchReason, string ExecutionGate, string? FailureCode, string? FailureDetail);

internal sealed record HomeCheck(string Text, bool? Passed = null);
internal sealed record HomeChecks(HomeCheck Directory, HomeCheck Channel, HomeCheck Signature,
    HomeCheck Integrity, HomeCheck Processes, HomeCheck Gate);

internal sealed record HomeComponent(string DisplayName, string StatusText, string FileVersionText, string FilePath)
{
    internal static string Name(string id) => id switch
    {
        "official-launcher" => "官方启动器",
        "official-backend" => "官方启动程序",
        "game-bootstrap" => "游戏引导程序",
        "game-client" => "游戏客户端",
        "anti-cheat" or "anti-cheat-artifact" => "反作弊组件",
        _ => "游戏组件"
    };

    internal static HomeComponent From(ComponentItemViewModel row, LaunchPreflightResult? result)
    {
        var id = row.ComponentId == "anti-cheat" ? "anti-cheat-artifact" : row.ComponentId;
        var receipt = result?.Components.FirstOrDefault(c => c.ComponentId == id);
        var status = receipt?.Passed switch
        {
            true => "已核对",
            false => "未通过",
            null when row.StatusText == "缺失" => "缺少文件",
            _ => "尚未核对"
        };
        return new(Name(id), status, row.FileVersionText, row.FilePath);
    }
}

internal sealed record HomeOverlayState(string GameRootPath, string ChannelText, string SelectedChannelLaunchRoute,
    string LauncherVersion, string Signature, string Conclusion, string ArgumentsIntro, string Executable,
    string WorkingDirectory, string Arguments, string HomeArtKey, string ArtworkTitle, bool CanNameArtwork,
    bool CanDiscover, bool CanExport, bool CanRefresh, bool OfficialCapability, HomeChecks Checks, HomeOverlayTechnical Technical,
    string WebPageFallback)
{
    public bool CanOpenRoot => Path.IsPathFullyQualified(GameRootPath);

    internal static HomeOverlayState From(MainPageViewModel vm)
    {
        var result = vm.HomePreflight;
        var state = vm.HomeState;
        var candidate = vm.HomeCandidate;
        var channel = candidate?.Identity.DistributionChannel;
        var channelName = vm.HomeProfile.Title;
        var route = channel switch
        {
            DistributionChannel.Official => "经官方启动器启动",
            DistributionChannel.Bilibili => "经哔哩哔哩启动程序启动（实验功能）",
            DistributionChannel.Steam => "经 Steam 版启动程序启动（实验功能）",
            _ => "尚无可用的启动方式"
        };
        var provider = candidate?.Provider;
        var direct = vm.HomeChannelPlan;
        var args = provider?.ArgumentList ?? (direct?.CanLaunch == true ? direct.ArgumentList : null);
        var intro = channel == DistributionChannel.Official
            ? "Nikkiward 通过官方启动器的启动程序启动游戏，参数是固定的，不能在这里改。"
            : args == null ? "当前档案还没有可用的启动参数。" : "Nikkiward 使用这个渠道的启动程序，参数是固定的，不能在这里改。";
        var launcher = vm.CurrentComponentRows.FirstOrDefault(c => c.ComponentId == "official-launcher");
        var version = vm.SelectedLauncherVersionName ?? launcher?.FileVersionText ?? "未知版本";
        var repair = channel == DistributionChannel.Steam ? "在 Steam 中校验游戏文件，或重装后再试。" :
            channel == DistributionChannel.Bilibili ? "用哔哩哔哩启动器校验或重装后再试。" : "用官方启动器校验或重装后再试。";
        var launcherCheck = result?.Components.FirstOrDefault(c => c.ComponentId == "official-launcher");
        var signature = SignatureCheck(launcherCheck, launcher?.SignatureStatus, repair);
        var failed = result?.Components.FirstOrDefault(c => !c.Passed);
        var reason = ConclusionFor(state, vm, failed, version, repair);
        var ready = state == LaunchButtonState.Ready;
        var processes = state switch
        {
            LaunchButtonState.Ready when vm.HomeProcessesClear == true => new HomeCheck("没有上次留下的游戏进程", true),
            LaunchButtonState.Ready when vm.HomeProcessesClear == false => new("仍有游戏或启动程序在运行。请先退出，再重新检查。", false),
            LaunchButtonState.CleanupRequired => new("上次的游戏程序仍在运行。请先关闭游戏，再重新检查。", false),
            LaunchButtonState.Running or LaunchButtonState.Launching => new(reason),
            _ => new("尚未确认是否有残留进程。")
        };
        var gate = ready && vm.HomeProcessesClear == false ? processes :
            ready ? new HomeCheck("只有你点「启动游戏」时才会启动；每次启动 Windows 都会请你确认管理员权限", true)
            : new HomeCheck(reason, state is LaunchButtonState.Checking or LaunchButtonState.Running or LaunchButtonState.Launching ? null : false);
        var complete = result is { Components.Count: > 0 } && result.Components.All(c => c.Passed) &&
            (result.StaticIdentityPassed || result.Contract is { RequiredComponents.Count: > 0 } contract &&
                contract.RequiredComponents.All(required => result.Components.Any(c => c.ComponentId == required.ComponentId)));
        var integrity = complete
            ? new HomeCheck($"{result!.Components.Count} 个必要组件都已核对", true)
            : failed != null ? new HomeCheck($"{HomeComponent.Name(failed.ComponentId)}未通过检查。{repair}", false)
            : state == LaunchButtonState.Incomplete ? new HomeCheck(reason, false) : new HomeCheck("必要组件尚未全部核对。");
        var checks = new HomeChecks(new(vm.GameRootPath, state == LaunchButtonState.NotInstalled ? false : candidate?.GameRootPath != null ? true : null),
            new(channelName, result?.FailureCode is LaunchPreflightFailureCode.ChannelMismatch or LaunchPreflightFailureCode.MarkerMismatch ? false : ready ? true : null),
            signature with { Text = $"{version} · {signature.Text}" }, integrity, processes, gate);

        var technical = new HomeOverlayTechnical(vm.GameRootPath, vm.ChannelText, vm.SelectedChannelLaunchRoute, vm.ManualDiscoveryStatusText,
            vm.LaunchCapabilityText, vm.CapabilityDetailText, vm.StaticIdentityText, vm.StaticReadinessText, vm.ProviderContractId,
            vm.ProviderExecutionEnabledText, vm.ManualAttemptAuthorizationText, vm.ProviderExecutablePath, vm.ProviderWorkingDirectory,
            vm.ProviderArgumentVector, vm.ProviderEvidenceState, vm.ProviderObservationText, vm.ExternalEvidenceText,
            vm.LatestControlledReceiptText, vm.ApplicationExecutionText, vm.InstallationCandidateText, vm.LifecycleText,
            vm.ProviderPreflightText, vm.ProviderObservationPolicyText, vm.LastUpdatedText, vm.ActivityText, vm.LastErrorText,
            vm.HomeFallback, launcher?.SignatureText ?? "未检查", vm.HomeLaunchDetail, vm.ExecutionGateText,
            vm.RuntimeBlockCode ?? result?.FailureCode.ToString() ?? vm.HomeDiscoveryFailure?.FailureCode.ToString(),
            result?.FailureDetail ?? failed?.FailureDetail ?? vm.HomeDiscoveryFailure?.Detail);

        return new(vm.GameRootPath, $"{channelName} / {vm.PlatformText}", route, version, signature.Text, reason, intro,
            provider?.BackendExecutablePath ?? (direct?.CanLaunch == true ? direct.FileName : null) ?? "尚未定位",
            provider?.WorkingDirectory ?? direct?.WorkingDirectory ?? "尚未定位",
            args == null ? "尚未确定" : args.Count == 0 ? "无附加参数" : string.Join(" ", args),
            vm.HomeArtKey, vm.AppearanceSettings.ArtworkTitles.GetValueOrDefault(vm.HomeArtKey)?.Name ?? "",
            vm.CanNameArtwork, vm.CanDiscover, vm.CanExport, vm.CanRefresh, vm.CanAttemptOfficialAssistedLaunch && vm.HomeProcessesClear != false, checks, technical, vm.WebPageFallback);
    }

    private static HomeCheck SignatureCheck(PreflightComponentResult? receipt, AuthenticodeSignatureStatus? inspected, string repair)
    {
        if (receipt?.FailureCode == LaunchPreflightFailureCode.SignerMismatch)
            return new("签名与官方发布的不一致，已拒绝启动。" + repair, false);
        if (receipt?.Passed == true) return new("签名已核对", true);
        return (receipt?.SignatureStatus ?? inspected) switch
        {
            AuthenticodeSignatureStatus.NotSigned => new("文件没有数字签名。" + repair, false),
            AuthenticodeSignatureStatus.Invalid => new("数字签名无效。" + repair, false),
            AuthenticodeSignatureStatus.Untrusted => new("签名者不受信任。" + repair, false),
            AuthenticodeSignatureStatus.Error => new("无法读取签名。请重新检查。"),
            AuthenticodeSignatureStatus.Valid => new("数字签名有效，发行者尚未核对。"),
            _ => new("尚未核对签名。请重新检查。")
        };
    }

    private static string ConclusionFor(LaunchButtonState state, MainPageViewModel vm,
        PreflightComponentResult? failed, string version, string repair)
    {
        var failure = vm.HomePreflight?.FailureCode;
        var discovery = vm.HomeDiscoveryFailure?.FailureCode;
        return state switch
        {
            LaunchButtonState.Ready when vm.HomeProcessesClear == false => "这份安装仍有游戏或启动程序在运行。请先退出，再重新检查。",
            LaunchButtonState.Ready => vm.SelectedChannelUsesOfficialAssisted
                ? $"可以启动。官方启动器 {version}，签名已核对。点「启动游戏」后 Windows 会请你确认管理员权限。"
                : $"可以启动。{vm.HomeProfile.Channel}版游戏的签名已核对。请回到首页点「启动游戏」，并确认 Windows 管理员权限。",
            LaunchButtonState.Checking => "正在核对安装文件和签名，请等检查完成。",
            LaunchButtonState.NotInstalled => "没有找到游戏。请在基本信息中点「定位游戏」，选择安装目录。",
            LaunchButtonState.SelectionRequired => "需要确认使用哪份安装。请在基本信息中点「定位游戏」，选择要玩的那一份。",
            LaunchButtonState.ChannelUnsupported when discovery == ProfileBuildFailureCode.UnsupportedVersion =>
                "当前安装版本还不支持从这里启动。请使用对应渠道的启动器。",
            LaunchButtonState.ChannelUnsupported => "当前渠道或平台尚不支持从这里启动。请使用对应渠道的启动器。",
            LaunchButtonState.Incomplete when discovery is ProfileBuildFailureCode.SteamManifestMissing or ProfileBuildFailureCode.SteamNotReady =>
                "Steam 还没有确认游戏安装完成。请打开 Steam 完成下载或校验文件。",
            LaunchButtonState.Incomplete when discovery == ProfileBuildFailureCode.BackendRootNotFound =>
                "没有找到启动程序。请打开官方启动器，完成更新后再试。",
            LaunchButtonState.Incomplete when discovery == ProfileBuildFailureCode.ChannelMarkerMissing || failure == LaunchPreflightFailureCode.MarkerMissing =>
                "缺少用于确认游戏渠道的文件。请检查所选目录，并用对应渠道的启动器校验安装。",
            LaunchButtonState.Incomplete => "安装不完整，缺少必要文件。" + repair,
            LaunchButtonState.ContractDrift when failure is LaunchPreflightFailureCode.VersionMismatch or LaunchPreflightFailureCode.ArtifactHashMismatch
                => "安装文件的版本或内容与预期不一致。" + repair,
            LaunchButtonState.ContractDrift => "安装目录的渠道标记与所选档案不一致。请重新定位对应渠道的安装目录。",
            LaunchButtonState.SignatureUntrusted when failed != null =>
                HomeComponent.Name(failed.ComponentId) + "：" +
                SignatureCheck(failed, null, repair).Text,
            LaunchButtonState.SignatureUntrusted => "游戏组件的签名无法确认为官方发布，已拒绝启动。" + repair,
            LaunchButtonState.CleanupRequired => "上次的启动程序还没有退出。请先点「关闭游戏」，再重新检查。",
            LaunchButtonState.Running when vm.RuntimeBlockCode != null => "检测到已有游戏或启动程序在运行。请先退出，再重新检查。",
            LaunchButtonState.Running => "游戏已在运行。请切回游戏，或退出后再启动。",
            LaunchButtonState.Launching => "正在启动游戏，请留意 Windows 的管理员确认窗口，等待游戏打开。",
            LaunchButtonState.Blocked when vm.RuntimeBlockCode == "Preflight.ObserverUnavailable" =>
                "无法确认正在运行的程序是否属于这份安装。请先关闭游戏和启动器，再重新检查。",
            LaunchButtonState.Blocked when vm.RuntimeBlockCode is "Runtime.ProcessStartFailed" or "Runtime.ProcessNotCreated" =>
                "Windows 没能打开启动程序。请关闭启动器后重试，仍有问题时请使用官方启动器。",
            LaunchButtonState.Blocked when vm.RuntimeBlockCode is "Preflight.PathMismatch" or "Preflight.PathUnavailable" =>
                "启动程序或工作目录的位置有误。请重新定位游戏，再检查文件。",
            LaunchButtonState.Blocked when vm.RuntimeBlockCode is "Preflight.ProfileMismatch" or "Preflight.ProfileUnavailable" =>
                "当前安装与所选档案不符。请重新选择档案并定位游戏。",
            LaunchButtonState.Blocked when vm.RuntimeBlockCode is "Runtime.BackendIdentityDrift" =>
                "启动程序在检查后发生了变化。请等更新结束后重新检查。",
            LaunchButtonState.Blocked when vm.RuntimeBlockCode is "Runtime.OperationInProgress" or "Preflight.OperationInProgress" =>
                "上一次启动尚未完成。请等它结束后再试。",
            LaunchButtonState.Blocked when vm.RuntimeBlockCode is "Preflight.ContractDrift" or "Preflight.PlanMismatch" or "Preflight.ExecutionStateMismatch" =>
                "这份安装的启动方式与已核对的配置不一致。请重新检查，或使用官方启动器。",
            LaunchButtonState.Blocked when vm.RuntimeBlockCode == "Preflight.ComponentReceiptIncomplete" =>
                "部分必要组件没有完整的检查结果。请重新检查游戏文件。",
            LaunchButtonState.Blocked when failure == LaunchPreflightFailureCode.BinaryIdentityDrift =>
                "游戏文件正在变动或被占用。请等更新结束后重新检查。",
            LaunchButtonState.Blocked when failure is LaunchPreflightFailureCode.PathOutsideExpectedRoot or LaunchPreflightFailureCode.ReparsePointRejected =>
                "启动文件的实际位置不在允许的安装目录内。请重新定位游戏，再检查文件。",
            LaunchButtonState.Blocked when failure == LaunchPreflightFailureCode.RequiredComponentMissing =>
                "缺少启动所需的组件。" + repair,
            LaunchButtonState.Blocked when discovery == ProfileBuildFailureCode.MetadataReadFailed =>
                "无法读取安装信息。请等游戏更新完成，再重新定位游戏目录。",
            LaunchButtonState.Blocked when discovery is ProfileBuildFailureCode.ConflictingInstallation or ProfileBuildFailureCode.ChannelMismatch =>
                "找到的安装信息与所选渠道不符。请重新定位对应渠道的游戏目录。",
            LaunchButtonState.Blocked when vm.HomeCandidate == null => "没有找到可核对的安装。请重新定位游戏目录，再检查。",
            LaunchButtonState.Blocked when vm.HomePreflight == null => "尚未完成组件检查。请点「重新检查」，等检查完成再启动。",
            LaunchButtonState.Blocked => "这份安装未通过启动前的检查。请重新检查，仍无法启动时请用对应渠道的启动器校验文件。",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
    }
}
