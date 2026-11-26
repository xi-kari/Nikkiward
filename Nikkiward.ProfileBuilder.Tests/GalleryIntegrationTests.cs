using System.Security.Cryptography;
using Nikkiward.Features.Gallery;

internal static class GalleryIntegrationTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("studio persists edits and share codes", TestStudioEdits),
        ("studio rejects protected export directories", TestStudioExports),
        ("studio actions use an explicit allowlist", TestStudioActions),
        ("studio image tickets are scoped and single use", TestStudioTickets),
        ("studio rejects leave original files untouched", TestStudioReject),
        ("gallery skips oversized screenshots before pixel decode", TestScreenshotPixelLimit),
        ("gallery serializes screenshot renders", TestScreenshotConcurrency),
        ("gallery caches screenshot thumbnails without changing originals", TestScreenshotCache),
        (nameof(TestScreenshotMemoryThumbnail), TestScreenshotMemoryThumbnail),
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
        ("filtered unstar preserves a hidden favorite's only copy", TestFilteredUnstar),
        ("unstar waits for confirmation before removing the last copy", TestUnstarConfirmation),
        ("unstar compares original content before release", TestUnstarOriginalContent),
        ("paused unstar and resumed cleanup protect the last copy", TestPausedUnstarCleanup),
        ("unstar consent names each protected version and paused root", TestUnstarConsentAcrossStores),
        ("restarring after pause retains the previous version for cleanup", TestRestarAfterPause),
        ("batch cleanup rechecks newly changed originals across stores", TestCleanupAcrossStores),
        ("legacy migration distinguishes unused roots from offline volumes", () => TestUnusedLegacyRoot(false)),
        ("legacy migration accepts an empty online object store", () => TestUnusedLegacyRoot(true)),
        ("same-profile galleries keep same-name favorites separate", TestGalleryRootIsolation),
        ("refresh defers object hashing until fallback", TestDeferredFallbackVerification),
        ("refresh opens each protection manifest once", TestManifestReadsPerRefresh),
        ("overlapping protection selections are rejected before probing", TestProtectionRootSelection),
        ("changing a gallery pauses an overlapping protection root", () => TestProtectionRootPause(false)),
        ("loading overlapping preferences leaves old copies untouched", () => TestProtectionRootPause(true)),
        ("legacy favorites require unambiguous original paths", TestLegacyFavoriteOwnership),
        ("conflicting legacy owners retain both manifests and objects", TestConflictingLegacyOwners),
        ("single unstar preserves shared and unrelated objects", TestReleaseSharedObject),
        ("failed multi-root unstar keeps the favorite recoverable", TestFailedUnstar),
        ("gallery and protection junction aliases are checked before writes", TestRootAliases),
        ("legacy drive-root galleries protect the entire drive", TestLegacyDriveRoot),
        ("offline legacy manifests defer ownership until reconnection", TestOfflineLegacyManifest),
        ("queued protection rechecks changed gallery directories", TestQueuedProtection),
        ("read-only manifests can serve a verified fallback", TestReadOnlyFallback),
        ("favorite protection preferences preserve prior storage roots", TestFavoriteProtectionPreferences),
        ("favorite protection reads leave an absent store untouched", TestFavoriteProtectionReadDoesNotCreateStore),
        ("favorite protection reports an unavailable store instead of an empty store", TestFavoriteProtectionUnavailableStore),
        ("disabled favorite protection does not copy a new favorite", TestFavoriteProtectionDisabled),
        ("NikkiGallery registration detects executable changes", TestNikkiGalleryTamperDetection),
        ("NikkiGallery disconnect keeps the external executable", TestNikkiGalleryDisconnect),
    ];

    private static Task TestStudioEdits()
    {
        using var fixture = new TempFixture();
        var store = new PhotoStudioStore(fixture.Root);
        var edits = System.Text.Json.JsonSerializer.SerializeToElement(new { exp = 30, codes = new[] { "搭配", "染色", "家园", "相机" } });
        store.Save("profile/photo", edits);
        AssertEqual(edits.GetRawText(), new PhotoStudioStore(fixture.Root).Load("profile/photo")!.Value.GetRawText(), "saved editor state");
        AssertEqual(true, store.Load("another/photo") == null, "isolated photo");
        return Task.CompletedTask;
    }

    private static Task TestStudioExports()
    {
        using var fixture = new TempFixture();
        var roots = new[] { Path.Combine(fixture.Root, "Gallery"), Path.Combine(fixture.Root, "Game") };
        foreach (var path in roots.SelectMany(p => new[] { p, Path.Combine(p, "Exports") }))
        {
            try { PhotoStudioStore.CheckDestination(path, roots); }
            catch (IOException) { continue; }
            throw new InvalidOperationException("Accepted protected directory: " + path);
        }
        PhotoStudioStore.CheckDestination(Path.Combine(fixture.Root, "Gallery-exports"), roots);
        return Task.CompletedTask;
    }

    private static Task TestStudioActions()
    {
        AssertEqual(true, StudioSession<string>.Allows("export"), "export");
        AssertEqual(false, StudioSession<string>.Allows("delete"), "delete");
        AssertEqual(false, StudioSession<string>.Allows("read-file"), "read-file");
        return Task.CompletedTask;
    }

    private static Task TestStudioTickets()
    {
        var session = new StudioSession<string>(["listed.jpg"]);
        var ticket = session.Issue(session.Photos.Keys.Single());
        AssertEqual(true, session.Consume("../outside.jpg") == null, "outside list");
        AssertEqual(true, new StudioSession<string>([]).Consume(ticket) == null, "another session");
        AssertEqual("listed.jpg", session.Consume(ticket), "issued photo");
        AssertEqual(true, session.Consume(ticket) == null, "spent ticket");
        return Task.CompletedTask;
    }

    private static Task TestStudioReject()
    {
        using var fixture = new TempFixture();
        var photo = Path.Combine(fixture.Root, "photo.jpg");
        File.WriteAllText(photo, "original");
        var stamp = File.GetLastWriteTimeUtc(photo);
        var store = new PhotoStudioStore(Path.Combine(fixture.Root, "app"));
        store.SetRejected(photo, true);
        AssertEqual(true, store.Rejected(photo), "marked reject");
        AssertEqual("original", File.ReadAllText(photo), "original intact");
        AssertEqual(stamp, File.GetLastWriteTimeUtc(photo), "original timestamp");
        return Task.CompletedTask;
    }

    private sealed class FakeDecoder(uint width, Action read) : GalleryScreenshotDecoder
    {
        public override uint Width => width;
        public override uint Height => 2;
        public override byte[] ReadPixels(CancellationToken token)
        {
            read();
            return [5, 6, 7, 8];
        }
        public override void Dispose() { }
    }

    private static async Task TestScreenshotPixelLimit()
    {
        using var fixture = new TempFixture();
        var path = Path.Combine(fixture.Root, "huge.avif");
        File.WriteAllBytes(path, [1]);
        var reads = 0;
        try
        {
            await GalleryScreenshotDecoder.RenderAsync(path, ".avif",
                (_, _) => new FakeDecoder(uint.MaxValue, () => reads++),
                (_, _, pixels) => Task.FromResult(pixels), default);
            throw new InvalidOperationException("oversized image accepted");
        }
        catch (InvalidDataException) { }
        AssertEqual(0, reads, "pixel reads");
    }

    private static async Task TestScreenshotConcurrency()
    {
        using var fixture = new TempFixture();
        var path = Path.Combine(fixture.Root, "small.jxl");
        File.WriteAllBytes(path, [1]);
        var active = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var renders = Enumerable.Range(0, 8).Select(_ => GalleryScreenshotDecoder.RenderAsync(
            path, ".jxl", (_, _) => new FakeDecoder(2, () => { }),
            async (_, _, pixels) =>
            {
                AssertEqual(1, Interlocked.Increment(ref active), "active codec renders");
                entered.TrySetResult();
                await release.Task;
                Interlocked.Decrement(ref active);
                return pixels;
            }, default)).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(100);
        release.SetResult();
        await Task.WhenAll(renders);
    }

    private static async Task TestScreenshotCache()
    {
        using var fixture = new TempFixture();
        var album = Directory.CreateDirectory(Path.Combine(fixture.Root, "album"));
        var cache = Directory.CreateDirectory(Path.Combine(fixture.Root, "cache"));
        var source = new FileInfo(Path.Combine(album.FullName, "photo.avif"));
        File.WriteAllBytes(source.FullName, [1, 2, 3]);
        var hash = SHA256.HashData(File.ReadAllBytes(source.FullName));
        var thumbnail = await GalleryThumbnailFile.GetOrCreateAsync(cache.FullName, album.FullName, source, allowMemory: true, async () =>
            await GalleryScreenshotDecoder.RenderAsync(source.FullName, ".avif",
                (_, _) => new FakeDecoder(2, () => { }),
                (_, _, pixels) => Task.FromResult(pixels), default), default);
        AssertEqual(cache.FullName, Path.GetDirectoryName(thumbnail!.FilePath), "thumbnail parent");
        AssertEqual(1, album.GetFiles().Length, "source folder file count");
        AssertEqual(1, cache.GetFiles().Length, "cache file count");
        Assert(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source.FullName))), "original hash");
    }

    private static async Task TestScreenshotMemoryThumbnail()
    {
        using var fixture = new TempFixture();
        var album = Directory.CreateDirectory(Path.Combine(fixture.Root, "album"));
        foreach (var extension in new[] { ".avif", ".jxl" })
        {
            var source = new FileInfo(Path.Combine(album.FullName, extension == ".jxl" ? "protected.bin" : "photo.avif"));
            File.WriteAllBytes(source.FullName, [1]);
            var reads = 0;
            var blocked = Path.Combine(fixture.Root, "file-in-place-of-cache");
            File.WriteAllBytes(blocked, []);
            foreach (var cache in new[] { Path.Combine(album.FullName, "cache"), blocked })
            {
                var result = await GalleryThumbnailFile.GetOrCreateAsync(cache, album.FullName, source,
                    GalleryFileTypes.UsesScreenshotCodec(extension), async () => await GalleryScreenshotDecoder.RenderAsync(
                        source.FullName, extension, (_, _) => new FakeDecoder(2, () => reads++),
                        (_, _, pixels) => Task.FromResult(pixels), default), default);
                Assert(result is { FilePath: null, Bytes: [5, 6, 7, 8] }, "memory thumbnail bytes");
            }
            AssertEqual(2, reads, "each memory thumbnail decodes once");
            AssertEqual(1, album.GetFileSystemInfos().Length, "only original in album");
            Assert(File.ReadAllBytes(source.FullName).SequenceEqual(new byte[] { 1 }), "original unchanged");
            File.Delete(source.FullName);
        }
    }

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

        var stars = new GalleryAnnotationStore(fixture.Root);
        await stars.SetStarredAsync("profile:main", "second.jpeg", true);
        var firstCleanup = await store.CleanUnstarredAsync("profile:main", stars);
        AssertEqual(1, firstCleanup.RemovedEntryCount, "first cleanup entry count");
        AssertEqual(0, firstCleanup.RemovedObjectCount, "shared object must remain");

        await stars.SetStarredAsync("profile:main", "second.jpeg", false);
        var secondCleanup = await store.CleanUnstarredAsync("profile:main", stars);
        AssertEqual(1, secondCleanup.RemovedEntryCount, "second cleanup entry count");
        AssertEqual(1, secondCleanup.RemovedObjectCount, "unreferenced object count");
        AssertEqual((long)bytes.Length, secondCleanup.ReclaimedBytes, "reclaimed bytes");
    }

    private static async Task TestFilteredUnstar()
    {
        using var fixture = new TempFixture();
        var stars = new GalleryAnnotationStore(fixture.Root);
        var service = fixture.CreateProtectionService();
        var scope = GalleryAnnotationStore.CreateScopeId("main", fixture.Album);
        var names = new[] { "visible.jpg", "hidden.jpg" };
        var objects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var path = Path.Combine(fixture.Album, name);
            await File.WriteAllTextAsync(path, name);
            await stars.SetStarredAsync(scope, name, true);
            objects[name] = (await service.ProtectAsync(scope, name, path)).ProtectedPath!;
        }

        File.Delete(Path.Combine(fixture.Album, "hidden.jpg"));
        var visible = (await stars.LoadStarredAsync(scope))
            .Where(name => name.StartsWith("visible", StringComparison.OrdinalIgnoreCase)).Single();
        Assert(await service.TryUnstarAsync(scope, visible), "release the selected photo");
        Assert(!File.Exists(objects[visible]), "selected object's last reference was released");
        var state = await service.LoadGalleryAsync("main", fixture.Album);
        Assert(state.StarredPaths.SetEquals(["hidden.jpg"]), "remaining persisted star");
        var recovered = state.ProtectedFavorites;
        AssertEqual(1, recovered.Count, "remaining protected favorite");
        Assert(recovered[0].IsUsingProtectedCopy, "hidden photo must fall back");
        AssertEqual("hidden.jpg", await File.ReadAllTextAsync(recovered[0].ProtectedPath), "remaining bytes");
    }

    private static async Task TestUnstarConfirmation()
    {
        using var fixture = new TempFixture();
        var stars = new GalleryAnnotationStore(fixture.Root);
        var service = fixture.CreateProtectionService();
        var scope = GalleryAnnotationStore.CreateScopeId("main", fixture.Album);
        const string name = "only.jpg";
        var path = Path.Combine(fixture.Album, name);
        await File.WriteAllTextAsync(path, "only copy");
        await stars.SetStarredAsync(scope, name, true);
        var protectedPhoto = await service.ProtectAsync(scope, name, path);
        File.Delete(path);

        Assert(!await service.TryUnstarAsync(scope, name), "last copy requires confirmation");
        Assert((await stars.LoadStarredAsync(scope)).Contains(name), "cancel keeps the persisted star");
        Assert(File.Exists(protectedPhoto.ProtectedPath), "cancel keeps the only copy");
        Assert(await service.TryUnstarAsync(scope, name, confirmed: true), "explicit release succeeds");
        Assert(!(await stars.LoadStarredAsync(scope)).Contains(name), "confirmed star removed");
        Assert(!File.Exists(protectedPhoto.ProtectedPath), "confirmed last reference releases its object");
    }

    private static async Task TestUnstarOriginalContent()
    {
        using var fixture = new TempFixture();
        var service = fixture.CreateProtectionService();
        var stars = new GalleryAnnotationStore(fixture.Root);
        const string name = "edited.jpg";
        const string scope = "profile:main";
        var path = Path.Combine(fixture.Album, name);
        await File.WriteAllTextAsync(path, "old");
        var photo = await service.ProtectAsync(scope, name, path);
        await stars.SetStarredAsync(scope, name, true);
        await File.WriteAllTextAsync(path, "new");
        Assert(!await service.TryUnstarAsync(scope, name), "same-length edit requires confirmation");
        Assert((await stars.LoadStarredAsync(scope)).Contains(name), "star retained");
        AssertEqual("old", await File.ReadAllTextAsync(photo.ProtectedPath!), "old version retained");
        await File.WriteAllTextAsync(path, "old");
        using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
        {
            Assert(!await service.TryUnstarAsync(scope, name), "an active writer prevents safe release");
        }
        Assert(await service.TryUnstarAsync(scope, name), "matching bytes allow release");
        Assert(!File.Exists(photo.ProtectedPath), "last reference released");
    }

    private static async Task TestPausedUnstarCleanup()
    {
        using var fixture = new TempFixture();
        string[] readOnly = [];
        var service = new GalleryFavoriteProtectionService(fixture.Root, Path.Combine(fixture.Root, "pictures"), () => readOnly);
        var stars = new GalleryAnnotationStore(fixture.Root);
        const string scope = "profile:main";
        const string name = "missing.jpg";
        var path = Path.Combine(fixture.Album, name);
        await File.WriteAllTextAsync(path, "protected");
        var photo = await service.ProtectAsync(scope, name, path);
        await stars.SetStarredAsync(scope, name, true);
        File.Delete(path);
        var store = new GalleryFavoriteProtectionStore((await service.GetPreferencesAsync()).ActiveRootPath);
        var manifest = await File.ReadAllBytesAsync(store.ManifestPath);
        readOnly = [store.RootPath];
        Assert(!await service.TryUnstarAsync(scope, name), "paused last copy requires confirmation");
        Assert((await stars.LoadStarredAsync(scope)).Contains(name), "paused star retained");
        Assert(await service.TryUnstarAsync(scope, name, confirmed: true), "paused unstar confirmed");
        Assert(!(await stars.LoadStarredAsync(scope)).Contains(name), "confirmed star removed");
        Assert(manifest.SequenceEqual(File.ReadAllBytes(store.ManifestPath)), "paused manifest retained");
        AssertEqual("protected", await File.ReadAllTextAsync(photo.ProtectedPath!), "paused bytes retained");
        readOnly = [];
        var pending = await service.CleanUnstarredAsync(scope);
        AssertEqual(0, pending.RemovedObjectCount, "cleanup requires fresh confirmation");
        Assert(manifest.SequenceEqual(File.ReadAllBytes(store.ManifestPath)), "pending cleanup keeps manifest");
        AssertEqual(GalleryAnnotationStore.NormalizeRelativePath(name), pending.NeedsConfirmation.Single().DisplayName, "cleanup identifies the only copy");
        AssertEqual(1, (await service.CleanUnstarredAsync(scope, confirmed: pending.NeedsConfirmation)).RemovedObjectCount,
            "confirmation releases the listed copy");
        Assert(!File.Exists(photo.ProtectedPath), "confirmed cleanup releases bytes");
    }

    private static async Task TestUnstarConsentAcrossStores()
    {
        using var fixture = new TempFixture();
        string[] readOnly = [];
        var service = new GalleryFavoriteProtectionService(fixture.Root, Path.Combine(fixture.Root, "pictures"), () => readOnly);
        const string scope = "profile:main";
        const string name = "same.jpg";
        var path = Path.Combine(fixture.Album, name);
        var firstRoot = (await service.GetPreferencesAsync()).ActiveRootPath;
        await File.WriteAllTextAsync(path, "first");
        var first = await service.ProtectAsync(scope, name, path);
        await service.SetActiveRootAsync(Path.Combine(fixture.Root, "second-store"));
        await File.WriteAllTextAsync(path, "second");
        var second = await service.ProtectAsync(scope, name, path);
        File.Delete(path);
        readOnly = [firstRoot];
        await new GalleryAnnotationStore(fixture.Root).SetStarredAsync(scope, name, true);
        var pending = await service.TryUnstarAsync(scope, name, CancellationToken.None, []);
        AssertEqual(2, pending.Count, "both versions listed");
        var paused = pending.Single(copy => copy.Paused);
        var unapproved = await service.TryUnstarAsync(scope, name, CancellationToken.None, [paused]);
        AssertEqual(second.Entry!.Sha256, unapproved.Single().Sha256, "paused approval cannot release the other version");
        Assert(File.Exists(first.ProtectedPath) && File.Exists(second.ProtectedPath), "no partial unstar");
        AssertEqual(0, (await service.TryUnstarAsync(scope, name, CancellationToken.None, pending)).Count, "unstar confirmed");
        Assert(File.Exists(first.ProtectedPath) && !File.Exists(second.ProtectedPath), "only writable version released");
    }

    private static async Task TestRestarAfterPause()
    {
        using var fixture = new TempFixture();
        string[] readOnly = [];
        var service = new GalleryFavoriteProtectionService(fixture.Root, Path.Combine(fixture.Root, "pictures"), () => readOnly);
        const string scope = "profile:main";
        const string name = "edited.jpg";
        var path = Path.Combine(fixture.Album, name);
        var stars = new GalleryAnnotationStore(fixture.Root);
        await File.WriteAllTextAsync(path, "old");
        var previous = await service.ProtectAsync(scope, name, path);
        await stars.SetStarredAsync(scope, name, true);
        await File.WriteAllTextAsync(path, "new");
        readOnly = [(await service.GetPreferencesAsync()).ActiveRootPath];
        var prompt = await service.TryUnstarAsync(scope, name, CancellationToken.None, []);
        Assert(prompt.Single() is { Paused: true, Reason: GalleryOriginalIssue.Changed }, "paused edit is identified");
        AssertEqual(0, (await service.TryUnstarAsync(scope, name, CancellationToken.None, prompt)).Count, "paused unstar succeeds");
        readOnly = [];
        await stars.SetStarredAsync(scope, name, true);
        var current = await service.ProtectAsync(scope, name, path);
        var cleanup = await service.CleanUnstarredAsync(scope);
        AssertEqual(previous.ProtectedPath!, cleanup.NeedsConfirmation.Single().ObjectPath, "old version requires cleanup approval");
        AssertEqual("old", await File.ReadAllTextAsync(previous.ProtectedPath!), "old bytes preserved");
        AssertEqual("new", await File.ReadAllTextAsync(current.ProtectedPath!), "new bytes protected");
    }

    private static async Task TestCleanupAcrossStores()
    {
        using var fixture = new TempFixture();
        var service = fixture.CreateProtectionService();
        const string scope = "profile:main";
        async Task<(string Original, string Copy)> Protect(string name)
        {
            var path = Path.Combine(fixture.Album, name);
            await File.WriteAllTextAsync(path, name);
            var photo = await service.ProtectAsync(scope, name, path);
            return (path, photo.ProtectedPath!);
        }
        var first = await Protect("first.jpg");
        await service.SetActiveRootAsync(Path.Combine(fixture.Root, "second-store"));
        var second = await Protect("second.jpg");
        File.Delete(first.Original);
        var pending = await service.CleanUnstarredAsync(scope);
        AssertEqual(first.Copy, pending.NeedsConfirmation.Single().ObjectPath, "missing original listed");
        Assert(File.Exists(second.Copy), "safe object in other store retained pending confirmation");
        await File.WriteAllTextAsync(second.Original, "edited");
        var revised = await service.CleanUnstarredAsync(scope, confirmed: pending.NeedsConfirmation);
        AssertEqual(second.Copy, revised.NeedsConfirmation.Single().ObjectPath, "newly changed original requires consent");
        Assert(File.Exists(first.Copy) && File.Exists(second.Copy), "revised plan retains both copies");
        var cleanup = await service.CleanUnstarredAsync(scope,
            confirmed: pending.NeedsConfirmation.Concat(revised.NeedsConfirmation).ToArray());
        AssertEqual(2, cleanup.RemovedObjectCount, "both confirmed versions released");
    }

    private static async Task TestUnusedLegacyRoot(bool createEmptyRoot)
    {
        using var fixture = new TempFixture();
        var service = fixture.CreateProtectionService();
        var unused = (await service.GetPreferencesAsync()).ActiveRootPath;
        var active = Path.Combine(fixture.Root, "new-store");
        await service.SetActiveRootAsync(active);
        const string name = "legacy.jpg";
        const string legacy = "profile:main";
        var original = Path.Combine(fixture.Album, name);
        await File.WriteAllTextAsync(original, "legacy bytes");
        await service.ProtectAsync(legacy, name, original);
        await new GalleryAnnotationStore(fixture.Root).SetStarredAsync(legacy, name, true);
        File.Delete(original);
        var offline = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => $"{(char)c}:\\")
            .First(drive => !Directory.GetLogicalDrives().Contains(drive, StringComparer.OrdinalIgnoreCase));
        async Task Register(string root) => await File.WriteAllTextAsync(service.SettingsFilePath,
            System.Text.Json.JsonSerializer.Serialize(new { schemaVersion = 1, isEnabled = true, activeRootPath = active, knownRootPaths = new[] { root, active } }));
        await Register(Path.Combine(offline, "protected"));
        AssertEqual(0, (await service.LoadGalleryAsync("main", fixture.Album)).StarredPaths.Count, "offline volume defers migration");
        if (createEmptyRoot)
        {
            Directory.CreateDirectory(Path.Combine(unused, "objects", "aa"));
        }
        await Register(unused);
        var restored = await service.LoadGalleryAsync("main", fixture.Album);
        Assert(restored.StarredPaths.Contains(name) && restored.ProtectedFavorites.Single().IsUsingProtectedCopy,
            "never-created root allows verified fallback migration");
        AssertEqual(createEmptyRoot, Directory.Exists(unused), "migration preserves empty root state");
    }

    private static async Task TestGalleryRootIsolation()
    {
        using var fixture = new TempFixture();
        var stars = new GalleryAnnotationStore(fixture.Root);
        var service = new GalleryFavoriteProtectionService(fixture.Root, Path.Combine(fixture.Root, "pictures"));
        var first = Directory.CreateDirectory(Path.Combine(fixture.Root, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(fixture.Root, "second")).FullName;
        var firstScope = GalleryAnnotationStore.CreateScopeId("main", first);
        var secondScope = GalleryAnnotationStore.CreateScopeId("main", second);
        const string name = "same.jpg";
        await File.WriteAllTextAsync(Path.Combine(first, name), "first");
        await File.WriteAllTextAsync(Path.Combine(second, name), "second");
        await stars.SetStarredAsync(firstScope, name, true);
        await service.ProtectAsync(firstScope, name, Path.Combine(first, name));
        File.Delete(Path.Combine(first, name));

        var secondStars = await stars.LoadStarredAsync(secondScope);
        AssertEqual(0, secondStars.Count, "second gallery must start unstarred");
        await stars.SetStarredAsync(secondScope, name, true);
        AssertEqual(0, (await service.LoadGalleryAsync("main", second)).ProtectedFavorites.Count,
            "second gallery must not inherit the first copy");
        await service.ProtectAsync(secondScope, name, Path.Combine(second, name));
        var firstCopies = (await service.LoadGalleryAsync("main", first)).ProtectedFavorites;
        AssertEqual("first", await File.ReadAllTextAsync(firstCopies.Single().ProtectedPath), "first fallback");
        File.Delete(Path.Combine(second, name));
        var secondCopies = (await service.LoadGalleryAsync("main", second)).ProtectedFavorites;
        AssertEqual("second", await File.ReadAllTextAsync(secondCopies.Single().ProtectedPath), "second fallback");
    }

    private static async Task TestDeferredFallbackVerification()
    {
        using var fixture = new TempFixture();
        var service = fixture.CreateProtectionService();
        var scope = GalleryAnnotationStore.CreateScopeId("main", fixture.Album);
        const string name = "original.jpg";
        var original = Path.Combine(fixture.Album, name);
        await new GalleryAnnotationStore(fixture.Root).SetStarredAsync(scope, name, true);
        await File.WriteAllBytesAsync(original, [1, 2, 3]);
        var result = await service.ProtectAsync(scope, name, original);
        using (File.Open(result.ProtectedPath!, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            AssertEqual(1, (await service.LoadGalleryAsync("main", fixture.Album)).ProtectedFavorites.Count,
                "original still present: refresh must not open protected bytes");
        }

        await File.WriteAllBytesAsync(result.ProtectedPath!, [3, 2, 1]);
        File.Delete(original);
        AssertEqual(0, (await service.LoadGalleryAsync("main", fixture.Album)).ProtectedFavorites.Count,
            "original missing: same-length corruption must block fallback");
    }

    private static async Task TestManifestReadsPerRefresh()
    {
        using var fixture = new TempFixture();
        var reads = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var service = new GalleryFavoriteProtectionService(fixture.Root, Path.Combine(fixture.Root, "pictures"),
            createStore: root => new GalleryFavoriteProtectionStore(root, path =>
            {
                reads[root] = reads.GetValueOrDefault(root) + 1;
                return File.OpenRead(path);
            }));
        var scope = GalleryAnnotationStore.CreateScopeId("main", fixture.Album);
        var annotations = new GalleryAnnotationStore(fixture.Root);
        foreach (var root in new[] { "first-store", "second-store" })
        {
            await service.SetActiveRootAsync(Path.Combine(fixture.Root, root));
            for (var index = 0; index < 3; index++)
            {
                var name = $"{root}-{index}.jpg";
                await annotations.SetStarredAsync(scope, name, true);
                var path = Path.Combine(fixture.Album, name);
                await File.WriteAllTextAsync(path, name);
                await service.ProtectAsync(scope, name, path);
                if (index == 0)
                {
                    File.Delete(path);
                }
            }
        }

        reads.Clear();
        AssertEqual(6, (await service.LoadGalleryAsync("main", fixture.Album)).ProtectedFavorites.Count, "favorites across roots");
        AssertEqual(2, reads.Count, "distinct manifests opened");
        Assert(reads.Values.All(count => count == 1), $"manifest open counts: {string.Join(", ", reads.Values)}");
    }

    private static async Task TestProtectionRootSelection()
    {
        using var fixture = new TempFixture();
        var gallery = Directory.CreateDirectory(Path.Combine(fixture.Root, "photos", "album")).FullName;
        var game = Directory.CreateDirectory(Path.Combine(fixture.Root, "games", "nikki")).FullName;
        var service = new GalleryFavoriteProtectionService(fixture.Root, Path.Combine(fixture.Root, "pictures"),
            readOnlyRoots: () => [gallery, game]);
        foreach (var readOnly in new[] { gallery, game })
        {
            foreach (var choice in new[] { readOnly.ToUpperInvariant() + "/", Path.Combine(readOnly, "new"),
                         Path.Combine(readOnly, ".."), Path.Combine(readOnly, "x", "..") })
            {
                var directory = new DirectoryInfo(choice);
                var existed = directory.Exists;
                var observed = existed ? directory : directory.Parent!;
                var past = new DateTime(2001, 1, 2, 3, 4, 5, DateTimeKind.Utc);
                Directory.SetLastWriteTimeUtc(observed.FullName, past);
                var rejected = false;
                try
                {
                    await service.SetActiveRootAsync(choice);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("重叠", StringComparison.Ordinal))
                {
                    rejected = true;
                }

                AssertEqual(past, Directory.GetLastWriteTimeUtc(observed.FullName), "no transient probe or directory creation");
                AssertEqual(existed, Directory.Exists(choice), "rejected selection leaves tree untouched");
                Assert(rejected, "overlap rejected with reason");
            }
        }

        var sibling = gallery + "-backup";
        AssertEqual(sibling, (await service.SetActiveRootAsync(sibling)).ActiveRootPath, "sibling prefix is safe");
    }

    private static async Task TestProtectionRootPause(bool reload)
    {
        using var fixture = new TempFixture();
        var album = Directory.CreateDirectory(Path.Combine(fixture.Root, "album")).FullName;
        var pictures = Path.Combine(fixture.Root, "pictures");
        string[] readOnly = [album];
        GalleryFavoriteProtectionService Create() => new(fixture.Root, pictures, () => readOnly);
        var service = Create();
        var root = Path.Combine(fixture.Root, "store");
        await service.SetActiveRootAsync(root);
        var original = Path.Combine(album, "old.jpg");
        await File.WriteAllTextAsync(original, "old");
        var scope = GalleryAnnotationStore.CreateScopeId("main", album);
        var old = await service.ProtectAsync(scope, "old.jpg", original);
        var store = new GalleryFavoriteProtectionStore(root);
        var manifest = await File.ReadAllBytesAsync(store.ManifestPath);
        File.Delete(Path.Combine(root, ".store.lock"));
        readOnly = [album, root.ToUpperInvariant() + "/"];
        if (reload)
        {
            service = Create();
        }

        Assert((await service.GetOverviewAsync(verify: false)).Preferences.IsPausedForOverlap, "reported pause");
        await service.VerifyAsync();
        var attempted = await service.ProtectAsync(scope, "new.jpg", original);
        await service.CleanUnstarredAsync(scope);
        await service.TryUnstarAsync(scope, "old.jpg");
        await service.LoadGalleryAsync("main", album);
        try
        {
            await service.GetRootToOpenAsync();
            throw new InvalidOperationException("opening an overlapping root must be refused");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("重叠", StringComparison.Ordinal))
        {
        }
        Assert(attempted.Entry is null, "overlap pauses protection");
        Assert(!File.Exists(Path.Combine(root, ".store.lock")), "no lock file created by read or write operations");
        var currentManifest = await File.ReadAllBytesAsync(store.ManifestPath);
        Assert(manifest.SequenceEqual(currentManifest), "old manifest remains byte-identical");
        AssertEqual("old", await File.ReadAllTextAsync(old.ProtectedPath!), "old protected bytes");

        var replacement = Path.Combine(fixture.Root, "replacement");
        var changed = await service.SetActiveRootAsync(replacement);
        Assert(!changed.IsPausedForOverlap && changed.OverlappingRoots.Contains(root), "safe replacement resumes protection");
        Assert((await service.ProtectAsync(scope, "old.jpg", original)).Entry is not null, "safe root receives new copies");
        await service.VerifyAsync();
        await service.CleanUnstarredAsync(scope);
        currentManifest = await File.ReadAllBytesAsync(store.ManifestPath);
        Assert(manifest.SequenceEqual(currentManifest), "former overlapping root remains read-only");
        Assert(!File.Exists(Path.Combine(root, ".store.lock")), "former root lock remains absent");
    }

    private static async Task TestLegacyFavoriteOwnership()
    {
        using var fixture = new TempFixture();
        var annotations = new GalleryAnnotationStore(fixture.Root);
        var service = new GalleryFavoriteProtectionService(fixture.Root, Path.Combine(fixture.Root, "pictures"));
        var owner = Directory.CreateDirectory(Path.Combine(fixture.Root, "owner")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(fixture.Root, "other")).FullName;
        const string legacy = "profile:main";
        const string known = "known.jpg";
        const string unknown = "unknown.jpg";
        await annotations.SetStarredAsync(legacy, known, true);
        await annotations.SetStarredAsync(legacy, unknown, true);
        await File.WriteAllTextAsync(Path.Combine(owner, known), "legacy bytes");
        var entry = await service.ProtectAsync(legacy, known, Path.Combine(owner, known));
        File.Delete(Path.Combine(owner, known));
        await File.WriteAllTextAsync(Path.Combine(other, known), "unrelated");
        AssertEqual(0, (await service.LoadGalleryAsync("main", other)).StarredPaths.Count, "another gallery cannot claim legacy stars");
        var owned = await service.LoadGalleryAsync("main", owner);
        Assert(owned.StarredPaths.SetEquals([known]), "only proven star migrates");
        Assert((await annotations.LoadStarredAsync(owned.ScopeId)).SetEquals([known]), "migration persisted");
        Assert(owned.ProtectedFavorites.Single().IsUsingProtectedCopy, "missing legacy original resolves");
        Assert((await annotations.LoadStarredAsync(legacy)).SetEquals([unknown]), "unknown legacy star retained");
        AssertEqual("legacy bytes", await File.ReadAllTextAsync(entry.ProtectedPath!), "legacy copy retained");
        var manifest = new GalleryFavoriteProtectionStore((await service.GetPreferencesAsync()).ActiveRootPath);
        AssertEqual(owned.ScopeId, manifest.GetEntries().Single().ScopeId, "manifest migration persisted");
        Assert((await service.LoadGalleryAsync("main", owner)).StarredPaths.SetEquals([known]), "migration is idempotent");
    }

    private static async Task TestConflictingLegacyOwners()
    {
        using var fixture = new TempFixture();
        var annotations = new GalleryAnnotationStore(fixture.Root);
        var service = fixture.CreateProtectionService();
        const string legacy = "profile:main";
        const string name = "same.jpg";
        await annotations.SetStarredAsync(legacy, name, true);
        var copies = new List<(string Gallery, string Store, string Copy)>();
        foreach (var id in new[] { "first", "second" })
        {
            var gallery = Directory.CreateDirectory(Path.Combine(fixture.Root, id)).FullName;
            var root = Path.Combine(fixture.Root, id + "-store");
            await service.SetActiveRootAsync(root);
            var path = Path.Combine(gallery, name);
            await File.WriteAllTextAsync(path, id);
            var result = await service.ProtectAsync(legacy, name, path);
            File.Delete(path);
            copies.Add((gallery, root, result.ProtectedPath!));
        }

        foreach (var (gallery, _, _) in copies)
        {
            var state = await service.LoadGalleryAsync("main", gallery);
            AssertEqual(0, state.StarredPaths.Count, "ambiguous star not claimed");
            AssertEqual(0, state.ProtectedFavorites.Count, "ambiguous copy not displayed");
            await service.CleanUnstarredAsync(state.ScopeId);
        }

        foreach (var (gallery, root, copy) in copies)
        {
            AssertEqual(legacy, new GalleryFavoriteProtectionStore(root).GetEntries().Single().ScopeId, "old record retained");
            AssertEqual(Path.GetFileName(gallery), await File.ReadAllTextAsync(copy), "old bytes retained");
        }
        Assert((await annotations.LoadStarredAsync(legacy)).Contains(name), "ambiguous annotation retained");
    }

    private static async Task TestReleaseSharedObject()
    {
        using var fixture = new TempFixture();
        var service = fixture.CreateProtectionService();
        var stars = new GalleryAnnotationStore(fixture.Root);
        var scope = GalleryAnnotationStore.CreateScopeId("main", fixture.Album);
        var store = new GalleryFavoriteProtectionStore((await service.GetPreferencesAsync()).ActiveRootPath);
        foreach (var name in new[] { "first.jpg", "last.jpg" })
        {
            var path = Path.Combine(fixture.Album, name);
            await File.WriteAllTextAsync(path, "same bytes");
            await stars.SetStarredAsync(scope, name, true);
            await service.ProtectAsync(scope, name, path);
            File.Delete(path);
        }

        var orphanHash = new string('a', 64);
        var orphan = Path.Combine(Directory.CreateDirectory(Path.Combine(store.ObjectsPath, "aa")).FullName, orphanHash + ".bin");
        await File.WriteAllTextAsync(orphan, "unrelated");
        Assert(await service.TryUnstarAsync(scope, "first.jpg"), "another reference retains the shared object");
        Assert(!await service.TryUnstarAsync(scope, "last.jpg"), "last reference needs confirmation");
        AssertEqual("same bytes", await File.ReadAllTextAsync((await service.LoadGalleryAsync("main", fixture.Album))
            .ProtectedFavorites.Single().ProtectedPath), "shared bytes retained");
        Assert(File.Exists(orphan), "single release must not sweep other objects");
    }

    private static async Task TestFailedUnstar()
    {
        using var fixture = new TempFixture();
        var service = fixture.CreateProtectionService();
        var stars = new GalleryAnnotationStore(fixture.Root);
        var scope = GalleryAnnotationStore.CreateScopeId("main", fixture.Album);
        const string name = "only.jpg";
        var original = Path.Combine(fixture.Album, name);
        await File.WriteAllTextAsync(original, "retained bytes");
        await stars.SetStarredAsync(scope, name, true);
        var previous = await service.ProtectAsync(scope, name, original);
        var oldRoot = (await service.GetPreferencesAsync()).ActiveRootPath;
        var activeRoot = Path.Combine(fixture.Root, "active");
        await service.SetActiveRootAsync(activeRoot);
        var active = await service.ProtectAsync(scope, name, original);
        File.Delete(original);
        using (File.OpenRead(new GalleryFavoriteProtectionStore(oldRoot).ManifestPath))
        {
            try
            {
                await service.TryUnstarAsync(scope, name, confirmed: true);
                throw new InvalidOperationException("manifest replacement should fail");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        Assert((await stars.LoadStarredAsync(scope)).Contains(name), "failed unstar preserves star");
        Assert(new GalleryFavoriteProtectionStore(activeRoot).GetEntry(scope, name) is not null, "active reference restored");
        Assert(File.Exists(active.ProtectedPath) && File.Exists(previous.ProtectedPath), "both objects retained");
        Assert((await service.LoadGalleryAsync("main", fixture.Album)).ProtectedFavorites.Single().IsUsingProtectedCopy,
            "favorite still falls back after failure");
        Assert(await service.TryUnstarAsync(scope, name, confirmed: true), "unstar can be retried");
    }

    private static async Task TestRootAliases()
    {
        using var fixture = new TempFixture();
        var gallery = fixture.Album;
        using var junction = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
            "/d /c mklink /J alias album")
        {
            WorkingDirectory = fixture.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        await junction.WaitForExitAsync();
        AssertEqual(0, junction.ExitCode, "test junction");
        var alias = Path.Combine(fixture.Root, "alias");
        string[] readOnly = [gallery];
        var service = new GalleryFavoriteProtectionService(fixture.Root, Path.Combine(fixture.Root, "pictures"), () => readOnly);
        foreach (var (protectedRoot, browsedRoot) in new[] { (alias, gallery), (gallery, alias) })
        {
            readOnly = [browsedRoot];
            try
            {
                await service.SetActiveRootAsync(Path.Combine(protectedRoot, "new"));
                throw new InvalidOperationException("aliased overlap should be rejected");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("重叠", StringComparison.Ordinal))
            {
            }
            Assert(!Directory.Exists(Path.Combine(gallery, "new")), "no directory through alias");
        }
    }

    private static async Task TestLegacyDriveRoot()
    {
        using var fixture = new TempFixture();
        var drive = Path.GetPathRoot(Path.GetFullPath("."))!;
        var legacy = drive.TrimEnd(Path.DirectorySeparatorChar);
        AssertEqual(GalleryAnnotationStore.CreateScopeId("main", drive), GalleryAnnotationStore.CreateScopeId("main", legacy),
            "legacy drive-root scope");
        var tempDrive = Path.GetPathRoot(fixture.Root)!.TrimEnd(Path.DirectorySeparatorChar);
        var service = new GalleryFavoriteProtectionService(fixture.Root, Path.Combine(fixture.Root, "pictures"), () => [tempDrive]);
        var choice = Path.Combine(fixture.Root, "rejected");
        try
        {
            await service.SetActiveRootAsync(choice);
            throw new InvalidOperationException("legacy drive overlap should be rejected");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("重叠", StringComparison.Ordinal))
        {
        }
        Assert(!Directory.Exists(choice), "old drive-root gallery is protected before probing");
    }

    private static async Task TestOfflineLegacyManifest()
    {
        using var fixture = new TempFixture();
        var service = fixture.CreateProtectionService();
        var annotations = new GalleryAnnotationStore(fixture.Root);
        const string legacy = "profile:main";
        const string name = "shared.jpg";
        var firstRoot = (await service.GetPreferencesAsync()).ActiveRootPath;
        await annotations.SetStarredAsync(legacy, name, true);
        var path = Path.Combine(fixture.Album, name);
        await File.WriteAllTextAsync(path, "one");
        await service.ProtectAsync(legacy, name, path);
        await service.SetActiveRootAsync(Path.Combine(fixture.Root, "second"));
        await service.ProtectAsync(legacy, name, path);
        File.Delete(path);
        var manifest = new GalleryFavoriteProtectionStore(firstRoot).ManifestPath;
        File.Move(manifest, manifest + ".offline");
        var deferred = await service.LoadGalleryAsync("main", fixture.Album);
        AssertEqual(0, deferred.StarredPaths.Count, "missing historical manifest cannot prove ownership");
        Assert((await annotations.LoadStarredAsync(legacy)).Contains(name), "legacy star retained while offline");
        File.Move(manifest + ".offline", manifest);
        var restored = await service.LoadGalleryAsync("main", fixture.Album);
        Assert(restored.ProtectedFavorites.Single().IsUsingProtectedCopy, "consistent ownership migrates after reconnect");
    }

    private static async Task TestQueuedProtection()
    {
        using var fixture = new TempFixture();
        string[] readOnly = [fixture.Album];
        var storeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new GalleryFavoriteProtectionService(fixture.Root, Path.Combine(fixture.Root, "pictures"),
            () => Volatile.Read(ref readOnly), root =>
            {
                storeRequested.TrySetResult();
                return new GalleryFavoriteProtectionStore(root);
            });
        var root = Path.Combine(fixture.Root, "store");
        await service.SetActiveRootAsync(root);
        var original = Path.Combine(fixture.Album, "pending.jpg");
        await File.WriteAllTextAsync(original, "bytes");
        Task<GalleryFavoriteProtectionResult> pending;
        using (File.Open(Path.Combine(root, ".store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            pending = service.ProtectAsync("main", "pending.jpg", original);
            await storeRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Volatile.Write(ref readOnly, [fixture.Album, root]);
        }
        Assert((await pending).IsPausedForOverlap, "queued writer observes the new gallery");
        Assert(!Directory.Exists(Path.Combine(root, "objects")), "no object was written");
    }

    private static async Task TestReadOnlyFallback()
    {
        using var fixture = new TempFixture();
        var service = fixture.CreateProtectionService();
        var stars = new GalleryAnnotationStore(fixture.Root);
        var scope = GalleryAnnotationStore.CreateScopeId("main", fixture.Album);
        const string name = "photo.jpg";
        var path = Path.Combine(fixture.Album, name);
        await File.WriteAllTextAsync(path, "bytes");
        await stars.SetStarredAsync(scope, name, true);
        await service.ProtectAsync(scope, name, path);
        File.Delete(path);
        var root = (await service.GetPreferencesAsync()).ActiveRootPath;
        var lockFile = Path.Combine(root, ".store.lock");
        File.SetAttributes(lockFile, FileAttributes.ReadOnly);
        try
        {
            var state = await service.LoadGalleryAsync("main", fixture.Album);
            Assert(state.ProtectedFavorites.Single().IsUsingProtectedCopy, "fallback needs only read access");
        }
        finally
        {
            File.SetAttributes(lockFile, FileAttributes.Normal);
        }

        var damaged = Path.Combine(fixture.Root, "damaged");
        await service.SetActiveRootAsync(damaged);
        var damagedManifest = new GalleryFavoriteProtectionStore(damaged).ManifestPath;
        await File.WriteAllTextAsync(damagedManifest, "invalid");
        await service.SetActiveRootAsync(root);
        Assert(!await service.TryUnstarAsync(scope, name), "healthy root still checks the only copy");
        Assert(await service.TryUnstarAsync(scope, name, confirmed: true), "unrelated broken root does not block unstar");
        AssertEqual("invalid", await File.ReadAllTextAsync(damagedManifest), "unrelated broken root untouched");
    }

    private static async Task TestFavoriteProtectionPreferences()
    {
        using var fixture = new TempFixture();
        var localRoot = Path.Combine(fixture.Root, "local");
        var picturesRoot = Path.Combine(fixture.Root, "pictures");
        var service = new GalleryFavoriteProtectionService(localRoot, picturesRoot, () => [fixture.Album]);
        var defaults = await service.GetPreferencesAsync();
        var expectedDefaultRoot = Path.Combine(
            picturesRoot,
            "Nikkiward",
            "ProtectedFavorites");
        Assert(defaults.IsEnabled, "favorite protection default enabled");
        AssertEqual(expectedDefaultRoot, defaults.ActiveRootPath, "default protection root");

        var sourcePath = Path.Combine(fixture.Album, "protected.jpeg");
        await File.WriteAllBytesAsync(sourcePath, Enumerable.Repeat((byte)0x81, 640).ToArray());
        const string scope = "profile:main";
        var protectedResult = await service.ProtectAsync(
            scope,
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
        await new GalleryAnnotationStore(localRoot).SetStarredAsync(scope, "protected.jpeg", true);
        var recovered = (await service.LoadGalleryAsync("main", fixture.Album)).ProtectedFavorites;
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
            new GalleryAnnotationStore(fixture.Root));
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
            Album = Directory.CreateDirectory(Path.Combine(Root, "album")).FullName;
        }

        public string Root { get; }

        public string Album { get; }

        public GalleryFavoriteProtectionService CreateProtectionService() =>
            new(Root, Path.Combine(Root, "pictures"), () => [Album]);

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
