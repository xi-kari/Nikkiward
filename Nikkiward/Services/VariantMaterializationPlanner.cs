using System.Runtime.InteropServices;
using System.Text;
using Nikkiward.Models;

namespace Nikkiward.Services;

public interface IVariantMaterializationPlanner
{
    Task<VariantMaterializationPlan> CreatePlanAsync(
        VariantMaterializationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class VariantMaterializationPlanner : IVariantMaterializationPlanner
{
    public async Task<VariantMaterializationPlan> CreatePlanAsync(
        VariantMaterializationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.Definition is null || request.Manifest is null)
        {
            return Failed(VariantPlanFailureCode.InvalidRequest, "A complete request is required.");
        }

        var frozenDefinition = VariantDefinitionCatalog.Find(request.Definition.VariantId);
        if (frozenDefinition is null || request.Definition != frozenDefinition)
        {
            return Failed(VariantPlanFailureCode.InvalidVariant, "Variant definition is not frozen catalog data.");
        }

        var manifest = Snapshot(request.Manifest);
        var validation = VariantManifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            var digestMismatch = validation.Errors.Any(error =>
                error.Contains("ContentSha256", StringComparison.Ordinal));
            return Failed(
                digestMismatch
                    ? VariantPlanFailureCode.ManifestDigestMismatch
                    : VariantPlanFailureCode.InvalidManifest,
                string.Join(" ", validation.Errors),
                request.Definition.VariantId,
                validation.ComputedContentSha256);
        }

        if (manifest.VariantId != frozenDefinition.VariantId)
        {
            return Failed(
                VariantPlanFailureCode.InvalidVariant,
                "Manifest variant does not match the requested definition.",
                frozenDefinition.VariantId,
                manifest.ContentSha256);
        }

        if (!TryNormalizeExistingDirectory(
                request.SharedContentRootPath,
                out var sharedRoot,
                out var sharedError))
        {
            return Failed(
                VariantPlanFailureCode.SourceRootMissing,
                $"Shared content root: {sharedError}",
                frozenDefinition.VariantId,
                manifest.ContentSha256);
        }

        if (!TryNormalizeExistingDirectory(
                request.VariantOverlayRootPath,
                out var overlayRoot,
                out var overlayError))
        {
            return Failed(
                VariantPlanFailureCode.SourceRootMissing,
                $"Variant overlay root: {overlayError}",
                frozenDefinition.VariantId,
                manifest.ContentSha256);
        }

        if (!TryNormalizeExistingDirectory(
                request.TargetRootPath,
                out var targetRoot,
                out var targetError))
        {
            return Failed(
                VariantPlanFailureCode.TargetRootMissing,
                $"Target root: {targetError}",
                frozenDefinition.VariantId,
                manifest.ContentSha256);
        }

        if (VariantPathPolicy.PathsOverlap(sharedRoot, overlayRoot) ||
            VariantPathPolicy.PathsOverlap(sharedRoot, targetRoot) ||
            VariantPathPolicy.PathsOverlap(overlayRoot, targetRoot))
        {
            return Failed(
                VariantPlanFailureCode.RootOverlap,
                "Shared, overlay, and target roots must be disjoint.",
                frozenDefinition.VariantId,
                manifest.ContentSha256,
                sharedRoot,
                overlayRoot,
                targetRoot);
        }

        if (VariantPathPolicy.ContainsReparsePoint(sharedRoot) ||
            VariantPathPolicy.ContainsReparsePoint(overlayRoot) ||
            VariantPathPolicy.ContainsReparsePoint(targetRoot))
        {
            return Failed(
                VariantPlanFailureCode.ReparsePointRejected,
                "A root path contains a reparse point.",
                frozenDefinition.VariantId,
                manifest.ContentSha256,
                sharedRoot,
                overlayRoot,
                targetRoot);
        }

        WindowsVolumeIdentity? targetVolume = null;
        var items = new List<VariantPlanItem>(manifest.Entries.Count);
        long hardLinkBytes = 0;
        long copyBytes = 0;

        foreach (var entry in manifest.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!VariantPathPolicy.TryResolveWithinRoot(
                    targetRoot,
                    entry.TargetRelativePath,
                    out var targetPath,
                    out var targetPathError))
            {
                return Failed(
                    VariantPlanFailureCode.PathOutsideRoot,
                    targetPathError,
                    frozenDefinition.VariantId,
                    manifest.ContentSha256,
                    sharedRoot,
                    overlayRoot,
                    targetRoot,
                    items);
            }

            if (VariantPathPolicy.ContainsReparsePointInExistingChain(targetRoot, targetPath))
            {
                return Failed(
                    VariantPlanFailureCode.ReparsePointRejected,
                    $"Target path contains a reparse point: {entry.TargetRelativePath}.",
                    frozenDefinition.VariantId,
                    manifest.ContentSha256,
                    sharedRoot,
                    overlayRoot,
                    targetRoot,
                    items);
            }

            if (entry.Classification is VariantFileClassification.AbsentPath)
            {
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    return Failed(
                        VariantPlanFailureCode.TargetConflict,
                        $"Manifest requires an absent path: {entry.TargetRelativePath}.",
                        frozenDefinition.VariantId,
                        manifest.ContentSha256,
                        sharedRoot,
                        overlayRoot,
                        targetRoot,
                        items);
                }

                items.Add(ToPlanItem(entry, null, targetPath, VariantPlanAction.ConfirmAbsent));
                continue;
            }

            var sourceRoot = entry.SourceKind switch
            {
                VariantSourceKind.SharedContent => sharedRoot,
                VariantSourceKind.VariantOverlay => overlayRoot,
                _ => string.Empty,
            };
            if (!VariantPathPolicy.TryResolveWithinRoot(
                    sourceRoot,
                    entry.SourceRelativePath,
                    out var sourcePath,
                    out var sourcePathError))
            {
                return Failed(
                    VariantPlanFailureCode.PathOutsideRoot,
                    sourcePathError,
                    frozenDefinition.VariantId,
                    manifest.ContentSha256,
                    sharedRoot,
                    overlayRoot,
                    targetRoot,
                    items);
            }

            var sourceExists = File.Exists(sourcePath);
            if (!sourceExists && entry.Classification is not VariantFileClassification.OptionalResource)
            {
                return Failed(
                    VariantPlanFailureCode.SourceFileMissing,
                    $"Required source file is missing: {entry.SourceRelativePath}.",
                    frozenDefinition.VariantId,
                    manifest.ContentSha256,
                    sharedRoot,
                    overlayRoot,
                    targetRoot,
                    items);
            }

            if (sourceExists &&
                VariantPathPolicy.ContainsReparsePointInExistingChain(sourceRoot, sourcePath))
            {
                return Failed(
                    VariantPlanFailureCode.ReparsePointRejected,
                    $"Source path contains a reparse point: {entry.SourceRelativePath}.",
                    frozenDefinition.VariantId,
                    manifest.ContentSha256,
                    sharedRoot,
                    overlayRoot,
                    targetRoot,
                    items);
            }

            if (Directory.Exists(targetPath))
            {
                return Failed(
                    VariantPlanFailureCode.TargetConflict,
                    $"A directory occupies the target file path: {entry.TargetRelativePath}.",
                    frozenDefinition.VariantId,
                    manifest.ContentSha256,
                    sharedRoot,
                    overlayRoot,
                    targetRoot,
                    items);
            }

            if (!sourceExists)
            {
                if (File.Exists(targetPath))
                {
                    if (entry.SourceKind is VariantSourceKind.SharedContent)
                    {
                        return Failed(
                            VariantPlanFailureCode.TargetConflict,
                            $"A shared optional target exists without its frozen source: {entry.TargetRelativePath}.",
                            frozenDefinition.VariantId,
                            manifest.ContentSha256,
                            sharedRoot,
                            overlayRoot,
                            targetRoot,
                            items);
                    }

                    var existingVerification = await VerifyEntryAsync(targetPath, entry, cancellationToken)
                        .ConfigureAwait(false);
                    if (!existingVerification.Passed)
                    {
                        return Failed(
                            VariantPlanFailureCode.TargetConflict,
                            $"Optional target conflicts with its frozen identity: {entry.TargetRelativePath}.",
                            frozenDefinition.VariantId,
                            manifest.ContentSha256,
                            sharedRoot,
                            overlayRoot,
                            targetRoot,
                            items);
                    }

                    items.Add(ToPlanItem(entry, null, targetPath, VariantPlanAction.KeepVerifiedFile));
                }
                else
                {
                    items.Add(ToPlanItem(
                        entry,
                        sourcePath,
                        targetPath,
                        VariantPlanAction.SkipMissingOptionalResource));
                }

                continue;
            }

            var sourceVerification = await VerifyEntryAsync(sourcePath, entry, cancellationToken)
                .ConfigureAwait(false);
            if (!sourceVerification.Passed)
            {
                return Failed(
                    VariantPlanFailureCode.SourceIdentityMismatch,
                    $"Source identity mismatch for '{entry.SourceRelativePath}': " +
                    sourceVerification.FailureDetail,
                    frozenDefinition.VariantId,
                    manifest.ContentSha256,
                    sharedRoot,
                    overlayRoot,
                    targetRoot,
                    items);
            }

            if (File.Exists(targetPath))
            {
                var targetVerification = await VerifyEntryAsync(targetPath, entry, cancellationToken)
                    .ConfigureAwait(false);
                if (!targetVerification.Passed)
                {
                    return Failed(
                        VariantPlanFailureCode.TargetConflict,
                        $"Existing target identity mismatch for '{entry.TargetRelativePath}': " +
                        targetVerification.FailureDetail,
                        frozenDefinition.VariantId,
                        manifest.ContentSha256,
                        sharedRoot,
                        overlayRoot,
                        targetRoot,
                        items);
                }

                if (entry.SourceKind is VariantSourceKind.SharedContent)
                {
                    if (!OperatingSystem.IsWindows() ||
                        !WindowsHardLinkIdentity.AreSameFile(sourcePath, targetPath))
                    {
                        return Failed(
                            VariantPlanFailureCode.TargetConflict,
                            $"Existing shared target is not the source hard link: {entry.TargetRelativePath}.",
                            frozenDefinition.VariantId,
                            manifest.ContentSha256,
                            sharedRoot,
                            overlayRoot,
                            targetRoot,
                            items);
                    }
                }

                items.Add(ToPlanItem(entry, sourcePath, targetPath, VariantPlanAction.KeepVerifiedFile));
                continue;
            }

            var action = entry.SourceKind is VariantSourceKind.SharedContent
                ? VariantPlanAction.CreateHardLink
                : VariantPlanAction.CopyFile;
            if (action is VariantPlanAction.CreateHardLink)
            {
                if ((File.GetAttributes(sourcePath) & FileAttributes.ReadOnly) != 0)
                {
                    return Failed(
                        VariantPlanFailureCode.UnsafeSourceAttributes,
                        $"A read-only shared source cannot support rollback without source mutation: {entry.SourceRelativePath}.",
                        frozenDefinition.VariantId,
                        manifest.ContentSha256,
                        sharedRoot,
                        overlayRoot,
                        targetRoot,
                        items);
                }

                if (!OperatingSystem.IsWindows())
                {
                    return Failed(
                        VariantPlanFailureCode.UnsupportedPlatform,
                        "NTFS hard-link planning requires Windows.",
                        frozenDefinition.VariantId,
                        manifest.ContentSha256,
                        sharedRoot,
                        overlayRoot,
                        targetRoot,
                        items);
                }

                var sourceVolumeRead = WindowsVolumeInspector.TryGet(
                    sourcePath,
                    out var sourceVolume,
                    out var sourceVolumeError);
                var targetVolumeRead = WindowsVolumeInspector.TryGet(
                    targetRoot,
                    out targetVolume,
                    out var targetVolumeError);
                if (!sourceVolumeRead || !targetVolumeRead)
                {
                    return Failed(
                        VariantPlanFailureCode.IoFailure,
                        sourceVolumeError ?? targetVolumeError,
                        frozenDefinition.VariantId,
                        manifest.ContentSha256,
                        sharedRoot,
                        overlayRoot,
                        targetRoot,
                        items);
                }

                if (!sourceVolume.IsNtfs || !targetVolume.IsNtfs)
                {
                    return Failed(
                        VariantPlanFailureCode.NonNtfsVolume,
                        "Shared source and target must both use NTFS.",
                        frozenDefinition.VariantId,
                        manifest.ContentSha256,
                        sharedRoot,
                        overlayRoot,
                        targetRoot,
                        items);
                }

                if (!sourceVolume.IsSameVolume(targetVolume))
                {
                    return Failed(
                        VariantPlanFailureCode.CrossVolumeHardLink,
                        $"Hard-link source and target are on different volumes: {entry.TargetRelativePath}.",
                        frozenDefinition.VariantId,
                        manifest.ContentSha256,
                        sharedRoot,
                        overlayRoot,
                        targetRoot,
                        items);
                }

                hardLinkBytes = checked(hardLinkBytes + entry.Length!.Value);
            }
            else
            {
                copyBytes = checked(copyBytes + entry.Length!.Value);
            }

            items.Add(ToPlanItem(entry, sourcePath, targetPath, action));
        }

        var plan = new VariantMaterializationPlan
        {
            CanExecute = true,
            FailureCode = VariantPlanFailureCode.None,
            VariantId = frozenDefinition.VariantId,
            ManifestContentSha256 = manifest.ContentSha256,
            SharedContentRootPath = sharedRoot,
            VariantOverlayRootPath = overlayRoot,
            TargetRootPath = targetRoot,
            Items = Array.AsReadOnly(items.ToArray()),
            HardLinkBytes = hardLinkBytes,
            CopyBytes = copyBytes,
        };

        return plan with { PlanSha256 = VariantPlanDigest.Compute(plan) };
    }

    private static VariantManifest Snapshot(VariantManifest manifest)
    {
        var entries = (manifest.Entries ?? Array.Empty<VariantManifestEntry>())
            .Where(entry => entry is not null)
            .Select(entry => entry with { })
            .ToArray();
        return manifest with { Entries = Array.AsReadOnly(entries) };
    }

    private static async Task<VariantFileVerification> VerifyEntryAsync(
        string filePath,
        VariantManifestEntry entry,
        CancellationToken cancellationToken) =>
        await VariantFileVerifier.VerifyAsync(
                filePath,
                entry.Length!.Value,
                entry.Sha256!,
                cancellationToken)
            .ConfigureAwait(false);

    private static VariantPlanItem ToPlanItem(
        VariantManifestEntry entry,
        string? sourcePath,
        string targetPath,
        VariantPlanAction action) => new()
        {
            TargetRelativePath = entry.TargetRelativePath,
            SourcePath = sourcePath,
            TargetPath = targetPath,
            Classification = entry.Classification,
            SourceKind = entry.SourceKind,
            Action = action,
            ExpectedLength = entry.Length,
            ExpectedSha256 = entry.Sha256,
        };

    private static bool TryNormalizeExistingDirectory(
        string? path,
        out string normalizedPath,
        out string? error)
    {
        normalizedPath = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is required.";
            return false;
        }

        try
        {
            normalizedPath = VariantPathPolicy.NormalizeFullPath(path);
            if (!Directory.Exists(normalizedPath))
            {
                error = $"Directory does not exist: {normalizedPath}.";
                normalizedPath = string.Empty;
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid directory path: {ex.GetType().Name}.";
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static VariantMaterializationPlan Failed(
        VariantPlanFailureCode code,
        string? detail,
        GameVariantId variantId = GameVariantId.Unknown,
        string manifestSha256 = "",
        string sharedRoot = "",
        string overlayRoot = "",
        string targetRoot = "",
        IReadOnlyList<VariantPlanItem>? items = null) => new()
        {
            CanExecute = false,
            FailureCode = code,
            FailureDetail = detail,
            VariantId = variantId,
            ManifestContentSha256 = manifestSha256,
            SharedContentRootPath = sharedRoot,
            VariantOverlayRootPath = overlayRoot,
            TargetRootPath = targetRoot,
            Items = items is null
                ? Array.Empty<VariantPlanItem>()
                : Array.AsReadOnly(items.ToArray()),
        };
}

internal sealed record WindowsVolumeIdentity(
    string MountPoint,
    string VolumeName,
    string FileSystemName)
{
    public bool IsNtfs => string.Equals(FileSystemName, "NTFS", StringComparison.OrdinalIgnoreCase);

    public bool IsSameVolume(WindowsVolumeIdentity other) =>
        string.Equals(VolumeName, other.VolumeName, StringComparison.OrdinalIgnoreCase);
}

internal static class WindowsVolumeInspector
{
    public static bool TryGet(
        string existingPath,
        out WindowsVolumeIdentity identity,
        out string? error)
    {
        identity = null!;
        error = null;
        if (!OperatingSystem.IsWindows())
        {
            error = "Windows volume inspection is unavailable on this platform.";
            return false;
        }

        var mountPoint = new StringBuilder(32768);
        if (!GetVolumePathName(existingPath, mountPoint, mountPoint.Capacity))
        {
            error = $"GetVolumePathName failed with Win32 error {Marshal.GetLastWin32Error()}.";
            return false;
        }

        var volumeName = new StringBuilder(32768);
        if (!GetVolumeNameForVolumeMountPoint(mountPoint.ToString(), volumeName, volumeName.Capacity))
        {
            error = $"GetVolumeNameForVolumeMountPoint failed with Win32 error {Marshal.GetLastWin32Error()}.";
            return false;
        }

        var fileSystemName = new StringBuilder(64);
        if (!GetVolumeInformation(
                mountPoint.ToString(),
                null,
                0,
                out _,
                out _,
                out _,
                fileSystemName,
                fileSystemName.Capacity))
        {
            error = $"GetVolumeInformation failed with Win32 error {Marshal.GetLastWin32Error()}.";
            return false;
        }

        identity = new WindowsVolumeIdentity(
            mountPoint.ToString(),
            volumeName.ToString(),
            fileSystemName.ToString());
        return true;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(
        string fileName,
        StringBuilder volumePathName,
        int bufferLength);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string volumeMountPoint,
        StringBuilder volumeName,
        int bufferLength);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder fileSystemNameBuffer,
        int fileSystemNameSize);
}
