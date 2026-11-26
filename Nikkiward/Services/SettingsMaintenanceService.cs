using System.IO.Compression;
using System.Text.Json;
using Nikkiward.Features.Gallery;
using Nikkiward.Serialization;
using Nikkiward.Models;

namespace Nikkiward.Services;

public sealed record SettingsCacheStatistics(
    long LogBytes,
    long ImageBytes,
    long BrowserBytes,
    long GameResourceBytes,
    long LauncherBackgroundBytes);

public sealed record SettingsBackupReceipt(
    string FilePath,
    DateTimeOffset CreatedAtUtc,
    int FileCount);

public sealed record SettingsCacheClearReceipt(
    int DeletedFileCount,
    long DeletedBytes,
    IReadOnlyList<string> FailedPaths);

public sealed class SettingsMaintenanceService
{
    private static readonly string[] LogDirectories = ["Logs", "log", "crash"];
    private static readonly string[] ImageDirectories =
    [
        Path.Combine("GalleryCache", "Thumbnails"),
        Path.Combine("JournalCache", "Assets"),
        Path.Combine("JournalCache", "ResonanceAssets"),
    ];
    private static readonly string[] GameResourceDirectories = ["GameCache", "update"];
    private static readonly string[] BackgroundDirectories = ["ArtCache", "PaletteCache"];
    private const string ProtectionSettingsRecord = "Gallery/protection-settings.json";
    private static readonly NikkiwardJsonContext BackupJson = new(
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    private static readonly string[] BackupRecords =
    [
        "settings.pre-schema6.rollback.json",
        "settings.pre-schema7.rollback.json",
        "settings.pre-schema8.rollback.json",
        "settings.pre-schema9.rollback.json",
        "JournalCache/journal-snapshot.json",
        "JournalCache/resonance-history.json",
        PlayTimeStore.RelativePath,
        "WishHistory/wish-history.json",
        "Gallery/stars.json",
        "Gallery/default-favorites-v1.seed",
        ProtectionSettingsRecord,
        "Gallery/ProtectedFavorites/manifest.json",
        "ExternalTools/nikkigallery.json",
        "Plugins/nikkiward.photo-album-importer/plugin.json",
    ];

    public SettingsMaintenanceService(
        string settingsFilePath,
        string browserDataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(browserDataPath);
        SettingsFilePath = Path.GetFullPath(settingsFilePath);
        DataRoot = Path.GetDirectoryName(SettingsFilePath)
            ?? throw new InvalidOperationException("The settings data root cannot be resolved.");
        BrowserDataPath = Path.GetFullPath(browserDataPath);
        BackupFolder = Path.Combine(DataRoot, "Backups");
        LogFolder = Path.Combine(DataRoot, "Logs");
    }

    public string SettingsFilePath { get; }

    public string DataRoot { get; }

    public string BrowserDataPath { get; }

    public string BackupFolder { get; }

    public string LogFolder { get; }

    public Task<SettingsCacheStatistics> GetCacheStatisticsAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => GetCacheStatistics(cancellationToken), cancellationToken);

    public Task<SettingsBackupReceipt> CreateBackupAsync(CancellationToken cancellationToken = default, UserSettings? snapshot = null) =>
        CreateBackupAsync(cancellationToken, progress: null, snapshot);

    internal async Task<SettingsBackupReceipt> CreateBackupAsync(CancellationToken cancellationToken, IProgress<int>? progress, UserSettings? snapshot = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsBackupPathAllowed(BackupFolder))
        {
            throw new IOException("备份目录与浏览器数据重合或包含重解析点。");
        }

        Directory.CreateDirectory(BackupFolder);
        var createdAt = DateTimeOffset.UtcNow;
        var targetPath = Path.Combine(
            BackupFolder,
            $"NikkiwardData_{createdAt.ToLocalTime():yyyyMMdd_HHmmssfff}_{Guid.NewGuid():N}.zip");
        using var target = new AtomicFile(targetPath);
        var added = 0;
        using (var archive = new ZipArchive(target.Stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (!IsBackupPathAllowed(SettingsFilePath))
            {
                throw new IOException("设置文件与浏览器数据重合或包含重解析点。");
            }

            var settingsEntry = archive.CreateEntry("settings.json", CompressionLevel.Optimal);
            await using (var output = settingsEntry.Open())
            {
                FileStream? input;
                try
                {
                    input = File.Open(SettingsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                }
                catch (FileNotFoundException)
                {
                    input = null;
                }

                if (input is null)
                {
                    await JsonSerializer.SerializeAsync(output, snapshot ?? new UserSettings(),
                        JsonUserSettingsStore.UserSettingsJsonTypeInfo, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    using (input)
                    {
                        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            added++;
            progress?.Report(added);

            var records = new Queue<(string Name, bool CheckManifest)>(
                BackupRecords.Select(name => (name, false)));
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (records.TryDequeue(out var candidate))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.GetFullPath(candidate.Name, DataRoot);
                if (!visited.Add(path) || !IsBackupPathAllowed(path) || !File.Exists(path))
                {
                    continue;
                }

                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (candidate.Name == ProtectionSettingsRecord)
                {
                    await AddProtectionManifestsAsync(input, records, cancellationToken).ConfigureAwait(false);
                    input.Position = 0;
                }
                else if (candidate.CheckManifest)
                {
                    if (!await IsProtectionManifestAsync(input, cancellationToken).ConfigureAwait(false))
                    {
                        continue;
                    }
                    input.Position = 0;
                }

                var entry = archive.CreateEntry(candidate.Name, CompressionLevel.Optimal);
                await using var output = entry.Open();
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                added++;
                progress?.Report(added);
            }
        }

        target.Commit(overwrite: false, cancellationToken);
        return new SettingsBackupReceipt(targetPath, createdAt, added);
    }

    private async Task AddProtectionManifestsAsync(
        Stream input, Queue<(string Name, bool CheckManifest)> records, CancellationToken cancellationToken)
    {
        GalleryFavoriteProtectionPreferences? preferences;
        try
        {
            preferences = await JsonSerializer.DeserializeAsync(input, BackupJson.GalleryFavoriteProtectionPreferences, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Preserve a damaged preferences file without following any paths from it.
            return;
        }

        if (preferences?.SchemaVersion != GalleryFavoriteProtectionPreferences.CurrentSchemaVersion || preferences.KnownRootPaths is null)
        {
            return;
        }

        foreach (var root in preferences.KnownRootPaths.Prepend(preferences.ActiveRootPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || !IsUnderRoot(root, DataRoot) ||
                IsUnderRoot(root, BackupFolder) || !IsBackupPathAllowed(root))
            {
                continue;
            }

            var manifest = Path.Combine(root, "manifest.json");
            records.Enqueue((Path.GetRelativePath(DataRoot, manifest).Replace('\\', '/'), true));
        }
    }

    private static async Task<bool> IsProtectionManifestAsync(Stream input, CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await JsonSerializer.DeserializeAsync(
                input, GalleryFavoriteProtectionStore.ManifestJsonTypeInfo, cancellationToken).ConfigureAwait(false);
            return manifest is { SchemaVersion: GalleryFavoriteProtectionManifest.CurrentSchemaVersion, Entries: not null };
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private bool IsBackupPathAllowed(string path)
    {
        if (IsUnderRoot(path, BrowserDataPath))
        {
            return false;
        }

        var parent = Path.GetDirectoryName(path);
        if (parent is not null && !IsBackupPathAllowed(parent))
        {
            return false;
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
    }

    public Task<SettingsCacheClearReceipt> ClearCachesAsync(
        bool clearLauncherBackgroundFiles,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => ClearCaches(clearLauncherBackgroundFiles, cancellationToken),
            cancellationToken);

    public Task DeleteSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(SettingsFilePath))
                {
                    File.Delete(SettingsFilePath);
                }
            },
            cancellationToken);

    private SettingsCacheStatistics GetCacheStatistics(CancellationToken cancellationToken)
    {
        var logs = SumRelativeDirectories(LogDirectories, cancellationToken);
        var images = SumRelativeDirectories(ImageDirectories, cancellationToken);
        var browser = BrowserCacheDirectories()
            .Where(IsAllowedExternalDirectory)
            .Sum(path => GetDirectorySize(path, cancellationToken));
        var game = SumRelativeDirectories(GameResourceDirectories, cancellationToken);
        var backgrounds = SumRelativeDirectories(BackgroundDirectories, cancellationToken);
        return new SettingsCacheStatistics(logs, images, browser, game, backgrounds);
    }

    private SettingsCacheClearReceipt ClearCaches(
        bool clearLauncherBackgroundFiles,
        CancellationToken cancellationToken)
    {
        var targets = LogDirectories
            .Concat(ImageDirectories)
            .Concat(GameResourceDirectories)
            .Select(relative => Path.Combine(DataRoot, relative))
            .Concat(BrowserCacheDirectories())
            .ToList();
        if (clearLauncherBackgroundFiles)
        {
            targets.AddRange(BackgroundDirectories.Select(relative => Path.Combine(DataRoot, relative)));
        }

        var deletedFiles = 0;
        var deletedBytes = 0L;
        var failed = new List<string>();
        foreach (var target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAllowedExternalDirectory(target) || !Directory.Exists(target))
            {
                continue;
            }

            try
            {
                var (count, bytes) = InspectDirectory(target, cancellationToken);
                Directory.Delete(target, recursive: true);
                deletedFiles += count;
                deletedBytes += bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(target);
            }
        }

        Directory.CreateDirectory(LogFolder);
        return new SettingsCacheClearReceipt(deletedFiles, deletedBytes, failed);
    }

    private long SumRelativeDirectories(
        IEnumerable<string> directories,
        CancellationToken cancellationToken) =>
        directories.Sum(relative => GetDirectorySize(
            Path.Combine(DataRoot, relative),
            cancellationToken));

    private static long GetDirectorySize(string path, CancellationToken cancellationToken) =>
        InspectDirectory(path, cancellationToken).Bytes;

    private static (int Count, long Bytes) InspectDirectory(
        string path,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path))
        {
            return (0, 0);
        }

        var count = 0;
        var bytes = 0L;
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    bytes += new FileInfo(filePath).Length;
                    count++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return (count, bytes);
    }

    private bool IsAllowedExternalDirectory(string path) =>
        IsUnderRoot(path, DataRoot) ||
        IsUnderRoot(path, Path.GetDirectoryName(BrowserDataPath)!);

    private IEnumerable<string> BrowserCacheDirectories()
    {
        var profileRoot = Path.Combine(BrowserDataPath, "EBWebView", "Default");
        yield return Path.Combine(profileRoot, "Cache");
        yield return Path.Combine(profileRoot, "Code Cache");
        yield return Path.Combine(profileRoot, "GPUCache");
        yield return Path.Combine(profileRoot, "Service Worker", "CacheStorage");
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }
}
