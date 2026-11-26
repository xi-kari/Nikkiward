using System.ComponentModel;
using System.Diagnostics;
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
    private readonly AfterLaunchWindowPolicy _afterLaunchWindow = new();
    internal event Action<AfterLaunchBehavior>? AfterLaunchWindowRequested;

    private sealed record RuntimeRefusal(string Code, string Detail, DateTimeOffset Time, InstallationProfileCandidate Candidate,
        IReadOnlyList<string> ProcessPaths)
    {
        public (string? Code, string? Detail) Block { get; set; } = (Code, Detail);
    }

    private LaunchButtonState _unselectedState = LaunchButtonState.Blocked;
    private InstallationProfileCandidate? _discoveryFailure;
    private readonly List<RuntimeRefusal> _runtimeRefusals = [];

    private RuntimeRefusal? SelectedRuntimeRefusal => _selectedCandidate is not { } candidate ? null :
        _runtimeRefusals.Find(failure => failure.Candidate.ProfileId == candidate.ProfileId &&
            string.Equals(failure.Candidate.GameRootPath, candidate.GameRootPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(failure.Candidate.LauncherRootPath, candidate.LauncherRootPath, StringComparison.OrdinalIgnoreCase));

    private string? RuntimeBlockCode => SelectedRuntimeRefusal?.Block.Code;

    internal void RecordRuntimeRefusal(string code, string detail, IReadOnlyList<string> processPaths)
    {
        if (_selectedCandidate is null)
        {
            return;
        }

        if (SelectedRuntimeRefusal is { } previous)
        {
            _runtimeRefusals.Remove(previous);
        }

        _runtimeRefusals.Add(new(code, RedactUiText(detail), DateTimeOffset.UtcNow, _selectedCandidate, processPaths));
    }

    private async Task RefreshRefusedBaselineAsync(CancellationToken cancellationToken)
    {
        if (SelectedRuntimeRefusal is not { Block.Code: "Preflight.BaselineDirty" or "Preflight.ObserverUnavailable" } refusal)
        {
            return;
        }

        var observation = await Task.Run(() => ObserveBaseline(refusal.ProcessPaths), cancellationToken);
        if (!ReferenceEquals(SelectedRuntimeRefusal, refusal) || observation == refusal.Block)
        {
            return;
        }

        if (observation.Code is null)
        {
            _launchLifecycleText = "NotRunning · 已确认目标组件均未运行";
        }

        refusal.Block = observation;
        NotifyLaunchAttemptChanged();
    }

    private static (string? Code, string? Detail) ObserveBaseline(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var processes = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in processes)
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    // The game normally runs elevated; MainModule cannot read an elevated
                    // process from here, the limited-information query can.
                    if (!OfficialAssistedLaunchCoordinator.TryQueryProcessPath(process.Id, out var actualPath))
                    {
                        return ("Preflight.ObserverUnavailable", $"无法确认活动 {name} 进程的精确路径。");
                    }

                    if (string.Equals(actualPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        return ("Preflight.BaselineDirty", $"目标组件 {Path.GetFileName(path)} 正在运行。");
                    }
                }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                return ("Preflight.ObserverUnavailable", $"无法读取活动 {name} 进程的精确路径。");
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return default;
    }

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

            if (RuntimeBlockCode is { } code)
            {
                return code == "Preflight.BaselineDirty" ? LaunchButtonState.Running : LaunchButtonState.Blocked;
            }

            if (CanAttemptSelectedChannelLaunch)
            {
                return LaunchButtonState.Ready;
            }

            if (_checkingInstallation || _selectedCandidate is null)
            {
                return _checkingInstallation ? LaunchButtonState.Checking : _unselectedState;
            }

            if (_preflightResult is null)
            {
                return IsBusy ? LaunchButtonState.Checking : LaunchButtonState.Blocked;
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

                LaunchPreflightFailureCode.RequiredComponentMissing when IsExternalChannelSelected =>
                    LaunchButtonState.Blocked,
                LaunchPreflightFailureCode.RequiredComponentMissing or
                LaunchPreflightFailureCode.MarkerMissing =>
                    LaunchButtonState.Incomplete,

                // BinaryIdentityDrift (file changed while being read) is a
                // transient condition — an update writing files right now —
                // so it lands in Blocked and clears on the next refresh.
                _ => LaunchButtonState.Blocked,
            };
        }
    }

    public string LaunchButtonLabel => LaunchButtonState.Label();

    /// <summary>
    /// One line under the button. Explains what to do, not what went wrong
    /// internally; the raw failure code stays in diagnostics.
    /// </summary>
    public string LaunchButtonDetail => DescribeLaunchState(LaunchButtonState);

    private string DescribeLaunchState(LaunchButtonState state) => state switch
    {
        LaunchButtonState.Checking =>
            "正在核对本机安装与组件签名。",
        LaunchButtonState.Incomplete when _selectedCandidate is null && _discoveryFailure is { } failed =>
            failed.MissingComponents.Count > 0 ? $"缺少组件：{string.Join("、", failed.MissingComponents)}。" : failed.Detail ?? "请用对应渠道的启动器校验安装文件。",
        LaunchButtonState.Incomplete => _preflightResult?.FailureDetail ?? "请用官方启动器校验安装文件。",
        LaunchButtonState.NotInstalled =>
            "请在游戏设置的「基本信息」中点「定位游戏」。",
        LaunchButtonState.SelectionRequired =>
            "请在游戏设置的「基本信息」中点「定位游戏」，选择要使用的安装目录。",
        LaunchButtonState.Running or LaunchButtonState.Blocked when SelectedRuntimeRefusal?.Block.Detail is { } detail =>
            detail,
        LaunchButtonState.Blocked when _selectedCandidate is null =>
            _discoveryFailure?.Detail ??
            "安装发现失败，请在游戏设置中点「定位游戏」重试。",
        LaunchButtonState.ChannelUnsupported =>
            _selectedCandidate?.Identity.DistributionChannel is DistributionChannel.Steam
                ? "Steam Windows 直启尚未验证，本次不会打开 Steam 或官方启动器。"
                : "当前渠道或区服没有对应的启动契约，暂不支持辅助启动。",
        LaunchButtonState.ContractDrift =>
            "游戏目录的 product.db 渠道标识与所选渠道不一致，请确认选择的是对应渠道的游戏目录。",
        LaunchButtonState.Blocked when _preflightResult is null =>
            "组件检查未完成，请在当前档案中点「刷新服务器列表」重试。",
        LaunchButtonState.Blocked when IsExternalChannelSelected &&
            _preflightResult?.FailureCode is LaunchPreflightFailureCode.BinaryIdentityDrift =>
            "安装文件正在变化或被占用。请等待更新完成，在当前档案中点「刷新服务器列表」后重试。",
        LaunchButtonState.SignatureUntrusted or LaunchButtonState.Blocked when IsExternalChannelSelected =>
            _selectedCandidate?.Identity.DistributionChannel is DistributionChannel.Steam
                ? "请在 Steam 中验证当前安装的游戏文件或重新安装，再打开当前档案，点「刷新服务器列表」。"
                : "请用 B 服官方启动器校验当前安装的文件或重新安装，再打开当前档案，点「刷新服务器列表」。",
        LaunchButtonState.SignatureUntrusted =>
            "游戏组件的数字签名无效或签名者不受信任，为安全起见已拒绝启动。请用官方启动器校验游戏文件或重新安装后重试。",
        LaunchButtonState.Blocked =>
            "静态校验未通过，可在设置的诊断页查看原因；若游戏正在更新，请等更新完成后重试。",
        LaunchButtonState.CleanupRequired =>
            "启动入口或 bootstrap 仍在运行，请先使用右侧关闭按钮清理。",
        LaunchButtonState.Ready =>
            IsExternalChannelSelected
                ? $"{SelectedChannelLaunchRoute}。点击后重新验签并请求管理员权限。"
                : SelectedLauncherVersionName is { } version
                    ? $"检测到启动器版本 {version}，组件签名验证通过；点击后构建瞬时 plan 并请求 UAC 提权。"
                    : "点击后重新核对组件签名、构建瞬时 plan 并请求 UAC 提权。",
        LaunchButtonState.Launching when _activeProcessBinding is null && IsExternalChannelSelected =>
            "正在核对组件签名并等待 Windows 管理员确认。",
        LaunchButtonState.Launching =>
            "已提交 xstarter，等待游戏进程建立。",
        LaunchButtonState.Running =>
            _selectedCandidate?.Identity.DistributionChannel is DistributionChannel.Bilibili
                ? "B服游戏已启动；首次使用时会停在游戏内 B站登录页等待认证。"
                : "目标进程已在运行，启动按钮已锁定。",
        _ => string.Empty,
    };

    public string ProfileVersionText
    {
        get
        {
            var fileVersion = _preflightResult?.Components.FirstOrDefault(item => item.ComponentId == "game-client")?.ActualFileVersion
                ?? _snapshot?.Components.FirstOrDefault(item => item.ComponentId == "game-client")?.FileVersion;
            var gameVersion = LaunchDiscoveryDisplay.FormatGameVersion(fileVersion);
            if (gameVersion == "未知" && _selectedCandidate?.ProductBuildNumber is { } build &&
                build.Length > 0 && build.All(char.IsAsciiDigit))
                gameVersion += $"（构建 {build}）";

            var buildId = _selectedCandidate?.SteamManifest?.BuildId;
            var steamBuild = buildId is { Length: > 0 } && buildId.All(char.IsAsciiDigit) ? $" · Steam BuildID {buildId}" : string.Empty;
            return $"游戏 {gameVersion}{steamBuild} · 启动器 {SelectedLauncherVersionName ?? "未知"}";
        }
    }

    internal IReadOnlyList<ComponentItemViewModel> CurrentComponentRows =>
        _snapshot is { } snapshot && snapshot.ProfileId == SelectedProfileId ? Components : [];

    internal string? SelectedLauncherVersionName =>
        Services.WindowsLaunchPreflightVerifier.TryGetVersionDirectoryName(
            _selectedCandidate?.Provider?.BackendExecutablePath ?? _profile?.XStarterPath);

    private void NotifyLaunchStateChanged()
    {
        var action = _afterLaunchWindow.Observe(_activeProcessBinding, LaunchButtonState,
            _lastObservedGameAtUtc, GeneralSettings.AfterLaunchBehavior);
        if (action != AfterLaunchBehavior.KeepWindow)
        {
            AfterLaunchWindowRequested?.Invoke(action);
        }

        OnPropertyChanged(nameof(HomeCheckText));
        OnPropertyChanged(nameof(HasHomeTask));
        OnPropertyChanged(nameof(HomeTaskText));
        OnPropertyChanged(nameof(HomeState));
        OnPropertyChanged(nameof(HomeLaunchLabel));
        OnPropertyChanged(nameof(HomeLaunchDetail));
        OnPropertyChanged(nameof(HomeLaunchEnabled));
        OnPropertyChanged(nameof(HomeBusy));
        OnPropertyChanged(nameof(HomeCloseEnabled));
        OnPropertyChanged(nameof(HomeCloseVisibility));
        OnPropertyChanged(nameof(ProfileVersionText));
        OnPropertyChanged(nameof(LaunchButtonState));
        OnPropertyChanged(nameof(LaunchButtonLabel));
        OnPropertyChanged(nameof(LaunchButtonDetail));
    }
}
