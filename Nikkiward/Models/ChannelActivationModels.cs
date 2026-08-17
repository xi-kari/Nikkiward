namespace Nikkiward.Models;

public enum ChannelActivationFailureCode
{
    None,
    InvalidRequest,
    UnsupportedChannel,
    CandidateNotSelectable,
    TargetRootMissing,
    TargetLayoutMismatch,
    MarkerMissing,
    MarkerMismatch,
    LauncherConfigMissing,
    LauncherConfigInvalid,
    SteamRootMismatch,
    PlanChanged,
    ConfigChanged,
    WriteFailed,
    VerificationFailed,
    RollbackFailed,
}

public sealed record ChannelActivationRequest
{
    public required InstallationProfileCandidate Candidate { get; init; }

    public required string TargetGameRootPath { get; init; }
}

public sealed record ChannelActivationPlan
{
    public bool CanActivate { get; init; }

    public ChannelActivationFailureCode FailureCode { get; init; }

    public string? FailureDetail { get; init; }

    public DistributionChannel DistributionChannel { get; init; }

    public string ProfileId { get; init; } = string.Empty;

    public string TargetGameRootPath { get; init; } = string.Empty;

    public string? LauncherConfigPath { get; init; }

    public string? PreviousGameRootPath { get; init; }

    public bool CreatesLauncherConfig { get; init; }

    public string ExpectedMarkerName { get; init; } = string.Empty;

    public string PlanSha256 { get; init; } = string.Empty;

    public DateTimeOffset PlannedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ChannelActivationReceipt
{
    public bool Succeeded { get; init; }

    public ChannelActivationFailureCode FailureCode { get; init; }

    public string? FailureDetail { get; init; }

    public DistributionChannel DistributionChannel { get; init; }

    public string ProfileId { get; init; } = string.Empty;

    public string TargetGameRootPath { get; init; } = string.Empty;

    public string? LauncherConfigPath { get; init; }

    public string? PreviousGameRootPath { get; init; }

    public string? ConfigSha256Before { get; init; }

    public string? ConfigSha256After { get; init; }

    public string PlanSha256 { get; init; } = string.Empty;

    public bool ConfigChanged { get; init; }

    public bool LauncherConfigCreated { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
