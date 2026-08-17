using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nikkiward.Models;

namespace Nikkiward.Services;

public interface IChannelEntryLauncher
{
    ChannelLaunchPlan CreatePlan(InstallationProfileCandidate candidate);

    Task<ChannelLaunchReceipt> LaunchAsync(
        InstallationProfileCandidate candidate,
        string expectedPlanSha256,
        CancellationToken cancellationToken = default);
}

internal interface IChannelProcessStarter
{
    ChannelProcessStartResult? Start(ProcessStartInfo startInfo);
}

internal sealed record ChannelProcessStartResult(
    int ProcessId,
    DateTimeOffset? StartTimeUtc);

internal sealed class WindowsChannelProcessStarter : IChannelProcessStarter
{
    public ChannelProcessStartResult? Start(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        DateTimeOffset? startTimeUtc = null;
        try
        {
            startTimeUtc = process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is
            System.ComponentModel.Win32Exception or
            InvalidOperationException or
            NotSupportedException)
        {
        }

        return new ChannelProcessStartResult(process.Id, startTimeUtc);
    }
}

public sealed class WindowsChannelEntryLauncher : IChannelEntryLauncher
{
    private const string SteamAppId = "3164330";
    private static readonly Regex VersionDirectoryPattern = new(
        "^\\d+\\.\\d+\\.\\d+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly IChannelProcessStarter _processStarter;

    public WindowsChannelEntryLauncher()
        : this(new WindowsChannelProcessStarter())
    {
    }

    internal WindowsChannelEntryLauncher(IChannelProcessStarter processStarter)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
    }

    public ChannelLaunchPlan CreatePlan(InstallationProfileCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Profile is null || candidate.State is not (
                InstallationCandidateState.Candidate or
                InstallationCandidateState.ReadyForStaticVerification))
        {
            return Rejected(candidate, ChannelLaunchFailureCode.InvalidCandidate, "Candidate is not selectable.");
        }

        ChannelLaunchPlan plan;
        switch (candidate.Identity.DistributionChannel)
        {
            case DistributionChannel.Bilibili:
                if (candidate.Identity.RegionFamily is not RegionFamily.MainlandChina ||
                    candidate.Identity.AccountAuthority is not AccountAuthority.Bilibili)
                {
                    return Rejected(
                        candidate,
                        ChannelLaunchFailureCode.InvalidCandidate,
                        "Bilibili candidate identity is inconsistent.");
                }

                var xstarterPath = ResolveVersionedXStarter(candidate);
                if (xstarterPath is null)
                {
                    return Rejected(
                        candidate,
                        ChannelLaunchFailureCode.LauncherMissing,
                        "The selected Bilibili xstarter.exe is missing or outside its version directory.");
                }

                plan = new ChannelLaunchPlan
                {
                    CanLaunch = true,
                    EntryKind = ChannelLaunchEntryKind.BilibiliXStarterDirect,
                    DistributionChannel = DistributionChannel.Bilibili,
                    ProfileId = candidate.ProfileId,
                    FileName = xstarterPath,
                    WorkingDirectory = NormalizeDirectory(candidate.LauncherRootPath!),
                    ArgumentList = ["-skiplauncher"],
                    RequiresElevation = true,
                };
                break;

            case DistributionChannel.Steam:
                if (candidate.Identity.RegionFamily is not RegionFamily.Overseas ||
                    candidate.Identity.AccountAuthority is not AccountAuthority.Steam ||
                    !string.Equals(
                        candidate.Identity.SteamAppId,
                        SteamAppId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Rejected(
                        candidate,
                        ChannelLaunchFailureCode.InvalidCandidate,
                        "Steam candidate does not match the Windows App 3164330 identity.");
                }

                var steamXStarterPath = ResolveVersionedXStarter(candidate);
                if (steamXStarterPath is null)
                {
                    return Rejected(
                        candidate,
                        ChannelLaunchFailureCode.LauncherMissing,
                        "The selected Steam xstarter.exe is missing or outside its version directory.");
                }

                plan = new ChannelLaunchPlan
                {
                    CanLaunch = true,
                    EntryKind = ChannelLaunchEntryKind.SteamXStarterDirect,
                    DistributionChannel = DistributionChannel.Steam,
                    ProfileId = candidate.ProfileId,
                    FileName = steamXStarterPath,
                    WorkingDirectory = NormalizeDirectory(candidate.LauncherRootPath!),
                    ArgumentList = ["-skiplauncher"],
                    RequiresElevation = true,
                };
                break;

            default:
                return Rejected(
                    candidate,
                    ChannelLaunchFailureCode.UnsupportedChannel,
                    "This launcher handles only verified direct channel entries.");
        }

        return plan with { PlanSha256 = ComputePlanSha256(plan) };
    }

    public Task<ChannelLaunchReceipt> LaunchAsync(
        InstallationProfileCandidate candidate,
        string expectedPlanSha256,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = CreatePlan(candidate);
        if (!plan.CanLaunch)
        {
            return Task.FromResult(Failure(plan, plan.FailureCode, plan.FailureDetail));
        }

        if (!VariantHash.IsSha256(expectedPlanSha256) ||
            !string.Equals(plan.PlanSha256, expectedPlanSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(Failure(
                plan,
                ChannelLaunchFailureCode.PlanChanged,
                "The fresh launch plan does not match the approved plan digest."));
        }

        if (plan.EntryKind is not (
                ChannelLaunchEntryKind.BilibiliXStarterDirect or
                ChannelLaunchEntryKind.SteamXStarterDirect))
        {
            return Task.FromResult(Failure(
                plan,
                ChannelLaunchFailureCode.DirectLaunchUnavailable,
                "The selected entry is not an approved direct executable."));
        }

        try
        {
            var attemptId = Guid.NewGuid();
            var submittedAtUtc = DateTimeOffset.UtcNow;
            var startInfo = new ProcessStartInfo
            {
                FileName = plan.FileName,
                WorkingDirectory = plan.WorkingDirectory ?? string.Empty,
                UseShellExecute = plan.RequiresElevation,
                Verb = plan.RequiresElevation ? "runas" : string.Empty,
                WindowStyle = ProcessWindowStyle.Normal,
            };
            foreach (var argument in plan.ArgumentList)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var processStart = _processStarter.Start(startInfo);
            return Task.FromResult(new ChannelLaunchReceipt
            {
                AttemptId = attemptId,
                Succeeded = processStart?.StartTimeUtc is not null,
                FailureCode = processStart switch
                {
                    null => ChannelLaunchFailureCode.StartFailed,
                    { StartTimeUtc: null } => ChannelLaunchFailureCode.ProcessIdentityUnavailable,
                    _ => ChannelLaunchFailureCode.None,
                },
                FailureDetail = processStart switch
                {
                    null => "The direct entry did not return a process handle.",
                    { StartTimeUtc: null } => "The direct entry started without a verifiable process identity.",
                    _ => null,
                },
                EntryKind = plan.EntryKind,
                DistributionChannel = plan.DistributionChannel,
                ProfileId = plan.ProfileId,
                PlanSha256 = plan.PlanSha256,
                SubmittedProcessId = processStart?.ProcessId,
                SubmittedProcessStartTimeUtc = processStart?.StartTimeUtc,
                SubmittedAtUtc = submittedAtUtc,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Task.FromResult(Failure(
                plan,
                ChannelLaunchFailureCode.StartFailed,
                $"Direct entry submission failed: {ex.GetType().Name}."));
        }
    }

    private static string? ResolveVersionedXStarter(InstallationProfileCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.LauncherRootPath) ||
            string.IsNullOrWhiteSpace(candidate.Profile?.XStarterPath) ||
            !Directory.Exists(candidate.LauncherRootPath))
        {
            return null;
        }

        try
        {
            var launcherRoot = NormalizeDirectory(candidate.LauncherRootPath);
            var selectedPath = Path.GetFullPath(candidate.Profile.XStarterPath);
            if (!string.Equals(
                    Path.GetFileName(selectedPath),
                    "xstarter.exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Directory.GetDirectories(launcherRoot)
                .Where(path => VersionDirectoryPattern.IsMatch(Path.GetFileName(path)))
                .Select(path => Path.Combine(path, "xstarter.exe"))
                .Where(File.Exists)
                .Select(Path.GetFullPath)
                .FirstOrDefault(path => string.Equals(
                    path,
                    selectedPath,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is
            ArgumentException or
            IOException or
            NotSupportedException or
            PathTooLongException or
            UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string ComputePlanSha256(ChannelLaunchPlan plan)
    {
        var canonical = new StringBuilder();
        AppendCanonical(canonical, ((int)plan.EntryKind).ToString(CultureInfo.InvariantCulture));
        AppendCanonical(canonical, ((int)plan.DistributionChannel).ToString(CultureInfo.InvariantCulture));
        AppendCanonical(canonical, plan.ProfileId);
        AppendCanonical(canonical, plan.FileName);
        AppendCanonical(canonical, plan.WorkingDirectory ?? string.Empty);
        AppendCanonical(canonical, plan.RequiresElevation ? "1" : "0");
        AppendCanonical(canonical, plan.ArgumentList.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var argument in plan.ArgumentList)
        {
            AppendCanonical(canonical, argument);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    private static void AppendCanonical(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('\n');

    private static ChannelLaunchPlan Rejected(
        InstallationProfileCandidate candidate,
        ChannelLaunchFailureCode code,
        string detail,
        ChannelLaunchEntryKind entryKind = ChannelLaunchEntryKind.Unknown) => new()
        {
            FailureCode = code,
            FailureDetail = detail,
            EntryKind = entryKind,
            DistributionChannel = candidate.Identity.DistributionChannel,
            ProfileId = candidate.ProfileId,
        };

    private static ChannelLaunchReceipt Failure(
        ChannelLaunchPlan plan,
        ChannelLaunchFailureCode code,
        string? detail) => new()
        {
            FailureCode = code,
            FailureDetail = detail,
            EntryKind = plan.EntryKind,
            DistributionChannel = plan.DistributionChannel,
            ProfileId = plan.ProfileId,
            PlanSha256 = plan.PlanSha256,
        };
}
