using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Nikkiward.Features.Background;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.Serialization;

namespace Nikkiward.ViewModels;

public sealed partial class MainPageViewModel : INotifyPropertyChanged
{
    private const string DiagnosticProfileId = "infinity-nikki-active";
    private readonly IUserSettingsStore _settingsStore;
    private readonly IInstallationInspector _installationInspector;
    private readonly IDiagnosticReportExporter _diagnosticExporter;
    private readonly IInstallationDiscoveryService _installationDiscovery;
    private readonly ILaunchPreflightVerifier _preflightVerifier;
    private readonly OfficialAssistedLaunchCoordinator _launchCoordinator;
    private readonly object _initializationSync = new();

    private LaunchProfile? _profile;
    private InstallationProfileCandidate? _selectedCandidate;
    private ProfileBuildResult? _profileBuildResult;
    private LaunchPreflightResult? _preflightResult;
    private LaunchSnapshot? _snapshot;
    private bool _initialized;
    private Task? _initializationTask;
    private UserSettings _persistedSettings = new();
    private bool _isBusy;
    private bool _isLaunchAttemptInProgress;
    private bool _isOfficialAssistedRunning;
    private bool _isLaunchCleanupRequired;
    private bool _isCloseGameInProgress;
    private OfficialAssistedProcessBinding? _activeProcessBinding;
    private DateTimeOffset? _lastObservedGameAtUtc;
    private string _activityText = "等待初始化";
    private string _staticReadinessText = "尚未检查";
    private string _lastUpdatedText = "尚未刷新";
    private string _lastErrorText = "尚未刷新";
    private string _settingsStatusText = "尚未读取设置";
    private string _exportStatusText = "刷新组件后可导出脱敏 JSON 与文本报告。";
    private string _providerValidationStatusText = "验证事务工件尚未读取。";
    private string _providerValidationReceiptText = "验证事务工件尚未读取。";
    private string _launchAttemptStatusText =
        "点击启动后自动 preflight + 构建瞬时 plan · OfficialAssisted · VerifiedOneClick=false";
    private string _launchLifecycleText = "NotRunning · 当前没有活动 attempt · 默认不自动启动";
    private string? _settingsErrorText;

    public MainPageViewModel(
        IUserSettingsStore settingsStore,
        IInstallationInspector installationInspector,
        IDiagnosticReportExporter diagnosticExporter,
        IInstallationDiscoveryService? installationDiscovery = null,
        ILaunchPreflightVerifier? preflightVerifier = null)
    {
        _settingsStore = settingsStore;
        _installationInspector = installationInspector;
        _diagnosticExporter = diagnosticExporter;
        _installationDiscovery = installationDiscovery ?? new WindowsInstallationProfileBuilder();
        _preflightVerifier = preflightVerifier ?? new WindowsLaunchPreflightVerifier();
        _launchCoordinator = new OfficialAssistedLaunchCoordinator(_preflightVerifier);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ComponentItemViewModel> Components { get; } = [];

    public ObservableCollection<LaunchProfileItemViewModel> Profiles { get; } = [];

    public ObservableCollection<PlatformEvidenceItemViewModel> PlatformEvidence { get; } = [];

    /// <summary>
    /// Last loaded or saved gamepad section. Unlike the launch surfaces this is
    /// a plain application preference, not verification evidence.
    /// </summary>
    public GamepadSettings GamepadSettings { get; private set; } = new();

    public AppearanceSettings AppearanceSettings { get; private set; } = new();

    public GeneralSettings GeneralSettings { get; private set; } = new();

    public DownloadSettings DownloadSettings { get; private set; } = new();

    public FileManagementSettings FileManagementSettings { get; private set; } = new();

    public ScreenshotSettings ScreenshotSettings { get; private set; } = new();

    public bool DeveloperModeEnabled { get; private set; }

    public ThemeMode ThemeMode => AppearanceSettings.ThemeMode;

    public string ProfileDisplayName => _profile?.DisplayName ?? "请选择已发现的游戏目录";

    public string ChannelText => _selectedCandidate is not null
        ? FormatDistribution(_selectedCandidate.Identity.DistributionChannel)
        : string.IsNullOrWhiteSpace(_profile?.Channel) ? "未配置" : _profile.Channel;

    public string PlatformText => "Windows";

    public string CurrentProfileSummary =>
        $"{ProfileDisplayName} · {ChannelText} / {PlatformText}";

    public string GameRootPath => !string.IsNullOrWhiteSpace(_selectedCandidate?.GameRootPath)
        ? _selectedCandidate.GameRootPath!
        : "尚未配置";

    public string? SelectedProfileId => _profile?.ProfileId;

    public string? GalleryRootPath => FindGalleryRoot(_profile?.ProfileId);

    public string XStarterPath => _selectedCandidate?.Provider?.BackendExecutablePath
        ?? "未绑定 provider";

    public string LaunchCapabilityText => _preflightResult is
        { StaticIdentityPassed: true, Contract.MaximumCapability: LaunchCapability.OfficialAssisted }
        ? "OfficialAssisted · contract 能力上限（非当前执行能力）"
        : "NotVerified · profile 尚未验证";

    public string CapabilityDetailText =>
        "VerifiedOneClick=false · 固定 A 已有一次 ObservedSuccess，但 B/Pair、重启恢复、10/10 零重试与登录界面人工验收尚未完成；应用初始化不自动执行，每次点击都会重新 preflight。";

    public string ExternalEvidenceText =>
        "最新受控证据：OfficialAssisted · A=ObservedSuccess（process-chain + cleanup）；LOGIN_STATUS=not_observed，不代表登录、公开 Play API 或当前应用执行能力已启用。";

    public string ApplicationExecutionText =>
        _isOfficialAssistedRunning
            ? "正在运行中 · 当前 Profile 游戏进程已确认 · 登录状态等待人工确认"
            : ExecutionGateText;

    public string DownloadStatusText
    {
        get
        {
            var steamCandidate = _profileBuildResult?.Candidates.FirstOrDefault(candidate =>
                candidate.Identity.DistributionChannel is DistributionChannel.Steam);
            if (steamCandidate?.State is InstallationCandidateState.Downloading)
            {
                return "Steam 下载中";
            }

            return _selectedCandidate?.Identity.DistributionChannel is DistributionChannel.Official
                ? "预下载未启用"
                : "下载状态未验证";
        }
    }

    public string DownloadStatusDetailText =>
        _selectedCandidate?.Identity.DistributionChannel is DistributionChannel.Steam
            ? "Windows staging · NotReady"
            : "当前应用不控制下载任务";

    public Visibility PreloadNoticeVisibility =>
        _selectedCandidate?.State is InstallationCandidateState.Downloading
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string PlayTimeText => "奇想手账未同步";

    public string PlayTimeDetailText => "当前应用不统计目标进程时长";

    public string PrimaryActionText => _isLaunchAttemptInProgress
        ? "正在启动"
        : _isOfficialAssistedRunning
            ? "正在运行中"
            : CanAttemptSelectedChannelLaunch
                ? "启动游戏"
                : "检查启动条件";

    public string PrimaryActionHelpText => _isOfficialAssistedRunning
        ? "当前 Profile 游戏进程已确认；启动按钮已锁定，右侧关闭按钮可结束游戏。"
        : _selectedCandidate?.Identity.DistributionChannel switch
        {
            DistributionChannel.Bilibili =>
                $"点击后提交 {SelectedChannelLaunchRoute}；不会打开 Bilibili launcher.exe。首次使用 Store B服需要在游戏内登录。",
            DistributionChannel.Steam =>
                $"点击后提交 {SelectedChannelLaunchRoute}；不会打开 Steam URI 或 launcher.exe。",
            _ => "点击后自动重新核对 CN frozen contract、构建瞬时 plan 并请求 Windows UAC；不代表登录已验证。",
        };

    public bool CanCloseGame =>
        !IsBusy &&
        !_isCloseGameInProgress &&
        _activeProcessBinding is not null;

    public Visibility CloseGameButtonVisibility =>
        CanCloseGame ? Visibility.Visible : Visibility.Collapsed;

    public string CloseGameButtonLabel => _isCloseGameInProgress ? "正在关闭" : "关闭游戏";

    public string PrimaryActionHintText => _launchAttemptStatusText;

    public string ServiceShortcutText =>
        "奇想手账 · 内置网页登录与本地只读快照";

    public string LifecycleText => _launchLifecycleText;

    public string ExecutionSummaryTitle =>
        _isOfficialAssistedRunning
            ? "OfficialAssisted · 正在运行中 · VerifiedOneClick=false"
            : "OfficialAssisted · ClickToAttempt · VerifiedOneClick=false";

    public string ManualAttemptAuthorizationText =>
        _isOfficialAssistedRunning
            ? "ManualAttemptAvailable=false · 当前 OfficialAssisted attempt 正在运行"
            : CanAttemptOfficialAssistedLaunch
            ? "ManualAttemptAvailable=true · click-to-run · 自动 preflight + Windows UAC"
            : "ManualAttemptAvailable=false · 需唯一 CN profile 与通过的静态 preflight";

    public string ProviderExecutablePath =>
        _selectedCandidate?.Provider?.BackendExecutablePath ?? "未绑定 provider";

    public string ProviderWorkingDirectory =>
        _selectedCandidate?.Provider?.WorkingDirectory ?? "未绑定 working directory";

    public string ProviderArgumentVector =>
        _selectedCandidate?.Provider is { } provider
            ? System.Text.Json.JsonSerializer.Serialize(
                provider.ArgumentList.ToArray(),
                NikkiwardJsonContext.Default.StringArray)
            : "未绑定参数 preset";

    public string ProviderEvidenceState =>
        "ObservedSuccess · frozen A=1 · OfficialAssisted · VerifiedOneClick=false · LOGIN_STATUS=not_observed";

    public string ProviderObservationText =>
        "官方 xstarter.exe → InfinityNikki.exe → X6Game-Win64-Shipping.exe 已观察稳定；本轮无 launcher.exe，cleanup Remaining.Count=0。B/Pair、重启恢复与登录状态仍未验证。";

    public string ProviderPreflightText =>
        _preflightResult is null
            ? "尚未执行静态 preflight；需从当前候选 roots + frozen catalog 重新派生路径。"
            : $"StaticIdentityPassed={_preflightResult.StaticIdentityPassed} · FailureCode={_preflightResult.FailureCode} · verifier Plan={(_preflightResult.Plan is null ? "null" : "present")}；每次点击时从 fresh roots + frozen contract 派生瞬时 plan。";

    public string ProviderObservationPolicyText =>
        "只允许本次 attempt 的 PID/父 PID、路径、退出码、窗口句柄、响应性、cleanup 与覆盖记录；不读取命令行、环境、token、内存或网络载荷。";

    public string ProfileIsolationText =>
        "单本体模式只硬链接完整 SHA-256 一致的不可变资源；launcher、bootstrap、product marker、ACE、热更状态、X6Game\\Saved 与账号状态按渠道隔离。";

    public string SwitchParameterText =>
        "-skiplauncher 是 CN / Windows 的当前候选参数；-SkipLauncherTokenCheck_SD 仅属于 SteamOS/Proton。两者不可互换，_SD 不进入 CN 或 Windows Steam。";

    public string InstallationCandidateText => _selectedCandidate switch
    {
        null => "未发现可选安装候选",
        { State: var state } => $"{FormatCandidateState(state)} · source={_selectedCandidate.DiscoverySource}",
    };

    public string StaticIdentityText => _preflightResult switch
    {
        { StaticIdentityPassed: true } => "已验证 · 静态身份通过",
        null => "NotVerified · 尚未执行静态 preflight",
        _ => $"NotVerified · {_preflightResult.FailureCode}",
    };

    public string ExecutionGateText => _preflightResult switch
    {
        {
            FailureCode: LaunchPreflightFailureCode.ExecutionGateClosed,
            ExecutionAllowed: false,
            Plan: null,
        } => "ExecutionGateClosed · catalog 自动执行门关闭；点击“启动游戏”时仅构建本次会话瞬时 OfficialAssisted plan",
        null when _selectedCandidate?.Provider?.ExecutionEnabled is false =>
            "ExecutionGateClosed · provider binding 执行门关闭",
        null => "NotVerified · 没有可验证 provider",
        _ => $"{_preflightResult.FailureCode} · ExecutionAllowed={_preflightResult.ExecutionAllowed}",
    };

    public string ProviderContractId =>
        _selectedCandidate?.Provider?.ProviderId ?? "未绑定 frozen provider contract";

    public string ProviderExecutionEnabledText =>
        _selectedCandidate?.Provider is { } provider
            ? $"ExecutionEnabled={provider.ExecutionEnabled} · MaximumCapability={provider.MaximumCapability}"
            : "ExecutionEnabled=false · provider 未绑定";

    public string ManualSelectionHint =>
        "自动发现只读取安装元数据；单本体模式会先生成 dry-run，再在 E 盘物化三个独立渠道根。";

    public string ProfileIdentityText => _selectedCandidate is null
        ? "region_family=Unknown · distribution_channel=Unknown · account_authority=Unknown · discovery_source=unresolved"
        : $"region_family={_selectedCandidate.Identity.RegionFamily} · distribution_channel={_selectedCandidate.Identity.DistributionChannel} · account_authority={_selectedCandidate.Identity.AccountAuthority} · discovery_source={_selectedCandidate.DiscoverySource} · observed_at={_selectedCandidate.ObservedAtUtc:O}";

    public string SteamProvenanceText
    {
        get
        {
            var steamCandidate = _profileBuildResult?.Candidates.FirstOrDefault(candidate =>
                candidate.Identity.DistributionChannel is DistributionChannel.Steam);
            if (steamCandidate?.SteamManifest is not { } manifest)
            {
                return "steam_app_id=3164330 · sub_id=null · depot_id=null · build_id=null · manifest_id=null · observed_platform_condition=unresolved";
            }

            return $"steam_app_id={manifest.AppId ?? "null"} · sub_id={manifest.SubId ?? "null"} · depot_id={manifest.DepotId ?? "null"} · build_id={manifest.BuildId ?? "null"} · manifest_id={manifest.ManifestId ?? "null"} · source={steamCandidate.DiscoverySource}";
        }
    }

    public string SteamPlatformConditionText =>
        "公开 diagnostic_only 证据：Windows 条件入口为 launcher.exe；Linux 条件入口为 InfinityNikki\\InfinityNikki.exe + -SkipLauncherTokenCheck_SD。internal_environment_gate=unresolved；不得迁移到 Windows/CN。";

    public string IpcResearchText =>
        "ipc_status=unresolved · uri_status=unresolved · evidence=description_only；公开资料没有 Infinity Nikki 的 QLocalServer name、角色、framing、schema、握手或生命周期，本应用不模拟 URI/IPC。";

    public string LatestControlledReceiptText =>
        "report=provider-verification-8edea914fdbf41e3b5dc86c2d2b6df42.json · SHA-256=6F351B1118BEFB12C1CEC3249586EEBB78E91B83F15DE0DBE8541958B7D610C4 · ShellElevated=true · FinalIdentityPassed=true · FailureCode=ObservedSuccess · cleanup passed · LOGIN_STATUS=not_observed。";

    public string ProviderValidationArtifactPath =>
        "未配置外部验证工件；当前状态以实时 provider/preflight 为准。";

    public string ProviderValidationStatusText => _providerValidationStatusText;

    public string ProviderValidationReceiptText => _providerValidationReceiptText;

    public string ActivityText
    {
        get => _activityText;
        private set => SetField(ref _activityText, value);
    }

    public string StaticReadinessText
    {
        get => _staticReadinessText;
        private set => SetField(ref _staticReadinessText, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetField(ref _lastUpdatedText, value);
    }

    public string LastErrorText
    {
        get => _lastErrorText;
        private set => SetField(ref _lastErrorText, value);
    }

    public string SettingsStatusText
    {
        get => _settingsStatusText;
        private set => SetField(ref _settingsStatusText, value);
    }

    public string ExportStatusText
    {
        get => _exportStatusText;
        private set => SetField(ref _exportStatusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRefresh));
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(CanDiscover));
                OnPropertyChanged(nameof(CanAttemptOfficialAssistedLaunch));
                OnPropertyChanged(nameof(CanAttemptSelectedChannelLaunch));
                OnPropertyChanged(nameof(CanPlanChannelStore));
                OnPropertyChanged(nameof(CanBuildChannelStore));
                OnPropertyChanged(nameof(CanActivateSelectedChannel));
                OnPropertyChanged(nameof(PrimaryActionText));
                OnPropertyChanged(nameof(ManualAttemptAuthorizationText));
                NotifyLaunchStateChanged();
            }
        }
    }

    public bool CanRefresh => _profile is not null && !IsBusy;

    public bool CanExport => _profile is not null && _snapshot is not null && !IsBusy;

    public bool CanDiscover => !IsBusy;

    public bool CanAttemptOfficialAssistedLaunch =>
        !IsBusy &&
        !_isLaunchAttemptInProgress &&
        !_isOfficialAssistedRunning &&
        _activeProcessBinding is null &&
        _selectedCandidate is
        {
            Identity.RegionFamily: RegionFamily.MainlandChina,
            Identity.DistributionChannel: DistributionChannel.Official,
            Identity.AccountAuthority: AccountAuthority.Papergames,
            Provider.ProviderId: LaunchProviderCatalog.CnWindows131ContractId,
            Provider.MaximumCapability: LaunchCapability.OfficialAssisted,
        } &&
        _preflightResult is
        {
            StaticIdentityPassed: true,
            FailureCode: LaunchPreflightFailureCode.ExecutionGateClosed,
            ExecutionAllowed: false,
            Plan: null,
        };

    public async Task RefreshOfficialAssistedRuntimeAsync(
        CancellationToken cancellationToken = default)
    {
        var binding = _activeProcessBinding;
        if (binding is null || _isCloseGameInProgress)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var observation = await Task.Run(
                () => _launchCoordinator.Observe(binding),
                cancellationToken)
            .ConfigureAwait(true);
        if (observation.RunningProcessAlive)
        {
            _lastObservedGameAtUtc = DateTimeOffset.UtcNow;
            _isLaunchCleanupRequired = false;
            if (!_isOfficialAssistedRunning)
            {
                _isOfficialAssistedRunning = true;
                _launchLifecycleText = "Running · 本次 profile 的游戏进程已确认";
                NotifyLaunchAttemptChanged();
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;
        var startupGracePeriod = binding.RootProcessStartTimeUtc.AddSeconds(45);
        if (observation.RootProcessAlive ||
            observation.GameProcesses.Count > 0 ||
            observation.AuxiliaryProcesses.Count > 0)
        {
            if (_lastObservedGameAtUtc is not null || now >= startupGracePeriod)
            {
                _isOfficialAssistedRunning = false;
                _isLaunchCleanupRequired = true;
                _launchLifecycleText =
                    $"FailedAwaitingCleanup · RootPid={binding.RootProcessId} · ShippingNotRunning";
                NotifyLaunchAttemptChanged();
            }

            return;
        }

        if (_lastObservedGameAtUtc is null && now < startupGracePeriod)
        {
            return;
        }

        _activeProcessBinding = null;
        _lastObservedGameAtUtc = null;
        _isOfficialAssistedRunning = false;
        _isLaunchCleanupRequired = false;
        _launchAttemptStatusText =
            $"attempt={binding.AttemptId:N} · 游戏进程已退出 · 可重新启动";
        _launchLifecycleText =
            $"Exited · RootPid={binding.RootProcessId} · 当前 profile 的游戏进程不再运行";
        NotifyLaunchAttemptChanged();
    }

    public async Task<OfficialAssistedProcessStopResult> CloseOfficialAssistedGameAsync(
        CancellationToken cancellationToken = default)
    {
        var binding = _activeProcessBinding;
        if (binding is null)
        {
            return new OfficialAssistedProcessStopResult
            {
                Succeeded = true,
                Detail = "当前没有绑定的游戏进程。",
            };
        }

        _isCloseGameInProgress = true;
        IsBusy = true;
        ActivityText = "正在关闭当前 profile 的游戏";
        NotifyLaunchAttemptChanged();
        try
        {
            var result = await Task.Run(
                    () => _launchCoordinator.Stop(binding),
                    cancellationToken)
                .ConfigureAwait(true);
            if (!result.Succeeded)
            {
                _isLaunchCleanupRequired = true;
                LastErrorText = CombineErrors(
                    _settingsErrorText,
                    RedactUiText(result.Detail))!;
                return result;
            }

            _activeProcessBinding = null;
            _lastObservedGameAtUtc = null;
            _isOfficialAssistedRunning = false;
            _isLaunchCleanupRequired = false;
            _launchAttemptStatusText =
                $"attempt={binding.AttemptId:N} · 已关闭游戏 · 可重新启动";
            _launchLifecycleText =
                $"Exited · RootPid={binding.RootProcessId} · 已结束当前 profile 游戏进程";
            NotifyLaunchAttemptChanged();
            return result;
        }
        finally
        {
            ActivityText = "空闲";
            _isCloseGameInProgress = false;
            IsBusy = false;
            NotifyLaunchAttemptChanged();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task initializationTask;
            lock (_initializationSync)
            {
                if (_initialized)
                {
                    return;
                }

                _initializationTask ??= InitializeCoreAsync(cancellationToken);
                initializationTask = _initializationTask;
            }

            try
            {
                await initializationTask.WaitAsync(cancellationToken);
                CompleteInitializationTask(initializationTask);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                CompleteInitializationTask(initializationTask);
            }
            catch
            {
                CompleteInitializationTask(initializationTask);
                throw;
            }
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        ActivityText = "正在读取设置";

        UserSettings? settings = null;
        try
        {
            settings = await _settingsStore.LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _settingsErrorText = RedactUiText($"设置读取失败：{ex.GetType().Name}: {ex.Message}");
            SettingsStatusText = "设置读取失败；本次不把设置内容当作安装或能力认证来源。";
        }

        var selectedProfileId = settings?.SelectedProfileId;
        if (settings is not null)
        {
            // Retained so a later profile save round-trips the sections this
            // view model does not otherwise touch, such as gamepad mappings.
            _persistedSettings = settings;
            GamepadSettings = settings.Gamepad;
            AppearanceSettings = settings.Appearance;
            GeneralSettings = settings.General;
            DownloadSettings = settings.Download;
            FileManagementSettings = settings.FileManagement;
            ScreenshotSettings = settings.Screenshot;
            DeveloperModeEnabled = settings.DeveloperModeEnabled;
            InitializeChannelStoreSettings(settings.ChannelStore);

            var unknownCapability = settings.Profiles.Any(profile =>
                profile.Capability is not LaunchCapability.NotVerified and
                    not LaunchCapability.OfficialAssisted);
            SettingsStatusText = unknownCapability
                ? $"已读取设置；能力字段仅作为非权威 seed，未知值已忽略 · {_settingsStore.SettingsFilePath}"
                : $"已读取设置；仅使用 SelectedProfileId 作为精确选择提示 · {_settingsStore.SettingsFilePath}";
        }

        ApplicationLanguageRuntime.Apply(GeneralSettings.LanguageTag);

        try
        {
            await DiscoverProfilesAsync(selectedProfileId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _profileBuildResult = null;
            _selectedCandidate = null;
            _preflightResult = null;
            _profile = null;
            PlatformEvidence.Clear();
            AddSteamOsEvidence();
            SetProfileInventory([], null);
            _settingsErrorText = CombineErrors(
                _settingsErrorText,
                RedactUiText($"安装发现失败：{ex.GetType().Name}: {ex.Message}"));
            SettingsStatusText = "安装发现失败；请选择游戏目录后重试。";
            NotifyProfileChanged();
        }

        if (_profile is null)
        {
            ActivityText = "等待选择游戏目录";
            StaticReadinessText = "NotVerified · 未发现唯一可验证 profile";
            LastErrorText = _settingsErrorText ?? "未找到唯一可验证安装候选。";
            return;
        }

        await RefreshAsync(cancellationToken);
    }

    private void CompleteInitializationTask(Task initializationTask)
    {
        if (!initializationTask.IsCompleted)
        {
            return;
        }

        lock (_initializationSync)
        {
            if (!ReferenceEquals(_initializationTask, initializationTask))
            {
                return;
            }

            _initialized = initializationTask.Status is TaskStatus.RanToCompletion;
            _initializationTask = null;
        }
    }

    public async Task DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ActivityText = "正在只读发现安装候选";
        var shouldRefresh = false;
        try
        {
            var result = await _installationDiscovery.DiscoverAsync(cancellationToken);
            await ApplyDiscoveryResultAsync(result, null, cancellationToken);
            if (_profile is not null)
            {
                await PersistSelectedSeedAsync(_profile.ProfileId, cancellationToken);
                shouldRefresh = true;
            }
        }
        catch (OperationCanceledException)
        {
            ActivityText = "安装发现已取消";
            throw;
        }
        catch (Exception ex)
        {
            LastErrorText = CombineErrors(
                _settingsErrorText,
                RedactUiText($"安装发现失败：{ex.GetType().Name}: {ex.Message}"))!;
            ActivityText = "空闲";
        }
        finally
        {
            IsBusy = false;
        }

        if (shouldRefresh)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    public async Task DiscoverFromManualRootAsync(
        string gameRootPath,
        string? launcherRootPath,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ActivityText = "正在验证手动选择的安装根";
        var shouldRefresh = false;
        try
        {
            var result = await _installationDiscovery.DiscoverFromManualGameRootAsync(
                gameRootPath,
                launcherRootPath,
                cancellationToken);
            await ApplyDiscoveryResultAsync(result, null, cancellationToken);
            if (_profile is not null)
            {
                await PersistSelectedSeedAsync(_profile.ProfileId, cancellationToken);
                shouldRefresh = true;
            }
        }
        catch (OperationCanceledException)
        {
            ActivityText = "手动发现已取消";
            throw;
        }
        catch (Exception ex)
        {
            LastErrorText = CombineErrors(
                _settingsErrorText,
                RedactUiText($"手动安装发现失败：{ex.GetType().Name}: {ex.Message}"))!;
            ActivityText = "空闲";
        }
        finally
        {
            IsBusy = false;
        }

        if (shouldRefresh)
        {
            await RefreshAsync(cancellationToken);
        }
    }

    public async Task<bool> SelectProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            LastErrorText = "ProfileId 为空；未改变当前选择。";
            return false;
        }

        if (IsBusy)
        {
            LastErrorText = "当前操作尚未完成；未改变当前选择。";
            return false;
        }

        if (_profileBuildResult is null)
        {
            LastErrorText = "尚无新鲜的 Profile 候选；未改变当前选择。";
            return false;
        }

        IsBusy = true;
        ActivityText = "正在选择已发现的 profile";
        var shouldRefresh = false;
        var selectionSucceeded = false;
        try
        {
            var matches = _profileBuildResult.Candidates
                .Where(candidate =>
                    candidate.Profile is not null &&
                    (candidate.State is InstallationCandidateState.Candidate or
                        InstallationCandidateState.ReadyForStaticVerification) &&
                    string.Equals(candidate.ProfileId, profileId, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                LastErrorText = "所选 ProfileId 没有唯一的新鲜候选；未改变当前选择。";
                return false;
            }

            await ApplyDiscoveryResultAsync(_profileBuildResult, profileId, cancellationToken);
            if (_profile is null ||
                _selectedCandidate is null ||
                !string.Equals(_profile.ProfileId, profileId, StringComparison.Ordinal) ||
                !string.Equals(_selectedCandidate.ProfileId, profileId, StringComparison.Ordinal))
            {
                LastErrorText = "Profile 选择未落到请求的候选；未改变当前选择。";
                return false;
            }

            await PersistSelectedSeedAsync(profileId, cancellationToken);
            shouldRefresh = true;
            selectionSucceeded = true;
        }
        catch (OperationCanceledException)
        {
            ActivityText = "Profile 选择已取消";
            throw;
        }
        catch (Exception ex)
        {
            LastErrorText = CombineErrors(
                _settingsErrorText,
                RedactUiText($"Profile 选择失败：{ex.GetType().Name}: {ex.Message}"))!;
        }
        finally
        {
            ActivityText = "空闲";
            IsBusy = false;
        }

        if (shouldRefresh)
        {
            await RefreshAsync(cancellationToken);
        }

        return selectionSucceeded &&
            string.Equals(_profile?.ProfileId, profileId, StringComparison.Ordinal) &&
            string.Equals(_selectedCandidate?.ProfileId, profileId, StringComparison.Ordinal);
    }

    private async Task DiscoverProfilesAsync(
        string? selectedProfileId,
        CancellationToken cancellationToken)
    {
        var result = await _installationDiscovery.DiscoverAsync(cancellationToken);
        await ApplyDiscoveryResultAsync(result, selectedProfileId, cancellationToken);
    }

    private async Task PersistSelectedSeedAsync(
        string selectedProfileId,
        CancellationToken cancellationToken)
    {
        if (_profileBuildResult is null)
        {
            return;
        }

        var profiles = _profileBuildResult.Candidates
            .Where(candidate => candidate.Profile is not null)
            .Select(candidate => NormalizeProfile(candidate.Profile!))
            .GroupBy(profile => profile.ProfileId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var updated = _persistedSettings with
        {
            SelectedProfileId = selectedProfileId,
            Profiles = profiles,
        };
        await _settingsStore.SaveAsync(updated, cancellationToken);
        _persistedSettings = updated;
        SettingsStatusText = $"已保存 SelectedProfileId 与非权威 profile seed · {_settingsStore.SettingsFilePath}";
    }

    /// <summary>
    /// Persists the gamepad section on its own, leaving the selected profile and
    /// the non-authoritative profile seed as they were last written.
    /// </summary>
    public async Task SaveGamepadSettingsAsync(
        GamepadSettings gamepad,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gamepad);

        var updated = _persistedSettings with { Gamepad = gamepad };
        await _settingsStore.SaveAsync(updated, cancellationToken);
        _persistedSettings = updated;
        GamepadSettings = gamepad;
    }

    public async Task SaveGeneralSettingsAsync(
        GeneralSettings general,
        CancellationToken cancellationToken = default)
    {
        var normalized = ApplicationSettingsValidator.Normalize(general);
        var updated = _persistedSettings with { General = normalized };
        await _settingsStore.SaveAsync(updated, cancellationToken);
        _persistedSettings = updated;
        GeneralSettings = normalized;
        OnPropertyChanged(nameof(GeneralSettings));
    }

    public async Task SaveDownloadSettingsAsync(
        DownloadSettings download,
        CancellationToken cancellationToken = default)
    {
        var normalized = ApplicationSettingsValidator.Normalize(download);
        var updated = _persistedSettings with { Download = normalized };
        await _settingsStore.SaveAsync(updated, cancellationToken);
        _persistedSettings = updated;
        DownloadSettings = normalized;
        OnPropertyChanged(nameof(DownloadSettings));
        OnPropertyChanged(nameof(ChannelStoreRootPath));
    }

    public async Task SaveFileManagementSettingsAsync(
        FileManagementSettings fileManagement,
        CancellationToken cancellationToken = default)
    {
        var normalized = ApplicationSettingsValidator.Normalize(fileManagement);
        var updated = _persistedSettings with { FileManagement = normalized };
        await _settingsStore.SaveAsync(updated, cancellationToken);
        _persistedSettings = updated;
        FileManagementSettings = normalized;
        OnPropertyChanged(nameof(FileManagementSettings));
    }

    public async Task SaveScreenshotSettingsAsync(
        ScreenshotSettings screenshot,
        CancellationToken cancellationToken = default)
    {
        var normalized = ApplicationSettingsValidator.Normalize(screenshot);
        var updated = _persistedSettings with { Screenshot = normalized };
        await _settingsStore.SaveAsync(updated, cancellationToken);
        _persistedSettings = updated;
        ScreenshotSettings = normalized;
        OnPropertyChanged(nameof(ScreenshotSettings));
    }

    public async Task SaveHotkeySettingsAsync(
        string mainWindowHotkey,
        string screenshotHotkey,
        CancellationToken cancellationToken = default)
    {
        var general = ApplicationSettingsValidator.Normalize(
            GeneralSettings with { MainWindowHotkey = mainWindowHotkey });
        var screenshot = ApplicationSettingsValidator.Normalize(
            ScreenshotSettings with { Hotkey = screenshotHotkey });
        var updated = _persistedSettings with
        {
            General = general,
            Screenshot = screenshot,
        };
        await _settingsStore.SaveAsync(updated, cancellationToken);
        _persistedSettings = updated;
        GeneralSettings = general;
        ScreenshotSettings = screenshot;
        OnPropertyChanged(nameof(GeneralSettings));
        OnPropertyChanged(nameof(ScreenshotSettings));
    }

    public async Task SaveDeveloperModeAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var updated = _persistedSettings with { DeveloperModeEnabled = enabled };
        await _settingsStore.SaveAsync(updated, cancellationToken);
        _persistedSettings = updated;
        DeveloperModeEnabled = enabled;
        OnPropertyChanged(nameof(DeveloperModeEnabled));
    }

    public async Task SaveThemeModeAsync(
        ThemeMode themeMode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(themeMode))
        {
            throw new ArgumentOutOfRangeException(nameof(themeMode));
        }

        await SaveAppearanceSettingsAsync(
            AppearanceSettings with { ThemeMode = themeMode },
            cancellationToken);
    }

    public async Task SaveAppearanceSettingsAsync(
        AppearanceSettings appearance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        var normalized = AppearanceSettingsValidator.Normalize(appearance);
        var updated = _persistedSettings with { Appearance = normalized };
        await _settingsStore.SaveAsync(updated, cancellationToken);
        _persistedSettings = updated;
        AppearanceSettings = normalized;
        OnPropertyChanged(nameof(AppearanceSettings));
        OnPropertyChanged(nameof(ThemeMode));
    }

    public async Task SaveGalleryRootAsync(
        string profileId,
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("ProfileId is required.", nameof(profileId));
        }

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Gallery root is required.", nameof(rootPath));
        }

        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var galleryProfiles = _persistedSettings.GalleryProfiles
            .Where(item => !string.Equals(
                item.ProfileId,
                profileId,
                StringComparison.Ordinal))
            .Append(new GalleryProfileSettings
            {
                ProfileId = profileId,
                RootPath = normalizedRoot,
            })
            .OrderBy(item => item.ProfileId, StringComparer.Ordinal)
            .ToArray();
        var updated = _persistedSettings with { GalleryProfiles = galleryProfiles };
        await _settingsStore.SaveAsync(updated, cancellationToken);
        _persistedSettings = updated;

        if (string.Equals(_profile?.ProfileId, profileId, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(GalleryRootPath));
        }
    }

    public async Task ResetGalleryRootAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("ProfileId is required.", nameof(profileId));
        }

        var galleryProfiles = _persistedSettings.GalleryProfiles
            .Where(item => !string.Equals(
                item.ProfileId,
                profileId,
                StringComparison.Ordinal))
            .ToArray();
        if (galleryProfiles.Length == _persistedSettings.GalleryProfiles.Count)
        {
            return;
        }

        var updated = _persistedSettings with { GalleryProfiles = galleryProfiles };
        await _settingsStore.SaveAsync(updated, cancellationToken);
        _persistedSettings = updated;

        if (string.Equals(_profile?.ProfileId, profileId, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(GalleryRootPath));
        }
    }

    private string? FindGalleryRoot(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return null;
        }

        var matches = _persistedSettings.GalleryProfiles
            .Where(item => string.Equals(
                item.ProfileId,
                profileId,
                StringComparison.Ordinal))
            .Select(item => item.RootPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private async Task ApplyDiscoveryResultAsync(
        ProfileBuildResult result,
        string? selectedProfileId,
        CancellationToken cancellationToken)
    {
        result = ApplyPersistedChannelStoreTargets(result);
        _profileBuildResult = result;
        _selectedCandidate = null;
        _preflightResult = null;
        _profile = null;

        var selectable = result.Candidates
            .Where(candidate =>
                candidate.Profile is not null &&
                (candidate.State is InstallationCandidateState.Candidate or
                    InstallationCandidateState.ReadyForStaticVerification))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(selectedProfileId))
        {
            var exactMatches = selectable
                .Where(candidate => string.Equals(
                    candidate.ProfileId,
                    selectedProfileId,
                    StringComparison.Ordinal))
                .ToArray();
            _selectedCandidate = exactMatches.Length == 1 ? exactMatches[0] : null;
            if (exactMatches.Length > 1)
            {
                _settingsErrorText = CombineErrors(
                    _settingsErrorText,
                    "SelectedProfileId 对应多个候选；未自动猜测渠道。");
            }
            else if (_selectedCandidate is null)
            {
                _settingsErrorText = CombineErrors(
                    _settingsErrorText,
                    "SelectedProfileId 未在本次新鲜发现结果中精确匹配；未自动选择其他 profile。");
            }
        }
        else if (selectable.Length == 1)
        {
            _selectedCandidate = selectable[0];
        }

        if (_selectedCandidate?.Profile is { } discoveredProfile)
        {
            _profile = NormalizeProfile(discoveredProfile);
        }

        SetProfileInventory(
            selectable
                .Select(candidate => candidate.Profile!)
                .Select(NormalizeProfile),
            _selectedCandidate?.ProfileId);

        if (_selectedCandidate is
            {
                State: InstallationCandidateState.ReadyForStaticVerification,
                Provider: not null,
                LauncherRootPath: not null,
                GameRootPath: not null,
            })
        {
            _preflightResult = await _preflightVerifier.VerifyAsync(
                _selectedCandidate,
                cancellationToken);
        }

        RebuildPlatformEvidence(result.Candidates);
        NotifyProfileChanged();
        StaticReadinessText = _selectedCandidate is null
            ? "NotVerified · 未选择唯一安装候选"
            : _preflightResult switch
            {
                { StaticIdentityPassed: true } =>
                    "静态身份已验证 · catalog gate closed · 点击后自动构建 session-only attempt",
                { FailureCode: var code } => $"静态身份未通过 · {code}",
                _ => $"候选已发现 · {FormatCandidateState(_selectedCandidate.State)}",
            };
        LastUpdatedText = DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        ActivityText = "空闲";
        OnPropertyChanged(nameof(CanRefresh));
    }

    private void RebuildPlatformEvidence(IEnumerable<InstallationProfileCandidate> candidates)
    {
        PlatformEvidence.Clear();
        foreach (var candidate in candidates)
        {
            var title = FormatCandidateTitle(candidate);
            var state = FormatCandidateState(candidate.State);
            var detail = string.IsNullOrWhiteSpace(candidate.Detail)
                ? BuildCandidateDetail(candidate)
                : RedactUiText(candidate.Detail);
            var capability = candidate.Provider is { } provider
                ? $"{provider.MaximumCapability} · ExecutionEnabled={provider.ExecutionEnabled} · VerifiedOneClick=false"
                : "NotVerified · Provider=null · VerifiedOneClick=false";
            PlatformEvidence.Add(new PlatformEvidenceItemViewModel(
                title,
                candidate.DiscoverySource.ToString(),
                state,
                detail,
                capability));
        }

        AddSteamOsEvidence();
        OnPropertyChanged(nameof(PlatformEvidence));
    }

    private void AddSteamOsEvidence() =>
        PlatformEvidence.Add(new PlatformEvidenceItemViewModel(
            "SteamOS / Proton",
            "Windows provider",
            "NotApplicable / NotVerified",
            "wrapper + -SkipLauncherTokenCheck_SD 仅属于 SteamOS/Proton 公开契约；Windows 不复制该参数。",
            "平台受限 · VerifiedOneClick=false"));

    private static string BuildCandidateDetail(InstallationProfileCandidate candidate)
    {
        if (candidate.SteamManifest is { } manifest)
        {
            return $"manifest={manifest.ManifestPath} · staging={manifest.StagingPath ?? "未提供"} · common={manifest.CommonInstallPath ?? "未提供"} · StateFlags={manifest.StateFlags ?? "未提供"} · SizeOnDisk={manifest.SizeOnDisk?.ToString() ?? "未提供"} · buildid={manifest.BuildId ?? "未提供"} · InstalledDepots={manifest.InstalledDepotIds.Count}";
        }

        return $"launcherRoot={candidate.LauncherRootPath ?? "未提供"} · gameRoot={candidate.GameRootPath ?? "未提供"} · failure={candidate.FailureCode}";
    }

    private static string FormatCandidateTitle(InstallationProfileCandidate candidate) =>
        string.IsNullOrWhiteSpace(candidate.DisplayName)
            ? candidate.ProfileId
            : candidate.DisplayName;

    private static string FormatCandidateState(InstallationCandidateState state) => state switch
    {
        InstallationCandidateState.Downloading => "Downloading / NotInstalled / NotVerified / NotReady",
        InstallationCandidateState.Candidate => "Candidate / NotVerified / NotReady",
        InstallationCandidateState.ReadyForStaticVerification => "ReadyForStaticVerification / NotVerified / NotReady",
        InstallationCandidateState.Incomplete => "Incomplete / NotVerified / NotReady",
        InstallationCandidateState.Unsupported => "Unsupported / NotVerified / NotReady",
        _ => "NotFound / NotVerified / NotReady",
    };

    private static string FormatDistribution(DistributionChannel channel) => channel switch
    {
        DistributionChannel.Official => "Official / CN",
        DistributionChannel.Bilibili => "Bilibili / B服",
        DistributionChannel.Steam => "Steam / 国际服",
        DistributionChannel.Epic => "Epic",
        _ => "Unknown",
    };

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_profile is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ActivityText = "正在只读检查组件";

        try
        {
            var results = await _installationInspector.InspectAsync(_profile, cancellationToken);

            if (_selectedCandidate is
                {
                    State: InstallationCandidateState.ReadyForStaticVerification,
                    Provider: not null,
                    LauncherRootPath: not null,
                    GameRootPath: not null,
                })
            {
                _preflightResult = await _preflightVerifier.VerifyAsync(
                    _selectedCandidate,
                    cancellationToken);
            }

            Components.Clear();
            foreach (var component in results)
            {
                Components.Add(new ComponentItemViewModel(component));
            }

            var readinessState = ResolveReadinessState(results);
            var failureReason = BuildFailureReason(results);
            var capturedAtUtc = DateTimeOffset.UtcNow;

            _snapshot = new LaunchSnapshot
            {
                ProfileId = _profile.ProfileId,
                State = readinessState is LaunchState.Ready
                    ? LaunchState.Failed
                    : readinessState,
                Capability = _profile.Capability,
                CapturedAtUtc = capturedAtUtc,
                Components = results,
                LastFailureReason = readinessState is LaunchState.Ready
                    ? "静态身份检查完成，但 Nikkiward 当前执行能力仍未启用。"
                    : failureReason,
            };

            StaticReadinessText = _preflightResult switch
            {
                { StaticIdentityPassed: true } => $"静态身份已验证 · {results.Count} 个组件（ExecutionGateClosed）",
                { FailureCode: var code } => $"静态身份未通过 · {code}",
                _ => readinessState switch
                {
                    LaunchState.Ready => $"静态身份已验证 · {results.Count} 个组件（非启动就绪）",
                    LaunchState.NotInstalled => "静态身份未通过 · 存在缺失组件",
                    _ => "静态身份未通过 · 检查结果不完整",
                },
            };
            LastUpdatedText = capturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
            LastErrorText = CombineErrors(_settingsErrorText, failureReason) ?? "无（本次只读检查）";
            ExportStatusText = "当前快照可导出；报告会脱敏路径并排除命令行、token、进程内存与网络载荷。";
            ActivityText = "空闲";
            OnPropertyChanged(nameof(CanExport));
            NotifyProfileChanged();
        }
        catch (OperationCanceledException)
        {
            ActivityText = "检查已取消";
            throw;
        }
        catch (Exception ex)
        {
            _snapshot = null;
            StaticReadinessText = "检查失败";
            LastErrorText = CombineErrors(
                _settingsErrorText,
                RedactUiText($"组件刷新失败：{ex.GetType().Name}: {ex.Message}"))!;
            ActivityText = "空闲";
            OnPropertyChanged(nameof(CanExport));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<DiagnosticReportExportResult> ExportDiagnosticsAsync(
        string destinationDirectory,
        ArtBackdropDiagnosticState? backdropState = null,
        CancellationToken cancellationToken = default)
    {
        if (_profile is null || _snapshot is null)
        {
            throw new InvalidOperationException("请先完成组件刷新，再导出诊断。");
        }

        if (IsBusy)
        {
            throw new InvalidOperationException("另一个操作正在进行，请稍后重试。");
        }

        IsBusy = true;
        ActivityText = "正在导出脱敏诊断";

        try
        {
            var diagnosticProfile = _profile with { ProfileId = DiagnosticProfileId };
            var diagnosticSnapshot = _snapshot with { ProfileId = DiagnosticProfileId };
            var result = await _diagnosticExporter.ExportAsync(
                diagnosticProfile,
                diagnosticSnapshot,
                destinationDirectory,
                backdropState,
                cancellationToken);

            if (result.Succeeded)
            {
                ExportStatusText = $"已导出：{result.JsonFilePath} · {result.TextFilePath}";
            }
            else
            {
                LastErrorText = CombineErrors(
                    _settingsErrorText,
                    result.Error ?? "诊断导出失败，服务未返回错误详情。")!;
                ExportStatusText = "最近一次诊断导出失败。";
            }

            return result;
        }
        finally
        {
            ActivityText = "空闲";
            IsBusy = false;
        }
    }

    public void ReportUiError(string message)
    {
        LastErrorText = CombineErrors(_settingsErrorText, RedactUiText(message))!;
        ActivityText = "空闲";
    }

    public void ReportOfficialAssistedLaunchNotStarted(
        string code,
        string detail,
        bool isError)
    {
        _isLaunchAttemptInProgress = false;
        _isOfficialAssistedRunning = false;
        _launchAttemptStatusText = $"{code} · RootPid=null · 没有创建 provider";
        _launchLifecycleText = $"NotRunning · {code} · RootPid=null";
        if (isError)
        {
            LastErrorText = CombineErrors(
                _settingsErrorText,
                RedactUiText($"实验启动未开始：{code}: {detail}"))!;
        }

        ActivityText = "空闲";
        NotifyLaunchAttemptChanged();
    }

    public async Task<OfficialAssistedLaunchPreparation> PrepareOfficialAssistedLaunchAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return new OfficialAssistedLaunchPreparation
            {
                FailureCode = "Preflight.OperationInProgress",
                Detail = "另一个操作正在进行；没有创建 provider。",
            };
        }

        _isLaunchAttemptInProgress = true;
        IsBusy = true;
        ActivityText = "正在重新验证 CN frozen contract";
        _launchLifecycleText = "PreparingBackend · 正在执行静态身份与 clean-baseline 检查";
        NotifyLaunchAttemptChanged();
        var keepLaunchState = false;
        try
        {
            var preparation = await _launchCoordinator.PrepareAsync(
                _selectedCandidate,
                cancellationToken);
            if (preparation.Succeeded)
            {
                _launchAttemptStatusText =
                    "正在启动 · 静态身份通过 · 已自动构建瞬时 plan · VerifiedOneClick=false";
                _launchLifecycleText =
                    "PreparingBackend · plan 已构建，马上请求 Windows UAC";
                keepLaunchState = true;
            }
            else
            {
                _launchAttemptStatusText =
                    $"{preparation.FailureCode} · 未创建 provider";
                _launchLifecycleText =
                    $"NotRunning · {preparation.FailureCode} · RootPid=null";
                LastErrorText = CombineErrors(
                    _settingsErrorText,
                    RedactUiText($"实验启动准备未通过：{preparation.FailureCode}: {preparation.Detail}"))!;
            }

            NotifyLaunchAttemptChanged();
            return preparation;
        }
        finally
        {
            ActivityText = "空闲";
            if (!keepLaunchState)
            {
                _isLaunchAttemptInProgress = false;
                NotifyLaunchAttemptChanged();
            }
            IsBusy = false;
        }
    }

    public async Task<OfficialAssistedLaunchReceipt> StartPreparedOfficialAssistedLaunchAsync(
        OfficialAssistedLaunchPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (IsBusy)
        {
            return new OfficialAssistedLaunchReceipt
            {
                AttemptId = Guid.NewGuid(),
                RequestedAtUtc = DateTimeOffset.UtcNow,
                FailureCode = "Runtime.OperationInProgress",
                Detail = "另一个操作正在进行；没有创建 provider。",
            };
        }

        _isLaunchAttemptInProgress = true;
        IsBusy = true;
        ActivityText = "正在向 Windows 请求启动官方 xstarter";
        _launchAttemptStatusText = "Launching · 等待 Windows UAC 结果";
        _launchLifecycleText = "Launching · 尚未取得 RootPid";
        NotifyLaunchAttemptChanged();
        await Task.Yield();
        try
        {
            var receipt = _launchCoordinator.Start(preparation);
            if (receipt.StartRequested)
            {
                _launchAttemptStatusText =
                    $"attempt={receipt.AttemptId:N} · RootPid={receipt.RootProcessId} · LOGIN_STATUS=not_observed";
                _launchLifecycleText =
                    $"Launching · official xstarter RootPid={receipt.RootProcessId} · 下游界面等待人工确认";
                if (!_launchCoordinator.TryBind(preparation, receipt, out var binding))
                {
                    _launchAttemptStatusText =
                        $"Runtime.ProcessBindingUnavailable · RootPid={receipt.RootProcessId}";
                    _launchLifecycleText =
                        "Failed · 无法绑定本次 profile 的游戏进程身份";
                    LastErrorText = CombineErrors(
                        _settingsErrorText,
                        "已提交官方 xstarter，但无法建立精确的游戏进程绑定。")!;
                    return receipt;
                }

                _activeProcessBinding = binding;
                _lastObservedGameAtUtc = null;
                _isLaunchCleanupRequired = false;
                _launchLifecycleText =
                    "Launching · 官方 xstarter 已提交 · 等待当前 Profile 的 Shipping 进程";
                NotifyLaunchAttemptChanged();
            }
            else if (string.Equals(
                         receipt.FailureCode,
                         "Runtime.UserCancelledElevation",
                         StringComparison.Ordinal))
            {
                _launchAttemptStatusText =
                    "用户取消 Windows UAC · RootPid=null · 没有创建 provider";
                _launchLifecycleText =
                    "NotRunning · UserCancelledElevation · RootPid=null";
            }
            else
            {
                _launchAttemptStatusText =
                    $"{receipt.FailureCode} · RootPid=null · 未创建 provider";
                _launchLifecycleText =
                    $"Failed · {receipt.FailureCode} · RootPid=null";
                LastErrorText = CombineErrors(
                    _settingsErrorText,
                    RedactUiText($"实验启动未创建 provider：{receipt.FailureCode}: {receipt.Detail}"))!;
            }

            NotifyLaunchAttemptChanged();
            return receipt;
        }
        finally
        {
            ActivityText = "空闲";
            _isLaunchAttemptInProgress = false;
            IsBusy = false;
            NotifyLaunchAttemptChanged();
        }
    }

    private void NotifyLaunchAttemptChanged()
    {
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(PrimaryActionHelpText));
        OnPropertyChanged(nameof(PrimaryActionHintText));
        OnPropertyChanged(nameof(CanCloseGame));
        OnPropertyChanged(nameof(CloseGameButtonVisibility));
        OnPropertyChanged(nameof(CloseGameButtonLabel));
        OnPropertyChanged(nameof(LifecycleText));
        OnPropertyChanged(nameof(ExecutionSummaryTitle));
        OnPropertyChanged(nameof(ApplicationExecutionText));
        OnPropertyChanged(nameof(ManualAttemptAuthorizationText));
        OnPropertyChanged(nameof(CanAttemptOfficialAssistedLaunch));
        OnPropertyChanged(nameof(CanAttemptExternalChannelLaunch));
        OnPropertyChanged(nameof(CanAttemptSelectedChannelLaunch));
        OnPropertyChanged(nameof(PreloadNoticeVisibility));
        NotifyLaunchStateChanged();
    }

    public async Task LoadProviderValidationReceiptAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _providerValidationStatusText =
                "未配置外部验证工件；执行状态仅以当前 provider/preflight 为准。";
            _providerValidationReceiptText = _providerValidationStatusText;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _providerValidationStatusText =
                $"验证事务工件读取失败：{ex.GetType().Name}: {RedactUiText(ex.Message)}";
            _providerValidationReceiptText = _providerValidationStatusText;
        }

        OnPropertyChanged(nameof(ProviderValidationStatusText));
        OnPropertyChanged(nameof(ProviderValidationReceiptText));
    }

    private static LaunchProfile NormalizeProfile(LaunchProfile profile) =>
        profile with { Capability = LaunchCapability.NotVerified };

    private void SetProfileInventory(
        IEnumerable<LaunchProfile> profiles,
        string? selectedProfileId)
    {
        Profiles.Clear();
        foreach (var profile in profiles)
        {
            var normalized = NormalizeProfile(profile);
            Profiles.Add(new LaunchProfileItemViewModel(
                normalized,
                string.Equals(
                    normalized.ProfileId,
                    selectedProfileId,
                    StringComparison.Ordinal)));
        }
    }

    private static LaunchState ResolveReadinessState(IReadOnlyList<ComponentVerification> components)
    {
        if (components.Count == 0)
        {
            return LaunchState.Failed;
        }

        if (components.Any(component => !component.InspectionSucceeded))
        {
            return LaunchState.Failed;
        }

        if (components.Any(component => !component.Exists))
        {
            return LaunchState.NotInstalled;
        }

        return components.Any(component =>
                component.SignatureStatus is not AuthenticodeSignatureStatus.Valid ||
                component.Sha256?.Length is not 64)
            ? LaunchState.Failed
            : LaunchState.Ready;
    }

    private static string? BuildFailureReason(IReadOnlyList<ComponentVerification> components)
    {
        if (components.Count == 0)
        {
            return "检查器未返回任何组件。";
        }

        var failures = components
            .Select(component =>
            {
                if (!component.InspectionSucceeded)
                {
                    return $"{component.DisplayName}: {RedactUiText(component.Error ?? "检查失败")}";
                }

                if (!component.Exists)
                {
                    return $"{component.DisplayName}: 文件不存在";
                }

                if (component.SignatureStatus is not AuthenticodeSignatureStatus.Valid)
                {
                    return $"{component.DisplayName}: 签名状态为 {component.SignatureStatus}";
                }

                return component.Sha256?.Length is 64
                    ? null
                    : $"{component.DisplayName}: SHA-256 未完成";
            })
            .Where(failure => failure is not null)
            .ToArray();

        return failures.Length == 0 ? null : string.Join(" | ", failures!);
    }

    private static string? CombineErrors(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return string.IsNullOrWhiteSpace(second) ? null : second;
        }

        return string.IsNullOrWhiteSpace(second) ? first : $"{first} | {second}";
    }

    internal static string RedactUiText(string value)
    {
        var result = value;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        result = result.Replace(
            ApplicationDataPaths.Root,
            "%NIKKIWARD_DATA%",
            StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            result = result.Replace(localAppData, "%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            result = result.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        if (Environment.UserName.Length >= 3)
        {
            result = result.Replace(Environment.UserName, "[USER]", StringComparison.OrdinalIgnoreCase);
        }

        if (Environment.MachineName.Length >= 3)
        {
            result = result.Replace(Environment.MachineName, "[HOST]", StringComparison.OrdinalIgnoreCase);
        }

        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"(?i)(authorization|bearer|cookie|token|paperlauncher|paperstartup|x-sdk)\s*[:=]\s*[^\s,;]+",
            "$1=[REDACTED]");

        return result;
    }

    private void NotifyProfileChanged()
    {
        OnPropertyChanged(nameof(ProfileDisplayName));
        OnPropertyChanged(nameof(ChannelText));
        OnPropertyChanged(nameof(CurrentProfileSummary));
        OnPropertyChanged(nameof(PlatformText));
        OnPropertyChanged(nameof(GameRootPath));
        OnPropertyChanged(nameof(SelectedProfileId));
        OnPropertyChanged(nameof(GalleryRootPath));
        OnPropertyChanged(nameof(XStarterPath));
        OnPropertyChanged(nameof(LaunchCapabilityText));
        OnPropertyChanged(nameof(InstallationCandidateText));
        OnPropertyChanged(nameof(StaticIdentityText));
        OnPropertyChanged(nameof(ExecutionGateText));
        OnPropertyChanged(nameof(ApplicationExecutionText));
        OnPropertyChanged(nameof(DownloadStatusText));
        OnPropertyChanged(nameof(DownloadStatusDetailText));
        OnPropertyChanged(nameof(PreloadNoticeVisibility));
        OnPropertyChanged(nameof(PlayTimeText));
        OnPropertyChanged(nameof(PlayTimeDetailText));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(PrimaryActionHelpText));
        OnPropertyChanged(nameof(PrimaryActionHintText));
        OnPropertyChanged(nameof(ServiceShortcutText));
        OnPropertyChanged(nameof(ProviderExecutablePath));
        OnPropertyChanged(nameof(ProviderWorkingDirectory));
        OnPropertyChanged(nameof(ProviderArgumentVector));
        OnPropertyChanged(nameof(ProviderContractId));
        OnPropertyChanged(nameof(ProviderExecutionEnabledText));
        OnPropertyChanged(nameof(ManualSelectionHint));
        OnPropertyChanged(nameof(ProfileIdentityText));
        OnPropertyChanged(nameof(SteamProvenanceText));
        OnPropertyChanged(nameof(SteamPlatformConditionText));
        OnPropertyChanged(nameof(IpcResearchText));
        OnPropertyChanged(nameof(LatestControlledReceiptText));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanDiscover));
        OnPropertyChanged(nameof(CanAttemptOfficialAssistedLaunch));
        OnPropertyChanged(nameof(CanAttemptExternalChannelLaunch));
        OnPropertyChanged(nameof(CanAttemptSelectedChannelLaunch));
        OnPropertyChanged(nameof(SelectedChannelLaunchRoute));
        OnPropertyChanged(nameof(SelectedChannelUsesOfficialAssisted));
        OnPropertyChanged(nameof(CanPlanChannelStore));
        OnPropertyChanged(nameof(CanBuildChannelStore));
        OnPropertyChanged(nameof(CanActivateSelectedChannel));
        OnPropertyChanged(nameof(CanRollbackChannelActivation));
        OnPropertyChanged(nameof(ChannelStoreRootPath));
        OnPropertyChanged(nameof(ChannelStoreCapacityText));
        NotifyLaunchStateChanged();
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ComponentItemViewModel
{
    public ComponentItemViewModel(ComponentVerification component)
    {
        ComponentId = component.ComponentId;
        DisplayName = component.DisplayName;
        FilePath = string.IsNullOrWhiteSpace(component.FilePath) ? "未配置" : component.FilePath;
        StatusText = !component.InspectionSucceeded
            ? "检查不完整"
            : !component.Exists
                ? "缺失"
                : component.SignatureStatus is not AuthenticodeSignatureStatus.Valid
                    ? "签名未通过"
                    : component.Sha256?.Length is not 64
                        ? "哈希未完成"
                        : "验证通过";
        FileVersionText = string.IsNullOrWhiteSpace(component.FileVersion) ? "未提供" : component.FileVersion;
        ProductVersionText = string.IsNullOrWhiteSpace(component.ProductVersion) ? "未提供" : component.ProductVersion;
        SignatureText = FormatSignature(component);
        Sha256Text = string.IsNullOrWhiteSpace(component.Sha256) ? "未计算" : component.Sha256;
        FileMetadataText = FormatMetadata(component);
        ErrorText = BuildComponentIssue(component);
    }

    public string ComponentId { get; }

    public string DisplayName { get; }

    public string FilePath { get; }

    public string StatusText { get; }

    public string FileVersionText { get; }

    public string ProductVersionText { get; }

    public string SignatureText { get; }

    public string Sha256Text { get; }

    public string FileMetadataText { get; }

    public string ErrorText { get; }

    private static string FormatSignature(ComponentVerification component)
    {
        var status = component.SignatureStatus switch
        {
            AuthenticodeSignatureStatus.Valid => "Valid · 有效",
            AuthenticodeSignatureStatus.NotSigned => "NotSigned · 未签名",
            AuthenticodeSignatureStatus.Invalid => "Invalid · 无效",
            AuthenticodeSignatureStatus.Untrusted => "Untrusted · 不受信任",
            AuthenticodeSignatureStatus.Error => "Error · 检查错误",
            _ => "NotChecked · 未检查",
        };

        return string.IsNullOrWhiteSpace(component.SignatureStatusCode)
            ? status
            : $"{status} ({component.SignatureStatusCode})";
    }

    private static string FormatMetadata(ComponentVerification component)
    {
        var size = component.FileSizeBytes is long bytes ? FormatBytes(bytes) : "大小未知";
        var modified = component.LastWriteTimeUtc is DateTimeOffset timestamp
            ? timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "时间未知";
        return $"{size} · 修改于 {modified}";
    }

    private static string BuildComponentIssue(ComponentVerification component)
    {
        if (!component.InspectionSucceeded)
        {
            return MainPageViewModel.RedactUiText(component.Error ?? "检查失败");
        }

        if (!component.Exists)
        {
            return "文件不存在";
        }

        if (component.SignatureStatus is not AuthenticodeSignatureStatus.Valid)
        {
            return $"签名状态为 {component.SignatureStatus}";
        }

        return component.Sha256?.Length is 64 ? "无" : "SHA-256 未完成";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB"];
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}

public sealed class LaunchProfileItemViewModel
{
    public LaunchProfileItemViewModel(LaunchProfile profile, bool isSelected)
    {
        ProfileId = profile.ProfileId;
        DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName)
            ? profile.ProfileId
            : profile.DisplayName;
        Channel = string.IsNullOrWhiteSpace(profile.Channel) ? "未配置" : profile.Channel;
        GameRootPath = string.IsNullOrWhiteSpace(profile.GameRootPath) ? "未配置" : profile.GameRootPath;
        XStarterPath = string.IsNullOrWhiteSpace(profile.XStarterPath) ? "未配置" : profile.XStarterPath;
        CapabilityText = "NotVerified";
        IsSelected = isSelected;
    }

    public string ProfileId { get; }

    public string DisplayName { get; }

    public string Channel { get; }

    public string GameRootPath { get; }

    public string XStarterPath { get; }

    public string CapabilityText { get; }

    public string VerifiedOneClickText => "false";

    public string CapabilitySummary =>
        $"{CapabilityText} · VerifiedOneClick={VerifiedOneClickText}";

    public string SelectionText => IsSelected ? "当前" : "已保存";

    public bool IsSelected { get; }
}

public sealed class PlatformEvidenceItemViewModel
{
    public PlatformEvidenceItemViewModel(
        string title,
        string source,
        string state,
        string detail,
        string capability)
    {
        Title = title;
        Source = source;
        State = state;
        Detail = detail;
        Capability = capability;
    }

    public string Title { get; }

    public string Source { get; }

    public string State { get; }

    public string Detail { get; }

    public string Capability { get; }
}
