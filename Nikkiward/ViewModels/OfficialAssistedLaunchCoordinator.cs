using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Nikkiward.Models;
using Nikkiward.Services;

namespace Nikkiward.ViewModels;

public sealed record OfficialAssistedLaunchPreparation
{
    public string ProfileId { get; init; } = string.Empty;

    public DateTimeOffset PreparedAtUtc { get; init; }

    public bool Succeeded { get; init; }

    public string FailureCode { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public LaunchPreflightResult? Preflight { get; init; }

    public LaunchPlan? Plan { get; init; }

    public IReadOnlyList<string> ObservedProcessPaths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> GameProcessPaths { get; init; } = Array.Empty<string>();
}

public sealed record OfficialAssistedLaunchReceipt
{
    public Guid AttemptId { get; init; }

    public DateTimeOffset RequestedAtUtc { get; init; }

    public bool StartRequested { get; init; }

    public int? RootProcessId { get; init; }

    public DateTimeOffset? RootProcessStartTimeUtc { get; init; }

    public string FailureCode { get; init; } = string.Empty;

    public int? NativeErrorCode { get; init; }

    public string Detail { get; init; } = string.Empty;
}

public sealed record OfficialAssistedProcessBinding
{
    public string ProfileId { get; init; } = string.Empty;

    public Guid AttemptId { get; init; }

    public DateTimeOffset RequestedAtUtc { get; init; }

    public int RootProcessId { get; init; }

    public DateTimeOffset RootProcessStartTimeUtc { get; init; }

    public string RootExecutablePath { get; init; } = string.Empty;

    public IReadOnlyList<string> GameProcessPaths { get; init; } = Array.Empty<string>();

    public string RunningProcessPath { get; init; } = string.Empty;

    public IReadOnlyList<string> AuxiliaryProcessPaths { get; init; } = Array.Empty<string>();
}

public sealed record OfficialAssistedProcessIdentity
{
    public int ProcessId { get; init; }

    public string ExecutablePath { get; init; } = string.Empty;

    public DateTimeOffset StartTimeUtc { get; init; }
}

public sealed record OfficialAssistedProcessObservation
{
    public bool RootProcessAlive { get; init; }

    public OfficialAssistedProcessIdentity? RootProcess { get; init; }

    public bool RunningProcessAlive { get; init; }

    public IReadOnlyList<OfficialAssistedProcessIdentity> GameProcesses { get; init; } =
        Array.Empty<OfficialAssistedProcessIdentity>();

    public IReadOnlyList<OfficialAssistedProcessIdentity> AuxiliaryProcesses { get; init; } =
        Array.Empty<OfficialAssistedProcessIdentity>();
}

public sealed record OfficialAssistedProcessStopResult
{
    public bool Succeeded { get; init; }

    public int StoppedProcessCount { get; init; }

    public string Detail { get; init; } = string.Empty;
}

public sealed class OfficialAssistedLaunchCoordinator
{
    private static readonly string[] RequiredObservedComponentIds =
    [
        "official-launcher",
        "official-backend",
        "game-bootstrap",
        "game-client",
        "anti-cheat-artifact",
    ];

    private readonly ILaunchPreflightVerifier _preflightVerifier;
    private readonly ConcurrentDictionary<
        Guid,
        ConcurrentDictionary<int, OfficialAssistedProcessIdentity>> _knownProcessesByAttempt = new();

    public OfficialAssistedLaunchCoordinator(ILaunchPreflightVerifier preflightVerifier)
    {
        _preflightVerifier = preflightVerifier;
    }

    public async Task<OfficialAssistedLaunchPreparation> PrepareAsync(
        InstallationProfileCandidate? candidate,
        CancellationToken cancellationToken = default)
    {
        if (candidate is null ||
            candidate.Provider is null ||
            string.IsNullOrWhiteSpace(candidate.LauncherRootPath) ||
            string.IsNullOrWhiteSpace(candidate.GameRootPath))
        {
            return FailedPreparation(
                "Preflight.ProfileUnavailable",
                "当前没有唯一且完整的 CN 安装候选。请先自动发现或选择游戏根与 launcher 根。");
        }

        var preflight = await _preflightVerifier.VerifyAsync(candidate, cancellationToken)
            .ConfigureAwait(false);
        if (!preflight.StaticIdentityPassed)
        {
            return FailedPreparation(
                $"Preflight.{preflight.FailureCode}",
                preflight.FailureDetail ?? "静态身份检查未通过。",
                preflight);
        }

        var contract = preflight.Contract;
        var provider = candidate.Provider;
        if ((!preflight.ExecutionAllowed &&
             (preflight.FailureCode is not LaunchPreflightFailureCode.ExecutionGateClosed ||
              preflight.Plan is not null ||
              contract?.ExecutionEnabled is not false)) ||
            (preflight.ExecutionAllowed &&
             (preflight.FailureCode is not LaunchPreflightFailureCode.None ||
              preflight.Plan is null)))
        {
            return FailedPreparation(
                "Preflight.ExecutionStateMismatch",
                "静态 verifier 返回了不一致的执行门状态。",
                preflight);
        }

        if (contract is null ||
            !string.Equals(
                contract.ContractId,
                LaunchProviderCatalog.CnWindows131ContractId,
                StringComparison.Ordinal) ||
            !string.Equals(provider.ProviderId, contract.ContractId, StringComparison.Ordinal) ||
            provider.ContractVersion != contract.ContractVersion ||
            candidate.Identity.RegionFamily is not RegionFamily.MainlandChina ||
            candidate.Identity.DistributionChannel is not DistributionChannel.Official ||
            candidate.Identity.AccountAuthority is not AccountAuthority.Papergames ||
            provider.MaximumCapability is not LaunchCapability.OfficialAssisted ||
            contract.MaximumCapability is not LaunchCapability.OfficialAssisted ||
            provider.ExecutionEnabled != contract.ExecutionEnabled)
        {
            return FailedPreparation(
                "Preflight.ProfileMismatch",
                "当前候选与冻结的 CN / Windows 1.3.1 OfficialAssisted contract 不一致。",
                preflight);
        }

        if (!string.Equals(
                provider.ArgumentPresetId,
                LaunchProviderCatalog.CnWindows131ArgumentPresetId,
                StringComparison.Ordinal) ||
            !provider.ArgumentList.SequenceEqual(["-skiplauncher"], StringComparer.Ordinal) ||
            !contract.ArgumentList.SequenceEqual(["-skiplauncher"], StringComparer.Ordinal) ||
            !string.Equals(contract.WorkingDirectoryRole, "LauncherRoot", StringComparison.Ordinal))
        {
            return FailedPreparation(
                "Preflight.ContractDrift",
                "参数 preset 或 WorkingDirectoryRole 已偏离冻结契约。",
                preflight);
        }

        if (!TryNormalizeDirectory(candidate.LauncherRootPath, out var launcherRoot) ||
            !TryNormalizeFile(provider.BackendExecutablePath, out var providerPath))
        {
            return FailedPreparation(
                "Preflight.PathUnavailable",
                "provider executable 或 launcher root 当前不可用。",
                preflight);
        }

        var expectedProviderPath = Path.GetFullPath(Path.Combine(
            launcherRoot,
            contract.BackendRelativeExecutablePath));
        if (!PathEquals(providerPath, expectedProviderPath) ||
            !TryNormalizeDirectory(provider.WorkingDirectory, out var workingDirectory) ||
            !PathEquals(workingDirectory, launcherRoot))
        {
            return FailedPreparation(
                "Preflight.PathMismatch",
                "provider executable 或 working directory 与 launcher root 派生结果不一致。",
                preflight);
        }

        var componentPaths = new List<string>();
        var componentPathsById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var componentId in RequiredObservedComponentIds)
        {
            var matches = preflight.Components
                .Where(component =>
                    component.Passed &&
                    string.Equals(component.ComponentId, componentId, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1 || !TryNormalizeFile(matches[0].FilePath, out var componentPath))
            {
                return FailedPreparation(
                    "Preflight.ComponentReceiptIncomplete",
                    $"静态 preflight 没有返回唯一且通过的 {componentId} 身份回执。",
                    preflight);
            }

            componentPaths.Add(componentPath);
            componentPathsById[componentId] = componentPath;
        }

        var plan = preflight is { ExecutionAllowed: true, Plan: not null }
            ? preflight.Plan
            : new LaunchPlan
            {
                ProviderId = contract.ContractId,
                ProviderExecutablePath = expectedProviderPath,
                WorkingDirectory = launcherRoot,
                ArgumentList = contract.ArgumentList.ToArray(),
            };

        if (!PathEquals(plan.ProviderExecutablePath, expectedProviderPath) ||
            !PathEquals(plan.WorkingDirectory, launcherRoot) ||
            !plan.ArgumentList.SequenceEqual(["-skiplauncher"], StringComparer.Ordinal))
        {
            return FailedPreparation(
                "Preflight.PlanMismatch",
                "最终 LaunchPlan 与冻结 contract 不一致。",
                preflight);
        }

        var baselineFailure = CheckCleanBaseline(componentPaths);
        if (baselineFailure is not null)
        {
            return FailedPreparation(
                baselineFailure.Value.Code,
                baselineFailure.Value.Detail,
                preflight);
        }

        return new OfficialAssistedLaunchPreparation
        {
            ProfileId = candidate.ProfileId,
            PreparedAtUtc = preflight.VerifiedAtUtc,
            Succeeded = true,
            Detail = preflight.ExecutionAllowed
                ? "静态身份和 contract 执行门均通过。"
                : "静态身份通过；本次点击形成一次不持久化的瞬时实验启动授权。",
            Preflight = preflight,
            Plan = plan,
            ObservedProcessPaths = componentPaths,
            GameProcessPaths =
            [
                componentPathsById["game-bootstrap"],
                componentPathsById["game-client"],
            ],
        };
    }

    public OfficialAssistedLaunchReceipt Start(OfficialAssistedLaunchPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var attemptId = Guid.NewGuid();
        var requestedAtUtc = DateTimeOffset.UtcNow;
        if (!preparation.Succeeded || preparation.Plan is null)
        {
            return new OfficialAssistedLaunchReceipt
            {
                AttemptId = attemptId,
                RequestedAtUtc = requestedAtUtc,
                FailureCode = "Runtime.PreparationMissing",
                Detail = "没有通过静态 preflight 的瞬时 LaunchPlan。",
            };
        }

        var baselineFailure = CheckCleanBaseline(preparation.ObservedProcessPaths);
        if (baselineFailure is not null)
        {
            return new OfficialAssistedLaunchReceipt
            {
                AttemptId = attemptId,
                RequestedAtUtc = requestedAtUtc,
                FailureCode = baselineFailure.Value.Code,
                Detail = baselineFailure.Value.Detail,
            };
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = preparation.Plan.ProviderExecutablePath,
                WorkingDirectory = preparation.Plan.WorkingDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal,
            };
            foreach (var argument in preparation.Plan.ArgumentList)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new OfficialAssistedLaunchReceipt
                {
                    AttemptId = attemptId,
                    RequestedAtUtc = requestedAtUtc,
                    FailureCode = "Runtime.ProcessNotCreated",
                    Detail = "Windows shell 没有返回已创建的官方 xstarter 进程。",
                };
            }

            DateTimeOffset? rootProcessStartTimeUtc = null;
            if (TryGetProcessCreationTime(process.Id, out var startTimeUtc))
            {
                rootProcessStartTimeUtc = startTimeUtc;
            }

            return new OfficialAssistedLaunchReceipt
            {
                AttemptId = attemptId,
                RequestedAtUtc = requestedAtUtc,
                StartRequested = true,
                RootProcessId = process.Id,
                RootProcessStartTimeUtc = rootProcessStartTimeUtc,
                Detail = "已把冻结 A contract 提交给官方 xstarter；下游界面与登录状态等待人工确认。",
            };
        }
        catch (Win32Exception ex)
        {
            return new OfficialAssistedLaunchReceipt
            {
                AttemptId = attemptId,
                RequestedAtUtc = requestedAtUtc,
                FailureCode = ex.NativeErrorCode == 1223
                    ? "Runtime.UserCancelledElevation"
                    : "Runtime.ProcessStartFailed",
                NativeErrorCode = ex.NativeErrorCode,
                Detail = ex.NativeErrorCode == 1223
                    ? "用户取消了 Windows 管理员确认；没有创建 provider。"
                    : $"Windows 未创建 provider（NativeCode={ex.NativeErrorCode}）。",
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            return new OfficialAssistedLaunchReceipt
            {
                AttemptId = attemptId,
                RequestedAtUtc = requestedAtUtc,
                FailureCode = "Runtime.ProcessStartFailed",
                Detail = $"Windows 未创建 provider（{ex.GetType().Name}）。",
            };
        }
    }

    public bool TryBind(
        OfficialAssistedLaunchPreparation preparation,
        OfficialAssistedLaunchReceipt receipt,
        out OfficialAssistedProcessBinding binding)
    {
        binding = new OfficialAssistedProcessBinding();
        if (!preparation.Succeeded ||
            preparation.Plan is null ||
            receipt is not { StartRequested: true, RootProcessId: > 0, RootProcessStartTimeUtc: not null } ||
            receipt.AttemptId == Guid.Empty ||
            preparation.GameProcessPaths.Count == 0 ||
            preparation.GameProcessPaths.Any(path => !TryNormalizeFile(path, out _)) ||
            !TryNormalizeFile(preparation.Plan.ProviderExecutablePath, out var rootExecutablePath))
        {
            return false;
        }

        var runningProcessPaths = preparation.GameProcessPaths
            .Where(path => string.Equals(
                Path.GetFileName(path),
                "X6Game-Win64-Shipping.exe",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (runningProcessPaths.Length != 1)
        {
            return false;
        }

        binding = new OfficialAssistedProcessBinding
        {
            ProfileId = preparation.ProfileId,
            AttemptId = receipt.AttemptId,
            RequestedAtUtc = receipt.RequestedAtUtc,
            RootProcessId = receipt.RootProcessId!.Value,
            RootProcessStartTimeUtc = receipt.RootProcessStartTimeUtc!.Value,
            RootExecutablePath = rootExecutablePath,
            GameProcessPaths = preparation.GameProcessPaths.ToArray(),
            RunningProcessPath = Path.GetFullPath(runningProcessPaths[0]),
        };
        return true;
    }

    public OfficialAssistedProcessObservation Observe(
        OfficialAssistedProcessBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var rootAlive = TryReadProcessIdentity(binding.RootProcessId, out var rootIdentity) &&
            rootIdentity.StartTimeUtc == binding.RootProcessStartTimeUtc &&
            PathEquals(rootIdentity.ExecutablePath, binding.RootExecutablePath);
        var knownProcesses = _knownProcessesByAttempt.GetOrAdd(
            binding.AttemptId,
            static _ => new ConcurrentDictionary<int, OfficialAssistedProcessIdentity>());
        var gameProcesses = ObservePaths(binding.GameProcessPaths, binding, knownProcesses);
        var auxiliaryProcesses = ObservePaths(binding.AuxiliaryProcessPaths, binding, knownProcesses);
        var observedProcessIds = gameProcesses
            .Concat(auxiliaryProcesses)
            .Select(process => process.ProcessId)
            .ToHashSet();
        foreach (var knownProcess in knownProcesses.ToArray())
        {
            if (observedProcessIds.Contains(knownProcess.Key))
            {
                continue;
            }

            if (!TryReadProcessIdentity(knownProcess.Key, out var currentIdentity) ||
                currentIdentity.StartTimeUtc != knownProcess.Value.StartTimeUtc ||
                !PathEquals(currentIdentity.ExecutablePath, knownProcess.Value.ExecutablePath))
            {
                knownProcesses.TryRemove(knownProcess.Key, out _);
            }
        }

        if (!rootAlive && gameProcesses.Count == 0 && auxiliaryProcesses.Count == 0)
        {
            _knownProcessesByAttempt.TryRemove(binding.AttemptId, out _);
        }

        return new OfficialAssistedProcessObservation
        {
            RootProcessAlive = rootAlive,
            RootProcess = rootAlive ? rootIdentity : null,
            RunningProcessAlive = gameProcesses.Any(process =>
                PathEquals(process.ExecutablePath, binding.RunningProcessPath)),
            GameProcesses = gameProcesses,
            AuxiliaryProcesses = auxiliaryProcesses,
        };
    }

    public OfficialAssistedProcessStopResult Stop(
        OfficialAssistedProcessBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var observation = Observe(binding);
        var stopped = 0;
        var failures = new List<string>();
        var targets = observation.AuxiliaryProcesses
            .Select(process => (Priority: 0, Process: process))
            .Concat(observation.GameProcesses.Select(process =>
                (Priority: PathEquals(process.ExecutablePath, binding.RunningProcessPath) ? 1 : 2, Process: process)))
            .Concat(observation.RootProcess is { } rootProcess
                ? [(Priority: 3, Process: rootProcess)]
                : Array.Empty<(int Priority, OfficialAssistedProcessIdentity Process)>())
            .GroupBy(target => target.Process.ProcessId)
            .Select(group => group.OrderBy(target => target.Priority).First())
            .OrderBy(target => target.Priority)
            .ThenBy(target => target.Process.ProcessId)
            .ToArray();
        foreach (var target in targets)
        {
            var identity = target.Process;
            try
            {
                using var process = Process.GetProcessById(identity.ProcessId);
                if (!TryReadProcessIdentity(process, out var current) ||
                    current.StartTimeUtc != identity.StartTimeUtc ||
                    !PathEquals(current.ExecutablePath, identity.ExecutablePath))
                {
                    continue;
                }

                if (!process.HasExited)
                {
                    var closeRequested = process.MainWindowHandle != IntPtr.Zero &&
                        process.CloseMainWindow();
                    if (closeRequested)
                    {
                        process.WaitForExit(8000);
                    }

                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: false);
                        process.WaitForExit(5000);
                    }

                    if (process.HasExited)
                    {
                        stopped++;
                    }
                    else
                    {
                        failures.Add($"PID={identity.ProcessId}:StillRunning");
                    }
                }
            }
            catch (ArgumentException)
            {
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                failures.Add(ex is Win32Exception win32Exception
                    ? $"PID={identity.ProcessId}:Win32={win32Exception.NativeErrorCode}"
                    : $"PID={identity.ProcessId}:{ex.GetType().Name}");
            }
        }

        var remaining = WaitForOwnedProcessesToExit(binding, TimeSpan.FromSeconds(5));
        if (HasOwnedProcesses(remaining))
        {
            failures.Add(
                $"RemainingRoot={remaining.RootProcessAlive};RemainingGame={remaining.GameProcesses.Count};RemainingAuxiliary={remaining.AuxiliaryProcesses.Count}");
        }

        var succeeded = !HasOwnedProcesses(remaining);
        if (succeeded)
        {
            _knownProcessesByAttempt.TryRemove(binding.AttemptId, out _);
        }

        return new OfficialAssistedProcessStopResult
        {
            Succeeded = succeeded,
            StoppedProcessCount = stopped,
            Detail = succeeded
                ? $"已结束本次 profile 的游戏进程（{stopped} 个）。"
                : $"部分游戏进程未能结束：{string.Join(", ", failures)}",
        };
    }

    private IReadOnlyList<OfficialAssistedProcessIdentity> ObservePaths(
        IReadOnlyList<string> paths,
        OfficialAssistedProcessBinding binding,
        ConcurrentDictionary<int, OfficialAssistedProcessIdentity> knownProcesses)
    {
        var processes = new List<OfficialAssistedProcessIdentity>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var processName = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(processName))
            {
                continue;
            }

            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    if (!TryReadProcessIdentity(process, out var identity) ||
                        !PathEquals(identity.ExecutablePath, path) ||
                        identity.StartTimeUtc < binding.RequestedAtUtc.AddSeconds(-5))
                    {
                        continue;
                    }

                    var knownIdentityMatches = knownProcesses.TryGetValue(process.Id, out var knownIdentity) &&
                        knownIdentity.StartTimeUtc == identity.StartTimeUtc &&
                        PathEquals(knownIdentity.ExecutablePath, identity.ExecutablePath);
                    if (!knownIdentityMatches && !IsDescendantOf(process.Id, binding))
                    {
                        continue;
                    }

                    knownProcesses[process.Id] = identity;
                    processes.Add(identity);
                }
            }
        }

        return processes
            .DistinctBy(process => process.ProcessId)
            .ToArray();
    }

    private OfficialAssistedProcessObservation WaitForOwnedProcessesToExit(
        OfficialAssistedProcessBinding binding,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        OfficialAssistedProcessObservation observation;
        do
        {
            observation = Observe(binding);
            if (!HasOwnedProcesses(observation) || DateTimeOffset.UtcNow >= deadline)
            {
                return observation;
            }

            Thread.Sleep(250);
        }
        while (true);
    }

    private static bool HasOwnedProcesses(OfficialAssistedProcessObservation observation) =>
        observation.RootProcessAlive ||
        observation.GameProcesses.Count > 0 ||
        observation.AuxiliaryProcesses.Count > 0;

    private static OfficialAssistedLaunchPreparation FailedPreparation(
        string code,
        string detail,
        LaunchPreflightResult? preflight = null) => new()
        {
            FailureCode = code,
            Detail = detail,
            Preflight = preflight,
        };

    private static (string Code, string Detail)? CheckCleanBaseline(
        IReadOnlyList<string> expectedProcessPaths)
    {
        foreach (var expectedPath in expectedProcessPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var processName = Path.GetFileNameWithoutExtension(expectedPath);
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            continue;
                        }

                        var actualPath = process.MainModule?.FileName;
                        if (string.IsNullOrWhiteSpace(actualPath))
                        {
                            return (
                                "Preflight.ObserverUnavailable",
                                $"无法确认活动 {processName} 进程的精确路径；未创建新的 provider。");
                        }

                        if (PathEquals(actualPath, expectedPath))
                        {
                            return (
                                "Preflight.BaselineDirty",
                                $"检测到目标组件已在运行：{Path.GetFileName(expectedPath)}（PID={process.Id}）。");
                        }
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                    {
                        return (
                            "Preflight.ObserverUnavailable",
                            $"无法读取活动 {processName} 进程的精确路径；未创建新的 provider。");
                    }
                }
            }
        }

        return null;
    }

    private static bool TryNormalizeDirectory(string path, out string normalized)
    {
        normalized = string.Empty;
        try
        {
            normalized = Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            return Directory.Exists(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryNormalizeFile(string path, out string normalized)
    {
        normalized = string.Empty;
        try
        {
            normalized = Path.GetFullPath(path);
            return File.Exists(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool PathEquals(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryReadProcessIdentity(
        int processId,
        out OfficialAssistedProcessIdentity identity)
    {
        identity = new OfficialAssistedProcessIdentity();
        try
        {
            using var process = Process.GetProcessById(processId);
            return TryReadProcessIdentity(process, out identity);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadProcessIdentity(
        Process process,
        out OfficialAssistedProcessIdentity identity)
    {
        identity = new OfficialAssistedProcessIdentity();
        try
        {
            if (!TryQueryProcessPath(process.Id, out var path) ||
                !TryGetProcessCreationTime(process.Id, out var startTimeUtc))
            {
                return false;
            }

            identity = new OfficialAssistedProcessIdentity
            {
                ProcessId = process.Id,
                ExecutablePath = path,
                StartTimeUtc = startTimeUtc,
            };
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsDescendantOf(
        int processId,
        OfficialAssistedProcessBinding binding)
    {
        var visited = new HashSet<int>();
        var current = processId;
        while (current > 0 && visited.Add(current))
        {
            if (current == binding.RootProcessId)
            {
                return RootProcessMatchesOrExited(binding);
            }

            try
            {
                using var process = Process.GetProcessById(current);
                if (!TryReadParentProcessId(process, out var parentProcessId))
                {
                    return false;
                }

                current = parentProcessId;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool RootProcessMatchesOrExited(OfficialAssistedProcessBinding binding)
    {
        try
        {
            using var process = Process.GetProcessById(binding.RootProcessId);
            if (process.HasExited)
            {
                return true;
            }

            return TryReadProcessIdentity(process, out var identity) &&
                identity.StartTimeUtc == binding.RootProcessStartTimeUtc &&
                PathEquals(identity.ExecutablePath, binding.RootExecutablePath);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryReadParentProcessId(Process process, out int parentProcessId)
    {
        parentProcessId = 0;
        try
        {
            var processHandle = OpenProcess(
                ProcessQueryLimitedInformation,
                bInheritHandle: false,
                process.Id);
            if (processHandle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var information = new ProcessBasicInformation();
                var status = NtQueryInformationProcess(
                    processHandle,
                    0,
                    ref information,
                    Marshal.SizeOf<ProcessBasicInformation>(),
                    out _);
                if (status != 0 || information.InheritedFromUniqueProcessId == IntPtr.Zero)
                {
                    return false;
                }

                parentProcessId = information.InheritedFromUniqueProcessId.ToInt32();
                return parentProcessId > 0;
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref ProcessBasicInformation processInformation,
        int processInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(
        uint processAccess,
        bool bInheritHandle,
        int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        int flags,
        StringBuilder exeName,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(
        IntPtr hProcess,
        out System.Runtime.InteropServices.ComTypes.FILETIME creationTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME exitTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint ProcessQueryLimitedInformation = 0x1000;

    private static bool TryQueryProcessPath(int processId, out string path)
    {
        path = string.Empty;
        var processHandle = OpenProcess(
            ProcessQueryLimitedInformation,
            bInheritHandle: false,
            processId);
        if (processHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var length = buffer.Capacity;
            if (!QueryFullProcessImageName(processHandle, 0, buffer, ref length))
            {
                return false;
            }

            path = buffer.ToString();
            return !string.IsNullOrWhiteSpace(path);
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    private static bool TryGetProcessCreationTime(
        int processId,
        out DateTimeOffset startTimeUtc)
    {
        startTimeUtc = default;
        var processHandle = OpenProcess(
            ProcessQueryLimitedInformation,
            bInheritHandle: false,
            processId);
        if (processHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (!GetProcessTimes(
                    processHandle,
                    out var creationTime,
                    out _,
                    out _,
                    out _))
            {
                return false;
            }

            var fileTime = ((long)creationTime.dwHighDateTime << 32) |
                (uint)creationTime.dwLowDateTime;
            startTimeUtc = new DateTimeOffset(
                DateTime.FromFileTimeUtc(fileTime));
            return true;
        }
        finally
        {
            CloseHandle(processHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}
