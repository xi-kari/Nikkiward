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
        ("application settings round trip through the current schema", SettingsRoundTrip),
        ("language preference and default install root drive runtime consumers", LanguageAndDefaultStoreRoot),
        ("schema 6 adds application settings with a rollback copy", Schema6MigrationAddsSections),
        (nameof(Schema7StartsWithoutWindowPlacement), Schema7StartsWithoutWindowPlacement),
        (nameof(WindowPlacementAndPreferencesSurviveConcurrentSaves), WindowPlacementAndPreferencesSurviveConcurrentSaves),
        (nameof(WindowClosePreservesReplacedSettingsFile), WindowClosePreservesReplacedSettingsFile),
        (nameof(ExitAfterSettingsDeletionKeepsFileDeleted), ExitAfterSettingsDeletionKeepsFileDeleted),
        ("cache clearing preserves the browser session and optional backgrounds", CacheClearingPreservesSession),
        ("data backup contains durable settings records", BackupContainsDurableRecords),
        ("data backups remain unique within the same timestamp", BackupsRemainUnique),
        ("data root selection requires a migrated settings file", DataRootSelectionRequiresSettings),
    ];

    private static Task DefaultsMatchVisibleControls()
    {
        var settings = new UserSettings();
        AssertEqual(UserSettings.CurrentSchemaVersion, settings.SchemaVersion, "schema version");
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

        AssertEqual(UserSettings.CurrentSchemaVersion, loaded.SchemaVersion, "migrated schema");
        Assert(loaded.Download.EnableHardLinks, "hard link default");
        AssertEqual(ScreenshotImageFormat.Png, loaded.Screenshot.Format, "screenshot default");
        Assert(File.Exists(store.Schema7MigrationRollbackFilePath), "schema 7 rollback copy");
        AssertEqual(
            schema6,
            await File.ReadAllTextAsync(store.Schema7MigrationRollbackFilePath),
            "schema 6 rollback bytes");
    }

    private static async Task Schema7StartsWithoutWindowPlacement()
    {
        using var fixture = new TemporaryFolder();
        var store = new JsonUserSettingsStore(fixture.Path);
        await store.SaveAsync(new UserSettings { DeveloperModeEnabled = true });
        var document = JsonNode.Parse(await File.ReadAllTextAsync(store.SettingsFilePath))!.AsObject();
        document["schemaVersion"] = 7;
        document.Remove("windowPlacement");
        var original = document.ToJsonString();
        await File.WriteAllTextAsync(store.SettingsFilePath, original);

        var loaded = await store.LoadAsync();
        AssertEqual(8, loaded.SchemaVersion, "migrated schema");
        Assert(loaded.DeveloperModeEnabled, "unrelated settings survive migration");
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(store.SettingsFilePath))!;
        Assert(persisted["windowPlacement"] is null, "old settings use first-open placement");
        AssertEqual(8, persisted["schemaVersion"]!.GetValue<int>(), "persisted schema");
        AssertEqual(original, await File.ReadAllTextAsync(Path.Combine(
            Path.GetDirectoryName(store.SettingsFilePath)!, "settings.pre-schema8.rollback.json")), "rollback bytes");
    }

    private static async Task WindowPlacementAndPreferencesSurviveConcurrentSaves()
    {
        using var fixture = new TemporaryFolder();
        var store = new JsonUserSettingsStore(fixture.Path);
        var session = new WindowSettingsSession(store);
        var initial = await session.LoadAsync();
        var placement = new WindowPlacementSettings(-1200, 90, 960, 600, -1600, 40, 144, true);
        await Task.WhenAll(session.SaveWindowPlacementAsync(placement),
            session.SaveAsync(initial with { DeveloperModeEnabled = true }));
        AssertEqual(placement, (await store.LoadAsync()).WindowPlacement, "placement survives stale preferences");
        await session.SaveWindowPlacementAsync(placement with { X = -1100 });
        var loaded = await store.LoadAsync();
        Assert(loaded.DeveloperModeEnabled, "placement save preserves preferences");
        AssertEqual(placement with { X = -1100 }, loaded.WindowPlacement, "normal bounds and maximized flag round trip");
    }

    private static async Task WindowClosePreservesReplacedSettingsFile()
    {
        using var fixture = new TemporaryFolder();
        var store = new JsonUserSettingsStore(fixture.Path);
        var session = new WindowSettingsSession(store);
        var initial = await session.LoadAsync();
        await store.SaveAsync(initial with { DeveloperModeEnabled = true });
        await session.SaveWindowPlacementAsync(new(40, 20, 640, 480, 0, 0, 96, false));
        Assert((await store.LoadAsync()).DeveloperModeEnabled, "closing must preserve the replacement file's preferences");

        await File.WriteAllTextAsync(store.SettingsFilePath, "damaged");
        try
        {
            await session.SaveWindowPlacementAsync(new(60, 20, 640, 480, 0, 0, 96, false));
        }
        catch (UserSettingsStoreException)
        {
        }
        AssertEqual("damaged", await File.ReadAllTextAsync(store.SettingsFilePath), "unreadable settings remain intact");
    }

    private static async Task ExitAfterSettingsDeletionKeepsFileDeleted()
    {
        using var fixture = new TemporaryFolder();
        var store = new JsonUserSettingsStore(fixture.Path);
        var session = new WindowSettingsSession(store);
        var initial = await session.LoadAsync();
        await session.SaveAsync(initial);
        var maintenance = new SettingsMaintenanceService(store.SettingsFilePath, Path.Combine(fixture.Path, "JournalWebView2"));
        await session.DeleteAsync(maintenance.DeleteSettingsAsync, CancellationToken.None);
        await session.SaveWindowPlacementAsync(new(0, 0, 640, 480, 0, 0, 96, false));
        Assert(!File.Exists(store.SettingsFilePath), "closing the window after deletion must leave settings deleted");
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
