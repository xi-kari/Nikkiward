namespace Nikkiward.Models;

public enum AuthenticodeSignatureStatus
{
    NotChecked,
    Valid,
    NotSigned,
    Invalid,
    Untrusted,
    Error,
}

public sealed record ComponentVerification
{
    public string ComponentId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public bool Exists { get; init; }

    public bool InspectionSucceeded { get; init; }

    public long? FileSizeBytes { get; init; }

    public DateTimeOffset? LastWriteTimeUtc { get; init; }

    public string? FileVersion { get; init; }

    public string? ProductVersion { get; init; }

    public string? Sha256 { get; init; }

    public AuthenticodeSignatureStatus SignatureStatus { get; init; } =
        AuthenticodeSignatureStatus.NotChecked;

    public string? SignatureStatusCode { get; init; }

    public string? Error { get; init; }

    public DateTimeOffset InspectedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
