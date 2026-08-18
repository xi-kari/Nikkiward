using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nikkiward.Models;
using Nikkiward.Services;

internal static class ApplicationSettingsTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("application settings defaults match the visible controls", DefaultsMatchVisibleControls),
        ("application settings round trip through schema 7", SettingsRoundTrip),
        ("language preference and default install root drive runtime consumers", LanguageAndDefaultStoreRoot),
        ("schema 6 adds application settings with a rollback copy", Schema6MigrationAddsSections),
        ("cache clearing preserves the browser session and optional backgrounds", CacheClearingPreservesSession),
        ("data backup contains durable settings records", BackupContainsDurableRecords),
        ("data backups remain unique within the same timestamp", BackupsRemainUnique),
        ("data root selection requires a migrated settings file", DataRootSelectionRequiresSettings),
    ];

    private static Task DefaultsMatchVisibleControls()
    {
        var settings = new UserSettings();
        AssertEqual(7, settings.SchemaVersion, "schema version");
        AssertEqual("zh-CN", settings.General.LanguageTag, "language");
        AssertEqual("Alt+S", settings.General.MainWindowHotkey, "main window hotkey");
        Assert(settings.General.EnableProfileQuickSwitcher, "profile quick switcher");
        Assert(settings.Download.EnableHardLinks, "hard links");
        AssertEqual(0, settings.Download.SpeedLimitKbps, "speed limit");
        AssertEqual("Alt+D", settings.Screenshot.Hotkey, "screenshot hotkey");
        AssertEqual(ScreenshotImageFormat.Png, settings.Screenshot.Format, "screenshot format");
        AssertEqual(ScreenshotImageQuality.High, settings.Screenshot.Quality, "screenshot quality");
        Assert(settings.Screenshot.EnableColorManagement, "color management");
        Assert(settings.Screenshot.AutoCopyToClipboard, "clipboard");
        Assert(settings.Screenshot.AutoConvertHdrToSdr, "HDR conversion");
        return Task.CompletedTask;
    }

    private static async Task SettingsRoundTrip()
    {
        using var fixture = new TemporaryFolder();
        var store = new JsonUserSettingsStore(fixture.Path);
        await store.SaveAsync(new UserSettings
        {
            General = new GeneralSettings
            {
                LanguageTag = " system ",
                MainWindowHotkey = " Ctrl+Shift+N ",
                CloseWindowBehavior = CloseWindowBehavior.MinimizeToTray,
                EnableProfileQuickSwitcher = false,
            },
            Download = new DownloadSettings
            {
                DefaultGameInstallPath = " D:\\Games\\Nikki ",
                EnableHardLinks = false,
                SpeedLimitKbps = 4096,
            },
            FileManagement = new FileManagementSettings
            {
                UserDataFolderPath = " D:\\NikkiwardData ",
                ClearLauncherBackgroundFiles = true,
            },
            Screenshot = new ScreenshotSettings
            {
                FolderPath = " D:\\Pictures\\Nikki ",
                Hotkey = " F10 ",
                Format = ScreenshotImageFormat.JpegXl,
                Quality = ScreenshotImageQuality.Lossless,
                EnableColorManagement = false,
                AutoCopyToClipboard = false,
                AutoConvertHdrToSdr = false,
            },
        });

        var loaded = await store.LoadAsync();
        AssertEqual("system", loaded.General.LanguageTag, "language");
        AssertEqual("Ctrl+Shift+N", loaded.General.MainWindowHotkey, "main window hotkey");
        AssertEqual(CloseWindowBehavior.MinimizeToTray, loaded.General.CloseWindowBehavior, "close behavior");
        Assert(!loaded.General.EnableProfileQuickSwitcher, "profile quick switcher");
        AssertEqual("D:\\Games\\Nikki", loaded.Download.DefaultGameInstallPath, "install path");
        Assert(!loaded.Download.EnableHardLinks, "hard link setting");
        AssertEqual(4096, loaded.Download.SpeedLimitKbps, "speed limit");
        AssertEqual("D:\\NikkiwardData", loaded.FileManagement.UserDataFolderPath, "data folder");
        Assert(loaded.FileManagement.ClearLauncherBackgroundFiles, "background cleanup");
        AssertEqual("D:\\Pictures\\Nikki", loaded.Screenshot.FolderPath, "screenshot folder");
        AssertEqual("F10", loaded.Screenshot.Hotkey, "screenshot hotkey");
        AssertEqual(ScreenshotImageFormat.JpegXl, loaded.Screenshot.Format, "format");
        AssertEqual(ScreenshotImageQuality.Lossless, loaded.Screenshot.Quality, "quality");
        Assert(!loaded.Screenshot.EnableColorManagement, "color management");
        Assert(!loaded.Screenshot.AutoCopyToClipboard, "clipboard");
        Assert(!loaded.Screenshot.AutoConvertHdrToSdr, "HDR conversion");
    }

    private static Task LanguageAndDefaultStoreRoot()
    {
        AssertEqual(
            ApplicationSettingsValidator.SystemLanguageTag,
            ApplicationSettingsValidator.NormalizeLanguageTag(" SYSTEM "),
            "system language normalization");
        AssertEqual(
            ApplicationSettingsValidator.SimplifiedChineseLanguageTag,
            ApplicationSettingsValidator.NormalizeLanguageTag("en-US"),
            "unsupported language falls back to Chinese");
        Assert(ApplicationLanguageRuntime.ResolveCulture("system") is null, "system culture follows Windows");
        AssertEqual(
            "zh-CN",
            ApplicationLanguageRuntime.ResolveCulture("zh-CN")!.Name,
            "Chinese runtime culture");
        AssertEqual(
            "D:\\Games\\Nikki\\NikkiwardStore",
            ApplicationSettingsValidator.ResolveDefaultChannelStoreRoot(
                new DownloadSettings { DefaultGameInstallPath = " D:\\Games\\Nikki " }),
            "default channel store root");
        AssertEqual(
            "D:\\Games\\NikkiwardStore",
            ApplicationSettingsValidator.ResolveDefaultChannelStoreRoot(
                new DownloadSettings { DefaultGameInstallPath = "D:\\Games\\NikkiwardStore" }),
            "existing store root stays stable");
        AssertEqual(
            null,
            ApplicationSettingsValidator.ResolveDefaultChannelStoreRoot(new DownloadSettings()),
            "unset install root");
        return Task.CompletedTask;
    }

    private static async Task Schema6MigrationAddsSections()
    {
        using var fixture = new TemporaryFolder();
        var store = new JsonUserSettingsStore(fixture.Path);
        await store.SaveAsync(new UserSettings());
        var document = JsonNode.Parse(await File.ReadAllTextAsync(store.SettingsFilePath))!.AsObject();
        document["schemaVersion"] = 6;
        document.Remove("general");
        document.Remove("download");
        document.Remove("fileManagement");
        document.Remove("screenshot");
        var schema6 = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(store.SettingsFilePath, schema6);

        var loaded = await store.LoadAsync();

        AssertEqual(7, loaded.SchemaVersion, "migrated schema");
        Assert(loaded.Download.EnableHardLinks, "hard link default");
        AssertEqual(ScreenshotImageFormat.Png, loaded.Screenshot.Format, "screenshot default");
        Assert(File.Exists(store.Schema7MigrationRollbackFilePath), "schema 7 rollback copy");
        AssertEqual(
            schema6,
            await File.ReadAllTextAsync(store.Schema7MigrationRollbackFilePath),
            "schema 6 rollback bytes");
    }

    private static async Task CacheClearingPreservesSession()
    {
        using var fixture = new TemporaryFolder();
        var dataRoot = Path.Combine(fixture.Path, "Nikkiward");
        var settingsPath = Path.Combine(dataRoot, "settings.json");
        var browserPath = Path.Combine(dataRoot, "JournalWebView2");
        var browserCache = Path.Combine(browserPath, "EBWebView", "Default", "Cache");
        var browserCookies = Path.Combine(browserPath, "EBWebView", "Default", "Cookies");
        var thumbnailCache = Path.Combine(dataRoot, "GalleryCache", "Thumbnails");
        var backgroundCache = Path.Combine(dataRoot, "ArtCache");
        WriteBytes(Path.Combine(dataRoot, "Logs", "current.log"), 7);
        WriteBytes(Path.Combine(thumbnailCache, "thumb.jpg"), 11);
        WriteBytes(Path.Combine(browserCache, "data.bin"), 13);
        WriteBytes(browserCookies, 17);
        WriteBytes(Path.Combine(backgroundCache, "plate.jpg"), 19);

        var service = new SettingsMaintenanceService(settingsPath, browserPath);
        var before = await service.GetCacheStatisticsAsync();
        AssertEqual(7L, before.LogBytes, "log bytes");
        AssertEqual(11L, before.ImageBytes, "image bytes");
        AssertEqual(13L, before.BrowserBytes, "browser bytes");
        AssertEqual(19L, before.LauncherBackgroundBytes, "background bytes");

        var first = await service.ClearCachesAsync(false);
        Assert(first.DeletedFileCount >= 3, "cleared file count");
        Assert(File.Exists(browserCookies), "browser cookies must remain");
        Assert(File.Exists(Path.Combine(backgroundCache, "plate.jpg")), "background must remain");

        _ = await service.ClearCachesAsync(true);
        Assert(!Directory.Exists(backgroundCache), "background cache must be optional");
    }

    private static async Task BackupContainsDurableRecords()
    {
        using var fixture = new TemporaryFolder();
        var dataRoot = Path.Combine(fixture.Path, "Nikkiward");
        var settingsPath = Path.Combine(dataRoot, "settings.json");
        var browserPath = Path.Combine(dataRoot, "JournalWebView2");
        WriteBytes(settingsPath, 23);
        WriteBytes(Path.Combine(dataRoot, "JournalCache", "journal-snapshot.json"), 29);
        WriteBytes(Path.Combine(dataRoot, "GalleryCache", "Thumbnails", "thumb.jpg"), 31);

        var service = new SettingsMaintenanceService(settingsPath, browserPath);
        var receipt = await service.CreateBackupAsync();

        Assert(File.Exists(receipt.FilePath), "backup file");
        AssertEqual(2, receipt.FileCount, "backup record count");
        using var archive = ZipFile.OpenRead(receipt.FilePath);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert(names.Contains("settings.json", StringComparer.Ordinal), "settings entry");
        Assert(names.Contains("JournalCache/journal-snapshot.json", StringComparer.Ordinal), "journal entry");
        Assert(!names.Any(name => name.EndsWith("thumb.jpg", StringComparison.Ordinal)), "thumbnail cache excluded");
    }

    private static async Task BackupsRemainUnique()
    {
        using var fixture = new TemporaryFolder();
        var dataRoot = Path.Combine(fixture.Path, "Nikkiward");
        var settingsPath = Path.Combine(dataRoot, "settings.json");
        WriteBytes(settingsPath, 23);

        var service = new SettingsMaintenanceService(
            settingsPath,
            Path.Combine(dataRoot, "JournalWebView2"));
        var first = await service.CreateBackupAsync();
        var second = await service.CreateBackupAsync();

        Assert(
            !string.Equals(first.FilePath, second.FilePath, StringComparison.OrdinalIgnoreCase),
            "backup paths must be unique");
        Assert(File.Exists(first.FilePath), "first backup file");
        Assert(File.Exists(second.FilePath), "second backup file");
    }

    private static Task DataRootSelectionRequiresSettings()
    {
        using var fixture = new TemporaryFolder();
        var fallback = Path.Combine(fixture.Path, "fallback");
        var configured = Path.Combine(fixture.Path, "configured");
        Directory.CreateDirectory(fallback);
        Directory.CreateDirectory(configured);

        AssertEqual(
            Path.GetFullPath(fallback),
            ApplicationDataPaths.ResolveRoot(configured, fallback),
            "configured root without settings");

        File.WriteAllText(Path.Combine(configured, "settings.json"), "{}");
        AssertEqual(
            Path.GetFullPath(configured),
            ApplicationDataPaths.ResolveRoot(configured, fallback),
            "configured root with settings");
        AssertEqual(
            Path.GetFullPath(configured),
            ApplicationDataPaths.ValidateExistingRoot(configured, requireSettings: true),
            "writable configured root");
        return Task.CompletedTask;
    }

    private static void WriteBytes(string path, int count)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Range(0, count).Select(value => (byte)value).ToArray());
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Nikkiward.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
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
