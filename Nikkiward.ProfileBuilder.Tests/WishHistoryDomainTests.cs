using Nikkiward.ViewModels;

internal static class WishHistoryDomainTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("wish history merge preserves older records and appends new records", MergePreservesOlderRecords),
        ("wish history merge is idempotent for repeated imports", MergeIsIdempotent),
        ("wish history merge enriches duplicate records without erasing known fields", MergeEnrichesDuplicateRecords),
        ("wish history keeps distinct stable IDs at the same timestamp", MergeUsesStableIdAndTimestamp),
        ("wish history projection sorts newest first and marks month boundaries", ProjectionSortsAndMarksMonths),
        ("wish history projection keeps missing summary values unknown", ProjectionKeepsMissingSummaryUnknown),
        ("wish history five-star rows expose only a subtle glow", FiveStarRowsExposeSubtleGlow),
        ("wish history store merges atomically and keeps history outside cache clearing", StoreMergesAtomically),
    ];

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
