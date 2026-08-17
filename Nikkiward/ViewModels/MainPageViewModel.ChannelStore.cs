using Microsoft.UI.Xaml;
using Nikkiward.Models;
using Nikkiward.Services;

namespace Nikkiward.ViewModels;

public sealed partial class MainPageViewModel
{
    private readonly IChannelStoreBuilder _channelStoreBuilder = new WindowsChannelStoreBuilder();
    private readonly IChannelActivationService _channelActivationService = new WindowsChannelActivationService();
    private readonly IChannelEntryLauncher _channelEntryLauncher = new WindowsChannelEntryLauncher();
    private ChannelStoreSettings _channelStoreSettings = new();
    private ChannelStoreBuildPlan? _channelStorePlan;
    private ChannelStoreBuildReceipt? _channelStoreReceipt;
    private ChannelActivationReceipt? _lastChannelActivationReceipt;
    private string? _pendingChannelStoreRootPath;
    private string _channelStoreStatusText = "尚未创建三渠道单本体计划";
    private double _channelStoreProgressPercent;
    private bool _isChannelStoreBusy;

    public string ChannelStoreRootPath =>
        _channelStorePlan?.StoreRootPath ??
        _pendingChannelStoreRootPath ??
        _channelStoreSettings.StoreRootPath ??
        "尚未选择";

    public string ChannelStoreStatusText
    {
        get => _channelStoreStatusText;
        private set => SetField(ref _channelStoreStatusText, value);
    }

    public string ChannelStoreCapacityText => _channelStorePlan is null
        ? "等待 dry-run：完整哈希后才计算硬链接与复制量"
        : $"硬链接 {FormatStoreBytes(_channelStorePlan.HardLinkBytes)} · " +
          $"复制 {FormatStoreBytes(_channelStorePlan.CopyBytes)} · " +
          $"计划 {_channelStorePlan.PlanSha256[..12]}";

    public double ChannelStoreProgressPercent
    {
        get => _channelStoreProgressPercent;
        private set => SetField(ref _channelStoreProgressPercent, value);
    }

    public Visibility ChannelStoreProgressVisibility =>
        IsChannelStoreBusy ? Visibility.Visible : Visibility.Collapsed;

    public bool IsChannelStoreBusy
    {
        get => _isChannelStoreBusy;
        private set
        {
            if (SetField(ref _isChannelStoreBusy, value))
            {
                OnPropertyChanged(nameof(ChannelStoreProgressVisibility));
                OnPropertyChanged(nameof(CanPlanChannelStore));
                OnPropertyChanged(nameof(CanBuildChannelStore));
                OnPropertyChanged(nameof(CanActivateSelectedChannel));
            }
        }
    }

    public bool CanPlanChannelStore =>
        !IsBusy &&
        !IsChannelStoreBusy &&
        HasUniqueSelectableChannel(DistributionChannel.Official) &&
        HasUniqueSelectableChannel(DistributionChannel.Bilibili) &&
        HasUniqueSelectableChannel(DistributionChannel.Steam);

    public bool CanBuildChannelStore =>
        CanPlanChannelStore &&
        _channelStorePlan?.CanExecute is true;

    public bool CanActivateSelectedChannel =>
        !IsBusy &&
        !IsChannelStoreBusy &&
        _selectedCandidate is
        {
            Profile: not null,
            GameRootPath: not null,
            State: InstallationCandidateState.Candidate or
                InstallationCandidateState.ReadyForStaticVerification,
        };

    public bool CanRollbackChannelActivation =>
        !IsBusy &&
        !IsChannelStoreBusy &&
        _lastChannelActivationReceipt is { Succeeded: true, ConfigChanged: true };

    public bool CanAttemptExternalChannelLaunch =>
        !IsBusy &&
        !_isLaunchAttemptInProgress &&
        !_isOfficialAssistedRunning &&
        _activeProcessBinding is null &&
        _selectedCandidate is not null &&
        _channelEntryLauncher.CreatePlan(_selectedCandidate).CanLaunch;

    public bool CanAttemptSelectedChannelLaunch =>
        CanAttemptOfficialAssistedLaunch || CanAttemptExternalChannelLaunch;

    public bool SelectedChannelUsesOfficialAssisted =>
        _selectedCandidate?.Identity.DistributionChannel is DistributionChannel.Official;

    public string SelectedChannelLaunchRoute => _selectedCandidate?.Identity.DistributionChannel switch
    {
        DistributionChannel.Official => "官方国服 xstarter",
        DistributionChannel.Bilibili => "Bilibili xstarter 直启",
        DistributionChannel.Steam => "Steam xstarter 直启",
        _ => "未选择",
    };

    public void SelectChannelStoreRoot(string storeRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRootPath);
        _pendingChannelStoreRootPath = Path.GetFullPath(storeRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _channelStorePlan = null;
        ChannelStoreStatusText = "已选择存储位置；点击 Dry-run 后开始枚举和完整哈希。";
        OnPropertyChanged(nameof(ChannelStoreRootPath));
        OnPropertyChanged(nameof(ChannelStoreCapacityText));
        OnPropertyChanged(nameof(CanBuildChannelStore));
    }

    public async Task PlanChannelStoreAsync(
        string storeRootPath,
        CancellationToken cancellationToken = default)
    {
        if (!CanPlanChannelStore || _profileBuildResult is null)
        {
            ChannelStoreStatusText = "需要同时发现官方国服、B 服和 Steam 国际服三个唯一候选。";
            return;
        }

        SelectChannelStoreRoot(storeRootPath);
        IsChannelStoreBusy = true;
        ChannelStoreProgressPercent = 0;
        ChannelStoreStatusText = "正在枚举并计算三渠道文件 SHA-256";
        try
        {
            var progress = new Progress<ChannelStoreProgress>(ApplyChannelStoreProgress);
            _channelStorePlan = await _channelStoreBuilder.CreatePlanAsync(
                new ChannelStoreBuildRequest
                {
                    Candidates = _profileBuildResult.Candidates,
                    StoreRootPath = storeRootPath,
                },
                progress,
                cancellationToken);
            ChannelStoreStatusText = _channelStorePlan.CanExecute
                ? $"dry-run 已冻结：{_channelStorePlan.Imports.Count} 个导入对象，" +
                  $"{_channelStorePlan.Variants.Count} 个渠道 manifest"
                : $"dry-run 被拒绝：{_channelStorePlan.FailureCode} · {_channelStorePlan.FailureDetail}";
            OnPropertyChanged(nameof(ChannelStoreRootPath));
            OnPropertyChanged(nameof(ChannelStoreCapacityText));
            OnPropertyChanged(nameof(CanBuildChannelStore));
        }
        finally
        {
            IsChannelStoreBusy = false;
        }
    }

    public async Task BuildChannelStoreAsync(CancellationToken cancellationToken = default)
    {
        if (!CanBuildChannelStore || _channelStorePlan is null)
        {
            ChannelStoreStatusText = "请先完成可执行的 dry-run。";
            return;
        }

        IsChannelStoreBusy = true;
        ChannelStoreProgressPercent = 0;
        ChannelStoreStatusText = "正在导入共享对象并物化三个渠道根";
        try
        {
            var progress = new Progress<ChannelStoreProgress>(ApplyChannelStoreProgress);
            _channelStoreReceipt = await _channelStoreBuilder.BuildAsync(
                _channelStorePlan,
                _channelStorePlan.PlanSha256,
                progress,
                cancellationToken);
            if (!_channelStoreReceipt.Succeeded)
            {
                ChannelStoreStatusText =
                    $"物化失败：{_channelStoreReceipt.FailureCode} · " +
                    _channelStoreReceipt.FailureDetail;
                return;
            }

            var materialized = ApplyChannelStoreTargets(_profileBuildResult!, _channelStorePlan);
            var selectedProfileId = _selectedCandidate?.ProfileId;
            var profiles = materialized.Candidates
                .Where(candidate => candidate.Profile is not null)
                .Select(candidate => NormalizeProfile(candidate.Profile!))
                .GroupBy(profile => profile.ProfileId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            _channelStoreSettings = new ChannelStoreSettings
            {
                StoreRootPath = _channelStorePlan.StoreRootPath,
                LastReceiptId = _channelStoreReceipt.ReceiptId,
                LastPlanSha256 = _channelStorePlan.PlanSha256,
                LastCompletedAtUtc = _channelStoreReceipt.CompletedAtUtc,
                Profiles = materialized.Candidates
                    .Where(candidate => candidate.Profile is not null && candidate.GameRootPath is not null)
                    .Select(candidate => new ChannelStoreProfileSettings
                    {
                        ProfileId = candidate.ProfileId,
                        DistributionChannel = candidate.Identity.DistributionChannel,
                        GameRootPath = candidate.GameRootPath!,
                        LauncherRootPath = candidate.LauncherRootPath,
                        XStarterPath = candidate.Profile!.XStarterPath,
                    })
                    .ToArray(),
            };
            var updatedSettings = _persistedSettings with
            {
                ChannelStore = _channelStoreSettings,
                Profiles = profiles,
                SelectedProfileId = selectedProfileId,
            };
            await _settingsStore.SaveAsync(updatedSettings, cancellationToken);
            _persistedSettings = updatedSettings;
            await ApplyDiscoveryResultAsync(materialized, selectedProfileId, cancellationToken);
            ChannelStoreStatusText =
                $"单本体已创建：receipt={_channelStoreReceipt.ReceiptId}；" +
                "三个源安装仍保留，完成逐渠道启动验证后可手动卸载。";
            OnPropertyChanged(nameof(ChannelStoreRootPath));
            OnPropertyChanged(nameof(ChannelStoreCapacityText));
            OnPropertyChanged(nameof(CanActivateSelectedChannel));
        }
        finally
        {
            IsChannelStoreBusy = false;
        }
    }

    public async Task<ChannelActivationReceipt?> ActivateSelectedChannelAsync(
        CancellationToken cancellationToken = default)
    {
        if (!CanActivateSelectedChannel || _selectedCandidate?.GameRootPath is null)
        {
            ChannelStoreStatusText = "当前没有可激活的渠道 profile。";
            return null;
        }

        IsChannelStoreBusy = true;
        try
        {
            var request = new ChannelActivationRequest
            {
                Candidate = _selectedCandidate,
                TargetGameRootPath = _selectedCandidate.GameRootPath,
            };
            var plan = await _channelActivationService.CreatePlanAsync(request, cancellationToken);
            if (!plan.CanActivate)
            {
                ChannelStoreStatusText = $"渠道激活被拒绝：{plan.FailureCode} · {plan.FailureDetail}";
                return null;
            }

            _lastChannelActivationReceipt = await _channelActivationService.ActivateAsync(
                request,
                plan.PlanSha256,
                cancellationToken);
            ChannelStoreStatusText = _lastChannelActivationReceipt.Succeeded
                ? $"已激活 {FormatDistribution(_selectedCandidate.Identity.DistributionChannel)} · " +
                  _selectedCandidate.GameRootPath
                : $"渠道激活失败：{_lastChannelActivationReceipt.FailureCode} · " +
                  _lastChannelActivationReceipt.FailureDetail;
            OnPropertyChanged(nameof(CanRollbackChannelActivation));
            return _lastChannelActivationReceipt;
        }
        finally
        {
            IsChannelStoreBusy = false;
        }
    }

    public async Task<ChannelActivationReceipt?> RollbackLastChannelActivationAsync(
        CancellationToken cancellationToken = default)
    {
        if (_lastChannelActivationReceipt is null || !CanRollbackChannelActivation)
        {
            return null;
        }

        IsChannelStoreBusy = true;
        try
        {
            var rollback = await _channelActivationService.RollbackAsync(
                _lastChannelActivationReceipt,
                cancellationToken);
            ChannelStoreStatusText = rollback.Succeeded
                ? "已恢复上一个 launcher gameDir。"
                : $"gameDir 回滚失败：{rollback.FailureCode} · {rollback.FailureDetail}";
            if (rollback.Succeeded)
            {
                _lastChannelActivationReceipt = null;
            }

            OnPropertyChanged(nameof(CanRollbackChannelActivation));
            return rollback;
        }
        finally
        {
            IsChannelStoreBusy = false;
        }
    }

    public async Task<ChannelLaunchReceipt?> StartSelectedExternalChannelAsync(
        CancellationToken cancellationToken = default)
    {
        if (_selectedCandidate is null)
        {
            return null;
        }

        if (_lastChannelActivationReceipt is not { Succeeded: true } activation ||
            !string.Equals(activation.ProfileId, _selectedCandidate.ProfileId, StringComparison.Ordinal) ||
            activation.DistributionChannel != _selectedCandidate.Identity.DistributionChannel ||
            !SamePath(activation.TargetGameRootPath, _selectedCandidate.GameRootPath))
        {
            ReportUiError("渠道激活结果与当前 Profile 不一致；本次启动已取消。");
            return null;
        }

        var plan = _channelEntryLauncher.CreatePlan(_selectedCandidate);
        if (!plan.CanLaunch)
        {
            ReportUiError($"渠道入口不可用：{plan.FailureCode} · {plan.FailureDetail}");
            return null;
        }

        _isLaunchAttemptInProgress = true;
        _isLaunchCleanupRequired = false;
        IsBusy = true;
        ActivityText = $"正在提交 {SelectedChannelLaunchRoute}";
        NotifyLaunchAttemptChanged();
        try
        {
            var receipt = await _channelEntryLauncher.LaunchAsync(
                _selectedCandidate,
                plan.PlanSha256,
                cancellationToken);
            if (!receipt.Succeeded)
            {
                _launchAttemptStatusText =
                    $"渠道入口提交失败：{receipt.FailureCode} · {receipt.FailureDetail}";
                NotifyLaunchAttemptChanged();
                return receipt;
            }

            if (!ExternalChannelProcessBindingFactory.TryCreate(
                    _selectedCandidate,
                    receipt,
                    out var binding))
            {
                _launchAttemptStatusText =
                    $"已提交 {SelectedChannelLaunchRoute}，但无法建立精确进程绑定";
                ReportUiError("渠道入口已提交，但根进程身份或当前 Profile 游戏路径不完整。");
                NotifyLaunchAttemptChanged();
                return receipt;
            }

            _activeProcessBinding = binding;
            _lastObservedGameAtUtc = null;
            _launchAttemptStatusText =
                $"已提交 {SelectedChannelLaunchRoute} · 正在确认游戏进程";
            _launchLifecycleText =
                $"Launching · RootPid={binding.RootProcessId} · 等待当前 profile 游戏进程";
            NotifyLaunchAttemptChanged();

            var observation = await WaitForExternalChannelGameAsync(binding, cancellationToken);
            if (!observation.RunningProcessAlive)
            {
                var cleanupRequired = observation.RootProcessAlive ||
                    observation.GameProcesses.Count > 0 ||
                    observation.AuxiliaryProcesses.Count > 0;
                _activeProcessBinding = cleanupRequired ? binding : null;
                _isLaunchCleanupRequired = cleanupRequired;
                _launchAttemptStatusText =
                    $"已提交 {SelectedChannelLaunchRoute}，但 45 秒内未观察到 Shipping 进程";
                _launchLifecycleText =
                    cleanupRequired
                        ? $"FailedAwaitingCleanup · RootPid={binding.RootProcessId} · GameProcessNotObserved"
                        : $"Failed · RootPid={binding.RootProcessId} · GameProcessNotObserved";
                ReportUiError("渠道 xstarter 已提交，但没有在等待窗口内进入当前 Profile 的游戏进程。");
                NotifyLaunchAttemptChanged();
                return receipt;
            }

            _isOfficialAssistedRunning = true;
            _isLaunchCleanupRequired = false;
            _launchAttemptStatusText = _selectedCandidate.Identity.DistributionChannel is
                DistributionChannel.Bilibili
                    ? "已进入 B服游戏进程 · 首次使用 Store B服需在游戏内登录"
                    : $"已进入 {SelectedChannelLaunchRoute} 游戏进程";
            _launchLifecycleText =
                $"Running · RootPid={binding.RootProcessId} · 当前 profile 游戏进程已确认";
            NotifyLaunchAttemptChanged();
            return receipt;
        }
        catch (OperationCanceledException)
        {
            if (_activeProcessBinding is { } binding)
            {
                var observation = await Task.Run(
                        () => _launchCoordinator.Observe(binding),
                        CancellationToken.None)
                    .ConfigureAwait(true);
                var cleanupRequired = observation.RootProcessAlive ||
                    observation.GameProcesses.Count > 0 ||
                    observation.AuxiliaryProcesses.Count > 0;
                _activeProcessBinding = cleanupRequired ? binding : null;
                _isLaunchCleanupRequired = cleanupRequired;
                NotifyLaunchAttemptChanged();
            }

            throw;
        }
        finally
        {
            ActivityText = "空闲";
            _isLaunchAttemptInProgress = false;
            IsBusy = false;
            NotifyLaunchAttemptChanged();
        }
    }

    private async Task<OfficialAssistedProcessObservation> WaitForExternalChannelGameAsync(
        OfficialAssistedProcessBinding binding,
        CancellationToken cancellationToken)
    {
        var deadline = binding.RootProcessStartTimeUtc.AddSeconds(45);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = await Task.Run(
                    () => _launchCoordinator.Observe(binding),
                    cancellationToken)
                .ConfigureAwait(true);
            if (observation.RunningProcessAlive)
            {
                _lastObservedGameAtUtc = DateTimeOffset.UtcNow;
                return observation;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return observation;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }

    private static bool SamePath(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            var firstFullPath = Path.GetFullPath(first)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var secondFullPath = Path.GetFullPath(second)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(firstFullPath, secondFullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void InitializeChannelStoreSettings(ChannelStoreSettings settings)
    {
        _channelStoreSettings = settings ?? new ChannelStoreSettings();
        _pendingChannelStoreRootPath = null;
        ChannelStoreStatusText = string.IsNullOrWhiteSpace(_channelStoreSettings.LastReceiptId)
            ? "尚未创建三渠道单本体计划"
            : $"已载入单本体 receipt={_channelStoreSettings.LastReceiptId}";
        OnPropertyChanged(nameof(ChannelStoreRootPath));
    }

    private ProfileBuildResult ApplyPersistedChannelStoreTargets(ProfileBuildResult result)
    {
        if (_channelStoreSettings.Profiles.Count == 0)
        {
            return result;
        }

        var candidates = result.Candidates
            .Select(candidate =>
            {
                var stored = _channelStoreSettings.Profiles.FirstOrDefault(item =>
                    string.Equals(item.ProfileId, candidate.ProfileId, StringComparison.Ordinal) &&
                    item.DistributionChannel == candidate.Identity.DistributionChannel);
                return stored is null || !IsUsableStoredProfile(stored)
                    ? candidate
                    : WithStoredProfileTargets(candidate, stored);
            })
            .ToList();
        foreach (var stored in _channelStoreSettings.Profiles)
        {
            var alreadyPresent = candidates.Any(candidate =>
                candidate.Profile is not null &&
                string.Equals(candidate.ProfileId, stored.ProfileId, StringComparison.Ordinal) &&
                candidate.Identity.DistributionChannel == stored.DistributionChannel &&
                candidate.State is InstallationCandidateState.Candidate or
                    InstallationCandidateState.ReadyForStaticVerification);
            if (alreadyPresent)
            {
                continue;
            }

            var storedCandidate = CreateStoredCandidate(stored);
            if (storedCandidate is not null)
            {
                candidates.Add(storedCandidate);
            }
        }

        return result with { Candidates = candidates.ToArray() };
    }

    private static ProfileBuildResult ApplyChannelStoreTargets(
        ProfileBuildResult result,
        ChannelStoreBuildPlan plan)
    {
        var candidates = result.Candidates
            .Select(candidate =>
            {
                var variantId = candidate.Identity.DistributionChannel switch
                {
                    DistributionChannel.Official => GameVariantId.MainlandOfficial,
                    DistributionChannel.Bilibili => GameVariantId.MainlandBilibili,
                    DistributionChannel.Steam => GameVariantId.GlobalSteam,
                    _ => GameVariantId.Unknown,
                };
                var target = plan.Variants.FirstOrDefault(item => item.Definition.VariantId == variantId);
                return target is null ? candidate : WithChannelStoreTargets(candidate, target);
            })
            .ToArray();
        return result with { Candidates = candidates };
    }

    private static InstallationProfileCandidate WithChannelStoreTargets(
        InstallationProfileCandidate candidate,
        ChannelStoreVariantPlan target) =>
        WithProfileTargets(
            candidate,
            target.TargetGameRootPath,
            target.TargetLauncherRootPath,
            target.TargetXStarterPath,
            ProfileDiscoverySource.ChannelStoreReceipt);

    private static InstallationProfileCandidate WithStoredProfileTargets(
        InstallationProfileCandidate candidate,
        ChannelStoreProfileSettings stored) =>
        WithProfileTargets(
            candidate,
            stored.GameRootPath,
            stored.LauncherRootPath!,
            stored.XStarterPath!,
            ProfileDiscoverySource.ChannelStoreReceipt);

    private static InstallationProfileCandidate WithProfileTargets(
        InstallationProfileCandidate candidate,
        string gameRoot,
        string launcherRoot,
        string xstarterPath,
        ProfileDiscoverySource discoverySource)
    {
        if (candidate.Profile is null)
        {
            return candidate;
        }

        var normalizedGameRoot = Path.GetFullPath(gameRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedLauncherRoot = Path.GetFullPath(launcherRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedXStarterPath = Path.GetFullPath(xstarterPath);
        var profile = candidate.Profile with
        {
            GameRootPath = normalizedGameRoot,
            LauncherPath = Path.Combine(normalizedLauncherRoot, "launcher.exe"),
            XStarterPath = normalizedXStarterPath,
            GameExecutablePath = Path.Combine(normalizedGameRoot, "InfinityNikki.exe"),
            ShippingExecutablePath = Path.Combine(
                normalizedGameRoot,
                "X6Game",
                "Binaries",
                "Win64",
                "X6Game-Win64-Shipping.exe"),
            AntiCheatExecutablePath = Path.Combine(
                normalizedGameRoot,
                "X6Game",
                "Binaries",
                "Win64",
                "AntiCheatExpert",
                "ACE-Service64.exe"),
        };
        return candidate with
        {
            DiscoverySource = discoverySource,
            LauncherRootPath = normalizedLauncherRoot,
            GameRootPath = normalizedGameRoot,
            Profile = profile,
            Provider = candidate.Provider is null
                ? null
                : candidate.Provider with
                {
                    BackendExecutablePath = normalizedXStarterPath,
                    WorkingDirectory = normalizedLauncherRoot,
                },
            SteamManifest = discoverySource is ProfileDiscoverySource.ChannelStoreReceipt
                ? null
                : candidate.SteamManifest,
        };
    }

    private InstallationProfileCandidate? CreateStoredCandidate(ChannelStoreProfileSettings stored)
    {
        if (!IsUsableStoredProfile(stored))
        {
            return null;
        }

        var seed = _persistedSettings.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.ProfileId, stored.ProfileId, StringComparison.Ordinal));
        if (seed is null)
        {
            return null;
        }

        var identity = stored.DistributionChannel switch
        {
            DistributionChannel.Official => new ProfileIdentity
            {
                RegionFamily = RegionFamily.MainlandChina,
                DistributionChannel = DistributionChannel.Official,
                AccountAuthority = AccountAuthority.Papergames,
            },
            DistributionChannel.Bilibili => new ProfileIdentity
            {
                RegionFamily = RegionFamily.MainlandChina,
                DistributionChannel = DistributionChannel.Bilibili,
                AccountAuthority = AccountAuthority.Bilibili,
            },
            DistributionChannel.Steam => new ProfileIdentity
            {
                RegionFamily = RegionFamily.Overseas,
                DistributionChannel = DistributionChannel.Steam,
                AccountAuthority = AccountAuthority.Steam,
                SteamAppId = "3164330",
            },
            _ => null,
        };
        if (identity is null)
        {
            return null;
        }

        LaunchProviderBinding? provider = null;
        var state = InstallationCandidateState.Candidate;
        if (stored.DistributionChannel is DistributionChannel.Official)
        {
            var version = Directory.GetParent(stored.XStarterPath!)?.Name;
            if (version is null || !LaunchProviderCatalog.TryGet(
                    DistributionChannel.Official,
                    version,
                    out var contract))
            {
                return null;
            }

            provider = new LaunchProviderBinding
            {
                ProviderId = contract.ContractId,
                ContractVersion = contract.ContractVersion,
                BackendExecutablePath = stored.XStarterPath!,
                WorkingDirectory = stored.LauncherRootPath!,
                ArgumentPresetId = contract.ArgumentPresetId,
                ArgumentList = contract.ArgumentList,
                MaximumCapability = contract.MaximumCapability,
                ExecutionEnabled = contract.ExecutionEnabled,
            };
            state = InstallationCandidateState.ReadyForStaticVerification;
        }

        var candidate = new InstallationProfileCandidate
        {
            ProfileId = stored.ProfileId,
            DisplayName = seed.DisplayName,
            Identity = identity,
            State = state,
            DiscoverySource = ProfileDiscoverySource.ChannelStoreReceipt,
            LauncherRootPath = stored.LauncherRootPath,
            GameRootPath = stored.GameRootPath,
            Profile = seed,
            Provider = provider,
            FailureCode = provider is null
                ? ProfileBuildFailureCode.ProviderContractUnavailable
                : ProfileBuildFailureCode.None,
            Detail = "Profile restored from a verified NikkiwardStore receipt.",
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };
        return WithStoredProfileTargets(candidate, stored);
    }

    private bool HasUniqueSelectableChannel(DistributionChannel channel) =>
        _profileBuildResult?.Candidates.Count(candidate =>
            candidate.Identity.DistributionChannel == channel &&
            candidate.Profile is not null &&
            candidate.State is InstallationCandidateState.Candidate or
                InstallationCandidateState.ReadyForStaticVerification) == 1;

    private bool IsUsableStoredProfile(ChannelStoreProfileSettings stored)
    {
        if (string.IsNullOrWhiteSpace(_channelStoreSettings.StoreRootPath) ||
            string.IsNullOrWhiteSpace(stored.LauncherRootPath) ||
            string.IsNullOrWhiteSpace(stored.XStarterPath) ||
            !ChannelStoreReceiptVerifier.Verify(_channelStoreSettings, stored))
        {
            return false;
        }

        var storeRoot = Path.GetFullPath(_channelStoreSettings.StoreRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!IsPathWithin(storeRoot, stored.GameRootPath) ||
            !IsPathWithin(storeRoot, stored.LauncherRootPath) ||
            !IsPathWithin(storeRoot, stored.XStarterPath) ||
            !Directory.Exists(stored.LauncherRootPath) ||
            !File.Exists(Path.Combine(stored.LauncherRootPath, "launcher.exe")) ||
            !File.Exists(stored.XStarterPath))
        {
            return false;
        }

        var versionDirectory = Directory.GetParent(Path.GetFullPath(stored.XStarterPath));
        return versionDirectory?.Parent is not null &&
               SamePath(versionDirectory.Parent.FullName, stored.LauncherRootPath) &&
               IsUsableMaterializedRoot(stored.GameRootPath, stored.DistributionChannel);
    }

    private static bool IsPathWithin(string root, string path)
    {
        try
        {
            var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
            return !Path.IsPathRooted(relative) &&
                   !string.Equals(relative, "..", StringComparison.Ordinal) &&
                   !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                   !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsUsableMaterializedRoot(
        string root,
        DistributionChannel channel)
    {
        if (!Directory.Exists(root) ||
            !File.Exists(Path.Combine(root, "InfinityNikki.exe")) ||
            !File.Exists(Path.Combine(root, "X6Game", "Binaries", "Win64", "X6Game-Win64-Shipping.exe")))
        {
            return false;
        }

        var expectedMarker = channel switch
        {
            DistributionChannel.Official => "InfinityNikki Launcher",
            DistributionChannel.Bilibili => "InfinityNikkiBili Launcher",
            DistributionChannel.Steam => "InfinityNikkiSteam Launcher",
            _ => null,
        };
        var marker = ProductMarkerReader.TryRead(Path.Combine(root, "product.db"));
        return expectedMarker is not null &&
               string.Equals(marker?.Name, expectedMarker, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyChannelStoreProgress(ChannelStoreProgress progress)
    {
        ChannelStoreProgressPercent = progress.TotalBytes > 0
            ? Math.Clamp(progress.BytesCompleted * 100d / progress.TotalBytes, 0, 100)
            : progress.TotalFiles > 0
                ? Math.Clamp(progress.FilesCompleted * 100d / progress.TotalFiles, 0, 100)
                : 0;
        ChannelStoreStatusText = progress.Stage switch
        {
            ChannelStoreProgressStage.Enumerating => "正在枚举三渠道文件",
            ChannelStoreProgressStage.Hashing =>
                $"正在计算 SHA-256 · {progress.FilesCompleted}/{progress.TotalFiles}",
            ChannelStoreProgressStage.Importing =>
                $"正在导入共享对象 · {progress.FilesCompleted}/{progress.TotalFiles}",
            ChannelStoreProgressStage.Materializing =>
                $"正在物化渠道根 · {progress.FilesCompleted}/{progress.TotalFiles}",
            ChannelStoreProgressStage.PersistingReceipts => "正在写入 manifest 与 receipt",
            ChannelStoreProgressStage.Completed => "三渠道单本体已完成",
            _ => ChannelStoreStatusText,
        };
    }

    private static string FormatStoreBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
