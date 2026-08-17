namespace Nikkiward.Models;

public enum LaunchPreflightFailureCode
{
    None,
    InvalidContract,
    PlatformMismatch,
    ChannelMismatch,
    PathOutsideExpectedRoot,
    ReparsePointRejected,
    RequiredComponentMissing,
    SignatureInvalid,
    SignerMismatch,
    ArtifactHashMismatch,
    VersionMismatch,
    MarkerMissing,
    MarkerMismatch,
    BinaryIdentityDrift,
    ExecutionGateClosed,
}

public sealed record LaunchPlan
{
    public string ProviderId { get; init; } = string.Empty;

    public string ProviderExecutablePath { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    public IReadOnlyList<string> ArgumentList { get; init; } = Array.Empty<string>();
}

public sealed record PreflightComponentResult
{
    public string ComponentId { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public bool Passed { get; init; }

    public string? ActualSha256 { get; init; }

    public string? ActualFileVersion { get; init; }

    public string? ActualProductVersion { get; init; }

    public AuthenticodeSignatureStatus SignatureStatus { get; init; } =
        AuthenticodeSignatureStatus.NotChecked;

    public string? ActualSignerThumbprint { get; init; }

    public string? FailureDetail { get; init; }
}

public sealed record LaunchPreflightResult
{
    public bool StaticIdentityPassed { get; init; }

    public bool ExecutionAllowed { get; init; }

    public LaunchPreflightFailureCode FailureCode { get; init; }

    public string? FailureDetail { get; init; }

    public LaunchProviderContract? Contract { get; init; }

    public LaunchPlan? Plan { get; init; }

    public IReadOnlyList<PreflightComponentResult> Components { get; init; } = Array.Empty<PreflightComponentResult>();

    public DateTimeOffset VerifiedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
