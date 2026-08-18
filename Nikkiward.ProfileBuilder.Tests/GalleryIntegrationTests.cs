using System.Security.Cryptography;
using Nikkiward.Features.Gallery;

internal static class GalleryIntegrationTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("gallery stars persist and can be removed", TestPersistenceAndRemoval),
        ("gallery stars stay isolated by profile", TestProfileIsolation),
        ("gallery star paths normalize case and separators", TestPathNormalization),
        ("gallery fallback scope is stable for an equivalent root", TestFallbackScopeStability),
        ("default favorite manifest contains the requested five images", TestDefaultFavoriteManifest),
        ("default favorites seed once and preserve removal", TestDefaultFavoritesSeedOnce),
        ("favorite protection copies and deduplicates original bytes", TestFavoriteProtectionCopyAndDeduplication),
        ("favorite protection survives a missing original", TestFavoriteProtectionMissingOriginal),
        ("favorite protection detects a changed original and corrupt object", TestFavoriteProtectionIntegrity),
        ("favorite protection cleanup removes only unstarred objects", TestFavoriteProtectionCleanup),
        ("favorite protection preferences preserve prior storage roots", TestFavoriteProtectionPreferences),
        ("favorite protection reads leave an absent store untouched", TestFavoriteProtectionReadDoesNotCreateStore),
        ("favorite protection reports an unavailable store instead of an empty store", TestFavoriteProtectionUnavailableStore),
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

    private static Task TestDefaultFavoriteManifest()
    {
        var expected = new Dictionary<string, (long Length, string Hash)>(StringComparer.OrdinalIgnoreCase)
        {
            ["01.jpg"] = (338679, "21093DD12A21385F76AD57819FF1EB2A80AF751579CB50EAF3C598BC0768F902"),
            ["02.jpg"] = (126923, "0FC974EE740B09D5E620F2AC34EB23126D56E8E957422BC580CB35C5AADBBB22"),
            ["03.jpg"] = (838353, "79E98642EC260C9CA8F4A89A12D8294B0474B78658DAB6DE330BFCB192514880"),
            ["04.jpg"] = (766431, "C2ADB227F963C6C46F98874A04027E8169DEEF425AE87FC8437BA810F68E275D"),
            ["05.jpg"] = (365308, "EC0C9FFE241C771256CE4B8500079850DC4FCCECA9F42B8F2D644A12DD672072"),
        };

        AssertEqual(expected.Count, GalleryDefaultFavoriteSeedService.AssetManifest.Count, "default asset count");
        foreach (var asset in GalleryDefaultFavoriteSeedService.AssetManifest)
        {
            Assert(expected.TryGetValue(asset.FileName, out var expectedAsset), "default asset file name");
            AssertEqual(expectedAsset.Hash, asset.Sha256, $"default asset hash for {asset.FileName}");
        }

        var assetRoot = Path.Combine(FindWorkspaceRoot(), "Nikkiward", "Assets", "DefaultFavorites");
        var files = Directory.GetFiles(assetRoot, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AssertEqual(expected.Count, files.Length, "packaged default asset file count");
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            Assert(expected.TryGetValue(name, out var expectedAsset), $"unexpected default asset {name}");
            var bytes = File.ReadAllBytes(path);
            AssertEqual(expectedAsset.Length, bytes.LongLength, $"default asset length for {name}");
            Assert(
                bytes.Length >= 4 &&
                bytes[0] == 0xFF &&
                bytes[1] == 0xD8 &&
                bytes[2] == 0xFF,
                $"default asset JPEG signature for {name}");
            AssertEqual(
                expectedAsset.Hash,
                Convert.ToHexString(SHA256.HashData(bytes)),
                $"default asset file hash for {name}");
        }

        return Task.CompletedTask;
    }

    private static async Task TestDefaultFavoritesSeedOnce()
    {
        using var fixture = new TempFixture();
        var assetsRoot = Path.Combine(fixture.Root, "assets");
        var localRoot = Path.Combine(fixture.Root, "local");
        Directory.CreateDirectory(assetsRoot);
        var firstBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22, 0xFF, 0xD9 };
        var secondBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x33, 0x44, 0xFF, 0xD9 };
        await File.WriteAllBytesAsync(Path.Combine(assetsRoot, "first.jpg"), firstBytes);
        await File.WriteAllBytesAsync(Path.Combine(assetsRoot, "second.jpg"), secondBytes);
        var assets = new[]
        {
            new GalleryDefaultFavoriteAsset("first.jpg", Convert.ToHexString(SHA256.HashData(firstBytes))),
            new GalleryDefaultFavoriteAsset("second.jpg", Convert.ToHexString(SHA256.HashData(secondBytes))),
        };
        var annotationStore = new GalleryAnnotationStore(localRoot);
        var service = new GalleryDefaultFavoriteSeedService(localRoot, assetsRoot, assets);

        var firstLoad = await service.EnsureSeededAsync(annotationStore);

        AssertEqual(2, firstLoad.Count, "initial default favorites");
        Assert(File.Exists(service.SeedMarkerFilePath), "default seed marker");
        Assert(firstLoad.All(item => File.Exists(item.FilePath)), "initial local copies");
        Assert(firstLoad.All(item => item.FilePath.StartsWith(localRoot, StringComparison.OrdinalIgnoreCase)), "local data destination");
        Assert(!Directory.EnumerateFiles(assetsRoot).Any(path => path.EndsWith(".seed", StringComparison.OrdinalIgnoreCase)), "asset directory remains read only");

        var removed = firstLoad[0];
        await annotationStore.SetStarredAsync(
            GalleryDefaultFavoriteSeedService.ScopeId,
            removed.RelativePath,
            isStarred: false);
        var reloadedService = new GalleryDefaultFavoriteSeedService(localRoot, assetsRoot, assets);
        var secondLoad = await reloadedService.EnsureSeededAsync(annotationStore);

        AssertEqual(1, secondLoad.Count, "default favorite after removal");
        Assert(
            !secondLoad.Any(item => string.Equals(
                item.RelativePath,
                removed.RelativePath,
                StringComparison.OrdinalIgnoreCase)),
            "removed default favorite must stay removed");
        var persistedStars = await annotationStore.LoadStarredAsync(
            GalleryDefaultFavoriteSeedService.ScopeId);
        AssertEqual(1, persistedStars.Count, "persisted default star count");

        File.Delete(secondLoad[0].FilePath);
        var thirdLoad = await new GalleryDefaultFavoriteSeedService(localRoot, assetsRoot, assets)
            .EnsureSeededAsync(annotationStore);
        AssertEqual(1, thirdLoad.Count, "default favorite after local copy recovery");
        Assert(File.Exists(thirdLoad[0].FilePath), "starred local copy recovery");
        AssertEqual(
            assets.Single(asset => string.Equals(
                asset.FileName,
                Path.GetFileName(thirdLoad[0].FilePath),
                StringComparison.OrdinalIgnoreCase)).Sha256,
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(thirdLoad[0].FilePath))),
            "recovered local copy hash");
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

    private static async Task TestFavoriteProtectionReadDoesNotCreateStore()
    {
        using var fixture = new TempFixture();
        var root = Path.Combine(fixture.Root, "absent-protected-store");
        var store = new GalleryFavoriteProtectionStore(root);

        AssertEqual(0, store.GetEntries().Count, "absent store entries");
        AssertEqual(0, store.GetEntries("profile:main").Count, "absent profile entries");
        Assert(store.GetEntry("profile:main", "missing.jpeg") is null, "absent store entry");
        Assert(
            await store.VerifyAsync("profile:main", "missing.jpeg") is null,
            "absent store single verification");
        AssertEqual(0, (await store.VerifyAsync()).Count, "absent store verification");
        AssertEqual(0, (await store.GetStatisticsAsync()).EntryCount, "absent store statistics");

        var cleanup = await store.CleanUnstarredAsync(
            "profile:main",
            Array.Empty<string>());
        AssertEqual(0, cleanup.RemovedEntryCount, "absent store cleanup entries");
        AssertEqual(0, cleanup.RemovedObjectCount, "absent store cleanup objects");
        Assert(!Directory.Exists(root), "read-only operations must not create the store root");
    }

    private static async Task TestFavoriteProtectionUnavailableStore()
    {
        using var fixture = new TempFixture();
        var picturesRoot = Path.Combine(fixture.Root, "pictures");
        var parent = Path.Combine(picturesRoot, "Nikkiward");
        Directory.CreateDirectory(parent);
        var unavailableRoot = Path.Combine(parent, "ProtectedFavorites");
        await File.WriteAllTextAsync(unavailableRoot, "not-a-directory");

        var store = new GalleryFavoriteProtectionStore(unavailableRoot);
        var rejected = false;
        try
        {
            _ = store.GetEntries();
        }
        catch (IOException)
        {
            rejected = true;
        }

        Assert(rejected, "a file at the protection root must not be reported as an empty store");

        var service = new GalleryFavoriteProtectionService(
            Path.Combine(fixture.Root, "local"),
            picturesRoot);
        var overview = await service.GetOverviewAsync(verify: false);
        AssertEqual(0, overview.Statistics.EntryCount, "unavailable root readable entries");
        AssertEqual(1, overview.UnavailableRootPaths.Count, "unavailable root count");
        AssertEqual(
            unavailableRoot,
            overview.UnavailableRootPaths[0],
            "unavailable root identity");
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

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nikkiward", "Nikkiward.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The Nikkiward workspace root was not found.");
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
