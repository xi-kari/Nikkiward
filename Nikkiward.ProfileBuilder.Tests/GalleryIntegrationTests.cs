using Nikkiward.Features.Gallery;

internal static class GalleryIntegrationTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("gallery stars persist and can be removed", TestPersistenceAndRemoval),
        ("gallery stars stay isolated by profile", TestProfileIsolation),
        ("gallery star paths normalize case and separators", TestPathNormalization),
        ("gallery fallback scope is stable for an equivalent root", TestFallbackScopeStability),
        ("favorite protection copies and deduplicates original bytes", TestFavoriteProtectionCopyAndDeduplication),
        ("favorite protection survives a missing original", TestFavoriteProtectionMissingOriginal),
        ("favorite protection detects a changed original and corrupt object", TestFavoriteProtectionIntegrity),
        ("favorite protection cleanup removes only unstarred objects", TestFavoriteProtectionCleanup),
        ("favorite protection preferences preserve prior storage roots", TestFavoriteProtectionPreferences),
        ("disabled favorite protection does not copy a new favorite", TestFavoriteProtectionDisabled),
        ("NikkiGallery registration detects executable changes", TestNikkiGalleryTamperDetection),
        ("NikkiGallery disconnect keeps the external executable", TestNikkiGalleryDisconnect),
    ];

    private static async Task TestPersistenceAndRemoval()
    {
        using var fixture = new TempFixture();
        var firstStore = new GalleryAnnotationStore(fixture.Root);

        await firstStore.SetStarredAsync("profile:main", @"ScreenShot\Photo01.JPG", true);

        var reloaded = await new GalleryAnnotationStore(fixture.Root)
            .LoadStarredAsync("profile:main");
        AssertEqual(1, reloaded.Count, "persisted star count");
        Assert(reloaded.Contains(@"SCREENSHOT\PHOTO01.JPG"), "persisted normalized path");

        await firstStore.SetStarredAsync("profile:main", "screenshot/photo01.jpg", false);

        var removed = await new GalleryAnnotationStore(fixture.Root)
            .LoadStarredAsync("profile:main");
        AssertEqual(0, removed.Count, "removed star count");
    }

    private static async Task TestProfileIsolation()
    {
        using var fixture = new TempFixture();
        var store = new GalleryAnnotationStore(fixture.Root);

        await store.SetStarredAsync("profile:first", "photo.jpg", true);

        var first = await store.LoadStarredAsync("profile:first");
        var second = await store.LoadStarredAsync("profile:second");
        AssertEqual(1, first.Count, "first profile star count");
        AssertEqual(0, second.Count, "second profile star count");
    }

    private static Task TestPathNormalization()
    {
        var normalizedWindows = GalleryAnnotationStore.NormalizeRelativePath(
            @"\ScreenShot\Folder\Photo.JpG");
        var normalizedPortable = GalleryAnnotationStore.NormalizeRelativePath(
            "/screenshot/folder/photo.jpg");

        AssertEqual(normalizedWindows, normalizedPortable, "normalized relative path");
        AssertEqual(@"SCREENSHOT\FOLDER\PHOTO.JPG", normalizedWindows, "canonical path");
        return Task.CompletedTask;
    }

    private static Task TestFallbackScopeStability()
    {
        using var fixture = new TempFixture();
        var first = GalleryAnnotationStore.CreateScopeId(null, fixture.Root);
        var equivalent = GalleryAnnotationStore.CreateScopeId(
            null,
            fixture.Root + Path.DirectorySeparatorChar);
        var different = GalleryAnnotationStore.CreateScopeId(
            null,
            Path.Combine(fixture.Root, "other"));

        AssertEqual(first, equivalent, "equivalent root scope");
        Assert(!string.Equals(first, different, StringComparison.Ordinal), "different roots need different scopes");
        return Task.CompletedTask;
    }

    private static async Task TestFavoriteProtectionCopyAndDeduplication()
    {
        using var fixture = new TempFixture();
        var sourceDirectory = Path.Combine(fixture.Root, "source");
        Directory.CreateDirectory(sourceDirectory);
        var firstPath = Path.Combine(sourceDirectory, "first.jpeg");
        var secondPath = Path.Combine(sourceDirectory, "second.jpeg");
        var bytes = Enumerable.Range(0, 2048).Select(index => (byte)(index % 251)).ToArray();
        await File.WriteAllBytesAsync(firstPath, bytes);
        await File.WriteAllBytesAsync(secondPath, bytes);
        var store = new GalleryFavoriteProtectionStore(Path.Combine(fixture.Root, "protected"));

        var first = await store.ProtectAsync("profile:main", "A/first.jpeg", firstPath);
        var second = await store.ProtectAsync("profile:main", "B/second.jpeg", secondPath);

        AssertEqual(first.Sha256, second.Sha256, "deduplicated hash");
        Assert(store.TryResolveProtectedPath(first.ScopeId, first.RelativePath, out var objectPath), "protected path");
        var protectedBytes = await File.ReadAllBytesAsync(objectPath);
        Assert(bytes.SequenceEqual(protectedBytes), "protected bytes");
        var statistics = await store.GetStatisticsAsync();
        AssertEqual(2, statistics.EntryCount, "protected entry count");
        AssertEqual(1, statistics.UniqueObjectCount, "unique protected object count");
        AssertEqual((long)bytes.Length, statistics.ProtectedBytes, "deduplicated protected bytes");
    }

    private static async Task TestFavoriteProtectionMissingOriginal()
    {
        using var fixture = new TempFixture();
        var sourcePath = Path.Combine(fixture.Root, "missing.jpeg");
        await File.WriteAllBytesAsync(sourcePath, Enumerable.Repeat((byte)0x51, 512).ToArray());
        var store = new GalleryFavoriteProtectionStore(Path.Combine(fixture.Root, "protected"));
        var protectedEntry = await store.ProtectAsync(
            "profile:main",
            "ScreenShot/missing.jpeg",
            sourcePath);

        File.Delete(sourcePath);
        var verified = await store.VerifyAsync(protectedEntry.ScopeId, protectedEntry.RelativePath);

        AssertEqual(
            GalleryFavoriteProtectionStatus.OriginalMissing,
            verified?.Status,
            "missing original status");
        Assert(
            store.TryResolveProtectedPath(
                protectedEntry.ScopeId,
                protectedEntry.RelativePath,
                out var protectedPath),
            "missing original protected path");
        Assert(File.Exists(protectedPath), "missing original protected object");
    }

    private static async Task TestFavoriteProtectionIntegrity()
    {
        using var fixture = new TempFixture();
        var sourcePath = Path.Combine(fixture.Root, "integrity.jpeg");
        await File.WriteAllBytesAsync(sourcePath, Enumerable.Repeat((byte)0x61, 768).ToArray());
        var store = new GalleryFavoriteProtectionStore(Path.Combine(fixture.Root, "protected"));
        var entry = await store.ProtectAsync("profile:main", "integrity.jpeg", sourcePath);

        await File.WriteAllBytesAsync(sourcePath, Enumerable.Repeat((byte)0x62, 768).ToArray());
        var changed = await store.VerifyAsync(entry.ScopeId, entry.RelativePath);
        AssertEqual(
            GalleryFavoriteProtectionStatus.OriginalChanged,
            changed?.Status,
            "changed original status");

        Assert(store.TryResolveProtectedPath(entry.ScopeId, entry.RelativePath, out var objectPath), "object before corruption");
        await File.WriteAllBytesAsync(objectPath, Enumerable.Repeat((byte)0x63, 768).ToArray());
        var corrupt = await store.VerifyAsync(entry.ScopeId, entry.RelativePath);
        AssertEqual(
            GalleryFavoriteProtectionStatus.ObjectCorrupt,
            corrupt?.Status,
            "corrupt object status");
        Assert(
            !store.TryResolveProtectedPath(entry.ScopeId, entry.RelativePath, out _),
            "corrupt object must not resolve");
    }

    private static async Task TestFavoriteProtectionCleanup()
    {
        using var fixture = new TempFixture();
        var firstPath = Path.Combine(fixture.Root, "first.jpeg");
        var secondPath = Path.Combine(fixture.Root, "second.jpeg");
        var bytes = Enumerable.Repeat((byte)0x71, 1024).ToArray();
        await File.WriteAllBytesAsync(firstPath, bytes);
        await File.WriteAllBytesAsync(secondPath, bytes);
        var store = new GalleryFavoriteProtectionStore(Path.Combine(fixture.Root, "protected"));
        await store.ProtectAsync("profile:main", "first.jpeg", firstPath);
        await store.ProtectAsync("profile:main", "second.jpeg", secondPath);

        var firstCleanup = await store.CleanUnstarredAsync(
            "profile:main",
            ["second.jpeg"]);
        AssertEqual(1, firstCleanup.RemovedEntryCount, "first cleanup entry count");
        AssertEqual(0, firstCleanup.RemovedObjectCount, "shared object must remain");

        var secondCleanup = await store.CleanUnstarredAsync(
            "profile:main",
            Array.Empty<string>());
        AssertEqual(1, secondCleanup.RemovedEntryCount, "second cleanup entry count");
        AssertEqual(1, secondCleanup.RemovedObjectCount, "unreferenced object count");
        AssertEqual((long)bytes.Length, secondCleanup.ReclaimedBytes, "reclaimed bytes");
    }

    private static async Task TestFavoriteProtectionPreferences()
    {
        using var fixture = new TempFixture();
        var localRoot = Path.Combine(fixture.Root, "local");
        var picturesRoot = Path.Combine(fixture.Root, "pictures");
        var service = new GalleryFavoriteProtectionService(localRoot, picturesRoot);
        var defaults = await service.GetPreferencesAsync();
        var expectedDefaultRoot = Path.Combine(
            picturesRoot,
            "Nikkiward",
            "ProtectedFavorites");
        Assert(defaults.IsEnabled, "favorite protection default enabled");
        AssertEqual(expectedDefaultRoot, defaults.ActiveRootPath, "default protection root");

        var sourcePath = Path.Combine(fixture.Root, "protected.jpeg");
        await File.WriteAllBytesAsync(sourcePath, Enumerable.Repeat((byte)0x81, 640).ToArray());
        var protectedResult = await service.ProtectAsync(
            "profile:main",
            "protected.jpeg",
            sourcePath);
        Assert(protectedResult.Entry is not null, "service protection entry");

        var replacementRoot = Path.Combine(fixture.Root, "replacement");
        var updated = await service.SetActiveRootAsync(replacementRoot);
        AssertEqual(replacementRoot, updated.ActiveRootPath, "replacement protection root");
        Assert(
            updated.KnownRootPaths.Contains(expectedDefaultRoot, StringComparer.OrdinalIgnoreCase),
            "previous protection root retained");

        File.Delete(sourcePath);
        var recovered = await service.GetProtectedFavoritesAsync(
            "profile:main",
            ["protected.jpeg"]);
        AssertEqual(1, recovered.Count, "protected favorite across prior root");
        Assert(recovered[0].IsUsingProtectedCopy, "protected fallback state");
        Assert(File.Exists(recovered[0].ProtectedPath), "protected fallback file");
    }

    private static async Task TestFavoriteProtectionDisabled()
    {
        using var fixture = new TempFixture();
        var service = new GalleryFavoriteProtectionService(
            Path.Combine(fixture.Root, "local"),
            Path.Combine(fixture.Root, "pictures"));
        await service.SetEnabledAsync(false);
        var sourcePath = Path.Combine(fixture.Root, "disabled.jpeg");
        await File.WriteAllBytesAsync(sourcePath, Enumerable.Repeat((byte)0x91, 512).ToArray());

        var result = await service.ProtectAsync(
            "profile:main",
            "disabled.jpeg",
            sourcePath);

        Assert(!result.IsEnabled, "disabled protection result");
        Assert(result.Entry is null, "disabled protection entry");
        Assert(result.ProtectedPath is null, "disabled protection path");
    }

    private static async Task TestNikkiGalleryTamperDetection()
    {
        using var fixture = new TempFixture();
        var executablePath = Path.Combine(fixture.Root, "NikkiGallery.exe");
        await File.WriteAllBytesAsync(
            executablePath,
            Enumerable.Repeat((byte)0x31, 256).ToArray());
        var registry = new NikkiGalleryToolRegistry(fixture.Root);

        var registered = await registry.RegisterAsync(executablePath);
        Assert(registered.IsRegistered, "registered state");
        Assert(registered.IsAvailable, "registered availability");
        Assert(File.Exists(registry.AssociationFilePath), "association manifest");

        await File.AppendAllTextAsync(executablePath, "changed");
        var changed = await new NikkiGalleryToolRegistry(fixture.Root).GetStateAsync();
        Assert(changed.IsRegistered, "changed executable remains associated");
        Assert(!changed.IsAvailable, "changed executable must not be launched");
        Assert(
            changed.StatusText.Contains("发生变化", StringComparison.Ordinal),
            "changed executable status");
    }

    private static async Task TestNikkiGalleryDisconnect()
    {
        using var fixture = new TempFixture();
        var executablePath = Path.Combine(fixture.Root, "NikkiGallery.exe");
        await File.WriteAllBytesAsync(
            executablePath,
            Enumerable.Repeat((byte)0x42, 256).ToArray());
        var registry = new NikkiGalleryToolRegistry(fixture.Root);
        await registry.RegisterAsync(executablePath);

        await registry.DisconnectAsync();

        var disconnected = await registry.GetStateAsync();
        Assert(!disconnected.IsRegistered, "disconnected state");
        Assert(!disconnected.IsAvailable, "disconnected availability");
        Assert(File.Exists(executablePath), "disconnect must not delete the external executable");
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

    private sealed class TempFixture : IDisposable
    {
        public TempFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"nikkiward-gallery-integration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
