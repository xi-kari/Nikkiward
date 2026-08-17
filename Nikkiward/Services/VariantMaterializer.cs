using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nikkiward.Models;

namespace Nikkiward.Services;

public interface IVariantMaterializer
{
    Task<VariantMaterializationReceipt> MaterializeAsync(
        VariantMaterializationRequest request,
        string expectedPlanSha256,
        CancellationToken cancellationToken = default);

    Task<VariantRollbackReceipt> RollbackAsync(
        VariantRollbackPlan rollbackPlan,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsVariantMaterializer : IVariantMaterializer
{
    private readonly IVariantMaterializationPlanner planner;

    public WindowsVariantMaterializer(IVariantMaterializationPlanner? planner = null)
    {
        this.planner = planner ?? new VariantMaterializationPlanner();
    }

    public async Task<VariantMaterializationReceipt> MaterializeAsync(
        VariantMaterializationRequest request,
        string expectedPlanSha256,
        CancellationToken cancellationToken = default)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var receiptId = Guid.NewGuid().ToString("N");
        var fileReceipts = new List<VariantMaterializationFileReceipt>();
        var createdFiles = new List<VariantMaterializationFileReceipt>();
        var createdDirectories = new List<string>();
        VariantMaterializationPlan? plan = null;

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new VariantMaterializationException(
                    VariantMaterializationFailureCode.PlanRejected,
                    "NTFS materialization requires Windows.");
            }

            plan = await planner.CreatePlanAsync(request, cancellationToken).ConfigureAwait(false);
            if (!plan.CanExecute)
            {
                throw new VariantMaterializationException(
                    VariantMaterializationFailureCode.PlanRejected,
                    $"Plan rejected: {plan.FailureCode}. {plan.FailureDetail}");
            }

            if (!VariantHash.IsSha256(expectedPlanSha256) ||
                !string.Equals(plan.PlanSha256, expectedPlanSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new VariantMaterializationException(
                    VariantMaterializationFailureCode.PlanChanged,
                    "The freshly computed plan does not match the approved dry-run digest.");
            }

            EnsureSafeTargetRoot(plan.TargetRootPath);

            foreach (var item in plan.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureTargetPathIsSafe(plan.TargetRootPath, item.TargetPath);

                switch (item.Action)
                {
                    case VariantPlanAction.ConfirmAbsent:
                        if (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath))
                        {
                            throw new VariantMaterializationException(
                                VariantMaterializationFailureCode.TargetChanged,
                                $"An absent target appeared after planning: {item.TargetRelativePath}.");
                        }

                        fileReceipts.Add(ToUnchangedReceipt(item));
                        break;

                    case VariantPlanAction.SkipMissingOptionalResource:
                        if (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath))
                        {
                            throw new VariantMaterializationException(
                                VariantMaterializationFailureCode.TargetChanged,
                                $"A skipped optional target appeared after planning: {item.TargetRelativePath}.");
                        }

                        fileReceipts.Add(ToUnchangedReceipt(item));
                        break;

                    case VariantPlanAction.KeepVerifiedFile:
                        var existingVerification = await VerifyPlanItemAsync(
                                item.TargetPath,
                                item,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (!existingVerification.Passed)
                        {
                            throw new VariantMaterializationException(
                                VariantMaterializationFailureCode.TargetChanged,
                                $"Existing target changed after planning: {item.TargetRelativePath}.");
                        }

                        fileReceipts.Add(new VariantMaterializationFileReceipt
                        {
                            TargetRelativePath = item.TargetRelativePath,
                            TargetPath = item.TargetPath,
                            Action = item.Action,
                            Created = false,
                            Length = existingVerification.ActualLength,
                            Sha256 = existingVerification.ActualSha256,
                            HardLinkIdentityVerified = item.SourceKind is VariantSourceKind.SharedContent &&
                                item.SourcePath is not null &&
                                WindowsHardLinkIdentity.AreSameFile(item.SourcePath, item.TargetPath),
                        });
                        break;

                    case VariantPlanAction.CreateHardLink:
                    case VariantPlanAction.CopyFile:
                        var createdReceipt = await MaterializeFileAsync(
                                plan.SharedContentRootPath,
                                plan.VariantOverlayRootPath,
                                plan.TargetRootPath,
                                item,
                                createdDirectories,
                                cancellationToken)
                            .ConfigureAwait(false);
                        fileReceipts.Add(createdReceipt);
                        createdFiles.Add(createdReceipt);
                        break;

                    default:
                        throw new VariantMaterializationException(
                            VariantMaterializationFailureCode.PlanRejected,
                            $"Unsupported plan action: {item.Action}.");
                }
            }

            var rollbackPlan = BuildRollbackPlan(
                receiptId,
                plan.TargetRootPath,
                plan.ManifestContentSha256,
                createdFiles,
                createdDirectories);
            return new VariantMaterializationReceipt
            {
                ReceiptId = receiptId,
                Succeeded = true,
                FailureCode = VariantMaterializationFailureCode.None,
                VariantId = plan.VariantId,
                ManifestContentSha256 = plan.ManifestContentSha256,
                PlanSha256 = plan.PlanSha256,
                TargetRootPath = plan.TargetRootPath,
                Files = Array.AsReadOnly(fileReceipts.ToArray()),
                RollbackPlan = rollbackPlan,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex) when (
            ex is VariantMaterializationException or
            IOException or
            UnauthorizedAccessException or
            OperationCanceledException)
        {
            var failureCode = ex switch
            {
                VariantMaterializationException materializationException => materializationException.Code,
                OperationCanceledException => VariantMaterializationFailureCode.Cancelled,
                _ => VariantMaterializationFailureCode.IoFailure,
            };
            var targetRoot = plan?.TargetRootPath ?? SafeNormalize(request?.TargetRootPath);
            var manifestSha256 = plan?.ManifestContentSha256 ?? request?.Manifest?.ContentSha256 ?? string.Empty;
            var rollbackPlan = BuildRollbackPlan(
                receiptId,
                targetRoot,
                manifestSha256,
                createdFiles,
                createdDirectories);
            var rollbackAttempted = rollbackPlan.Entries.Count > 0;
            var rollbackSucceeded = true;
            string? rollbackFailure = null;
            if (rollbackAttempted)
            {
                var rollbackReceipt = await RollbackAsync(rollbackPlan, CancellationToken.None)
                    .ConfigureAwait(false);
                rollbackSucceeded = rollbackReceipt.Succeeded;
                rollbackFailure = rollbackReceipt.FailureDetail;
            }

            if (!rollbackSucceeded)
            {
                failureCode = VariantMaterializationFailureCode.RollbackFailed;
            }

            return new VariantMaterializationReceipt
            {
                ReceiptId = receiptId,
                Succeeded = false,
                FailureCode = failureCode,
                FailureDetail = rollbackFailure is null
                    ? ex.Message
                    : $"{ex.Message} Automatic rollback failed: {rollbackFailure}",
                VariantId = plan?.VariantId ?? request?.Definition?.VariantId ?? GameVariantId.Unknown,
                ManifestContentSha256 = manifestSha256,
                PlanSha256 = plan?.PlanSha256 ?? string.Empty,
                TargetRootPath = targetRoot,
                Files = Array.AsReadOnly(fileReceipts.ToArray()),
                RollbackPlan = rollbackPlan,
                AutomaticRollbackAttempted = rollbackAttempted,
                AutomaticRollbackSucceeded = rollbackAttempted && rollbackSucceeded,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
        }
    }

    public async Task<VariantRollbackReceipt> RollbackAsync(
        VariantRollbackPlan rollbackPlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rollbackPlan);
        var deletedFiles = 0;
        var deletedDirectories = 0;

        try
        {
            if (!Directory.Exists(rollbackPlan.TargetRootPath))
            {
                throw new VariantMaterializationException(
                    VariantMaterializationFailureCode.RollbackFailed,
                    "Rollback target root does not exist.");
            }

            var targetRoot = VariantPathPolicy.NormalizeFullPath(rollbackPlan.TargetRootPath);
            EnsureSafeTargetRoot(targetRoot);

            foreach (var entry in rollbackPlan.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!VariantPathPolicy.IsSameOrDescendant(entry.Path, targetRoot) ||
                    string.Equals(
                        VariantPathPolicy.NormalizeFullPath(entry.Path),
                        targetRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new VariantMaterializationException(
                        VariantMaterializationFailureCode.RollbackFailed,
                        $"Rollback path is outside the target root: {entry.Path}.");
                }

                if (VariantPathPolicy.ContainsReparsePointInExistingChain(targetRoot, entry.Path))
                {
                    throw new VariantMaterializationException(
                        VariantMaterializationFailureCode.ReparsePointRejected,
                        $"Rollback path contains a reparse point: {entry.Path}.");
                }

                if (entry.Action is VariantRollbackAction.DeleteCreatedFile)
                {
                    if (!File.Exists(entry.Path))
                    {
                        continue;
                    }

                    if (entry.ExpectedLength is null || !VariantHash.IsSha256(entry.ExpectedSha256))
                    {
                        throw new VariantMaterializationException(
                            VariantMaterializationFailureCode.RollbackFailed,
                            $"Rollback file identity is incomplete: {entry.Path}.");
                    }

                    var verification = await VariantFileVerifier.VerifyAsync(
                            entry.Path,
                            entry.ExpectedLength.Value,
                            entry.ExpectedSha256!,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (!verification.Passed)
                    {
                        throw new VariantMaterializationException(
                            VariantMaterializationFailureCode.RollbackFailed,
                            $"Rollback refused a changed file: {entry.Path}.");
                    }

                    if (entry.CreatedByAction is not VariantPlanAction.CreateHardLink)
                    {
                        File.SetAttributes(entry.Path, FileAttributes.Normal);
                    }

                    File.Delete(entry.Path);
                    deletedFiles++;
                    continue;
                }

                if (entry.Action is VariantRollbackAction.DeleteCreatedDirectoryIfEmpty &&
                    Directory.Exists(entry.Path) &&
                    !Directory.EnumerateFileSystemEntries(entry.Path).Any())
                {
                    Directory.Delete(entry.Path, recursive: false);
                    deletedDirectories++;
                }
            }

            return new VariantRollbackReceipt
            {
                Succeeded = true,
                FailureCode = VariantMaterializationFailureCode.None,
                ReceiptId = rollbackPlan.ReceiptId,
                DeletedFileCount = deletedFiles,
                DeletedDirectoryCount = deletedDirectories,
            };
        }
        catch (Exception ex) when (
            ex is VariantMaterializationException or
            IOException or
            UnauthorizedAccessException or
            OperationCanceledException)
        {
            return new VariantRollbackReceipt
            {
                Succeeded = false,
                FailureCode = ex is OperationCanceledException
                    ? VariantMaterializationFailureCode.Cancelled
                    : VariantMaterializationFailureCode.RollbackFailed,
                FailureDetail = ex.Message,
                ReceiptId = rollbackPlan.ReceiptId,
                DeletedFileCount = deletedFiles,
                DeletedDirectoryCount = deletedDirectories,
            };
        }
    }

    private static async Task<VariantMaterializationFileReceipt> MaterializeFileAsync(
        string sharedContentRoot,
        string variantOverlayRoot,
        string targetRoot,
        VariantPlanItem item,
        List<string> createdDirectories,
        CancellationToken cancellationToken)
    {
        if (item.SourcePath is null ||
            item.ExpectedLength is null ||
            !VariantHash.IsSha256(item.ExpectedSha256))
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.PlanRejected,
                $"Plan item identity is incomplete: {item.TargetRelativePath}.");
        }

        EnsureTargetPathIsSafe(targetRoot, item.TargetPath);
        EnsureParentDirectories(targetRoot, item.TargetPath, createdDirectories);
        if (File.Exists(item.TargetPath) || Directory.Exists(item.TargetPath))
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.TargetChanged,
                $"Target appeared after planning: {item.TargetRelativePath}.");
        }

        var expectedSourceRoot = item.Action is VariantPlanAction.CreateHardLink
            ? sharedContentRoot
            : variantOverlayRoot;
        if (!VariantPathPolicy.IsSameOrDescendant(item.SourcePath, expectedSourceRoot) ||
            VariantPathPolicy.ContainsReparsePointInExistingChain(expectedSourceRoot, item.SourcePath))
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.ReparsePointRejected,
                $"Source path contains a reparse point: {item.SourcePath}.");
        }

        var sourceVerification = await VariantFileVerifier.VerifyAsync(
                item.SourcePath,
                item.ExpectedLength.Value,
                item.ExpectedSha256!,
                cancellationToken)
            .ConfigureAwait(false);
        if (!sourceVerification.Passed)
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.SourceChanged,
                $"Source changed after planning: {item.TargetRelativePath}.");
        }

        var temporaryPath = CreateTemporaryPath(item.TargetPath);
        var temporaryIsHardLink = false;
        var targetCommitted = false;
        try
        {
            var hardLinkIdentityVerified = false;
            if (item.Action is VariantPlanAction.CreateHardLink)
            {
                if ((File.GetAttributes(item.SourcePath) & FileAttributes.ReadOnly) != 0)
                {
                    throw new VariantMaterializationException(
                        VariantMaterializationFailureCode.SourceChanged,
                        "Shared source became read-only after planning.");
                }

                ValidateHardLinkVolumes(item.SourcePath, Path.GetDirectoryName(item.TargetPath)!);
                if (!WindowsHardLink.TryCreate(
                        temporaryPath,
                        item.SourcePath,
                        out var hardLinkError))
                {
                    throw new VariantMaterializationException(
                        VariantMaterializationFailureCode.HardLinkCreationFailed,
                        $"CreateHardLink failed with Win32 error {hardLinkError}.");
                }

                temporaryIsHardLink = true;

                hardLinkIdentityVerified = WindowsHardLinkIdentity.AreSameFile(
                    item.SourcePath,
                    temporaryPath);
                if (!hardLinkIdentityVerified)
                {
                    throw new VariantMaterializationException(
                        VariantMaterializationFailureCode.VerificationFailed,
                        "Created hard link does not resolve to the source file identity.");
                }
            }
            else if (item.Action is VariantPlanAction.CopyFile)
            {
                await CopyToTemporaryFileAsync(item.SourcePath, temporaryPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                throw new VariantMaterializationException(
                    VariantMaterializationFailureCode.PlanRejected,
                    $"Plan item cannot create a file: {item.Action}.");
            }

            var temporaryVerification = await VariantFileVerifier.VerifyAsync(
                    temporaryPath,
                    item.ExpectedLength.Value,
                    item.ExpectedSha256!,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!temporaryVerification.Passed)
            {
                throw new VariantMaterializationException(
                    VariantMaterializationFailureCode.VerificationFailed,
                    $"Temporary file identity verification failed: {item.TargetRelativePath}.");
            }

            EnsureTargetPathIsSafe(targetRoot, item.TargetPath);
            try
            {
                File.Move(temporaryPath, item.TargetPath, overwrite: false);
                targetCommitted = true;
            }
            catch (IOException ex)
            {
                throw new VariantMaterializationException(
                    VariantMaterializationFailureCode.AtomicMoveFailed,
                    $"Atomic target commit failed: {ex.Message}");
            }

            var finalVerification = await VariantFileVerifier.VerifyAsync(
                    item.TargetPath,
                    item.ExpectedLength.Value,
                    item.ExpectedSha256!,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!finalVerification.Passed)
            {
                throw new VariantMaterializationException(
                    VariantMaterializationFailureCode.VerificationFailed,
                    $"Committed target identity verification failed: {item.TargetRelativePath}.");
            }

            if (item.Action is VariantPlanAction.CreateHardLink &&
                !WindowsHardLinkIdentity.AreSameFile(item.SourcePath, item.TargetPath))
            {
                throw new VariantMaterializationException(
                    VariantMaterializationFailureCode.VerificationFailed,
                    $"Committed target is not the planned hard link: {item.TargetRelativePath}.");
            }

            return new VariantMaterializationFileReceipt
            {
                TargetRelativePath = item.TargetRelativePath,
                TargetPath = item.TargetPath,
                Action = item.Action,
                Created = true,
                Length = finalVerification.ActualLength,
                Sha256 = finalVerification.ActualSha256,
                HardLinkIdentityVerified = hardLinkIdentityVerified,
            };
        }
        catch
        {
            if (targetCommitted && File.Exists(item.TargetPath))
            {
                try
                {
                    if (item.Action is not VariantPlanAction.CreateHardLink)
                    {
                        File.SetAttributes(item.TargetPath, FileAttributes.Normal);
                    }

                    File.Delete(item.TargetPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new VariantMaterializationException(
                        VariantMaterializationFailureCode.RollbackFailed,
                        $"A failed commit could not be removed: {ex.Message}");
                }
            }

            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    if (!temporaryIsHardLink)
                    {
                        File.SetAttributes(temporaryPath, FileAttributes.Normal);
                    }

                    File.Delete(temporaryPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static async Task CopyToTemporaryFileAsync(
        string sourcePath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        await using (var source = new FileStream(
                         sourcePath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         1024 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var destination = new FileStream(
                         temporaryPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         1024 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }

        File.SetLastWriteTimeUtc(temporaryPath, File.GetLastWriteTimeUtc(sourcePath));
        var sourceAttributes = File.GetAttributes(sourcePath);
        var retainedAttributes = sourceAttributes &
            (FileAttributes.ReadOnly |
             FileAttributes.Hidden |
             FileAttributes.System |
             FileAttributes.Archive |
             FileAttributes.NotContentIndexed);
        File.SetAttributes(temporaryPath, retainedAttributes);
    }

    private static async Task<VariantFileVerification> VerifyPlanItemAsync(
        string filePath,
        VariantPlanItem item,
        CancellationToken cancellationToken)
    {
        if (item.ExpectedLength is null || !VariantHash.IsSha256(item.ExpectedSha256))
        {
            return new VariantFileVerification(false, null, null, "Plan identity is incomplete.");
        }

        return await VariantFileVerifier.VerifyAsync(
                filePath,
                item.ExpectedLength.Value,
                item.ExpectedSha256!,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static VariantMaterializationFileReceipt ToUnchangedReceipt(VariantPlanItem item) => new()
    {
        TargetRelativePath = item.TargetRelativePath,
        TargetPath = item.TargetPath,
        Action = item.Action,
        Created = false,
        Length = item.ExpectedLength,
        Sha256 = item.ExpectedSha256,
    };

    private static VariantRollbackPlan BuildRollbackPlan(
        string receiptId,
        string targetRoot,
        string manifestSha256,
        IReadOnlyList<VariantMaterializationFileReceipt> createdFiles,
        IReadOnlyList<string> createdDirectories)
    {
        var entries = new List<VariantRollbackEntry>(createdFiles.Count + createdDirectories.Count);
        for (var index = createdFiles.Count - 1; index >= 0; index--)
        {
            var file = createdFiles[index];
            entries.Add(new VariantRollbackEntry
            {
                Action = VariantRollbackAction.DeleteCreatedFile,
                Path = file.TargetPath,
                CreatedByAction = file.Action,
                ExpectedLength = file.Length,
                ExpectedSha256 = file.Sha256,
            });
        }

        foreach (var directory in createdDirectories
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(path => path.Length))
        {
            entries.Add(new VariantRollbackEntry
            {
                Action = VariantRollbackAction.DeleteCreatedDirectoryIfEmpty,
                Path = directory,
            });
        }

        return new VariantRollbackPlan
        {
            ReceiptId = receiptId,
            TargetRootPath = targetRoot,
            ManifestContentSha256 = manifestSha256,
            Entries = Array.AsReadOnly(entries.ToArray()),
        };
    }

    private static void EnsureSafeTargetRoot(string targetRoot)
    {
        if (string.IsNullOrWhiteSpace(targetRoot) || !Directory.Exists(targetRoot))
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.TargetChanged,
                "Target root does not exist.");
        }

        if (VariantPathPolicy.ContainsReparsePoint(targetRoot))
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.ReparsePointRejected,
                "Target root contains a reparse point.");
        }
    }

    private static void EnsureTargetPathIsSafe(string targetRoot, string targetPath)
    {
        if (!VariantPathPolicy.IsSameOrDescendant(targetPath, targetRoot) ||
            string.Equals(
                VariantPathPolicy.NormalizeFullPath(targetPath),
                VariantPathPolicy.NormalizeFullPath(targetRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.TargetChanged,
                $"Target path escapes the explicit root: {targetPath}.");
        }

        if (VariantPathPolicy.ContainsReparsePointInExistingChain(targetRoot, targetPath))
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.ReparsePointRejected,
                $"Target path contains a reparse point: {targetPath}.");
        }
    }

    private static void EnsureParentDirectories(
        string targetRoot,
        string targetPath,
        List<string> createdDirectories)
    {
        var targetParent = Path.GetDirectoryName(targetPath)!;
        var missing = new Stack<string>();
        var current = targetParent;
        while (!Directory.Exists(current))
        {
            if (!VariantPathPolicy.IsSameOrDescendant(current, targetRoot) ||
                string.Equals(current, targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new VariantMaterializationException(
                    VariantMaterializationFailureCode.TargetChanged,
                    $"Parent directory escapes the target root: {current}.");
            }

            missing.Push(current);
            current = Path.GetDirectoryName(current)!;
        }

        if (VariantPathPolicy.ContainsReparsePointInExistingChain(targetRoot, current))
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.ReparsePointRejected,
                $"Parent path contains a reparse point: {current}.");
        }

        while (missing.Count > 0)
        {
            var directory = missing.Pop();
            Directory.CreateDirectory(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new VariantMaterializationException(
                    VariantMaterializationFailureCode.ReparsePointRejected,
                    $"Created parent became a reparse point: {directory}.");
            }

            createdDirectories.Add(directory);
        }
    }

    private static void ValidateHardLinkVolumes(string sourcePath, string targetDirectory)
    {
        var sourceVolumeRead = WindowsVolumeInspector.TryGet(
            sourcePath,
            out var sourceVolume,
            out var sourceError);
        var targetVolumeRead = WindowsVolumeInspector.TryGet(
            targetDirectory,
            out var targetVolume,
            out var targetError);
        if (!sourceVolumeRead || !targetVolumeRead)
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.IoFailure,
                sourceError ?? targetError ?? "Volume inspection failed.");
        }

        if (!sourceVolume.IsNtfs || !targetVolume.IsNtfs)
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.NonNtfsVolume,
                "Hard-link source and target must both use NTFS.");
        }

        if (!sourceVolume.IsSameVolume(targetVolume))
        {
            throw new VariantMaterializationException(
                VariantMaterializationFailureCode.CrossVolumeHardLink,
                "Hard-link source and target are on different volumes.");
        }
    }

    private static string CreateTemporaryPath(string targetPath) =>
        Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".{Path.GetFileName(targetPath)}.nikkiward-{Guid.NewGuid():N}.tmp");

    private static string SafeNormalize(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : VariantPathPolicy.NormalizeFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

}

internal static class WindowsHardLink
{
    public static bool TryCreate(
        string fileName,
        string existingFileName,
        out int errorCode)
    {
        var created = CreateHardLink(
            ToExtendedWindowsPath(fileName),
            ToExtendedWindowsPath(existingFileName),
            IntPtr.Zero);
        errorCode = created ? 0 : Marshal.GetLastWin32Error();
        return created;
    }

    internal static string ToExtendedWindowsPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + fullPath[2..]
            : @"\\?\" + fullPath;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);
}

internal static class WindowsHardLinkIdentity
{
    public static bool AreSameFile(string firstPath, string secondPath)
    {
        using var firstHandle = File.OpenHandle(
            firstPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var secondHandle = File.OpenHandle(
            secondPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (!GetFileInformationByHandle(firstHandle, out var firstInformation) ||
            !GetFileInformationByHandle(secondHandle, out var secondInformation))
        {
            return false;
        }

        return firstInformation.VolumeSerialNumber == secondInformation.VolumeSerialNumber &&
               firstInformation.FileIndexHigh == secondInformation.FileIndexHigh &&
               firstInformation.FileIndexLow == secondInformation.FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle fileHandle,
        out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}

internal sealed class VariantMaterializationException : Exception
{
    public VariantMaterializationException(
        VariantMaterializationFailureCode code,
        string message)
        : base(message)
    {
        Code = code;
    }

    public VariantMaterializationFailureCode Code { get; }
}
