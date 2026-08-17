using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nikkiward.Models;

namespace Nikkiward.Services;

public interface IChannelStoreBuilder
{
    Task<ChannelStoreBuildPlan> CreatePlanAsync(
        ChannelStoreBuildRequest request,
        IProgress<ChannelStoreProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ChannelStoreBuildReceipt> BuildAsync(
        ChannelStoreBuildPlan plan,
        string expectedPlanSha256,
        IProgress<ChannelStoreProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<bool> RollbackAsync(
        ChannelStoreBuildReceipt receipt,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsChannelStoreBuilder : IChannelStoreBuilder
{
    private static readonly Regex VersionDirectoryPattern = new(
        "^\\d+\\.\\d+\\.\\d+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions ReceiptJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IVariantMaterializationPlanner planner;
    private readonly IVariantMaterializer materializer;

    public WindowsChannelStoreBuilder(
        IVariantMaterializationPlanner? planner = null,
        IVariantMaterializer? materializer = null)
    {
        this.planner = planner ?? new VariantMaterializationPlanner();
        this.materializer = materializer ?? new WindowsVariantMaterializer(this.planner);
    }

    public Task<ChannelStoreBuildPlan> CreatePlanAsync(
        ChannelStoreBuildRequest request,
        IProgress<ChannelStoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(
            () => CreatePlanCoreAsync(request, progress, cancellationToken),
            cancellationToken);
    }

    public async Task<ChannelStoreBuildReceipt> BuildAsync(
        ChannelStoreBuildPlan plan,
        string expectedPlanSha256,
        IProgress<ChannelStoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var startedAtUtc = DateTimeOffset.UtcNow;
        var receiptId = Guid.NewGuid().ToString("N");
        var importedFiles = new List<string>();
        var variantReceipts = new List<ChannelStoreVariantReceipt>();

        if (!plan.CanExecute)
        {
            return FailedReceipt(
                receiptId,
                plan,
                ChannelStoreFailureCode.InvalidRequest,
                "The channel store plan is not executable.",
                startedAtUtc);
        }

        var computedPlanSha256 = ComputePlanSha256(plan);
        if (!VariantHash.IsSha256(expectedPlanSha256) ||
            !string.Equals(computedPlanSha256, expectedPlanSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.PlanSha256, expectedPlanSha256, StringComparison.OrdinalIgnoreCase))
        {
            return FailedReceipt(
                receiptId,
                plan,
                ChannelStoreFailureCode.PlanChanged,
                "The channel store plan digest changed before execution.",
                startedAtUtc);
        }

        try
        {
            Directory.CreateDirectory(plan.StoreRootPath);
            Directory.CreateDirectory(plan.SharedContentRootPath);
            foreach (var variant in plan.Variants)
            {
                Directory.CreateDirectory(variant.OverlayRootPath);
                Directory.CreateDirectory(variant.TargetGameRootPath);
                Directory.CreateDirectory(variant.TargetLauncherRootPath);
            }

            long importedBytes = 0;
            var totalImportBytes = plan.Imports.Sum(item => item.Length);
            for (var index = 0; index < plan.Imports.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = plan.Imports[index];
                progress?.Report(new ChannelStoreProgress
                {
                    Stage = ChannelStoreProgressStage.Importing,
                    FilesCompleted = index,
                    TotalFiles = plan.Imports.Count,
                    BytesCompleted = importedBytes,
                    TotalBytes = totalImportBytes,
                    CurrentPath = item.DestinationPath,
                });
                var created = await ImportAsync(item, cancellationToken).ConfigureAwait(false);
                if (created)
                {
                    importedFiles.Add(item.DestinationPath);
                }

                importedBytes += item.Length;
            }

            for (var index = 0; index < plan.Variants.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var variant = plan.Variants[index];
                progress?.Report(new ChannelStoreProgress
                {
                    Stage = ChannelStoreProgressStage.Materializing,
                    FilesCompleted = index,
                    TotalFiles = plan.Variants.Count,
                    CurrentPath = variant.TargetGameRootPath,
                });
                var request = new VariantMaterializationRequest
                {
                    Definition = variant.Definition,
                    Manifest = variant.Manifest,
                    SharedContentRootPath = plan.SharedContentRootPath,
                    VariantOverlayRootPath = variant.OverlayRootPath,
                    TargetRootPath = variant.TargetGameRootPath,
                };
                var materializationPlan = await planner.CreatePlanAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                if (!materializationPlan.CanExecute)
                {
                    throw new ChannelStoreException(
                        ChannelStoreFailureCode.MaterializationFailed,
                        $"{variant.Definition.VariantKey}: {materializationPlan.FailureCode}. " +
                        materializationPlan.FailureDetail);
                }

                var materialization = await materializer.MaterializeAsync(
                        request,
                        materializationPlan.PlanSha256,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!materialization.Succeeded)
                {
                    throw new ChannelStoreException(
                        ChannelStoreFailureCode.MaterializationFailed,
                        $"{variant.Definition.VariantKey}: {materialization.FailureCode}. " +
                        materialization.FailureDetail);
                }

                variantReceipts.Add(new ChannelStoreVariantReceipt
                {
                    VariantId = variant.Definition.VariantId,
                    TargetGameRootPath = variant.TargetGameRootPath,
                    TargetLauncherRootPath = variant.TargetLauncherRootPath,
                    TargetXStarterPath = variant.TargetXStarterPath,
                    ManifestContentSha256 = variant.Manifest.ContentSha256,
                    Materialization = materialization,
                });
            }

            progress?.Report(new ChannelStoreProgress
            {
                Stage = ChannelStoreProgressStage.PersistingReceipts,
                FilesCompleted = plan.Variants.Count,
                TotalFiles = plan.Variants.Count,
            });
            var cleanupRoots = plan.Variants
                .SelectMany(variant => new[]
                {
                    variant.SourceGameRootPath,
                    variant.SourceLauncherRootPath,
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var receipt = new ChannelStoreBuildReceipt
            {
                ReceiptId = receiptId,
                Succeeded = true,
                StoreRootPath = plan.StoreRootPath,
                PlanSha256 = plan.PlanSha256,
                ImportedFiles = Array.AsReadOnly(importedFiles.ToArray()),
                Variants = Array.AsReadOnly(variantReceipts.ToArray()),
                SourceRootsEligibleForManualCleanup = Array.AsReadOnly(cleanupRoots),
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
            await PersistArtifactsAsync(plan, receipt, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ChannelStoreProgress
            {
                Stage = ChannelStoreProgressStage.Completed,
                FilesCompleted = plan.Imports.Count,
                TotalFiles = plan.Imports.Count,
                BytesCompleted = totalImportBytes,
                TotalBytes = totalImportBytes,
            });
            return receipt;
        }
        catch (Exception ex) when (
            ex is ChannelStoreException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            var rollbackSucceeded = await RollbackCreatedStateAsync(
                    variantReceipts,
                    importedFiles,
                    cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
            var code = ex switch
            {
                ChannelStoreException storeException => storeException.Code,
                OperationCanceledException => ChannelStoreFailureCode.Cancelled,
                _ => ChannelStoreFailureCode.IoFailure,
            };
            return new ChannelStoreBuildReceipt
            {
                ReceiptId = receiptId,
                FailureCode = code,
                FailureDetail = ex.Message,
                StoreRootPath = plan.StoreRootPath,
                PlanSha256 = plan.PlanSha256,
                ImportedFiles = Array.AsReadOnly(importedFiles.ToArray()),
                Variants = Array.AsReadOnly(variantReceipts.ToArray()),
                AutomaticRollbackAttempted = importedFiles.Count > 0 || variantReceipts.Count > 0,
                AutomaticRollbackSucceeded = rollbackSucceeded,
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
        }
    }

    public async Task<bool> RollbackAsync(
        ChannelStoreBuildReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return await RollbackCreatedStateAsync(
                receipt.Variants,
                receipt.ImportedFiles,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ChannelStoreBuildPlan> CreatePlanCoreAsync(
        ChannelStoreBuildRequest request,
        IProgress<ChannelStoreProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeStoreRoot(request.StoreRootPath, out var storeRoot, out var storeError))
        {
            return Rejected(ChannelStoreFailureCode.InvalidStoreRoot, storeError);
        }

        var selected = request.Candidates
            .Where(candidate => candidate.Profile is not null &&
                                candidate.GameRootPath is not null &&
                                candidate.State is InstallationCandidateState.Candidate or
                                    InstallationCandidateState.ReadyForStaticVerification)
            .ToArray();
        var requiredChannels = new[]
        {
            DistributionChannel.Official,
            DistributionChannel.Bilibili,
            DistributionChannel.Steam,
        };
        var byChannel = new Dictionary<DistributionChannel, InstallationProfileCandidate>();
        foreach (var channel in requiredChannels)
        {
            var matches = selected
                .Where(candidate => candidate.Identity.DistributionChannel == channel)
                .ToArray();
            if (matches.Length == 0)
            {
                return Rejected(ChannelStoreFailureCode.MissingChannel, $"Missing selectable channel: {channel}.");
            }

            if (matches.Length != 1)
            {
                return Rejected(ChannelStoreFailureCode.DuplicateChannel, $"Channel is not unique: {channel}.");
            }

            byChannel[channel] = matches[0];
        }

        var sources = new List<ChannelSource>();
        foreach (var channel in requiredChannels)
        {
            try
            {
                sources.Add(CreateSource(byChannel[channel]));
            }
            catch (ChannelStoreException ex)
            {
                return Rejected(ex.Code, ex.Message);
            }
        }

        foreach (var source in sources)
        {
            if (!Directory.Exists(source.GameRootPath) ||
                !Directory.Exists(source.LauncherRootPath) ||
                !Directory.Exists(source.VersionDirectoryPath) ||
                !File.Exists(source.XStarterPath))
            {
                return Rejected(
                    ChannelStoreFailureCode.InvalidSourceRoot,
                    $"Invalid source roots for {source.Definition.VariantKey}.");
            }

            if (VariantPathPolicy.ContainsReparsePoint(source.GameRootPath) ||
                VariantPathPolicy.ContainsReparsePoint(source.LauncherRootPath) ||
                VariantPathPolicy.ContainsReparsePoint(source.VersionDirectoryPath))
            {
                return Rejected(
                    ChannelStoreFailureCode.ReparsePointRejected,
                    $"Source roots contain a reparse point for {source.Definition.VariantKey}.");
            }

            if (VariantPathPolicy.PathsOverlap(storeRoot, source.GameRootPath) ||
                VariantPathPolicy.PathsOverlap(storeRoot, source.LauncherRootPath))
            {
                return Rejected(
                    ChannelStoreFailureCode.RootOverlap,
                    $"Store root overlaps a source root for {source.Definition.VariantKey}.");
            }
        }

        progress?.Report(new ChannelStoreProgress { Stage = ChannelStoreProgressStage.Enumerating });
        var discovered = new List<SourceFile>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            discovered.AddRange(EnumerateSourceFiles(source, cancellationToken));
        }

        var runtimeFiles = new List<RuntimeFile>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            runtimeFiles.AddRange(EnumerateRuntimeFiles(source, cancellationToken));
        }

        for (var index = 0; index < discovered.Count; index++)
        {
            discovered[index] = discovered[index] with { Id = index + 1 };
        }

        var totalBytes = discovered.Sum(file => file.Length) + runtimeFiles.Sum(file => file.Length);
        var totalFiles = discovered.Count + runtimeFiles.Count;
        long hashedBytes = 0;
        for (var index = 0; index < discovered.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = discovered[index];
            progress?.Report(new ChannelStoreProgress
            {
                Stage = ChannelStoreProgressStage.Hashing,
                FilesCompleted = index,
                TotalFiles = totalFiles,
                BytesCompleted = hashedBytes,
                TotalBytes = totalBytes,
                CurrentPath = file.SourcePath,
            });
            discovered[index] = file with
            {
                Sha256 = await ComputeFileSha256Async(file.SourcePath, cancellationToken)
                    .ConfigureAwait(false),
            };
            hashedBytes += file.Length;
        }

        for (var index = 0; index < runtimeFiles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = runtimeFiles[index];
            progress?.Report(new ChannelStoreProgress
            {
                Stage = ChannelStoreProgressStage.Hashing,
                FilesCompleted = discovered.Count + index,
                TotalFiles = totalFiles,
                BytesCompleted = hashedBytes,
                TotalBytes = totalBytes,
                CurrentPath = file.SourcePath,
            });
            runtimeFiles[index] = file with
            {
                Sha256 = await ComputeFileSha256Async(file.SourcePath, cancellationToken)
                    .ConfigureAwait(false),
            };
            hashedBytes += file.Length;
        }

        var sharedIds = ResolveSharedFiles(discovered);
        var sharedRoot = Path.Combine(storeRoot, "content");
        var imports = new List<ChannelStoreImportItem>();
        var sharedImports = discovered
            .Where(file => sharedIds.Contains(file.Id))
            .GroupBy(file => (file.Length, file.Sha256), FileIdentityComparer.Instance);
        foreach (var group in sharedImports)
        {
            var preferred = group
                .OrderBy(file => SourcePriority(file.VariantId))
                .First();
            var relativeObjectPath = SharedObjectRelativePath(preferred.Sha256);
            imports.Add(CreateImport(
                preferred.SourcePath,
                Path.Combine(sharedRoot, relativeObjectPath),
                preferred.Length,
                preferred.Sha256,
                sharedContent: true,
                storeRoot));
        }

        var variantPlans = new List<ChannelStoreVariantPlan>();
        foreach (var source in sources)
        {
            var overlayRoot = Path.Combine(storeRoot, "overlays", source.Definition.VariantKey);
            var targetRoot = Path.Combine(
                storeRoot,
                "profiles",
                source.Definition.VariantKey,
                source.Definition.VariantId is GameVariantId.MainlandBilibili
                    ? "InfinityNikkiBili"
                    : "InfinityNikki");
            var targetLauncherRoot = Path.Combine(
                storeRoot,
                "runtimes",
                source.Definition.VariantKey);
            var xstarterRelativePath = Path.GetRelativePath(
                source.LauncherRootPath,
                source.XStarterPath);
            var targetXStarterPath = Path.Combine(targetLauncherRoot, xstarterRelativePath);
            foreach (var runtimeFile in runtimeFiles.Where(file =>
                         file.VariantId == source.Definition.VariantId))
            {
                imports.Add(CreateImport(
                    runtimeFile.SourcePath,
                    Path.Combine(targetLauncherRoot, runtimeFile.RelativePath),
                    runtimeFile.Length,
                    runtimeFile.Sha256,
                    sharedContent: false,
                    storeRoot));
            }

            var entries = new List<VariantManifestEntry>();
            foreach (var file in discovered.Where(file => file.VariantId == source.Definition.VariantId))
            {
                if (sharedIds.Contains(file.Id))
                {
                    entries.Add(new VariantManifestEntry
                    {
                        TargetRelativePath = file.RelativePath,
                        SourceRelativePath = SharedObjectRelativePath(file.Sha256),
                        Classification = file.Classification is VariantFileClassification.OptionalResource
                            ? VariantFileClassification.OptionalResource
                            : VariantFileClassification.SharedImmutable,
                        SourceKind = VariantSourceKind.SharedContent,
                        Length = file.Length,
                        Sha256 = file.Sha256,
                    });
                    continue;
                }

                var classification = file.Classification is VariantFileClassification.SharedImmutable
                    ? VariantFileClassification.VariantExclusive
                    : file.Classification;
                entries.Add(new VariantManifestEntry
                {
                    TargetRelativePath = file.RelativePath,
                    SourceRelativePath = file.RelativePath,
                    Classification = classification,
                    SourceKind = VariantSourceKind.VariantOverlay,
                    Length = file.Length,
                    Sha256 = file.Sha256,
                });
                imports.Add(CreateImport(
                    file.SourcePath,
                    Path.Combine(overlayRoot, file.RelativePath),
                    file.Length,
                    file.Sha256,
                    sharedContent: false,
                    storeRoot));
            }

            if (source.Definition.VariantId is GameVariantId.MainlandBilibili &&
                !entries.Any(entry => string.Equals(
                    entry.TargetRelativePath,
                    "productVersion.json",
                    StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(new VariantManifestEntry
                {
                    TargetRelativePath = "productVersion.json",
                    Classification = VariantFileClassification.AbsentPath,
                    SourceKind = VariantSourceKind.None,
                });
            }

            var buildId = source.Candidate.Identity.SteamBuildId ??
                          ProductMarkerReader.TryRead(Path.Combine(source.GameRootPath, "product.db"))?.Version ??
                          "unknown";
            var manifest = VariantManifestFactory.Freeze(
                $"{source.Definition.VariantKey}-{buildId}",
                buildId,
                source.Definition.VariantId,
                entries);
            variantPlans.Add(new ChannelStoreVariantPlan
            {
                Definition = source.Definition,
                SourceGameRootPath = source.GameRootPath,
                TargetGameRootPath = targetRoot,
                SourceLauncherRootPath = source.LauncherRootPath,
                TargetLauncherRootPath = targetLauncherRoot,
                TargetXStarterPath = targetXStarterPath,
                OverlayRootPath = overlayRoot,
                Manifest = manifest,
                UsesExistingTarget = false,
            });
        }

        imports = imports
            .GroupBy(item => item.DestinationPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.DestinationPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var plan = new ChannelStoreBuildPlan
        {
            CanExecute = true,
            StoreRootPath = storeRoot,
            SharedContentRootPath = sharedRoot,
            Imports = Array.AsReadOnly(imports.ToArray()),
            Variants = Array.AsReadOnly(variantPlans.ToArray()),
            HardLinkBytes = imports
                .Where(item => item.Action is ChannelStoreImportAction.CreateHardLink)
                .Sum(item => item.Length),
            CopyBytes = imports
                .Where(item => item.Action is ChannelStoreImportAction.CopyFile)
                .Sum(item => item.Length),
        };
        return plan with { PlanSha256 = ComputePlanSha256(plan) };
    }

    private static IEnumerable<SourceFile> EnumerateSourceFiles(
        ChannelSource source,
        CancellationToken cancellationToken)
    {
        var files = new List<SourceFile>();
        var directories = new Stack<string>();
        directories.Push(source.GameRootPath);
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source.GameRootPath, path);
                if (IsExcludedTopLevelGamePath(relative))
                {
                    continue;
                }

                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ChannelStoreException(
                        ChannelStoreFailureCode.ReparsePointRejected,
                        $"Source contains a reparse point: {path}.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Push(path);
                    continue;
                }

                var classification = Classify(relative);
                if (classification is VariantFileClassification.VariantMutable)
                {
                    continue;
                }

                var info = new FileInfo(path);
                files.Add(new SourceFile(
                    0,
                    source.Definition.VariantId,
                    path,
                    relative,
                    info.Length,
                    string.Empty,
                    classification));
            }
        }

        return files;
    }

    private static bool IsExcludedTopLevelGamePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return string.Equals(normalized, "DownloadCache", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("DownloadCache/", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "NikkiGallery", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("NikkiGallery/", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<RuntimeFile> EnumerateRuntimeFiles(
        ChannelSource source,
        CancellationToken cancellationToken)
    {
        var files = new List<RuntimeFile>();
        foreach (var path in Directory.EnumerateFiles(source.LauncherRootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(
                    Path.GetFileName(path),
                    "uninst.exe",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddRuntimeFile(source, path, files);
        }

        var directories = new Stack<string>();
        directories.Push(source.VersionDirectoryPath);
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ChannelStoreException(
                        ChannelStoreFailureCode.ReparsePointRejected,
                        $"Runtime source contains a reparse point: {path}.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Push(path);
                    continue;
                }

                AddRuntimeFile(source, path, files);
            }
        }

        return files;
    }

    private static void AddRuntimeFile(
        ChannelSource source,
        string path,
        ICollection<RuntimeFile> files)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ChannelStoreException(
                ChannelStoreFailureCode.ReparsePointRejected,
                $"Runtime source contains a reparse point: {path}.");
        }

        var info = new FileInfo(path);
        files.Add(new RuntimeFile(
            source.Definition.VariantId,
            path,
            Path.GetRelativePath(source.LauncherRootPath, path),
            info.Length,
            string.Empty));
    }

    private static HashSet<int> ResolveSharedFiles(IReadOnlyList<SourceFile> files)
    {
        var shared = new HashSet<int>();
        foreach (var group in files
                     .Where(file => file.Classification is
                         VariantFileClassification.SharedImmutable or
                         VariantFileClassification.OptionalResource)
                     .GroupBy(file => (file.Length, file.Sha256), FileIdentityComparer.Instance))
        {
            var variants = group
                .Select(file => file.VariantId)
                .Distinct()
                .ToArray();
            if (variants.Length < 2)
            {
                continue;
            }

            foreach (var variant in variants)
            {
                var selected = group
                    .Where(file => file.VariantId == variant)
                    .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .First();
                shared.Add(selected.Id);
            }
        }

        return shared;
    }

    private static VariantFileClassification Classify(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        if (string.Equals(normalized, "product.db", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "productVersion.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "InfinityNikki.exe", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/AntiCheatExpert/", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "X6Game-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "X6Game-Win64-Shipping_backup.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "HotUpdateBuildVersion.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "PaperHotUpdateProfile.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "BackupProfile.json", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "$VersionStrMap.bin", StringComparison.OrdinalIgnoreCase))
        {
            return VariantFileClassification.VariantExclusive;
        }

        if (normalized.Contains("VOICE_", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("X6Game/Content/Movies/", StringComparison.OrdinalIgnoreCase))
        {
            return VariantFileClassification.OptionalResource;
        }

        if (normalized.StartsWith("X6Game/Saved/Paks/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("X6Game/Saved/FastPatchPaks/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("X6Game/Content/Paks/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("X6Game/Plugins/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("X6Game/Content/CustomConfigs/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("X6Game/Samples/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Engine/", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("Engine/Saved/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("X6Game/Binaries/Win64/", StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return VariantFileClassification.SharedImmutable;
        }

        if (normalized.StartsWith("X6Game/Saved/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("Engine/Saved/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(".quality/", StringComparison.OrdinalIgnoreCase))
        {
            return VariantFileClassification.VariantMutable;
        }

        return VariantFileClassification.VariantExclusive;
    }

    private static ChannelStoreImportItem CreateImport(
        string sourcePath,
        string destinationPath,
        long length,
        string sha256,
        bool sharedContent,
        string storeRoot)
    {
        var action = File.Exists(destinationPath)
            ? ChannelStoreImportAction.KeepVerifiedFile
            : CanHardLink(sourcePath, storeRoot)
                ? ChannelStoreImportAction.CreateHardLink
                : ChannelStoreImportAction.CopyFile;
        return new ChannelStoreImportItem
        {
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            Action = action,
            Length = length,
            Sha256 = sha256,
            SharedContent = sharedContent,
        };
    }

    private static async Task<bool> ImportAsync(
        ChannelStoreImportItem item,
        CancellationToken cancellationToken)
    {
        var sourceVerification = await VerifyFileAsync(
                item.SourcePath,
                item.Length,
                item.Sha256,
                cancellationToken)
            .ConfigureAwait(false);
        if (!sourceVerification)
        {
            throw new ChannelStoreException(
                ChannelStoreFailureCode.SourceChanged,
                $"Source identity changed: {item.SourcePath}.");
        }

        if (File.Exists(item.DestinationPath))
        {
            var destinationVerified = await VerifyFileAsync(
                    item.DestinationPath,
                    item.Length,
                    item.Sha256,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!destinationVerified ||
                item.SharedContent && !WindowsHardLinkIdentity.AreSameFile(
                    item.SourcePath,
                    item.DestinationPath))
            {
                throw new ChannelStoreException(
                    ChannelStoreFailureCode.ImportConflict,
                    $"Existing import destination conflicts: {item.DestinationPath}.");
            }

            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(item.DestinationPath)!);
        var temporaryPath = item.DestinationPath + ".nikkiward-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (item.Action is ChannelStoreImportAction.CreateHardLink)
            {
                if (!WindowsHardLink.TryCreate(
                        temporaryPath,
                        item.SourcePath,
                        out var hardLinkError))
                {
                    throw new ChannelStoreException(
                        ChannelStoreFailureCode.ImportFailed,
                        $"CreateHardLink failed with Win32 error {hardLinkError}.");
                }
            }
            else
            {
                await CopyFileAsync(item.SourcePath, temporaryPath, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!await VerifyFileAsync(temporaryPath, item.Length, item.Sha256, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new ChannelStoreException(
                    ChannelStoreFailureCode.ImportFailed,
                    $"Imported file verification failed: {item.DestinationPath}.");
            }

            File.Move(temporaryPath, item.DestinationPath);
            return true;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task PersistArtifactsAsync(
        ChannelStoreBuildPlan plan,
        ChannelStoreBuildReceipt receipt,
        CancellationToken cancellationToken)
    {
        var manifestRoot = Path.Combine(plan.StoreRootPath, "manifests");
        var receiptRoot = Path.Combine(plan.StoreRootPath, "receipts");
        Directory.CreateDirectory(manifestRoot);
        Directory.CreateDirectory(receiptRoot);
        foreach (var variant in plan.Variants)
        {
            await WriteJsonAtomicallyAsync(
                    Path.Combine(manifestRoot, variant.Definition.VariantKey + ".json"),
                    variant.Manifest,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await WriteJsonAtomicallyAsync(
                Path.Combine(receiptRoot, receipt.ReceiptId + ".json"),
                receipt,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, ReceiptJsonOptions);
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task<bool> RollbackCreatedStateAsync(
        IReadOnlyList<ChannelStoreVariantReceipt> variants,
        IReadOnlyList<string> importedFiles,
        CancellationToken cancellationToken)
    {
        var succeeded = true;
        foreach (var variant in variants.Reverse())
        {
            var rollback = await materializer.RollbackAsync(
                    variant.Materialization.RollbackPlan,
                    cancellationToken)
                .ConfigureAwait(false);
            succeeded &= rollback.Succeeded;
        }

        foreach (var path in importedFiles.Reverse())
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                succeeded = false;
            }
        }

        return succeeded;
    }

    private static string ComputePlanSha256(ChannelStoreBuildPlan plan)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("storeRootPath", plan.StoreRootPath);
        writer.WriteString("sharedContentRootPath", plan.SharedContentRootPath);
        writer.WriteStartArray("imports");
        foreach (var item in plan.Imports.OrderBy(item => item.DestinationPath, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteStartObject();
            writer.WriteString("sourcePath", item.SourcePath);
            writer.WriteString("destinationPath", item.DestinationPath);
            writer.WriteNumber("action", (int)item.Action);
            writer.WriteNumber("length", item.Length);
            writer.WriteString("sha256", item.Sha256);
            writer.WriteBoolean("sharedContent", item.SharedContent);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("variants");
        foreach (var variant in plan.Variants.OrderBy(item => item.Definition.VariantKey, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("variantKey", variant.Definition.VariantKey);
            writer.WriteString("sourceGameRootPath", variant.SourceGameRootPath);
            writer.WriteString("targetGameRootPath", variant.TargetGameRootPath);
            writer.WriteString("sourceLauncherRootPath", variant.SourceLauncherRootPath);
            writer.WriteString("targetLauncherRootPath", variant.TargetLauncherRootPath);
            writer.WriteString("targetXStarterPath", variant.TargetXStarterPath);
            writer.WriteString("overlayRootPath", variant.OverlayRootPath);
            writer.WriteString("manifestContentSha256", variant.Manifest.ContentSha256);
            writer.WriteBoolean("usesExistingTarget", variant.UsesExistingTarget);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await source.CopyToAsync(destination, 1024 * 1024, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
    }

    private static async Task<bool> VerifyFileAsync(
        string path,
        long expectedLength,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != expectedLength)
        {
            return false;
        }

        return string.Equals(
            await ComputeFileSha256Async(path, cancellationToken).ConfigureAwait(false),
            expectedSha256,
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeFileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static bool TryNormalizeStoreRoot(
        string path,
        out string normalized,
        out string error)
    {
        normalized = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Store root is required.";
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Directory.GetParent(normalized);
            if (parent is null || !Directory.Exists(parent.FullName))
            {
                error = "Store root parent does not exist.";
                return false;
            }

            if (File.Exists(normalized))
            {
                error = "Store root is occupied by a file.";
                return false;
            }

            var drive = new DriveInfo(Path.GetPathRoot(normalized)!);
            if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                error = "Store root must be on NTFS.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            error = ex.GetType().Name;
            return false;
        }
    }

    private static bool CanHardLink(string sourcePath, string storeRoot)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
        var targetRoot = Path.GetPathRoot(Path.GetFullPath(storeRoot));
        return string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string SharedObjectRelativePath(string sha256) =>
        Path.Combine("objects", sha256[..2], sha256);

    private static int SourcePriority(GameVariantId variantId) => variantId switch
    {
        GameVariantId.GlobalSteam => 0,
        GameVariantId.MainlandOfficial => 1,
        GameVariantId.MainlandBilibili => 2,
        _ => 3,
    };

    private static ChannelSource CreateSource(InstallationProfileCandidate candidate)
    {
        var definition = candidate.Identity.DistributionChannel switch
        {
            DistributionChannel.Official => VariantDefinitionCatalog.MainlandOfficial,
            DistributionChannel.Bilibili => VariantDefinitionCatalog.MainlandBilibili,
            DistributionChannel.Steam => VariantDefinitionCatalog.GlobalSteam,
            _ => throw new ChannelStoreException(
                ChannelStoreFailureCode.InvalidRequest,
                "Unsupported channel candidate."),
        };
        if (candidate.Profile is null ||
            string.IsNullOrWhiteSpace(candidate.GameRootPath) ||
            string.IsNullOrWhiteSpace(candidate.LauncherRootPath) ||
            string.IsNullOrWhiteSpace(candidate.Profile.XStarterPath))
        {
            throw new ChannelStoreException(
                ChannelStoreFailureCode.InvalidSourceRoot,
                $"Channel runtime paths are incomplete: {definition.VariantKey}.");
        }

        var gameRoot = Path.GetFullPath(candidate.GameRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var launcherRoot = Path.GetFullPath(candidate.LauncherRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var xstarterPath = Path.GetFullPath(candidate.Profile.XStarterPath);
        var versionDirectory = Directory.GetParent(xstarterPath);
        var versionParent = versionDirectory?.Parent?.FullName
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (versionDirectory is null ||
            !string.Equals(Path.GetFileName(xstarterPath), "xstarter.exe", StringComparison.OrdinalIgnoreCase) ||
            !VersionDirectoryPattern.IsMatch(versionDirectory.Name) ||
            !string.Equals(versionParent, launcherRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ChannelStoreException(
                ChannelStoreFailureCode.InvalidSourceRoot,
                $"Channel xstarter is not bound to a direct version directory: {definition.VariantKey}.");
        }

        return new ChannelSource(
            candidate,
            definition,
            gameRoot,
            launcherRoot,
            versionDirectory.FullName,
            xstarterPath);
    }

    private static ChannelStoreBuildPlan Rejected(ChannelStoreFailureCode code, string? detail) => new()
    {
        FailureCode = code,
        FailureDetail = detail,
    };

    private static ChannelStoreBuildReceipt FailedReceipt(
        string receiptId,
        ChannelStoreBuildPlan plan,
        ChannelStoreFailureCode code,
        string detail,
        DateTimeOffset startedAtUtc) => new()
        {
            ReceiptId = receiptId,
            FailureCode = code,
            FailureDetail = detail,
            StoreRootPath = plan.StoreRootPath,
            PlanSha256 = plan.PlanSha256,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };

    private sealed record ChannelSource(
        InstallationProfileCandidate Candidate,
        VariantDefinition Definition,
        string GameRootPath,
        string LauncherRootPath,
        string VersionDirectoryPath,
        string XStarterPath);

    private sealed record RuntimeFile(
        GameVariantId VariantId,
        string SourcePath,
        string RelativePath,
        long Length,
        string Sha256);

    private sealed record SourceFile(
        int Id,
        GameVariantId VariantId,
        string SourcePath,
        string RelativePath,
        long Length,
        string Sha256,
        VariantFileClassification Classification);

    private sealed class FileIdentityComparer : IEqualityComparer<(long Length, string Sha256)>
    {
        public static FileIdentityComparer Instance { get; } = new();

        public bool Equals(
            (long Length, string Sha256) x,
            (long Length, string Sha256) y) =>
            x.Length == y.Length &&
            string.Equals(x.Sha256, y.Sha256, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((long Length, string Sha256) obj) =>
            HashCode.Combine(obj.Length, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Sha256));
    }

    private sealed class ChannelStoreException : Exception
    {
        public ChannelStoreException(ChannelStoreFailureCode code, string message)
            : base(message)
        {
            Code = code;
        }

        public ChannelStoreFailureCode Code { get; }
    }
}
