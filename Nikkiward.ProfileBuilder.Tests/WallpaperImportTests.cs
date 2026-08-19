using System.Text;
using Nikkiward.Features.Background;

internal static class WallpaperImportTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("Wallpaper PKGV scene packages resolve their preview card", PkgSceneResolvesPreviewCard),
        ("Standalone Wallpaper PKGV packages resolve without project metadata", StandalonePkgResolvesRuntime),
        ("Wallpaper video projects route to the existing motion pipeline", VideoProjectRoutesToMotion),
        ("Wallpaper motion mode does not fall back to a still preview", MotionModeRejectsPreviewOnlyVideo),
        ("Wallpaper package parser rejects invalid indexes and paths", PackageParserRejectsInvalidIndexesAndPaths),
        ("Wallpaper import modes reject non-playable package types", ImportModesRejectNonPlayablePackages),
        ("Wallpaper package cache retains the runtime scene layout", PackageCacheRetainsRuntimeLayout),
        ("Wallpaper package cache backfills missing companions after a canonical hit", PackageCacheBackfillsMissingCompanions),
    ];

    private static Task PkgSceneResolvesPreviewCard()
    {
        using var fixture = new WallpaperFixture();
        fixture.WriteFile("preview.jpg", [0xFF, 0xD8, 0xFF, 0xD9]);
        fixture.WriteProject("""
            { "type": "scene", "file": "scene.json", "preview": "preview.jpg" }
            """);
        var package = fixture.WritePackage(
            "scene.pkg",
            "PKGV0001",
            [("scene.json", new byte[] { 0x7B, 0x7D })]);

        var inspected = WallpaperPackageReader.Inspect(package);
        Assert(inspected.IsUsable, inspected.RejectReason ?? "scene package should be valid");
        AssertEqual("PKGV0001", inspected.Version, "package version");
        AssertEqual(1, inspected.Entries.Count, "package entry count");
        AssertEqual(WallpaperPackageType.Scene, inspected.ProjectType, "project type");
        AssertEqual(
            Path.Combine(fixture.RootPath, "preview.jpg"),
            inspected.PreviewPath,
            "scene preview path");

        var card = WallpaperSourceRules.Resolve(
            package,
            WallpaperImportMode.HolographicCard);
        Assert(card.IsUsable, card.RejectReason ?? "scene card import should resolve");
        AssertEqual(WallpaperResolvedKind.WallpaperEnginePackage, card.Kind, "scene card kind");
        AssertEqual(package, card.SourcePath, "scene card package source");
        AssertEqual(inspected.PreviewPath, card.PreviewPath, "scene card preview source");

        var motion = WallpaperSourceRules.Resolve(
            package,
            WallpaperImportMode.MotionBackdrop);
        Assert(motion.IsUsable, "scene package should resolve to the runtime path");
        AssertEqual(WallpaperResolvedKind.WallpaperEnginePackage, motion.Kind, "scene motion kind");
        AssertEqual(package, motion.SourcePath, "scene motion package source");
        return Task.CompletedTask;
    }

    private static Task StandalonePkgResolvesRuntime()
    {
        using var fixture = new WallpaperFixture();
        var package = fixture.WritePackage(
            "standalone.pkg",
            "PKGV0023",
            [("scene.json", new byte[] { 0x7B, 0x7D })]);

        var inspected = WallpaperPackageReader.Inspect(package);
        Assert(inspected.IsUsable, inspected.RejectReason ?? "standalone package should be valid");
        AssertEqual(WallpaperPackageType.Unknown, inspected.ProjectType, "standalone package metadata type");
        Assert(inspected.PreviewPath is null, "standalone package should not invent a preview");

        foreach (var mode in new[]
        {
            WallpaperImportMode.HolographicCard,
            WallpaperImportMode.MotionBackdrop,
        })
        {
            var resolution = WallpaperSourceRules.Resolve(package, mode);
            Assert(resolution.IsUsable, resolution.RejectReason ?? "standalone package should resolve");
            AssertEqual(
                WallpaperResolvedKind.WallpaperEnginePackage,
                resolution.Kind,
                $"standalone package kind ({mode})");
            AssertEqual(package, resolution.SourcePath, $"standalone package source ({mode})");
            AssertEqual(WallpaperPackageType.Unknown, resolution.PackageType, $"standalone package type ({mode})");
        }

        return Task.CompletedTask;
    }

    private static Task VideoProjectRoutesToMotion()
    {
        using var fixture = new WallpaperFixture();
        fixture.WriteFile("preview.jpg", [0xFF, 0xD8, 0xFF, 0xD9]);
        fixture.WriteFile("loop.mp4", [0x00, 0x00, 0x00, 0x18]);
        var project = fixture.WriteProject("""
            { "type": "Video", "file": "loop.mp4", "preview": "preview.jpg" }
            """);

        var motion = WallpaperSourceRules.Resolve(
            project,
            WallpaperImportMode.MotionBackdrop);
        Assert(motion.IsUsable, motion.RejectReason ?? "video project should resolve");
        AssertEqual(WallpaperResolvedKind.Motion, motion.Kind, "video project motion kind");
        AssertEqual(Path.Combine(fixture.RootPath, "loop.mp4"), motion.SourcePath, "video project source");

        var card = WallpaperSourceRules.Resolve(
            project,
            WallpaperImportMode.HolographicCard);
        Assert(card.IsUsable, card.RejectReason ?? "video card preview should resolve");
        AssertEqual(WallpaperResolvedKind.Still, card.Kind, "video card preview kind");
        AssertEqual(Path.Combine(fixture.RootPath, "preview.jpg"), card.SourcePath, "video card preview source");
        return Task.CompletedTask;
    }

    private static Task PackageParserRejectsInvalidIndexesAndPaths()
    {
        using var fixture = new WallpaperFixture();
        fixture.WriteProject("""
            { "type": "scene", "file": "scene.json", "preview": "../outside.jpg" }
            """);
        var traversal = fixture.WritePackage(
            "traversal.pkg",
            "PKGV0001",
            [("../scene.json", new byte[] { 0x7B })]);
        Assert(
            !WallpaperPackageReader.Inspect(traversal).IsUsable,
            "path traversal in a package index must be rejected");

        var unknownVersion = fixture.WritePackage(
            "unknown.pkg",
            "PKGV0025",
            [("scene.json", new byte[] { 0x7B })]);
        Assert(
            !WallpaperPackageReader.Inspect(unknownVersion).IsUsable,
            "unknown PKGV versions must fail closed");

        var overrun = fixture.WritePackage(
            "overrun.pkg",
            "PKGV0001",
            [("scene.json", new byte[] { 0x7B })],
            offsetOverride: uint.MaxValue);
        Assert(
            !WallpaperPackageReader.Inspect(overrun).IsUsable,
            "out-of-range package entries must be rejected");

        var safePackage = fixture.WritePackage(
            "safe.pkg",
            "PKGV0001",
            [("scene.json", new byte[] { 0x7B })]);
        var resolution = WallpaperSourceRules.Resolve(
            safePackage,
            WallpaperImportMode.HolographicCard);
        Assert(resolution.IsUsable, "a valid package may run without an optional preview");
        Assert(resolution.PreviewPath is null, "preview paths escaping the project root must be omitted");
        return Task.CompletedTask;
    }

    private static Task MotionModeRejectsPreviewOnlyVideo()
    {
        using var fixture = new WallpaperFixture();
        fixture.WriteFile("preview.jpg", [0xFF, 0xD8, 0xFF, 0xD9]);
        var project = fixture.WriteProject(
            """
            { "type": "video", "file": "missing.mp4", "preview": "preview.jpg" }
            """);

        var motion = WallpaperSourceRules.Resolve(
            project,
            WallpaperImportMode.MotionBackdrop);
        Assert(!motion.IsUsable, "motion mode must reject a video without media");
        Assert(
            motion.RejectReason?.Contains("动态播放", StringComparison.Ordinal) == true,
            "motion rejection should explain the missing playback media");

        var card = WallpaperSourceRules.Resolve(
            project,
            WallpaperImportMode.HolographicCard);
        Assert(card.IsUsable, card.RejectReason ?? "card mode should use the preview");
        AssertEqual(WallpaperResolvedKind.Still, card.Kind, "preview-only card kind");
        return Task.CompletedTask;
    }

    private static Task ImportModesRejectNonPlayablePackages()
    {
        using var fixture = new WallpaperFixture();
        fixture.WriteFile("preview.jpg", [0xFF, 0xD8, 0xFF, 0xD9]);
        var webProject = fixture.WriteProject("""
            { "type": "web", "file": "index.html", "preview": "preview.jpg" }
            """);
        fixture.WriteFile("index.html", Encoding.UTF8.GetBytes("<html></html>"));

        var card = WallpaperSourceRules.Resolve(
            webProject,
            WallpaperImportMode.HolographicCard);
        Assert(card.IsUsable, card.RejectReason ?? "web project preview should be usable as a card");
        AssertEqual(WallpaperResolvedKind.Still, card.Kind, "web card kind");

        var motion = WallpaperSourceRules.Resolve(
            webProject,
            WallpaperImportMode.MotionBackdrop);
        Assert(!motion.IsUsable, "web files must not execute as dynamic wallpaper");

        var mobile = fixture.WriteFile("mobile.mpkg", [0x01]);
        var mobileResult = WallpaperSourceRules.Resolve(
            mobile,
            WallpaperImportMode.HolographicCard);
        Assert(!mobileResult.IsUsable, "mobile package files must remain distinct from desktop pkg files");
        return Task.CompletedTask;
    }

    private static async Task PackageCacheRetainsRuntimeLayout()
    {
        using var fixture = new WallpaperFixture();
        fixture.WriteFile("preview.jpg", [0xFF, 0xD8, 0xFF, 0xD9]);
        fixture.WriteProject(
            """
            { "type": "scene", "file": "scene.json", "preview": "preview.jpg" }
            """);
        var package = fixture.WritePackage(
            "renamed-source.pkg",
            "PKGV0023",
            [("scene.json", new byte[] { 0x7B, 0x7D })]);

        var cache = new WallpaperPackageCache(
            Path.Combine(fixture.RootPath, "cache"));
        var imported = await cache.ImportAsync(package);
        Assert(imported.Validation.IsUsable, imported.Validation.RejectReason ?? "package import failed");
        Assert(imported.ImportedPath is not null, "package cache should return a path");
        AssertEqual("scene.pkg", Path.GetFileName(imported.ImportedPath), "runtime package filename");
        var importedDirectory = Path.GetDirectoryName(imported.ImportedPath)!;
        Assert(
            File.Exists(Path.Combine(importedDirectory, "project.json")),
            "runtime project metadata should be copied");
        Assert(
            File.Exists(Path.Combine(importedDirectory, "preview.jpg")),
            "runtime preview should be copied");

        var repeated = await cache.ImportAsync(package);
        AssertEqual(imported.ImportedPath, repeated.ImportedPath, "package cache should deduplicate");
    }

    private static async Task PackageCacheBackfillsMissingCompanions()
    {
        using var fixture = new WallpaperFixture();
        var package = fixture.WritePackage(
            "canonical.pkg",
            "PKGV0023",
            [("scene.json", new byte[] { 0x7B, 0x7D })]);
        var cache = new WallpaperPackageCache(
            Path.Combine(fixture.RootPath, "cache"));

        var initial = await cache.ImportAsync(package);
        Assert(initial.Validation.IsUsable, initial.Validation.RejectReason ?? "initial package import failed");
        var importedDirectory = Path.GetDirectoryName(initial.ImportedPath)!;
        var cachedProject = Path.Combine(importedDirectory, "project.json");
        var cachedPreview = Path.Combine(importedDirectory, "preview.jpg");
        Assert(!File.Exists(cachedProject), "the first import should have no companion metadata");
        Assert(!File.Exists(cachedPreview), "the first import should have no companion preview");

        const string originalProject =
            "{ \"type\": \"scene\", \"file\": \"scene.json\", \"preview\": \"preview.jpg\" }";
        var originalPreview = new byte[] { 0xFF, 0xD8, 0x01, 0xD9 };
        fixture.WriteProject(originalProject);
        fixture.WriteFile("preview.jpg", originalPreview);

        var backfilled = await cache.ImportAsync(package);
        Assert(backfilled.Validation.IsUsable, backfilled.Validation.RejectReason ?? "companion backfill failed");
        AssertEqual(initial.ImportedPath, backfilled.ImportedPath, "canonical package path must remain stable");
        AssertEqual(originalProject, File.ReadAllText(cachedProject), "project metadata should be backfilled");
        Assert(File.ReadAllBytes(cachedPreview).SequenceEqual(originalPreview), "preview should be backfilled");

        fixture.WriteProject(
            "{ \"type\": \"scene\", \"file\": \"scene.json\", \"preview\": \"preview.jpg\", \"updated\": true }");
        var updatedPreview = new byte[] { 0xFF, 0xD8, 0x02, 0xD9 };
        fixture.WriteFile("preview.jpg", updatedPreview);
        File.Delete(cachedPreview);

        var repaired = await cache.ImportAsync(package);
        Assert(repaired.Validation.IsUsable, repaired.Validation.RejectReason ?? "missing preview repair failed");
        AssertEqual(originalProject, File.ReadAllText(cachedProject), "existing project metadata must not be overwritten");
        Assert(File.ReadAllBytes(cachedPreview).SequenceEqual(updatedPreview), "a missing preview should be refreshed from the selected source");
    }

    private sealed class WallpaperFixture : IDisposable
    {
        public WallpaperFixture()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                $"nikkiward-wallpaper-import-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string WriteProject(string json) =>
            WriteFile("project.json", Encoding.UTF8.GetBytes(json));

        public string WriteFile(string relativePath, byte[] bytes)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public string WritePackage(
            string fileName,
            string version,
            IReadOnlyList<(string Name, byte[] Bytes)> entries,
            uint? offsetOverride = null)
        {
            var path = Path.Combine(RootPath, fileName);
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
            writer.Write(8U);
            writer.Write(Encoding.ASCII.GetBytes(version));
            writer.Write((uint)entries.Count);
            uint offset = 0;
            foreach (var entry in entries)
            {
                var name = Encoding.UTF8.GetBytes(entry.Name);
                writer.Write((uint)name.Length);
                writer.Write(name);
                writer.Write(offsetOverride ?? offset);
                writer.Write((uint)entry.Bytes.Length);
                checked
                {
                    offset += (uint)entry.Bytes.Length;
                }
            }

            foreach (var entry in entries)
            {
                writer.Write(entry.Bytes);
            }

            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message}: expected '{expected}', actual '{actual}'.");
        }
    }
}
