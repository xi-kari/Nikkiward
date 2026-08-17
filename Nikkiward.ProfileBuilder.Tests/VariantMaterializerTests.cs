using System.Diagnostics;
using System.Security.Cryptography;
using Nikkiward.Models;
using Nikkiward.Services;

internal static class VariantMaterializerTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("variant catalog freezes the three channel identities", CatalogDefinesThreeChannels),
        ("variant manifest digest detects entry mutation", ManifestDigestDetectsMutation),
        ("variant manifest rejects target traversal", ManifestRejectsTraversal),
        ("variant manifest rejects file-directory target collisions", ManifestRejectsHierarchyCollision),
        ("variant dry-run plans hard links and copies without writes", DryRunPlansWithoutWriting),
        ("variant materializer creates verified files and rolls them back", MaterializesAndRollsBack),
        ("variant materializer supports NTFS hard links beyond MAX_PATH", MaterializesLongHardLinkAndRollsBack),
        ("variant materializer automatically rolls back a source race", MaterializerRollsBackSourceRace),
        ("variant planner rejects overlapping roots", PlannerRejectsOverlappingRoots),
        ("variant planner rejects a target reparse path", PlannerRejectsTargetReparsePath),
        ("variant planner rejects conflicting target content", PlannerRejectsTargetConflict),
        ("variant planner rejects a content-equal shared copy", PlannerRejectsUnlinkedSharedCopy),
        ("variant planner rejects a read-only shared source", PlannerRejectsReadOnlySharedSource),
        ("variant planner rejects cross-volume hard links", PlannerRejectsCrossVolumeHardLink),
        ("variant rollback refuses a changed created file", RollbackRefusesChangedFile),
    ];

    public static async Task RunAll()
    {
        foreach (var test in All)
        {
            await test.Run().ConfigureAwait(false);
        }
    }

    private static Task CatalogDefinesThreeChannels()
    {
        AssertEqual(3, VariantDefinitionCatalog.All.Count, "variant count");
        AssertEqual(
            "InfinityNikki Launcher",
            VariantDefinitionCatalog.MainlandOfficial.ProductMarkerName,
            "official marker");
        AssertEqual(
            "InfinityNikkiBili Launcher",
            VariantDefinitionCatalog.MainlandBilibili.ProductMarkerName,
            "Bilibili marker");
        AssertEqual(
            "InfinityNikkiSteam Launcher",
            VariantDefinitionCatalog.GlobalSteam.ProductMarkerName,
            "Steam marker");
        AssertEqual(
            VariantLaunchAuthority.SteamClient,
            VariantDefinitionCatalog.GlobalSteam.LaunchAuthority,
            "Steam authority");
        return Task.CompletedTask;
    }

    private static Task ManifestDigestDetectsMutation()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var manifest = VariantManifestFactory.Freeze(
            "digest-test",
            "2828",
            GameVariantId.MainlandOfficial,
            [SharedEntry("shared.bin", "game/shared.bin", bytes)]);
        Assert(VariantManifestValidator.Validate(manifest).IsValid, "frozen manifest should validate");

        var tampered = manifest with
        {
            Entries =
            [
                manifest.Entries[0] with { Length = manifest.Entries[0].Length + 1 },
            ],
        };
        var validation = VariantManifestValidator.Validate(tampered);
        Assert(!validation.IsValid, "entry mutation must invalidate the digest");
        Assert(
            validation.Errors.Any(error => error.Contains("ContentSha256", StringComparison.Ordinal)),
            "digest mismatch should be explicit");
        return Task.CompletedTask;
    }

    private static Task ManifestRejectsTraversal()
    {
        var bytes = new byte[] { 9 };
        AssertThrows<ArgumentException>(() => VariantManifestFactory.Freeze(
            "traversal-test",
            "2828",
            GameVariantId.MainlandOfficial,
            [SharedEntry("shared.bin", "..\\escaped.bin", bytes)]));
        return Task.CompletedTask;
    }

    private static Task ManifestRejectsHierarchyCollision()
    {
        var bytes = new byte[] { 7 };
        AssertThrows<ArgumentException>(() => VariantManifestFactory.Freeze(
            "hierarchy-test",
            "2828",
            GameVariantId.MainlandOfficial,
            [
                SharedEntry("first.bin", "a", bytes),
                SharedEntry("second.bin", "a-b", bytes),
                SharedEntry("third.bin", "a/child.bin", bytes),
            ]));
        return Task.CompletedTask;
    }

    private static async Task DryRunPlansWithoutWriting()
    {
        using var fixture = VariantFixture.Create();
        var request = fixture.CreateRequest();
        var before = Directory.EnumerateFileSystemEntries(fixture.TargetRoot).ToArray();

        var plan = await new VariantMaterializationPlanner().CreatePlanAsync(request)
            .ConfigureAwait(false);

        Assert(plan.CanExecute, $"plan should execute: {plan.FailureCode} {plan.FailureDetail}");
        Assert(VariantHashForTest.IsSha256(plan.PlanSha256), "plan digest");
        AssertEqual(1, plan.Items.Count(item => item.Action is VariantPlanAction.CreateHardLink), "hard-link count");
        AssertEqual(2, plan.Items.Count(item => item.Action is VariantPlanAction.CopyFile), "copy count");
        AssertEqual(
            1,
            plan.Items.Count(item => item.Action is VariantPlanAction.SkipMissingOptionalResource),
            "missing optional count");
        AssertEqual(1, plan.Items.Count(item => item.Action is VariantPlanAction.ConfirmAbsent), "absent count");
        AssertEqual(0, before.Length, "target starts empty");
        AssertEqual(
            0,
            Directory.EnumerateFileSystemEntries(fixture.TargetRoot).Count(),
            "dry-run must not write the target");
    }

    private static async Task MaterializesAndRollsBack()
    {
        using var fixture = VariantFixture.Create();
        var request = fixture.CreateRequest();
        var sharedAttributesBefore = File.GetAttributes(fixture.SharedFile);
        var planner = new VariantMaterializationPlanner();
        var plan = await planner.CreatePlanAsync(request).ConfigureAwait(false);
        Assert(plan.CanExecute, $"plan should execute: {plan.FailureDetail}");

        var materializer = new WindowsVariantMaterializer(planner);
        var receipt = await materializer.MaterializeAsync(request, plan.PlanSha256)
            .ConfigureAwait(false);

        Assert(receipt.Succeeded, $"materialization failed: {receipt.FailureCode} {receipt.FailureDetail}");
        AssertEqual(3, receipt.Files.Count(file => file.Created), "created file count");
        var sharedTarget = Path.Combine(fixture.TargetRoot, "game", "shared.bin");
        var exclusiveTarget = Path.Combine(fixture.TargetRoot, "launcher", "channel.bin");
        var mutableTarget = Path.Combine(fixture.TargetRoot, "z-saved", "state.bin");
        Assert(File.Exists(sharedTarget), "shared target should exist");
        Assert(File.Exists(exclusiveTarget), "exclusive target should exist");
        Assert(File.Exists(mutableTarget), "mutable target should exist");
        Assert(
            WindowsHardLinkIdentity.AreSameFile(fixture.SharedFile, sharedTarget),
            "shared target should be the source hard link");
        Assert(
            !WindowsHardLinkIdentity.AreSameFile(fixture.ExclusiveFile, exclusiveTarget),
            "variant-exclusive target must be copied");
        Assert(
            receipt.Files.Single(file => file.TargetPath == sharedTarget).HardLinkIdentityVerified,
            "receipt should attest the hard-link identity");

        var resumedPlan = await planner.CreatePlanAsync(request).ConfigureAwait(false);
        Assert(resumedPlan.CanExecute, "an already materialized target should remain valid");
        AssertEqual(
            VariantPlanAction.KeepVerifiedFile,
            resumedPlan.Items.Single(item => item.TargetPath == sharedTarget).Action,
            "existing shared hard link should be retained");

        var rollback = await materializer.RollbackAsync(receipt.RollbackPlan).ConfigureAwait(false);
        Assert(rollback.Succeeded, $"rollback failed: {rollback.FailureDetail}");
        AssertEqual(3, rollback.DeletedFileCount, "rollback file count");
        AssertEqual(
            0,
            Directory.EnumerateFileSystemEntries(fixture.TargetRoot).Count(),
            "rollback should restore the empty target root");
        Assert(File.Exists(fixture.SharedFile), "rollback must not delete the shared source");
        Assert(File.Exists(fixture.ExclusiveFile), "rollback must not delete the overlay source");
        AssertEqual(
            sharedAttributesBefore,
            File.GetAttributes(fixture.SharedFile),
            "rollback must preserve shared source attributes");
    }

    private static async Task MaterializesLongHardLinkAndRollsBack()
    {
        using var fixture = VariantFixture.Create();
        var bytes = File.ReadAllBytes(fixture.SharedFile);
        var targetRelativePath = string.Join(
            '/',
            Enumerable.Range(0, 7).Select(index => $"long-path-segment-{index:D2}-{new string('x', 20)}")
                .Append("shared.bin"));
        var manifest = VariantManifestFactory.Freeze(
            "long-path-test",
            "2828",
            GameVariantId.MainlandOfficial,
            [SharedEntry("shared.bin", targetRelativePath, bytes)]);
        var request = fixture.CreateRequest() with { Manifest = manifest };
        var planner = new VariantMaterializationPlanner();
        var plan = await planner.CreatePlanAsync(request).ConfigureAwait(false);
        Assert(plan.CanExecute, $"long-path plan should execute: {plan.FailureDetail}");

        var targetPath = Path.Combine(
            fixture.TargetRoot,
            targetRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert(targetPath.Length > 260, "long-path target must exceed MAX_PATH");

        var materializer = new WindowsVariantMaterializer(planner);
        var receipt = await materializer.MaterializeAsync(request, plan.PlanSha256)
            .ConfigureAwait(false);

        Assert(receipt.Succeeded, $"long-path materialization failed: {receipt.FailureCode} {receipt.FailureDetail}");
        Assert(File.Exists(targetPath), "long-path hard link should exist");
        Assert(
            WindowsHardLinkIdentity.AreSameFile(fixture.SharedFile, targetPath),
            "long-path target should share the source file identity");
        Assert(
            receipt.Files.Single(file => file.TargetPath == targetPath).HardLinkIdentityVerified,
            "long-path receipt should attest the hard-link identity");

        var rollback = await materializer.RollbackAsync(receipt.RollbackPlan).ConfigureAwait(false);
        Assert(rollback.Succeeded, $"long-path rollback failed: {rollback.FailureDetail}");
        AssertEqual(1, rollback.DeletedFileCount, "long-path rollback file count");
        Assert(!File.Exists(targetPath), "long-path rollback should remove the target");
        Assert(File.Exists(fixture.SharedFile), "long-path rollback must preserve the source");
        AssertEqual(
            0,
            Directory.EnumerateFileSystemEntries(fixture.TargetRoot).Count(),
            "long-path rollback should restore the empty target root");
    }

    private static async Task MaterializerRollsBackSourceRace()
    {
        using var fixture = VariantFixture.Create();
        var request = fixture.CreateRequest();
        var planner = new VariantMaterializationPlanner();
        var approvedPlan = await planner.CreatePlanAsync(request).ConfigureAwait(false);
        Assert(approvedPlan.CanExecute, "approved plan should execute");

        var racingPlanner = new SourceRacePlanner(planner, fixture.ExclusiveFile);
        var receipt = await new WindowsVariantMaterializer(racingPlanner)
            .MaterializeAsync(request, approvedPlan.PlanSha256)
            .ConfigureAwait(false);

        Assert(!receipt.Succeeded, "source race must fail materialization");
        AssertEqual(
            VariantMaterializationFailureCode.SourceChanged,
            receipt.FailureCode,
            "source race failure code");
        Assert(receipt.AutomaticRollbackAttempted, "source race should trigger automatic rollback");
        Assert(receipt.AutomaticRollbackSucceeded, "automatic rollback should succeed");
        AssertEqual(
            0,
            Directory.EnumerateFileSystemEntries(fixture.TargetRoot).Count(),
            "automatic rollback should restore the empty target root");
        Assert(File.Exists(fixture.SharedFile), "automatic rollback must preserve the shared source");
    }

    private static async Task PlannerRejectsOverlappingRoots()
    {
        using var fixture = VariantFixture.Create();
        var request = fixture.CreateRequest() with { TargetRootPath = fixture.SharedRoot };
        var plan = await new VariantMaterializationPlanner().CreatePlanAsync(request)
            .ConfigureAwait(false);
        Assert(!plan.CanExecute, "overlapping roots must fail");
        AssertEqual(VariantPlanFailureCode.RootOverlap, plan.FailureCode, "overlap failure code");
    }

    private static async Task PlannerRejectsTargetReparsePath()
    {
        using var fixture = VariantFixture.Create();
        var linkTarget = Path.Combine(fixture.Root, "link-target");
        var linkPath = Path.Combine(fixture.TargetRoot, "linked");
        Directory.CreateDirectory(linkTarget);
        CreateDirectoryReparsePoint(linkPath, linkTarget);
        try
        {
            var bytes = File.ReadAllBytes(fixture.SharedFile);
            var manifest = VariantManifestFactory.Freeze(
                "reparse-test",
                "2828",
                GameVariantId.MainlandOfficial,
                [SharedEntry("shared.bin", "linked/shared.bin", bytes)]);
            var plan = await new VariantMaterializationPlanner().CreatePlanAsync(
                    fixture.CreateRequest() with { Manifest = manifest })
                .ConfigureAwait(false);
            Assert(!plan.CanExecute, "a target reparse path must fail");
            AssertEqual(
                VariantPlanFailureCode.ReparsePointRejected,
                plan.FailureCode,
                "target reparse failure code");
            AssertEqual(
                0,
                Directory.EnumerateFileSystemEntries(linkTarget).Count(),
                "planner must not write through the reparse path");
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }
        }
    }

    private static async Task PlannerRejectsTargetConflict()
    {
        using var fixture = VariantFixture.Create();
        var conflictingPath = Path.Combine(fixture.TargetRoot, "game", "shared.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(conflictingPath)!);
        File.WriteAllText(conflictingPath, "wrong-content");

        var plan = await new VariantMaterializationPlanner()
            .CreatePlanAsync(fixture.CreateRequest())
            .ConfigureAwait(false);
        Assert(!plan.CanExecute, "conflicting target must fail");
        AssertEqual(VariantPlanFailureCode.TargetConflict, plan.FailureCode, "target conflict code");
        AssertEqual("wrong-content", File.ReadAllText(conflictingPath), "planner must not alter conflicts");
    }

    private static async Task PlannerRejectsUnlinkedSharedCopy()
    {
        using var fixture = VariantFixture.Create();
        var targetPath = Path.Combine(fixture.TargetRoot, "game", "shared.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(fixture.SharedFile, targetPath);

        var plan = await new VariantMaterializationPlanner()
            .CreatePlanAsync(fixture.CreateRequest())
            .ConfigureAwait(false);
        Assert(!plan.CanExecute, "a content-equal copy must not satisfy shared physical identity");
        AssertEqual(VariantPlanFailureCode.TargetConflict, plan.FailureCode, "unlinked copy failure code");
        Assert(
            !WindowsHardLinkIdentity.AreSameFile(fixture.SharedFile, targetPath),
            "fixture target should be a separate file identity");
    }

    private static async Task PlannerRejectsReadOnlySharedSource()
    {
        using var fixture = VariantFixture.Create();
        File.SetAttributes(fixture.SharedFile, File.GetAttributes(fixture.SharedFile) | FileAttributes.ReadOnly);

        var plan = await new VariantMaterializationPlanner()
            .CreatePlanAsync(fixture.CreateRequest())
            .ConfigureAwait(false);
        Assert(!plan.CanExecute, "a read-only shared source must fail closed");
        AssertEqual(
            VariantPlanFailureCode.UnsafeSourceAttributes,
            plan.FailureCode,
            "read-only source failure code");
    }

    private static async Task PlannerRejectsCrossVolumeHardLink()
    {
        var firstBase = Path.GetTempPath();
        var secondBase = AppContext.BaseDirectory;
        if (string.Equals(
                Path.GetPathRoot(firstBase),
                Path.GetPathRoot(secondBase),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var id = Guid.NewGuid().ToString("N");
        var firstRoot = Path.Combine(firstBase, "NikkiwardCrossVolume", id);
        var secondRoot = Path.Combine(secondBase, "NikkiwardCrossVolume", id);
        try
        {
            var sharedRoot = Path.Combine(firstRoot, "shared");
            var overlayRoot = Path.Combine(secondRoot, "overlay");
            var targetRoot = Path.Combine(secondRoot, "target");
            Directory.CreateDirectory(sharedRoot);
            Directory.CreateDirectory(overlayRoot);
            Directory.CreateDirectory(targetRoot);
            var bytes = new byte[] { 6, 1, 8 };
            File.WriteAllBytes(Path.Combine(sharedRoot, "shared.bin"), bytes);
            var manifest = VariantManifestFactory.Freeze(
                "cross-volume-test",
                "2828",
                GameVariantId.MainlandOfficial,
                [SharedEntry("shared.bin", "game/shared.bin", bytes)]);
            var plan = await new VariantMaterializationPlanner().CreatePlanAsync(
                    new VariantMaterializationRequest
                    {
                        Definition = VariantDefinitionCatalog.MainlandOfficial,
                        Manifest = manifest,
                        SharedContentRootPath = sharedRoot,
                        VariantOverlayRootPath = overlayRoot,
                        TargetRootPath = targetRoot,
                    })
                .ConfigureAwait(false);

            Assert(!plan.CanExecute, "cross-volume hard link must fail");
            AssertEqual(
                VariantPlanFailureCode.CrossVolumeHardLink,
                plan.FailureCode,
                "cross-volume failure code");
        }
        finally
        {
            DeleteTree(firstRoot);
            DeleteTree(secondRoot);
        }
    }

    private static async Task RollbackRefusesChangedFile()
    {
        using var fixture = VariantFixture.Create();
        var request = fixture.CreateRequest();
        var planner = new VariantMaterializationPlanner();
        var plan = await planner.CreatePlanAsync(request).ConfigureAwait(false);
        var materializer = new WindowsVariantMaterializer(planner);
        var receipt = await materializer.MaterializeAsync(request, plan.PlanSha256)
            .ConfigureAwait(false);
        Assert(receipt.Succeeded, $"materialization failed: {receipt.FailureDetail}");

        var mutableTarget = Path.Combine(fixture.TargetRoot, "z-saved", "state.bin");
        File.WriteAllText(mutableTarget, "user-changed-state");
        var rollback = await materializer.RollbackAsync(receipt.RollbackPlan).ConfigureAwait(false);
        Assert(!rollback.Succeeded, "rollback must refuse a changed created file");
        Assert(File.Exists(mutableTarget), "changed file must remain in place");
        AssertEqual("user-changed-state", File.ReadAllText(mutableTarget), "changed content must remain intact");
    }

    private static VariantManifestEntry SharedEntry(
        string sourceRelativePath,
        string targetRelativePath,
        byte[] bytes) => new()
        {
            SourceRelativePath = sourceRelativePath,
            TargetRelativePath = targetRelativePath,
            Classification = VariantFileClassification.SharedImmutable,
            SourceKind = VariantSourceKind.SharedContent,
            Length = bytes.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
        };

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
        where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
        }
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void DeleteTree(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(root, recursive: true);
    }

    private static void CreateDirectoryReparsePoint(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c mklink /J \"{linkPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        Assert(process is not null, "junction helper should start");
        process!.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Junction creation failed: {process.StandardError.ReadToEnd()}");
        }
    }

    private sealed class VariantFixture : IDisposable
    {
        private VariantFixture(string root)
        {
            Root = root;
            SharedRoot = Path.Combine(root, "shared");
            OverlayRoot = Path.Combine(root, "overlay");
            TargetRoot = Path.Combine(root, "target");
            Directory.CreateDirectory(SharedRoot);
            Directory.CreateDirectory(OverlayRoot);
            Directory.CreateDirectory(TargetRoot);

            SharedFile = WriteFile(SharedRoot, "shared.bin", [1, 3, 3, 7]);
            ExclusiveFile = WriteFile(OverlayRoot, "channel.bin", [2, 8, 2, 8]);
            MutableFile = WriteFile(OverlayRoot, "state.bin", [4, 2, 4, 2]);
        }

        public string Root { get; }

        public string SharedRoot { get; }

        public string OverlayRoot { get; }

        public string TargetRoot { get; }

        public string SharedFile { get; }

        public string ExclusiveFile { get; }

        public string MutableFile { get; }

        public static VariantFixture Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "NikkiwardVariantMaterializerTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new VariantFixture(root);
        }

        public VariantMaterializationRequest CreateRequest()
        {
            var sharedBytes = File.ReadAllBytes(SharedFile);
            var exclusiveBytes = File.ReadAllBytes(ExclusiveFile);
            var mutableBytes = File.ReadAllBytes(MutableFile);
            var manifest = VariantManifestFactory.Freeze(
                "test-cn-official",
                "2828",
                GameVariantId.MainlandOfficial,
                [
                    SharedEntry("shared.bin", "game/shared.bin", sharedBytes),
                    new VariantManifestEntry
                    {
                        SourceRelativePath = "channel.bin",
                        TargetRelativePath = "launcher/channel.bin",
                        Classification = VariantFileClassification.VariantExclusive,
                        SourceKind = VariantSourceKind.VariantOverlay,
                        Length = exclusiveBytes.LongLength,
                        Sha256 = Convert.ToHexString(SHA256.HashData(exclusiveBytes)),
                    },
                    new VariantManifestEntry
                    {
                        SourceRelativePath = "state.bin",
                        TargetRelativePath = "z-saved/state.bin",
                        Classification = VariantFileClassification.VariantMutable,
                        SourceKind = VariantSourceKind.VariantOverlay,
                        Length = mutableBytes.LongLength,
                        Sha256 = Convert.ToHexString(SHA256.HashData(mutableBytes)),
                    },
                    new VariantManifestEntry
                    {
                        SourceRelativePath = "missing.bin",
                        TargetRelativePath = "optional/missing.bin",
                        Classification = VariantFileClassification.OptionalResource,
                        SourceKind = VariantSourceKind.SharedContent,
                        Length = 1,
                        Sha256 = Convert.ToHexString(SHA256.HashData([0xFF])),
                    },
                    new VariantManifestEntry
                    {
                        TargetRelativePath = "forbidden.dll",
                        Classification = VariantFileClassification.AbsentPath,
                        SourceKind = VariantSourceKind.None,
                    },
                ]);

            return new VariantMaterializationRequest
            {
                Definition = VariantDefinitionCatalog.MainlandOfficial,
                Manifest = manifest,
                SharedContentRootPath = SharedRoot,
                VariantOverlayRootPath = OverlayRoot,
                TargetRootPath = TargetRoot,
            };
        }

        public void Dispose()
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Root, recursive: true);
        }

        private static string WriteFile(string root, string relativePath, byte[] bytes)
        {
            var path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }
    }

    private sealed class SourceRacePlanner(
        IVariantMaterializationPlanner inner,
        string sourcePath) : IVariantMaterializationPlanner
    {
        public async Task<VariantMaterializationPlan> CreatePlanAsync(
            VariantMaterializationRequest request,
            CancellationToken cancellationToken = default)
        {
            var plan = await inner.CreatePlanAsync(request, cancellationToken).ConfigureAwait(false);
            File.WriteAllText(sourcePath, "changed-after-plan");
            return plan;
        }
    }

    private static class VariantHashForTest
    {
        public static bool IsSha256(string? value) =>
            value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }
}
