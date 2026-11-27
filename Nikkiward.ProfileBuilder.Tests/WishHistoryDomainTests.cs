using Nikkiward.Features.Pages;
using Nikkiward.ViewModels;

internal static class WishHistoryDomainTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("fresh wish image directory is ready for web mapping", FreshWishImageDirectory),
        ("web pages route only registered destinations", RegisteredPages),
        ("web pages isolate actions by active page", PageActions),
        ("wish export refuses game, gallery and linked destinations before writing", ExportProtectsRoots),
        ("wish export replaces only a completed backup", ExportIsAtomic),
        ("wish import and sync preserve unreadable local history", PreserveUnreadableHistory),
        ("wish import and sync accept missing and valid empty history", InitializeEmptyHistory),
        ("wish JSON round trip preserves and deduplicates entries", RoundTrip),
        ("wish pool timeline orders simultaneous pulls and preserves unknown distances", PoolTimeline),
        ("invalid wish JSON leaves stored history intact", InvalidBackup),
        ("wish history merge preserves older records and appends new records", MergePreservesOlderRecords),
        ("wish history merge is idempotent for repeated imports", MergeIsIdempotent),
        ("wish history merge enriches duplicate records without erasing known fields", MergeEnrichesDuplicateRecords),
        ("wish history keeps distinct stable IDs at the same timestamp", MergeUsesStableIdAndTimestamp),
        ("wish history projection sorts newest first and marks month boundaries", ProjectionSortsAndMarksMonths),
        ("wish history projection keeps missing summary values unknown", ProjectionKeepsMissingSummaryUnknown),
        ("wish history five-star rows expose only a subtle glow", FiveStarRowsExposeSubtleGlow),
        ("wish history store merges atomically and keeps history outside cache clearing", StoreMergesAtomically),
    ];

    private static Task FreshWishImageDirectory()
    {
        using var fixture = new TemporaryDirectory();
        var cache = new ResonanceHistoryCache(fixture.Root);
        Assert(!Directory.Exists(cache.RootPath), "fresh data directory");
        var path = cache.EnsureAssetsDirectory();
        Assert(Directory.Exists(path) && path == cache.AssetsPath, "mapped image directory");
        AssertEqual(path, cache.EnsureAssetsDirectory(), "existing image directory");
        return Task.CompletedTask;
    }

    private static PageAdapter TestAdapter(params string[] actions) =>
        new("wish", actions, () => new object(), (_, _) => Task.CompletedTask);

    private static Task RegisteredPages()
    {
        var routes = PageRoutes.Register(("wish", TestAdapter()));
        Assert(routes.ContainsKey("wish"), "wish is web");
        foreach (var page in new[] { "gallery", "favorites", "journal", "settings" })
            Assert(!routes.ContainsKey(page) && PageRoutes.NavigationTag(page) != null, page + " is native");
        return Task.CompletedTask;
    }

    private static Task PageActions()
    {
        var wish = TestAdapter("import");
        Assert(PageRoutes.Allows("wish", "wish", wish, "import"), "own action");
        Assert(!PageRoutes.Allows("wish", "gallery", wish, "import"), "foreign envelope");
        Assert(!PageRoutes.Allows("wish", "wish", wish, "delete"), "unknown action");
        return Task.CompletedTask;
    }

    private static async Task ExportProtectsRoots()
    {
        using var fixture = new TemporaryDirectory();
        var store = new WishHistoryStore(fixture.Root);
        var game = Directory.CreateDirectory(Path.Combine(fixture.Root, "game")).FullName;
        var album = Directory.CreateDirectory(Path.Combine(fixture.Root, "album")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(fixture.Root, "other")).FullName;
        string[] roots = [game, album, other];
        async Task RefuseExport(string dir, string[] protectedRoots)
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "export.json");
            foreach (var existing in new[] { false, true })
            {
                if (existing) await File.WriteAllTextAsync(path, "original");
                try { await store.ExportFileAsync(path, protectedRoots); throw new Exception("protected export accepted"); }
                catch (IOException) { }
                Assert(existing == File.Exists(path), "a refused export cannot create a file");
                if (existing) AssertEqual("original", await File.ReadAllTextAsync(path), "protected contents");
            }
            File.Delete(path);
            Assert(!Directory.EnumerateFiles(dir).Any(), "no temporary export remains");
        }
        foreach (var dir in roots)
        {
            await RefuseExport(dir, roots);
            await RefuseExport(Path.Combine(dir, "sub"), roots);
        }

        using var junction = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", "/d /c mklink /J alias album") { WorkingDirectory = fixture.Root, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true })!;
        await junction.WaitForExitAsync();
        AssertEqual(0, junction.ExitCode, "test directory junction");
        var alias = Path.Combine(fixture.Root, "alias");
        await RefuseExport(Path.Combine(alias, "sub"), roots);
        await RefuseExport(Path.Combine(album, "sub"), [alias]);

        var drive = Path.GetPathRoot(album)!;
        var volume = new System.Text.StringBuilder(128);
        Assert(GetVolumeNameForVolumeMountPointW(drive, volume, (uint)volume.Capacity), "test volume name");
        var volumeAlbum = Path.Combine(volume.ToString(), Path.GetRelativePath(drive, album));
        await RefuseExport(Path.Combine(album, "sub"), [volumeAlbum]);
        var allowed = Path.Combine(fixture.Root, "outside.json");
        await store.ExportFileAsync(allowed, [volumeAlbum]);
        Assert(File.Exists(allowed), "unrelated directory on the same volume remains writable");
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode,
        ExactSpelling = true, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, System.Text.StringBuilder name, uint capacity);

    private static async Task ExportIsAtomic()
    {
        using var fixture = new TemporaryDirectory();
        var store = new WishHistoryStore(fixture.Root);
        var path = Path.Combine(fixture.Root, "export.json");
        await File.WriteAllTextAsync(path, "previous export");
        Directory.CreateDirectory(store.RootPath);
        await File.WriteAllTextAsync(store.HistoryPath, "{");
        try { await store.ExportFileAsync(path, []); throw new Exception("invalid source exported"); }
        catch (InvalidDataException) { }
        AssertEqual("previous export", await File.ReadAllTextAsync(path), "existing destination after failed write");
        Assert(!Directory.EnumerateFiles(fixture.Root, "*.tmp").Any(), "temporary export removed");

        await File.WriteAllTextAsync(store.HistoryPath, """{"schemaVersion":1,"entries":[]}""");
        try { await store.ExportFileAsync(store.HistoryPath, []); throw new Exception("database selected as export"); }
        catch (InvalidDataException) { }
        await store.ExportFileAsync(path, []);
        using var stream = File.OpenRead(path);
        AssertEqual(0, (await store.ImportAsync(stream)).Entries.Count, "complete replacement can be imported");
    }

    private static async Task PreserveUnreadableHistory()
    {
        using var fixture = new TemporaryDirectory();
        var store = new WishHistoryStore(fixture.Root);
        using var backup = new MemoryStream();
        await store.ExportAsync(backup);
        Directory.CreateDirectory(store.RootPath);
        var record = Entry("sync", "2026-05-01T00:00:00Z");
        foreach (var invalid in new[] { "{", "null", "{}", """{"entries":[]}""",
            """{"schemaVersion":2,"entries":[]}""", """{"schemaVersion":1}""",
            """{"schemaVersion":1,"entries":null}""", """{"schemaVersion":1,"entries":{}}""",
            """{"schemaVersion":1,"entries":[null]}""", """{"schemaVersion":1,"entries":[{}]}""" })
        {
            var original = System.Text.Encoding.UTF8.GetBytes(invalid);
            await File.WriteAllBytesAsync(store.HistoryPath, original);
            backup.Position = 0;
            foreach (Func<Task> write in new Func<Task>[] {
                () => store.ImportAsync(backup), () => store.MergeAndSaveAsync([record], null, DateTimeOffset.UtcNow) })
            {
                try { await write(); throw new Exception("unreadable history replaced"); }
                catch (InvalidDataException ex) { Assert(ex.Message.Contains("本机心愿历史"), "identify the unreadable local file"); }
                AssertSequence(original, await File.ReadAllBytesAsync(store.HistoryPath), "preserve original bytes");
            }
        }
    }

    private static async Task InitializeEmptyHistory()
    {
        using var source = new TemporaryDirectory();
        var record = Entry("first", "2026-05-01T00:00:00Z");
        var exporter = new WishHistoryStore(source.Root);
        await exporter.MergeAndSaveAsync([record], null, DateTimeOffset.UtcNow);
        using var backup = new MemoryStream();
        await exporter.ExportAsync(backup);
        foreach (var emptyFile in new[] { false, true })
        foreach (var import in new[] { false, true })
        {
            using var fixture = new TemporaryDirectory();
            var store = new WishHistoryStore(fixture.Root);
            if (emptyFile)
            {
                Directory.CreateDirectory(store.RootPath);
                await File.WriteAllTextAsync(store.HistoryPath, """{"schemaVersion":1,"entries":[]}""");
            }
            backup.Position = 0;
            if (import) await store.ImportAsync(backup);
            else await store.MergeAndSaveAsync([record], null, DateTimeOffset.UtcNow);
            AssertSequence([record], (await store.LoadAsync()).Entries, "first record");
        }
    }

    private static async Task RoundTrip()
    {
        using var source = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var store = new WishHistoryStore(source.Root);
        var record = Entry("same", "2026-05-01T00:00:00Z", "花", 5, 40);
        await store.MergeAndSaveAsync([record], new() { TotalPulls = 40 }, DateTimeOffset.UtcNow);
        using var stream = new MemoryStream();
        await store.ExportAsync(stream);
        var target = new WishHistoryStore(destination.Root);
        var older = record with { TimestampUtc = record.TimestampUtc.AddDays(-1) };
        await target.MergeAndSaveAsync([older], null, DateTimeOffset.UtcNow);
        for (var i = 0; i < 2; i++)
        {
            stream.Position = 0;
            var snapshot = await target.ImportAsync(stream);
            AssertSequence([older, record], snapshot.Entries, "records");
            AssertEqual(40, snapshot.Summary?.TotalPulls, "summary");
        }
    }

    private static Task PoolTimeline()
    {
        const string instant = "2026-06-18T21:19:00Z";
        var rows = WishHistoryProjector.Project([
            Entry("first-id", instant, rarity: 4, pullNumber: 42),
            Entry("last-id", instant, rarity: 5, pullNumber: 40),
            Entry("unknown-rarity", instant, pullNumber: 43),
            Entry("after-unknown", instant, rarity: 4, pullNumber: 44)
        ], null, TimeZoneInfo.Utc).Rows;
        var timeline = WishHistoryProjector.PoolTimeline(rows);
        AssertSequence<int?>([44, 43, 42, 40], timeline.Select(item => item.Row.Entry.PullNumber), "same-time pull order");
        AssertSequence<int?>([null, 3, 2, null], timeline.Select(item => item.Distance), "known five-star interval");
        return Task.CompletedTask;
    }

    private static async Task InvalidBackup()
    {
        using var fixture = new TemporaryDirectory();
        var store = new WishHistoryStore(fixture.Root);
        await store.MergeAndSaveAsync([Entry("kept", "2026-05-01T00:00:00Z")], null, DateTimeOffset.UtcNow);
        var before = await File.ReadAllBytesAsync(store.HistoryPath);
        foreach (var json in new[] { "{}", """{"format":123}""",
            """{"format":"nikkiward-wish-v1","history":{"schemaVersion":1,"entries":[{}]}}""" })
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            try { await store.ImportAsync(stream); throw new Exception("invalid file accepted"); }
            catch (InvalidDataException) { }
            AssertSequence(before, await File.ReadAllBytesAsync(store.HistoryPath), "unchanged history");
        }
    }

    private static Task MergePreservesOlderRecords()
    {
        var older = Entry("wish-old", "2026-05-01T00:00:00Z", itemName: "Old Outfit");
        var newer = Entry("wish-new", "2026-06-01T00:00:00Z", itemName: "New Outfit");
        var merged = WishHistoryMerger.Merge([older], [newer]);
        AssertEqual(2, merged.Count, "merged record count");
        Assert(merged.Any(entry => entry.StableId == "wish-old"), "older record was removed");
        Assert(merged.Any(entry => entry.StableId == "wish-new"), "new record was not appended");
        return Task.CompletedTask;
    }

    private static Task MergeIsIdempotent()
    {
        var imported = Entry("wish-repeat", "2026-06-18T21:19:00Z", itemName: "Winter Breath", rarity: 5, pullNumber: 74);
        var first = WishHistoryMerger.Merge([], [imported]);
        var second = WishHistoryMerger.Merge(first, [imported]);
        AssertEqual(1, second.Count, "repeated import record count");
        AssertEqual("Winter Breath", second[0].ItemName, "repeated import item name");
        AssertEqual(74, second[0].PullNumber, "repeated import pull number");
        return Task.CompletedTask;
    }

    private static Task MergeEnrichesDuplicateRecords()
    {
        const string timestamp = "2026-06-18T21:19:00Z";
        var existing = Entry("wish-enrich", timestamp, itemName: "Winter Breath", rarity: 5, poolId: "limited-2026-06");
        var imported = Entry("wish-enrich", timestamp, pullNumber: 74, poolName: "Limited Resonance", imageUri: "https://example.test/winter-breath.png");
        var record = WishHistoryMerger.Merge([existing], [imported])[0];
        AssertEqual("Winter Breath", record.ItemName, "known item name must survive sparse import");
        AssertEqual(5, record.Rarity, "known rarity must survive sparse import");
        AssertEqual("limited-2026-06", record.PoolId, "known pool ID must survive sparse import");
        AssertEqual("Limited Resonance", record.PoolName, "new pool name must be retained");
        AssertEqual(74, record.PullNumber, "new pull number must be retained");
        AssertEqual("https://example.test/winter-breath.png", record.ImageUri, "new image URI must be retained");
        return Task.CompletedTask;
    }

    private static Task MergeUsesStableIdAndTimestamp()
    {
        const string timestamp = "2026-06-18T21:19:00Z";
        var left = Entry("wish-left", timestamp, itemName: "Left");
        var right = Entry("wish-right", timestamp, itemName: "Right");
        var correctedTimestamp = Entry("wish-left", "2026-06-18T21:20:00Z", itemName: "Corrected");
        var merged = WishHistoryMerger.Merge([left], [right, correctedTimestamp]);
        AssertEqual(3, merged.Count, "stable ID and timestamp form the deduplication key");
        AssertEqual(2, merged.Count(entry => entry.StableId == "wish-left"), "timestamp-distinct record count");
        return Task.CompletedTask;
    }

    private static Task ProjectionSortsAndMarksMonths()
    {
        var projected = WishHistoryProjector.Project(
            [
                Entry("may-old", "2026-05-01T08:00:00Z"),
                Entry("june-new", "2026-06-18T21:19:00Z"),
                Entry("june-old", "2026-06-02T04:00:00Z"),
                Entry("may-new", "2026-05-29T12:00:00Z"),
            ],
            summary: null,
            TimeZoneInfo.Utc);
        AssertSequence(["june-new", "june-old", "may-new", "may-old"], projected.Rows.Select(row => row.Entry.StableId), "newest-first order");
        Assert(projected.Rows[0].StartsMonth, "first row must start a month");
        AssertEqual("2026-06", projected.Rows[0].MonthKey, "June month key");
        Assert(!projected.Rows[1].StartsMonth, "second June row must not repeat marker");
        Assert(projected.Rows[2].StartsMonth, "first May row must start a month");
        AssertSequence(["2026-06", "2026-05"], projected.MonthMarkers.Select(marker => marker.MonthKey), "annotated month markers");
        return Task.CompletedTask;
    }

    private static Task ProjectionKeepsMissingSummaryUnknown()
    {
        var projected = WishHistoryProjector.Project(
            [Entry("only", "2026-06-18T21:19:00Z", rarity: 5)],
            new WishHistorySummary(),
            TimeZoneInfo.Utc);
        Assert(projected.Summary.TotalPulls is null, "missing total pulls was converted to zero");
        Assert(projected.Summary.FiveStarCount is null, "missing five-star count was inferred from partial rows");
        Assert(projected.Summary.AveragePullsPerFiveStar is null, "missing average pulls was converted to zero");
        Assert(projected.Summary.PullsUntilGuarantee is null, "missing guarantee distance was converted to zero");
        return Task.CompletedTask;
    }

    private static Task FiveStarRowsExposeSubtleGlow()
    {
        var fiveStar = WishHistoryProjector.Project(
            [Entry("five", "2026-06-18T21:19:00Z", rarity: 5)],
            summary: null,
            TimeZoneInfo.Utc).Rows[0];
        var fourStar = WishHistoryProjector.Project(
            [Entry("four", "2026-06-18T21:18:00Z", rarity: 4)],
            summary: null,
            TimeZoneInfo.Utc).Rows[0];
        AssertEqual(0.08d, fiveStar.FiveStarGlowOpacity, "five-star glow opacity");
        AssertEqual(0d, fourStar.FiveStarGlowOpacity, "non-five-star glow opacity");
        return Task.CompletedTask;
    }

    private static async Task StoreMergesAtomically()
    {
        using var fixture = new TemporaryDirectory();
        var store = new WishHistoryStore(fixture.Root);
        await store.MergeAndSaveAsync(
            [Entry("old", "2026-05-01T00:00:00Z", itemName: "Old")],
            new WishHistorySummary { TotalPulls = 1 },
            DateTimeOffset.Parse("2026-06-01T00:00:00Z"));
        await store.MergeAndSaveAsync(
            [Entry("new", "2026-06-02T00:00:00Z", itemName: "New")],
            summary: null,
            DateTimeOffset.Parse("2026-06-02T00:00:00Z"));

        var loaded = await store.LoadAsync();
        AssertEqual(2, loaded.Entries.Count, "store merge must retain older history");
        AssertEqual(1, loaded.Summary?.TotalPulls, "null summary must not erase known summary");
        Assert(File.Exists(store.HistoryPath), "history file must be committed");
        Assert(!Directory.EnumerateFiles(store.RootPath, "*.tmp").Any(), "temporary file must be removed");
        await store.ClearCacheOnlyAsync();
        Assert(File.Exists(store.HistoryPath), "cache clearing must not remove history");
    }

    private static WishHistoryEntry Entry(
        string stableId,
        string timestamp,
        string? itemName = null,
        int? rarity = null,
        int? pullNumber = null,
        string? poolId = null,
        string? poolName = null,
        string? imageUri = null) =>
        new()
        {
            StableId = stableId,
            TimestampUtc = DateTimeOffset.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture),
            ItemName = itemName,
            Rarity = rarity,
            PullNumber = pullNumber,
            PoolId = poolId,
            PoolName = poolName,
            ImageUri = imageUri,
        };

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
            throw new InvalidOperationException($"{message}: expected '{expected}', actual '{actual}'.");
        }
    }

    private static void AssertSequence<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string message)
    {
        var expectedValues = expected.ToArray();
        var actualValues = actual.ToArray();
        if (!expectedValues.SequenceEqual(actualValues))
        {
            throw new InvalidOperationException(
                $"{message}: expected '[{string.Join(", ", expectedValues)}]', " +
                $"actual '[{string.Join(", ", actualValues)}]'.");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), "NikkiwardWish", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
