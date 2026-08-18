using System.IO.Compression;

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

    public async Task<SettingsBackupReceipt> CreateBackupAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(BackupFolder);
        var createdAt = DateTimeOffset.UtcNow;
        var targetPath = Path.Combine(
            BackupFolder,
            $"NikkiwardData_{createdAt.ToLocalTime():yyyyMMdd_HHmmssfff}_{Guid.NewGuid():N}.zip");
        var candidates = Directory.Exists(DataRoot)
            ? Directory.EnumerateFiles(DataRoot, "*", SearchOption.AllDirectories)
                .Where(IsBackupCandidate)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        await using var target = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var archive = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
        var added = 0;
        foreach (var path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(
                Path.GetRelativePath(DataRoot, path).Replace('\\', '/'),
                CompressionLevel.Optimal);
            await using var input = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = entry.Open();
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            added++;
        }

        archive.Dispose();
        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        target.Flush(flushToDisk: true);
        return new SettingsBackupReceipt(targetPath, createdAt, added);
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

    private bool IsBackupCandidate(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsUnderRoot(fullPath, DataRoot) || IsUnderRoot(fullPath, BackupFolder))
        {
            return false;
        }

        var extension = Path.GetExtension(fullPath);
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
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
