using System.Text.Json;
using Nikkiward.ViewModels;

internal static class JournalSnapshotTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("journal snapshot persists only sourced fields", SnapshotRequiresFieldSources),
        ("journal snapshot preserves stable section identity and metric sources", SnapshotPreservesStructuralSources),
        ("journal cache rejects prior snapshot schemas", CacheRejectsPriorSchema),
        ("journal cache rejects paths outside its asset root", CacheRejectsEscapedAssetPath),
    ];

    private static async Task SnapshotRequiresFieldSources()
    {
        using var fixture = new JournalCacheFixture();
        var cache = new JournalSnapshotCache(fixture.Root);
        var snapshot = new JournalSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            SourcePagePath = "/tools/journal",
            LoginDays = "128",
            LoginDaysSource = "text-near:登录总天数",
            GameHours = "12h",
        };

        await cache.DownloadAndSaveAsync(snapshot);
        var loaded = await cache.LoadAsync();

        Assert(loaded is not null, "current snapshot should load");
        AssertEqual("128", loaded!.LoginDays, "sourced field value");
        AssertEqual("text-near:登录总天数", loaded.LoginDaysSource, "sourced field selector");
        Assert(loaded.GameHours is null, "unproven field must not persist");
        Assert(loaded.GameHoursSource is null, "unproven source must stay empty");
    }

    private static async Task SnapshotPreservesStructuralSources()
    {
        using var fixture = new JournalCacheFixture();
        var cache = new JournalSnapshotCache(fixture.Root);
        var snapshot = new JournalSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            SourcePagePath = "/tools/journal",
            Sections =
            [
                new JournalSectionSnapshot
                {
                    SectionKey = "anchor:wish-resonance",
                    Source = "title-text:wish-resonance",
                    Title = "心愿共鸣",
                    Metrics =
                    [
                        new JournalMetricSnapshot
                        {
                            Label = "共鸣次数",
                            Value = "42",
                            Source = "module-line:3",
                        },
                    ],
                },
            ],
        };

        await cache.DownloadAndSaveAsync(snapshot);
        var loaded = await cache.LoadAsync();

        AssertEqual("anchor:wish-resonance", loaded!.Sections.Single().SectionKey, "section key");
        AssertEqual("title-text:wish-resonance", loaded.Sections.Single().Source, "section source");
        AssertEqual("module-line:3", loaded.Sections.Single().Metrics.Single().Source, "metric source");
    }

    private static async Task CacheRejectsPriorSchema()
    {
        using var fixture = new JournalCacheFixture();
        var cache = new JournalSnapshotCache(fixture.Root);
        Directory.CreateDirectory(cache.RootPath);
        await File.WriteAllTextAsync(
            cache.SnapshotPath,
            "{\"schemaVersion\":1,\"capturedAtUtc\":\"2026-08-13T12:00:00Z\"}");

        Assert(await cache.LoadAsync() is null, "schema 1 must fail closed after source schema adoption");
    }

    private static async Task CacheRejectsEscapedAssetPath()
    {
        using var fixture = new JournalCacheFixture();
        var cache = new JournalSnapshotCache(fixture.Root);
        var escaped = Path.Combine(fixture.Root, "outside.png");
        await File.WriteAllBytesAsync(escaped, [0x89, 0x50, 0x4E, 0x47]);
        Directory.CreateDirectory(cache.RootPath);
        await File.WriteAllTextAsync(
            cache.SnapshotPath,
            JsonSerializer.Serialize(new JournalSnapshot
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                SourcePagePath = "/tools/journal",
                Resources =
                [
                    new JournalResourceSnapshot
                    {
                        Url = "https://assets.papegames.com/banner.png",
                        LocalFilePath = escaped,
                    },
                ],
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        var loaded = await cache.LoadAsync();
        Assert(loaded!.Resources.Single().LocalFilePath is null, "escaped file must not be trusted");
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
            throw new InvalidOperationException($"{message}: expected '{expected}', actual '{actual}'.");
        }
    }

    private sealed class JournalCacheFixture : IDisposable
    {
        public JournalCacheFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Nikkiward.Journal.Tests", Guid.NewGuid().ToString("N"));
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
