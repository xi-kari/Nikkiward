namespace Nikkiward.Models;

public enum VariantFileClassification
{
    Unknown,
    SharedImmutable,
    VariantExclusive,
    VariantMutable,
    OptionalResource,
    AbsentPath,
}

public enum VariantSourceKind
{
    None,
    SharedContent,
    VariantOverlay,
}

public enum VariantPlanAction
{
    Unknown,
    CreateHardLink,
    CopyFile,
    KeepVerifiedFile,
    SkipMissingOptionalResource,
    ConfirmAbsent,
}

public enum VariantPlanFailureCode
{
    None,
    InvalidRequest,
    InvalidVariant,
    InvalidManifest,
    ManifestDigestMismatch,
    SourceRootMissing,
    TargetRootMissing,
    RootOverlap,
    ReparsePointRejected,
    PathOutsideRoot,
    SourceFileMissing,
    SourceIdentityMismatch,
    UnsafeSourceAttributes,
    TargetConflict,
    CrossVolumeHardLink,
    NonNtfsVolume,
    UnsupportedPlatform,
    IoFailure,
}

public enum VariantMaterializationFailureCode
{
    None,
    PlanRejected,
    PlanChanged,
    SourceChanged,
    TargetChanged,
    ReparsePointRejected,
    CrossVolumeHardLink,
    NonNtfsVolume,
    HardLinkCreationFailed,
    CopyFailed,
    AtomicMoveFailed,
    VerificationFailed,
    RollbackFailed,
    Cancelled,
    IoFailure,
}

public enum VariantRollbackAction
{
    DeleteCreatedFile,
    DeleteCreatedDirectoryIfEmpty,
}

public sealed record VariantManifestEntry
{
    public required string TargetRelativePath { get; init; }

    public string? SourceRelativePath { get; init; }

    public required VariantFileClassification Classification { get; init; }

    public required VariantSourceKind SourceKind { get; init; }

    public long? Length { get; init; }

    public string? Sha256 { get; init; }
}

public sealed record VariantManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string ManifestId { get; init; }

    public required string GameBuildId { get; init; }

    public required GameVariantId VariantId { get; init; }

    public required IReadOnlyList<VariantManifestEntry> Entries { get; init; }

    public required string ContentSha256 { get; init; }
}

public sealed record VariantManifestValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public string ComputedContentSha256 { get; init; } = string.Empty;
}

public sealed record VariantMaterializationRequest
{
    public required VariantDefinition Definition { get; init; }

    public required VariantManifest Manifest { get; init; }

    public required string SharedContentRootPath { get; init; }

    public required string VariantOverlayRootPath { get; init; }

    public required string TargetRootPath { get; init; }
}

public sealed record VariantPlanItem
{
    public required string TargetRelativePath { get; init; }

    public string? SourcePath { get; init; }

    public required string TargetPath { get; init; }

    public required VariantFileClassification Classification { get; init; }

    public required VariantSourceKind SourceKind { get; init; }

    public required VariantPlanAction Action { get; init; }

    public long? ExpectedLength { get; init; }

    public string? ExpectedSha256 { get; init; }
}

public sealed record VariantMaterializationPlan
{
    public bool CanExecute { get; init; }

    public VariantPlanFailureCode FailureCode { get; init; }

    public string? FailureDetail { get; init; }

    public GameVariantId VariantId { get; init; }

    public string ManifestContentSha256 { get; init; } = string.Empty;

    public string SharedContentRootPath { get; init; } = string.Empty;

    public string VariantOverlayRootPath { get; init; } = string.Empty;

    public string TargetRootPath { get; init; } = string.Empty;

    public IReadOnlyList<VariantPlanItem> Items { get; init; } = Array.Empty<VariantPlanItem>();

    public long HardLinkBytes { get; init; }

    public long CopyBytes { get; init; }

    public string PlanSha256 { get; init; } = string.Empty;

    public DateTimeOffset PlannedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record VariantMaterializationFileReceipt
{
    public required string TargetRelativePath { get; init; }

    public required string TargetPath { get; init; }

    public required VariantPlanAction Action { get; init; }

    public bool Created { get; init; }

    public long? Length { get; init; }

    public string? Sha256 { get; init; }

    public bool HardLinkIdentityVerified { get; init; }
}

public sealed record VariantRollbackEntry
{
    public required VariantRollbackAction Action { get; init; }

    public required string Path { get; init; }

    public VariantPlanAction CreatedByAction { get; init; }

    public long? ExpectedLength { get; init; }

    public string? ExpectedSha256 { get; init; }
}

public sealed record VariantRollbackPlan
{
    public required string ReceiptId { get; init; }

    public required string TargetRootPath { get; init; }

    public required string ManifestContentSha256 { get; init; }

    public IReadOnlyList<VariantRollbackEntry> Entries { get; init; } = Array.Empty<VariantRollbackEntry>();
}

public sealed record VariantMaterializationReceipt
{
    public required string ReceiptId { get; init; }

    public bool Succeeded { get; init; }

    public VariantMaterializationFailureCode FailureCode { get; init; }

    public string? FailureDetail { get; init; }

    public required GameVariantId VariantId { get; init; }

    public required string ManifestContentSha256 { get; init; }

    public required string PlanSha256 { get; init; }

    public required string TargetRootPath { get; init; }

    public IReadOnlyList<VariantMaterializationFileReceipt> Files { get; init; } =
        Array.Empty<VariantMaterializationFileReceipt>();

    public required VariantRollbackPlan RollbackPlan { get; init; }

    public bool AutomaticRollbackAttempted { get; init; }

    public bool AutomaticRollbackSucceeded { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }
}

public sealed record VariantRollbackReceipt
{
    public bool Succeeded { get; init; }

    public VariantMaterializationFailureCode FailureCode { get; init; }

    public string? FailureDetail { get; init; }

    public required string ReceiptId { get; init; }

    public int DeletedFileCount { get; init; }

    public int DeletedDirectoryCount { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
