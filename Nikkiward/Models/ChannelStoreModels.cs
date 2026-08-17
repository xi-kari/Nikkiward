namespace Nikkiward.Models;

public enum ChannelStoreFailureCode
{
    None,
    InvalidRequest,
    MissingChannel,
    DuplicateChannel,
    InvalidSourceRoot,
    InvalidStoreRoot,
    RootOverlap,
    ReparsePointRejected,
    SourceChanged,
    ImportConflict,
    PlanChanged,
    ImportFailed,
    MaterializationFailed,
    ReceiptWriteFailed,
    RollbackFailed,
    Cancelled,
    IoFailure,
}

public enum ChannelStoreImportAction
{
    KeepVerifiedFile,
    CreateHardLink,
    CopyFile,
}

public enum ChannelStoreProgressStage
{
    Enumerating,
    Hashing,
    Importing,
    Materializing,
    PersistingReceipts,
    Completed,
}

public sealed record ChannelStoreBuildRequest
{
    public required IReadOnlyList<InstallationProfileCandidate> Candidates { get; init; }

    public required string StoreRootPath { get; init; }
}

public sealed record ChannelStoreProgress
{
    public ChannelStoreProgressStage Stage { get; init; }

    public int FilesCompleted { get; init; }

    public int TotalFiles { get; init; }

    public long BytesCompleted { get; init; }

    public long TotalBytes { get; init; }

    public string? CurrentPath { get; init; }
}

public sealed record ChannelStoreImportItem
{
    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }

    public required ChannelStoreImportAction Action { get; init; }

    public required long Length { get; init; }

    public required string Sha256 { get; init; }

    public bool SharedContent { get; init; }
}

public sealed record ChannelStoreVariantPlan
{
    public required VariantDefinition Definition { get; init; }

    public required string SourceGameRootPath { get; init; }

    public required string TargetGameRootPath { get; init; }

    public required string SourceLauncherRootPath { get; init; }

    public required string TargetLauncherRootPath { get; init; }

    public required string TargetXStarterPath { get; init; }

    public required string OverlayRootPath { get; init; }

    public required VariantManifest Manifest { get; init; }

    public bool UsesExistingTarget { get; init; }
}

public sealed record ChannelStoreBuildPlan
{
    public bool CanExecute { get; init; }

    public ChannelStoreFailureCode FailureCode { get; init; }

    public string? FailureDetail { get; init; }

    public string StoreRootPath { get; init; } = string.Empty;

    public string SharedContentRootPath { get; init; } = string.Empty;

    public IReadOnlyList<ChannelStoreImportItem> Imports { get; init; } =
        Array.Empty<ChannelStoreImportItem>();

    public IReadOnlyList<ChannelStoreVariantPlan> Variants { get; init; } =
        Array.Empty<ChannelStoreVariantPlan>();

    public long HardLinkBytes { get; init; }

    public long CopyBytes { get; init; }

    public string PlanSha256 { get; init; } = string.Empty;

    public DateTimeOffset PlannedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ChannelStoreVariantReceipt
{
    public required GameVariantId VariantId { get; init; }

    public required string TargetGameRootPath { get; init; }

    public required string TargetLauncherRootPath { get; init; }

    public required string TargetXStarterPath { get; init; }

    public required string ManifestContentSha256 { get; init; }

    public required VariantMaterializationReceipt Materialization { get; init; }
}

public sealed record ChannelStoreBuildReceipt
{
    public required string ReceiptId { get; init; }

    public bool Succeeded { get; init; }

    public ChannelStoreFailureCode FailureCode { get; init; }

    public string? FailureDetail { get; init; }

    public string StoreRootPath { get; init; } = string.Empty;

    public string PlanSha256 { get; init; } = string.Empty;

    public IReadOnlyList<string> ImportedFiles { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ChannelStoreVariantReceipt> Variants { get; init; } =
        Array.Empty<ChannelStoreVariantReceipt>();

    public IReadOnlyList<string> SourceRootsEligibleForManualCleanup { get; init; } =
        Array.Empty<string>();

    public bool AutomaticRollbackAttempted { get; init; }

    public bool AutomaticRollbackSucceeded { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }
}
