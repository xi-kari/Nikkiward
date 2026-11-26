using Nikkiward.Models;

namespace Nikkiward.ViewModels;

/// <summary>
/// Honest projection of the launch gate onto the primary action button.
/// See LAUNCH_CONTRACT.md §7: the button must never disguise a blocked gate as
/// launchable, and a security refusal ("this binary cannot be attributed to
/// the official publisher") must be told apart from "the install is broken" —
/// otherwise users reinstall a game that was never the problem.
/// </summary>
public enum LaunchButtonState
{
    /// <summary>Preflight has not run yet.</summary>
    Checking,

    /// <summary>No install candidate was discovered.</summary>
    NotInstalled,

    /// <summary>Discovery found several installs or an ambiguous root; the user has to pick one.</summary>
    SelectionRequired,

    /// <summary>An install exists but its channel/region has no frozen contract.</summary>
    ChannelUnsupported,

    /// <summary>
    /// The product marker no longer matches the selected channel (or a legacy
    /// drift code surfaced). Since the adaptive contract a routine game update
    /// no longer reaches this state — the signature gate absorbs it.
    /// </summary>
    ContractDrift,

    /// <summary>
    /// A component's Authenticode signature is invalid or its signer is not in
    /// the contract's trusted list. Deliberately fail-closed: Nikkiward never
    /// elevates a binary it cannot attribute to the official publishers.
    /// </summary>
    SignatureUntrusted,

    /// <summary>Static identity failed for a reason other than drift.</summary>
    Blocked,

    /// <summary>A submitted root or bootstrap remains and must be closed before retrying.</summary>
    CleanupRequired,

    /// <summary>
    /// The normal launchable state. The static gate is deliberately closed and
    /// the coordinator synthesises a transient plan on click.
    /// </summary>
    Ready,

    /// <summary>A launch attempt is in flight.</summary>
    Launching,

    /// <summary>The target process tree is already running.</summary>
    Running,
}

internal static class LaunchDiscoveryDisplay
{
    internal static string Label(this LaunchButtonState state) => state switch
    {
        LaunchButtonState.Checking => "检查安装中",
        LaunchButtonState.NotInstalled => "未发现安装",
        LaunchButtonState.SelectionRequired => "需要选择安装",
        LaunchButtonState.ChannelUnsupported => "渠道暂不支持",
        LaunchButtonState.ContractDrift => "渠道身份不符",
        LaunchButtonState.SignatureUntrusted => "签名验证未通过",
        LaunchButtonState.Blocked => "启动条件不满足",
        LaunchButtonState.CleanupRequired => "需要关闭残留进程",
        LaunchButtonState.Ready => "启动游戏",
        LaunchButtonState.Launching => "正在启动",
        LaunchButtonState.Running => "游戏运行中",
        _ => "启动游戏",
    };

    internal static LaunchButtonState WithoutSelection(bool checking, ProfileBuildResult? result)
    {
        if (checking)
        {
            return LaunchButtonState.Checking;
        }

        if (result is null)
        {
            return LaunchButtonState.Blocked;
        }

        var needsSelection = result.Candidates.Any(candidate =>
            candidate.FailureCode is ProfileBuildFailureCode.AmbiguousLauncherRoot or ProfileBuildFailureCode.AmbiguousGameRoot ||
            candidate.Profile is not null && candidate.State is InstallationCandidateState.Candidate or InstallationCandidateState.ReadyForStaticVerification);
        return needsSelection ? LaunchButtonState.SelectionRequired : LaunchButtonState.NotInstalled;
    }

    internal static string FormatGameVersion(string? value)
    {
        if (value is null)
        {
            return "未知";
        }

        var parts = value.Split(value.Contains(',') ? ',' : '.', StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)) ||
            !Version.TryParse(string.Join('.', parts), out var version))
        {
            return "未知";
        }

        return $"{version.Major}.{version.Minor}.{version.Build}（构建 {version.Revision}）";
    }
}
