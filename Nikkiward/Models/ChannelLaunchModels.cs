namespace Nikkiward.Models;

public enum ChannelLaunchEntryKind
{
    Unknown,
    BilibiliXStarterDirect,
    SteamXStarterDirect,
}

public enum ChannelLaunchFailureCode
{
    None,
    InvalidCandidate,
    UnsupportedChannel,
    LauncherMissing,
    DirectLaunchUnavailable,
    PlanChanged,
    StartFailed,
    ProcessIdentityUnavailable,
}

public sealed record ChannelLaunchPlan
{
    public bool CanLaunch { get; init; }

    public ChannelLaunchFailureCode FailureCode { get; init; }

    public string? FailureDetail { get; init; }

    public ChannelLaunchEntryKind EntryKind { get; init; }

    public DistributionChannel DistributionChannel { get; init; }

    public string ProfileId { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }

    public IReadOnlyList<string> ArgumentList { get; init; } = Array.Empty<string>();

    public bool RequiresElevation { get; init; }

    public string PlanSha256 { get; init; } = string.Empty;
}

public sealed record ChannelLaunchReceipt
{
    public Guid AttemptId { get; init; }

    public bool Succeeded { get; init; }

    public ChannelLaunchFailureCode FailureCode { get; init; }

    public string? FailureDetail { get; init; }

    public ChannelLaunchEntryKind EntryKind { get; init; }

    public DistributionChannel DistributionChannel { get; init; }

    public string ProfileId { get; init; } = string.Empty;

    public string PlanSha256 { get; init; } = string.Empty;

    public int? SubmittedProcessId { get; init; }

    public DateTimeOffset? SubmittedProcessStartTimeUtc { get; init; }

    public DateTimeOffset SubmittedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
