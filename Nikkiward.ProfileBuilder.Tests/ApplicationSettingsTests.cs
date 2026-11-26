using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nikkiward.Features.Gallery;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;

internal static class ApplicationSettingsTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        (nameof(PlayTimeContinuity), PlayTimeContinuity),
        (nameof(PlayTimePidReuse), PlayTimePidReuse),
        (nameof(PlayTimeSleep), PlayTimeSleep),
        (nameof(PlayTimeDisabled), PlayTimeDisabled),
        (nameof(PlayTimeStorage), PlayTimeStorage),
        ("application settings defaults match the visible controls", DefaultsMatchVisibleControls),
        ("application settings round trip through the current schema", SettingsRoundTrip),
        ("language preference and default install root drive runtime consumers", LanguageAndDefaultStoreRoot),
        ("schema 6 adds application settings with a rollback copy", Schema6MigrationAddsSections),
        (nameof(OnlyReadyAllowsHomeIdle), OnlyReadyAllowsHomeIdle),
        (nameof(WatchFollowsFirstPhotoDirectory), WatchFollowsFirstPhotoDirectory),
        (nameof(RecentPhotoTimeUsesLocalCalendar), RecentPhotoTimeUsesLocalCalendar),
        (nameof(HomeCheckTimeUsesLocalCalendarDay), HomeCheckTimeUsesLocalCalendarDay),
        (nameof(HomeFactsNotifyAndUseActualArtwork), HomeFactsNotifyAndUseActualArtwork),
        (nameof(RecentPhotosUseFileModificationTime), RecentPhotosUseFileModificationTime),
        (nameof(Schema8DefaultsToStitch), Schema8DefaultsToStitch),
        (nameof(ArtworkTitlesStayWithTheirSource), ArtworkTitlesStayWithTheirSource),
        (nameof(Schema7StartsWithoutWindowPlacement), Schema7StartsWithoutWindowPlacement),
        (nameof(WindowPlacementAndPreferencesSurviveConcurrentSaves), WindowPlacementAndPreferencesSurviveConcurrentSaves),
        (nameof(WindowClosePreservesReplacedSettingsFile), WindowClosePreservesReplacedSettingsFile),
        (nameof(ExitAfterSettingsDeletionKeepsFileDeleted), ExitAfterSettingsDeletionKeepsFileDeleted),
        ("cache clearing preserves the browser session and optional backgrounds", CacheClearingPreservesSession),
        ("data backup contains durable settings records", BackupContainsDurableRecords),
        ("data backup skips an overlapping browser profile", BackupSkipsBrowserProfile),
        ("data backup includes registered protection manifests inside the data folder", BackupIncludesLocalProtectionManifest),
        ("cancelling an in-progress backup leaves no partial archive", CancelInProgressBackup),
        ("a failed backup leaves no partial archive", FailedBackupLeavesNoArchive),
        ("data backups remain unique within the same timestamp", BackupsRemainUnique),
        (nameof(StartupKeepsSelectedFolderWithoutSettings), StartupKeepsSelectedFolderWithoutSettings),
        (nameof(StartupReportsMissingSelectedFolder), StartupReportsMissingSelectedFolder),
        (nameof(StartupUsesDefaultWithoutSelection), StartupUsesDefaultWithoutSelection),
        ("data root selection requires a migrated settings file", DataRootSelectionRequiresSettings),
    ];

    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch.AddDays(10);
    private static readonly Dictionary<string, string> Installs = new(StringComparer.OrdinalIgnoreCase) { ["game.exe"] = "官方" };

    private static ObservedGame Game(int created) => new("GAME.exe", 42, Epoch.AddSeconds(created));

    private static Task PlayTimeContinuity()
    {
        var ledger = new PlayTimeLedger([]);
        ledger.Observe(Epoch, [Game(-10)], Installs);
        ledger.Observe(Epoch.AddSeconds(30), [Game(-10)], Installs);
        ledger.Observe(Epoch.AddSeconds(60), [], Installs);
        var session = ledger.Sessions.Single();
        AssertEqual(Epoch.AddSeconds(-10), session.StartUtc, "recent creation");
        AssertEqual(Epoch.AddSeconds(30), session.EndUtc, "last seen");
        AssertEqual(TimeSpan.FromSeconds(40), session.Duration, "observed duration");
        ledger.Observe(Epoch.AddSeconds(89), [Game(60)], Installs);
        AssertEqual(Epoch.AddSeconds(60), ledger.Sessions[1].StartUtc, "first sight at next sample");
        ledger.Observe(Epoch.AddSeconds(89), [], Installs);
        ledger.Observe(Epoch.AddSeconds(90), [Game(60)], Installs);
        AssertEqual(Epoch.AddSeconds(90), ledger.Sessions[2].StartUtc, "do not credit creation twice");
        return Task.CompletedTask;
    }

    private static Task PlayTimePidReuse()
    {
        var ledger = new PlayTimeLedger([]);
        ledger.Observe(Epoch, [Game(-300)], Installs);
        ledger.Observe(Epoch.AddSeconds(30), [Game(20)], Installs);
        AssertEqual(Epoch, ledger.Sessions[0].StartUtc, "already running");
        AssertEqual(Epoch.AddSeconds(20), ledger.Sessions[1].StartUtc, "reused PID");
        return Task.CompletedTask;
    }

    private static Task PlayTimeSleep()
    {
        var ledger = new PlayTimeLedger([]);
        ledger.Observe(Epoch, [Game(-300)], Installs);
        ledger.Observe(Epoch.AddSeconds(60), [Game(-300)], Installs);
        ledger.Observe(Epoch.AddSeconds(121), [Game(-300)], Installs);
        AssertEqual(TimeSpan.FromSeconds(60), ledger.Sessions[0].Duration, "two periods inclusive");
        AssertEqual(Epoch.AddSeconds(121), ledger.Sessions[1].StartUtc, "sleep gap");
        return Task.CompletedTask;
    }

    private static Task PlayTimeDisabled()
    {
        Assert(!new GeneralSettings().RecordLocalPlayTime, "disabled by default");
        var ledger = new PlayTimeLedger([]);
        ledger.Observe(Epoch, [Game(-10)], Installs, enabled: false);
        AssertEqual(0, ledger.Sessions.Count, "disabled");
        ledger.Observe(Epoch, [Game(-10) with { Path = "unknown.exe" }], Installs);
        AssertEqual(0, ledger.Sessions.Count, "unknown installation");
        ledger.Disconnect(Epoch.AddSeconds(20));
        ledger.Observe(Epoch.AddSeconds(20), [Game(10)], Installs);
        AssertEqual(Epoch.AddSeconds(20), ledger.Sessions.Single().StartUtc, "exclude time before opt-in");
        return Task.CompletedTask;
    }

    private static Task PlayTimeStorage()
    {
        using var fixture = new TemporaryFolder();
        var store = new PlayTimeStore(fixture.Path);
        var ledger = new PlayTimeLedger([]);
        ledger.Observe(Epoch, [Game(-10)], Installs);
        store.Save(ledger.Sessions);
        AssertEqual(ledger.Sessions.Single(), new PlayTimeStore(fixture.Path).Load().Single(), "round trip");
        store.Clear();
        AssertEqual(0, store.Load().Count, "clear");
        var path = Path.Combine(fixture.Path, "PlayTime", "sessions.json");
        var hugeSession = new PlaySession("官方", DateTimeOffset.MinValue, DateTimeOffset.MaxValue,
            DateTimeOffset.MaxValue - DateTimeOffset.MinValue);
        var overflowing = JsonSerializer.Serialize(new PlayTimeDocument(1, Enumerable.Repeat(hugeSession, 4).ToArray()));
        foreach (var (document, reason) in new[]
        {
            (overflowing, "损坏"), ("{", "损坏"), ("{}", "损坏"),
            ("""{"SchemaVersion":2,"FutureLayout":{}}""", "更高版本"),
            ("""{"SchemaVersion":1,"Sessions":[{}]}""", "损坏"),
        })
        {
            File.WriteAllText(path, document);
            var damaged = new PlayTimeStore(fixture.Path);
            try
            {
                damaged.Load();
                throw new InvalidOperationException("invalid play time document loaded");
            }
            catch (InvalidDataException ex) { Assert(ex.Message.Contains(reason), "protection reason"); }
            try { damaged.Save([]); } catch (InvalidOperationException) { }
            AssertEqual(document, File.ReadAllText(path), "protected original");
        }
        return Task.CompletedTask;
    }

    private static Task DefaultsMatchVisibleControls()
    {
        var settings = new UserSettings();
        AssertEqual(UserSettings.CurrentSchemaVersion, settings.SchemaVersion, "schema version");
        AssertEqual("zh-CN", settings.General.LanguageTag, "language");
        AssertEqual("Alt+S", settings.General.MainWindowHotkey, "main window hotkey");
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
                RecordLocalPlayTime = true,
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
        Assert(loaded.General.RecordLocalPlayTime, "play time opt-in");
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

    private static Task OnlyReadyAllowsHomeIdle()
    {
        var eligible = Enum.GetValues<LaunchButtonState>().Where(state => state.AllowsHomeIdle());
        Assert(eligible.SequenceEqual(new[] { LaunchButtonState.Ready }), "every state needing attention stays visible");
        return Task.CompletedTask;
    }

    private static Task WatchFollowsFirstPhotoDirectory()
    {
        using var fixture = new TemporaryFolder();
        var saved = Path.Combine(fixture.Path, "Saved");
        var photos = Path.Combine(saved, "GamePlayPhotos");
        AssertEqual(new GalleryWatchTarget(fixture.Path, "Saved", false), GalleryWatchTarget.Resolve(photos), "missing ancestors");
        Directory.CreateDirectory(saved);
        AssertEqual(new GalleryWatchTarget(saved, "GamePlayPhotos", false), GalleryWatchTarget.Resolve(photos), "first directory created");
        Directory.CreateDirectory(photos);
        AssertEqual(new GalleryWatchTarget(photos, "*", true), GalleryWatchTarget.Resolve(photos), "watch captures recursively");
        return Task.CompletedTask;
    }

    private static Task RecentPhotoTimeUsesLocalCalendar()
    {
        var today = new DateTime(2026, 1, 1);
        var time = today.AddHours(19);
        AssertEqual("今天 19:00", GalleryTimestamp.FormatHomeTime(time, today), "today");
        AssertEqual("昨天 19:00", GalleryTimestamp.FormatHomeTime(time.AddDays(-1), today), "yesterday across years");
        AssertEqual("01-01 19:00", GalleryTimestamp.FormatHomeTime(time, today.AddDays(2)), "same year");
        AssertEqual("2025-12-30", GalleryTimestamp.FormatHomeTime(time.AddDays(-2), today), "previous year");
        return Task.CompletedTask;
    }

    private static Task HomeCheckTimeUsesLocalCalendarDay()
    {
        var stamp = new DateTimeOffset(2026, 11, 25, 21, 40, 30, TimeSpan.FromHours(-8));
        AssertEqual("尚未刷新", MainPageViewModel.FormatHomeCheckTime(null, stamp.Date), "before check");
        AssertEqual("今天 21:40", MainPageViewModel.FormatHomeCheckTime(stamp, stamp.Date), "local today");
        AssertEqual("11-25 21:40", MainPageViewModel.FormatHomeCheckTime(stamp, stamp.Date.AddDays(1)), "after midnight");
        return Task.CompletedTask;
    }

    private static async Task HomeFactsNotifyAndUseActualArtwork()
    {
        using var fixture = new TemporaryFolder();
        var page = new MainPageViewModel(new JsonUserSettingsStore(fixture.Path),
            new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
        var notifications = new List<string?>();
        page.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        page.OfficialJournalDuration = "16h";
        page.LocalPlayDuration = "本机 1 小时";
        Assert(notifications.Contains(nameof(page.OfficialJournalDuration)) &&
            notifications.Contains(nameof(page.HomeLocalTimeVisibility)), "duration notifications");
        page.LocalPlayDuration = null;
        AssertEqual(Microsoft.UI.Xaml.Visibility.Collapsed, page.HomeLocalTimeVisibility, "local time hidden");
        page.ShowArtwork(AppearanceBackgroundPresets.Preset2Source);
        AssertEqual("花信", page.HomeArtName, "preset title");
        Assert(!page.CanNameArtwork, "preset has a fixed title");
        page.ShowArtwork("imported.jpg");
        AssertEqual("自定义背景", page.HomeArtName, "unknown original filename");
        var settings = new AppearanceSettings().WithArtworkTitle("imported.jpg", original: "晚霞.jpg");
        await page.SaveAppearanceSettingsAsync(settings);
        AssertEqual("晚霞", page.HomeArtName, "known original filename without extension");
        notifications.Clear();
        await page.SaveAppearanceSettingsAsync(settings.WithArtworkTitle("imported.jpg", name: "天空"));
        Assert(notifications.Contains(nameof(page.HomeArtName)), "successful save refreshes the displayed title");
        AssertEqual("天空", page.HomeArtName, "player title wins");
        AssertEqual(page.LaunchButtonLabel, page.HomeLaunchLabel, "live state label");
        AssertEqual(page.CanAttemptSelectedChannelLaunch, page.HomeLaunchEnabled, "live action gate");
    }

    private static Task RecentPhotosUseFileModificationTime()
    {
        using var fixture = new TemporaryFolder();
        foreach (var (name, age) in new[] { ("20991231.jpg", 2), ("a.jpg", 0), ("b.png", 0), ("ignored.txt", -1) })
        {
            var file = Path.Combine(fixture.Path, name);
            File.WriteAllText(file, "");
            File.SetLastWriteTimeUtc(file, Epoch.AddDays(-age).UtcDateTime);
        }
        Assert(GalleryFiles.NewestFirst(fixture.Path, default).Take(4).Select(f => f.Name)
            .SequenceEqual(new[] { "a.jpg", "b.png", "20991231.jpg" }), "mtime with stable ties");
        return Task.CompletedTask;
    }

    private static async Task Schema8DefaultsToStitch()
    {
        using var fixture = new TemporaryFolder();
        var store = new JsonUserSettingsStore(fixture.Path);
        await store.SaveAsync(new UserSettings());
        var json = JsonNode.Parse(await File.ReadAllTextAsync(store.SettingsFilePath))!.AsObject();
        json["schemaVersion"] = 8;
        json["appearance"]!["launcherCapsuleStyle"] = "ocean";
        json["general"]!["enableProfileQuickSwitcher"] = false;
        json["appearance"]!.AsObject().Remove("homeSkin");
        var original = json.ToJsonString();
        await File.WriteAllTextAsync(store.SettingsFilePath, original);
        var loaded = await store.LoadAsync();
        AssertEqual("Stitch", loaded.Appearance.HomeSkin, "requested skin survives the Cinema fallback");
        AssertEqual(original, await File.ReadAllTextAsync(store.Schema9MigrationRollbackFilePath), "rollback bytes");
        await store.SaveAsync(loaded with { Appearance = loaded.Appearance with { HomeSkin = "future" } });
        AssertEqual("future", (await store.LoadAsync()).Appearance.HomeSkin, "unknown skin remains readable");
    }

    private static async Task ArtworkTitlesStayWithTheirSource()
    {
        using var fixture = new TemporaryFolder();
        var store = new JsonUserSettingsStore(fixture.Path);
        var art = new AppearanceSettings().WithArtworkTitle("one", original: "晨.jpg")
            .WithArtworkTitle("one", name: " 晨光 ").WithArtworkTitle("two", original: "夜.jpg");
        await store.SaveAsync(new UserSettings { Appearance = art });
        var loaded = (await store.LoadAsync()).Appearance;
        AssertEqual("晨光", loaded.ArtworkTitles["one"].Name!, "trim title");
        AssertEqual("晨.jpg", loaded.WithArtworkTitle("one", name: "").ArtworkTitles["one"].OriginalFileName!, "reset keeps filename");
        AssertEqual("夜.jpg", loaded.ArtworkTitles["two"].OriginalFileName!, "second artwork");
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
        AssertEqual(9, loaded.SchemaVersion, "migrated schema");
        Assert(loaded.DeveloperModeEnabled, "unrelated settings survive migration");
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(store.SettingsFilePath))!;
        Assert(persisted["windowPlacement"] is null, "old settings use first-open placement");
        AssertEqual(9, persisted["schemaVersion"]!.GetValue<int>(), "persisted schema");
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
        string[] records =
        [
            "settings.json",
            "settings.pre-schema6.rollback.json",
            "settings.pre-schema7.rollback.json",
            "settings.pre-schema8.rollback.json",
            "settings.pre-schema9.rollback.json",
            "JournalCache/journal-snapshot.json",
            "JournalCache/resonance-history.json",
            "PlayTime/sessions.json",
            "WishHistory/wish-history.json",
            "Gallery/stars.json",
            "Gallery/default-favorites-v1.seed",
            "Gallery/protection-settings.json",
            "Gallery/ProtectedFavorites/manifest.json",
            "ExternalTools/nikkigallery.json",
            "Plugins/nikkiward.photo-album-importer/plugin.json",
        ];
        foreach (var record in records)
        {
            WriteBytes(Path.Combine(dataRoot, record), 23);
        }

        foreach (var excluded in new[]
        {
            "JournalWebView2/EBWebView/Default/sample.db",
            "JournalWebView2/EBWebView/Default/preferences.json",
            "JournalCache/Assets/sample.json",
            "GalleryCache/Thumbnails/thumb.jpg",
            "Gallery/ProtectedFavorites/objects/photo.jpg",
            "Plugins/nikkiward.photo-album-importer/companion.json",
            "Logs/sample.txt",
            "unrelated.sqlite",
        })
        {
            WriteBytes(Path.Combine(dataRoot, excluded), 31);
        }

        var service = new SettingsMaintenanceService(settingsPath, browserPath);
        var receipt = await service.CreateBackupAsync();

        Assert(File.Exists(receipt.FilePath), "backup file");
        AssertEqual(records.Length, receipt.FileCount, "backup record count");
        using var archive = ZipFile.OpenRead(receipt.FilePath);
        Assert(records.ToHashSet(StringComparer.Ordinal).SetEquals(archive.Entries.Select(entry => entry.FullName)),
            "only application-owned records belong in the archive");
    }

    private static async Task BackupSkipsBrowserProfile()
    {
        using var fixture = new TemporaryFolder();
        var settings = Path.Combine(fixture.Path, "settings.json");
        var browser = Path.Combine(fixture.Path, "JournalCache");
        WriteBytes(settings, 5);
        var browserFile = Path.Combine(browser, "journal-snapshot.json");
        WriteBytes(browserFile, 7);
        using var locked = File.Open(browserFile, FileMode.Open, FileAccess.Read, FileShare.None);

        var receipt = await new SettingsMaintenanceService(settings, browser).CreateBackupAsync();
        using var archive = ZipFile.OpenRead(receipt.FilePath);
        AssertEqual("settings.json", archive.Entries.Single().FullName, "browser subtree is never opened");
    }

    private static async Task CancelInProgressBackup()
    {
        using var fixture = new TemporaryFolder();
        var settings = Path.Combine(fixture.Path, "settings.json");
        WriteBytes(settings, 10);
        WriteBytes(Path.Combine(fixture.Path, "JournalCache", "journal-snapshot.json"), 20);
        var service = new SettingsMaintenanceService(settings, Path.Combine(fixture.Path, "browser"));
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(count =>
        {
            AssertEqual(1, count, "cancel after copying the first entry");
            Assert(Directory.EnumerateFiles(service.BackupFolder).Any(), "the writer must have started");
            Assert(!Directory.EnumerateFiles(service.BackupFolder, "*.zip").Any(), "an unfinished ZIP must not be published");
            cancellation.Cancel();
        });
        try
        {
            await service.CreateBackupAsync(cancellation.Token, progress);
            throw new InvalidOperationException("the backup should have been cancelled");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }

        Assert(!Directory.EnumerateFiles(service.BackupFolder).Any(), "cancelled archive or temporary file remains");
    }

    private static async Task BackupIncludesLocalProtectionManifest()
    {
        using var fixture = new TemporaryFolder();
        var dataRoot = Path.Combine(fixture.Path, "Nikkiward");
        var browser = Path.Combine(dataRoot, "browser");
        var protection = new GalleryFavoriteProtectionService(fixture.Path, Path.Combine(fixture.Path, "pictures"));
        var external = Path.Combine(fixture.Path, "external");
        var local = Path.Combine(dataRoot, "MyFavorites");
        var unrelated = Path.Combine(dataRoot, "UninitializedStore");
        await protection.SetActiveRootAsync(external);
        await protection.SetActiveRootAsync(browser);
        await protection.SetActiveRootAsync(unrelated);
        await protection.SetActiveRootAsync(local);
        File.WriteAllText(Path.Combine(unrelated, "manifest.json"), """{"name":"fixture"}""");
        foreach (var root in new[] { local, browser, external })
        {
            WriteBytes(Path.Combine(root, ".store.lock"), 0);
            File.WriteAllText(Path.Combine(root, "manifest.json"), """{"schemaVersion":1,"entries":[]}""");
            WriteBytes(Path.Combine(root, "objects", "image.jpg"), 5);
        }
        using var externalLock = File.Open(Path.Combine(external, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.None);
        using var browserLock = File.Open(Path.Combine(browser, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.None);
        var service = new SettingsMaintenanceService(Path.Combine(dataRoot, "settings.json"), browser);
        var receipt = await service.CreateBackupAsync();
        using var archive = ZipFile.OpenRead(receipt.FilePath);
        Assert(archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal)
            .SetEquals(["settings.json", "Gallery/protection-settings.json", "MyFavorites/manifest.json"]), "registered local manifest only");
        File.Delete(protection.SettingsFilePath);
        File.Delete(Path.Combine(local, "manifest.json"));
        File.Delete(Path.Combine(local, ".store.lock"));
        archive.ExtractToDirectory(dataRoot);
        var next = await service.CreateBackupAsync();
        using var restored = ZipFile.OpenRead(next.FilePath);
        Assert(restored.GetEntry("MyFavorites/manifest.json") is not null, "the restored manifest must survive another backup");
    }

    private static async Task FailedBackupLeavesNoArchive()
    {
        using var fixture = new TemporaryFolder();
        var settings = Path.Combine(fixture.Path, "settings.json");
        WriteBytes(settings, 5);
        using var locked = File.Open(settings, FileMode.Open, FileAccess.Read, FileShare.None);
        var service = new SettingsMaintenanceService(settings, Path.Combine(fixture.Path, "browser"));
        try
        {
            await service.CreateBackupAsync();
            throw new InvalidOperationException("an unreadable record should fail the backup");
        }
        catch (IOException)
        {
        }

        Assert(!Directory.EnumerateFiles(service.BackupFolder).Any(), "failed archive or temporary file remains");
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

    private static Task StartupKeepsSelectedFolderWithoutSettings()
    {
        using var fixture = new TemporaryFolder();
        var configured = Directory.CreateDirectory(Path.Combine(fixture.Path, "selected")).FullName;
        var oldSettings = Path.Combine(fixture.Path, "settings.json");
        File.WriteAllText(oldSettings, "old data");
        var root = ApplicationDataPaths.ResolveRoot(configured, fixture.Path);
        AssertEqual(configured, root, "selected folder");
        return Task.CompletedTask;
    }

    private static Task StartupReportsMissingSelectedFolder()
    {
        using var fixture = new TemporaryFolder();
        File.WriteAllText(Path.Combine(fixture.Path, "settings.json"), "{}");
        var missing = Path.Combine(fixture.Path, "missing");
        Assert(ApplicationDataPaths.ResolveRoot(missing, fixture.Path) is null, "selected directory unavailable");
        return Task.CompletedTask;
    }

    private static Task StartupUsesDefaultWithoutSelection()
    {
        using var fixture = new TemporaryFolder();
        var fallback = Path.Combine(fixture.Path, "default");
        AssertEqual(fallback, ApplicationDataPaths.ResolveRoot(null, fallback), "first-run default folder");
        return Task.CompletedTask;
    }

    private static Task DataRootSelectionRequiresSettings()
    {
        using var fixture = new TemporaryFolder();
        var configured = fixture.Path;
        try
        {
            ApplicationDataPaths.ValidateExistingRoot(configured, requireSettings: true);
            throw new InvalidOperationException("Switching to a folder without settings must fail.");
        }
        catch (FileNotFoundException)
        {
        }

        File.WriteAllText(Path.Combine(configured, "settings.json"), "{}");
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

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
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
