using Nikkiward.Models;

namespace Nikkiward.ViewModels;

/// <summary>
/// Read-only projection of the existing launch gate onto UI state. This file
/// derives display values from <see cref="_preflightResult"/> and friends; it
/// does not decide whether a launch may proceed. The authority for that stays
/// <see cref="CanAttemptOfficialAssistedLaunch"/>.
/// </summary>
public sealed partial class MainPageViewModel
{
    public LaunchButtonState LaunchButtonState
    {
        get
        {
            if (_isLaunchAttemptInProgress)
            {
                return LaunchButtonState.Launching;
            }

            if (_isLaunchCleanupRequired)
            {
                return LaunchButtonState.CleanupRequired;
            }

            if (_isOfficialAssistedRunning)
            {
                return LaunchButtonState.Running;
            }

            if (_activeProcessBinding is not null)
            {
                return LaunchButtonState.Launching;
            }

            if (CanAttemptSelectedChannelLaunch)
            {
                return LaunchButtonState.Ready;
            }

            if (_selectedCandidate is null)
            {
                return _preflightResult is null
                    ? LaunchButtonState.Checking
                    : LaunchButtonState.NotInstalled;
            }

            if (_preflightResult is null)
            {
                return LaunchButtonState.Checking;
            }

            return _preflightResult.FailureCode switch
            {
                LaunchPreflightFailureCode.SignatureInvalid or
                LaunchPreflightFailureCode.SignerMismatch =>
                    LaunchButtonState.SignatureUntrusted,

                LaunchPreflightFailureCode.ArtifactHashMismatch or
                LaunchPreflightFailureCode.VersionMismatch or
                LaunchPreflightFailureCode.MarkerMismatch =>
                    LaunchButtonState.ContractDrift,

                LaunchPreflightFailureCode.ChannelMismatch or
                LaunchPreflightFailureCode.PlatformMismatch or
                LaunchPreflightFailureCode.InvalidContract =>
                    LaunchButtonState.ChannelUnsupported,

                LaunchPreflightFailureCode.RequiredComponentMissing or
                LaunchPreflightFailureCode.MarkerMissing =>
                    LaunchButtonState.NotInstalled,

                // BinaryIdentityDrift (file changed while being read) is a
                // transient condition — an update writing files right now —
                // so it lands in Blocked and clears on the next refresh.
                _ => LaunchButtonState.Blocked,
            };
        }
    }

    public string LaunchButtonLabel => LaunchButtonState switch
    {
        LaunchButtonState.Checking => "检查安装中",
        LaunchButtonState.NotInstalled => "未找到游戏",
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

    /// <summary>
    /// One line under the button. Explains what to do, not what went wrong
    /// internally; the raw failure code stays in diagnostics.
    /// </summary>
    public string LaunchButtonDetail => LaunchButtonState switch
    {
        LaunchButtonState.Checking =>
            "正在核对本机安装与组件签名。",
        LaunchButtonState.NotInstalled =>
            "未发现无限暖暖安装，请在 Profile 页手动指定游戏目录。",
        LaunchButtonState.ChannelUnsupported =>
            _selectedCandidate?.Identity.DistributionChannel is DistributionChannel.Steam
                ? "Steam Windows 直启尚未验证，本次不会打开 Steam 或官方启动器。"
                : "当前渠道或区服没有对应的启动契约，暂不支持辅助启动。",
        LaunchButtonState.ContractDrift =>
            "游戏目录的 product.db 渠道标识与所选渠道不一致，请确认选择的是国服目录。",
        LaunchButtonState.SignatureUntrusted =>
            "游戏组件的数字签名无效或签名者不受信任，为安全起见已拒绝启动。请用官方启动器校验游戏文件或重新安装后重试。",
        LaunchButtonState.Blocked =>
            "静态校验未通过，可在设置的诊断页查看原因；若游戏正在更新，请等更新完成后重试。",
        LaunchButtonState.CleanupRequired =>
            "启动入口或 bootstrap 仍在运行，请先使用右侧关闭按钮清理。",
        LaunchButtonState.Ready =>
            _selectedCandidate?.Identity.DistributionChannel is
                DistributionChannel.Bilibili or DistributionChannel.Steam
                ? $"点击后提交 {SelectedChannelLaunchRoute}。"
                : SelectedLauncherVersionName is { } version
                    ? $"检测到启动器版本 {version}，组件签名验证通过；点击后构建瞬时 plan 并请求 UAC 提权。"
                    : "点击后重新核对组件签名、构建瞬时 plan 并请求 UAC 提权。",
        LaunchButtonState.Launching =>
            "已提交 xstarter，等待游戏进程建立。",
        LaunchButtonState.Running =>
            _selectedCandidate?.Identity.DistributionChannel is DistributionChannel.Bilibili
                ? "B服游戏已启动；首次使用时会停在游戏内 B站登录页等待认证。"
                : "目标进程已在运行，启动按钮已锁定。",
        _ => string.Empty,
    };

    private string? SelectedLauncherVersionName =>
        Services.WindowsLaunchPreflightVerifier.TryGetVersionDirectoryName(
            _selectedCandidate?.Provider?.BackendExecutablePath);

    /// <summary>
    /// Whether the launch attempt is blocked in a way the user cannot act on
    /// from this screen. Drives the skeleton/disabled styling.
    /// </summary>
    public bool IsLaunchButtonBusy =>
        LaunchButtonState is LaunchButtonState.Checking or LaunchButtonState.Launching;

    private void NotifyLaunchStateChanged()
    {
        OnPropertyChanged(nameof(LaunchButtonState));
        OnPropertyChanged(nameof(LaunchButtonLabel));
        OnPropertyChanged(nameof(LaunchButtonDetail));
        OnPropertyChanged(nameof(IsLaunchButtonBusy));
    }
}
