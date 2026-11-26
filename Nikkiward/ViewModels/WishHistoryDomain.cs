using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Nikkiward.Serialization;
using Nikkiward.Services;

namespace Nikkiward.ViewModels;

public sealed record WishHistoryEntry
{
    public string StableId { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; }
    public string? PoolId { get; set; }
    public string? PoolName { get; set; }
    public string? ItemId { get; set; }
    public string? ItemName { get; set; }
    public int? Rarity { get; set; }
    public int? PullNumber { get; set; }
    public string? ImageUri { get; set; }
    public string? TimestampSource { get; set; }
}

public sealed record WishHistorySummary
{
    public int? TotalPulls { get; init; }
    public int? FiveStarCount { get; init; }
    public decimal? AveragePullsPerFiveStar { get; init; }
    public int? PullsUntilGuarantee { get; init; }
}

public sealed record WishHistoryRow
{
    public WishHistoryEntry Entry { get; set; } = new();
    public DateTimeOffset LocalTimestamp { get; set; }
    public string MonthKey { get; set; } = string.Empty;
    public string MonthLabel { get; set; } = string.Empty;
    public bool StartsMonth { get; set; }

    public double FiveStarGlowOpacity => Entry.Rarity == 5 ? 0.08d : 0d;

    public string MonthLabelDisplay => StartsMonth ? MonthLabel : string.Empty;

    public string PoolLabel =>
        Entry.PoolName ??
        Entry.PoolId ??
        "暂无数据";

    public string ItemLabel => Entry.ItemName ?? Entry.ItemId ?? "暂无数据";

    public string RarityLabel =>
        Entry.Rarity is >= 1 and <= 5
            ? new string('★', Entry.Rarity.Value)
            : "暂无数据";

    public string PullLabel =>
        Entry.PullNumber is int pullNumber
            ? $"第 {pullNumber} 抽"
            : "暂无数据";

    public string TimeLabel =>
        LocalTimestamp.ToString("MM/dd HH:mm", CultureInfo.CurrentCulture);
}

public sealed record WishHistoryMonthMarker
{
    public required string MonthKey { get; init; }
    public required string MonthLabel { get; init; }
    public required int RowIndex { get; init; }
}

public sealed record WishHistoryProjection
{
    public required IReadOnlyList<WishHistoryRow> Rows { get; init; }
    public required IReadOnlyList<WishHistoryMonthMarker> MonthMarkers { get; init; }
    public required WishHistorySummary Summary { get; init; }
}

public sealed record WishPoolFilter
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public double IndicatorOpacity => IsSelected ? 1d : 0d;
}

public static class WishHistoryMerger
{
    public static IReadOnlyList<WishHistoryEntry> Merge(
        IEnumerable<WishHistoryEntry> existing,
        IEnumerable<WishHistoryEntry> imported)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(imported);

        var merged = new List<WishHistoryEntry>();
        var positions = new Dictionary<WishHistoryKey, int>();
        AddEntries(existing, merged, positions);
        AddEntries(imported, merged, positions);
        return merged.ToArray();
    }

    internal static WishHistoryEntry Normalize(WishHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var stableId = entry.StableId?.Trim();
        if (string.IsNullOrWhiteSpace(stableId))
        {
            throw new ArgumentException("A wish history entry requires a stable ID.", nameof(entry));
        }

        if (entry.TimestampUtc == default)
        {
            throw new ArgumentException("A wish history entry requires a timestamp.", nameof(entry));
        }

        return entry with
        {
            StableId = stableId,
            TimestampUtc = entry.TimestampUtc.ToUniversalTime(),
            PoolId = NormalizeOptionalText(entry.PoolId),
            PoolName = NormalizeOptionalText(entry.PoolName),
            ItemId = NormalizeOptionalText(entry.ItemId),
            ItemName = NormalizeOptionalText(entry.ItemName),
            ImageUri = NormalizeOptionalText(entry.ImageUri),
        };
    }

    private static void AddEntries(
        IEnumerable<WishHistoryEntry> source,
        List<WishHistoryEntry> merged,
        Dictionary<WishHistoryKey, int> positions)
    {
        foreach (var candidate in source)
        {
            var entry = Normalize(candidate);
            var key = new WishHistoryKey(entry.StableId, entry.TimestampUtc.UtcDateTime.Ticks);
            if (positions.TryGetValue(key, out var position))
            {
                merged[position] = MergeFields(merged[position], entry);
                continue;
            }

            positions.Add(key, merged.Count);
            merged.Add(entry);
        }
    }

    private static WishHistoryEntry MergeFields(
        WishHistoryEntry existing,
        WishHistoryEntry imported) =>
        existing with
        {
            PoolId = PreferKnown(existing.PoolId, imported.PoolId),
            PoolName = PreferKnown(existing.PoolName, imported.PoolName),
            ItemId = PreferKnown(existing.ItemId, imported.ItemId),
            ItemName = PreferKnown(existing.ItemName, imported.ItemName),
            Rarity = existing.Rarity ?? imported.Rarity,
            PullNumber = existing.PullNumber ?? imported.PullNumber,
            ImageUri = PreferKnown(existing.ImageUri, imported.ImageUri),
        };

    private static string? PreferKnown(string? existing, string? imported) =>
        !string.IsNullOrWhiteSpace(existing) ? existing : imported;

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private readonly record struct WishHistoryKey(string StableId, long TimestampTicks);
}

public static class WishHistoryProjector
{
    internal static (WishHistoryRow Row, int? Distance)[] PoolTimeline(IEnumerable<WishHistoryRow> pool)
    {
        var ordered = pool.OrderBy(r => r.Entry.TimestampUtc).ThenBy(r => r.Entry.PullNumber);
        var timeline = new List<(WishHistoryRow, int?)>();
        int? previousFive = null;
        foreach (var row in ordered)
        {
            var distance = row.Entry.PullNumber - previousFive;
            timeline.Add((row, distance >= 0 ? distance : null));
            if (row.Entry.Rarity == 5) previousFive = row.Entry.PullNumber;
            else if (row.Entry.Rarity == null) previousFive = null;
        }
        timeline.Reverse();
        return timeline.ToArray();
    }

    public static WishHistoryProjection Project(
        IEnumerable<WishHistoryEntry> entries,
        WishHistorySummary? summary,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(timeZone);

        var orderedEntries = WishHistoryMerger.Merge([], entries)
            .OrderByDescending(entry => entry.TimestampUtc)
            .ThenBy(entry => entry.StableId, StringComparer.Ordinal)
            .ToArray();
        var rows = new List<WishHistoryRow>(orderedEntries.Length);
        var markers = new List<WishHistoryMonthMarker>();
        string? previousMonthKey = null;

        foreach (var entry in orderedEntries)
        {
            var localTimestamp = TimeZoneInfo.ConvertTime(entry.TimestampUtc, timeZone);
            var monthKey = localTimestamp.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var startsMonth = !string.Equals(
                monthKey,
                previousMonthKey,
                StringComparison.Ordinal);
            var monthLabel = localTimestamp.ToString("yyyy年M月", CultureInfo.InvariantCulture);
            if (startsMonth)
            {
                markers.Add(new WishHistoryMonthMarker
                {
                    MonthKey = monthKey,
                    MonthLabel = monthLabel,
                    RowIndex = rows.Count,
                });
            }

            rows.Add(new WishHistoryRow
            {
                Entry = entry,
                LocalTimestamp = localTimestamp,
                MonthKey = monthKey,
                MonthLabel = monthLabel,
                StartsMonth = startsMonth,
            });
            previousMonthKey = monthKey;
        }

        return new WishHistoryProjection
        {
            Rows = rows.ToArray(),
            MonthMarkers = markers.ToArray(),
            Summary = summary is null ? new WishHistorySummary() : summary with { },
        };
    }
}

public sealed partial class WishHistoryStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private static readonly JsonTypeInfo<WishHistoryStoreSnapshot> WishHistoryJsonTypeInfo =
        new NikkiwardJsonContext(JsonOptions).WishHistoryStoreSnapshot;

    public WishHistoryStore(string? localApplicationDataPath = null)
    {
        var applicationRoot = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? ApplicationDataPaths.Root
            : Path.Combine(
                Path.GetFullPath(localApplicationDataPath),
                "Nikkiward");
        RootPath = Path.Combine(applicationRoot, "WishHistory");
        HistoryPath = Path.Combine(RootPath, "wish-history.json");
    }

    public string RootPath { get; }
    public string HistoryPath { get; }

    public async Task<WishHistoryStoreSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = new FileStream(
                HistoryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ReadSnapshot(document.RootElement, "本机心愿历史");
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new WishHistoryStoreSnapshot();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("本机心愿历史 JSON 损坏，原文件已保留。", ex);
        }
    }

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task ExportAsync(Stream output)
    {
        var snapshot = await LoadAsync();
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("format", "nikkiward-wish-v1");
        writer.WritePropertyName("history");
        JsonSerializer.Serialize(writer, snapshot, WishHistoryJsonTypeInfo);
        writer.WriteEndObject();
        await writer.FlushAsync();
    }

    public async Task<WishHistoryStoreSnapshot> ImportAsync(Stream input)
    {
        using var document = await JsonDocument.ParseAsync(input);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || !format.ValueEquals("nikkiward-wish-v1") ||
            !root.TryGetProperty("history", out var history))
            throw new InvalidDataException("请选择 Nikkiward 导出的心愿共鸣 JSON 文件。");

        var snapshot = ReadSnapshot(history, "导入文件");
        return await SaveAsync(snapshot.Entries, snapshot.Summary, DateTimeOffset.UtcNow, true, default);
    }

    private static WishHistoryStoreSnapshot ReadSnapshot(JsonElement history, string source)
    {
        if (history.ValueKind != JsonValueKind.Object ||
            !history.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var schema))
            throw new InvalidDataException($"{source}缺少有效版本号，未修改本机历史。");
        if (schema != CurrentSchemaVersion)
            throw new InvalidDataException($"{source}版本 {schema} 不受支持，未修改本机历史。");
        if (!history.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{source}的 entries 应为记录数组，未修改本机历史。");

        var snapshot = history.Deserialize(WishHistoryJsonTypeInfo)!;
        if (snapshot.Entries.Any(e => e == null || string.IsNullOrWhiteSpace(e.StableId) ||
            e.TimestampUtc == default || e.Rarity is < 1 or > 5))
            throw new InvalidDataException($"{source}包含无效的共鸣记录，未修改本机历史。");
        return snapshot;
    }

    public Task<WishHistoryStoreSnapshot> MergeAndSaveAsync(
        IEnumerable<WishHistoryEntry> imported, WishHistorySummary? summary,
        DateTimeOffset capturedAtUtc, CancellationToken cancellationToken = default) =>
        SaveAsync(imported, summary, capturedAtUtc, false, cancellationToken);

    private async Task<WishHistoryStoreSnapshot> SaveAsync(
        IEnumerable<WishHistoryEntry> imported, WishHistorySummary? summary,
        DateTimeOffset capturedAtUtc, bool backup, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await MergeCoreAsync(imported, summary, capturedAtUtc, backup, cancellationToken).ConfigureAwait(false); }
        finally { _writeLock.Release(); }
    }

    private async Task<WishHistoryStoreSnapshot> MergeCoreAsync(
        IEnumerable<WishHistoryEntry> imported,
        WishHistorySummary? summary,
        DateTimeOffset capturedAtUtc,
        bool backup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imported);
        var existing = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var importedEntries = imported.ToArray();
        var existingByStableId = existing.Entries
            .GroupBy(entry => entry.StableId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        for (var index = 0; index < importedEntries.Length; index++)
        {
            var entry = importedEntries[index];
            if (!backup && entry.TimestampSource == "capture" &&
                existingByStableId.TryGetValue(entry.StableId, out var known))
            {
                importedEntries[index] = entry with
                {
                    TimestampUtc = known.TimestampUtc,
                    TimestampSource = known.TimestampSource,
                };
            }
        }

        var entries = WishHistoryMerger.Merge(existing.Entries, importedEntries);
        var merged = new WishHistoryStoreSnapshot
        {
            SchemaVersion = CurrentSchemaVersion,
            CapturedAtUtc = capturedAtUtc.ToUniversalTime(),
            Entries = entries.ToList(),
            Summary = backup ? existing.Summary ?? summary : summary ?? existing.Summary,
        };

        Directory.CreateDirectory(RootPath);
        var temporaryPath = $"{HistoryPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                32 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    merged,
                    WishHistoryJsonTypeInfo,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, HistoryPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return merged;
    }

    public Task ClearCacheOnlyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

}

public sealed class WishHistoryStoreSnapshot
{
    public int SchemaVersion { get; set; } = WishHistoryStore.CurrentSchemaVersion;
    public DateTimeOffset CapturedAtUtc { get; set; }
    public List<WishHistoryEntry> Entries { get; set; } = [];
    public WishHistorySummary? Summary { get; set; }
}

public static class WishHistoryCaptureAdapter
{
    public static IReadOnlyList<WishHistoryEntry> FromRows(
        IEnumerable<WishHistoryCaptureRow> rows,
        DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var captureTime = capturedAtUtc.ToUniversalTime();
        return rows.Select((row, index) => new WishHistoryEntry
        {
            StableId = string.IsNullOrWhiteSpace(row.StableId)
                ? $"{NormalizePart(row.PoolId)}|{NormalizePart(row.ImageUri)}|{row.SlotIndex}|{index}"
                : row.StableId.Trim(),
            TimestampUtc = row.TimestampUtc?.ToUniversalTime() ?? captureTime,
            PoolId = row.PoolId,
            PoolName = row.PoolName,
            ItemId = row.ItemId ?? row.ImageUri,
            ItemName = row.ItemName,
            Rarity = row.Rarity is >= 1 and <= 5 ? row.Rarity : null,
            PullNumber = row.PullNumber is > 0 and <= 9999 ? row.PullNumber : null,
            ImageUri = row.ImageUri,
            TimestampSource = row.TimestampUtc is null ? "capture" : "item",
        }).ToArray();
    }

    private static string NormalizePart(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim().ToLowerInvariant();
        return normalized.Length <= 160 ? normalized : normalized[..160];
    }
}

public sealed record WishHistoryCaptureRow
{
    public string? StableId { get; init; }
    public DateTimeOffset? TimestampUtc { get; init; }
    public string? PoolId { get; init; }
    public string? PoolName { get; init; }
    public string? ItemId { get; init; }
    public string? ItemName { get; init; }
    public int? Rarity { get; init; }
    public int? PullNumber { get; init; }
    public string? ImageUri { get; init; }
    public int SlotIndex { get; init; }
}
