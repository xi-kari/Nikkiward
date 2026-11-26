using System.Text.Json;
using System.Text.Json.Nodes;
using Nikkiward.Models;
using Nikkiward.ViewModels;
using Nikkiward.Services;

internal static class UserSettingsTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("old general settings keep the window and after-launch choices round trip", AfterLaunchSettingsRoundTrip),
        ("damaged settings survive profile discovery and window close", () => DiscoverWithSettingsAsync("{broken")),
        ("future settings survive profile discovery and window close", () => DiscoverWithSettingsAsync("{\"schemaVersion\":999}")),
        ("first run saves the discovered profile", () => DiscoverWithSettingsAsync(null)),
        ("overlapping preference saves retain both sections", OverlappingPreferenceSaves),
        ("queued preference changes preserve both hotkeys", QueuedPreferencesPreserveHotkeys),
        ("failed hotkey save restores registration and display", () => HotkeysRollBack(new IOException())),
        ("cancelled hotkey save restores registration and display", () => HotkeysRollBack(new OperationCanceledException())),
        ("first-run backup restores defaults", () => FirstRunBackupRestoresSettings(false)),
        ("missing settings backup restores current session snapshot", () => FirstRunBackupRestoresSettings(true)),
        ("protected settings restore only a validated backup", RestoreProtectedSettings),
        ("new settings use the single appearance authority", NewSettingsUseAppearance),
        ("appearance settings round trip through settings json", AppearanceRoundTripsThroughSettingsJson),
        ("Wallpaper Engine presentation settings round trip through settings json", WallpaperEnginePresentationRoundTripsThroughSettingsJson),
        ("current schema settings default absent Wallpaper Engine fields", CurrentSchemaDefaultsAbsentWallpaperEngineFields),
        ("schema 5 holographic migration preserves a rollback copy", Schema5HolographicMigrationPreservesRollback),
        ("gallery roots round trip per profile", GalleryRootsRoundTripPerProfile),
        ("schema 5 without channel store migrates to empty defaults", Schema5WithoutChannelStoreLoadsDefaults),
        ("channel store settings round trip through settings json", ChannelStoreSettingsRoundTrip),
        ("settings schema 3 migrates profiles gallery gamepad and theme", SettingsSchema3MigratesEverySection),
        ("settings migration is persisted atomically in the current schema", MigratedSettingsPersist),
        ("unsupported settings schemas fail closed", UnsupportedSettingsSchemasFailClosed),
        ("damaged settings documents fail closed", DamagedSettingsDocumentsFailClosed),
        ("settings save rejects invalid schema and appearance", SaveRejectsInvalidSettings),
        ("settings atomic replacement leaves no temporary files", AtomicReplacementLeavesNoTemporaryFiles),
        ("application paths preserve Windows drive roots", ApplicationPathsPreserveDriveRoots),
    ];

    private static async Task AfterLaunchSettingsRoundTrip()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            await store.SaveAsync(new UserSettings());
            var document = JsonNode.Parse(await File.ReadAllTextAsync(store.SettingsFilePath))!;
            document["general"]!.AsObject().Remove("afterLaunchBehavior");
            await File.WriteAllTextAsync(store.SettingsFilePath, document.ToJsonString());
            AssertEqual(AfterLaunchBehavior.KeepWindow, (await store.LoadAsync()).General.AfterLaunchBehavior, "old settings default");
            var page = CreatePage(new WindowSettingsSession(store));
            foreach (var choice in Enum.GetValues<AfterLaunchBehavior>())
            {
                await page.SaveGeneralSettingsAsync(page.GeneralSettings with { AfterLaunchBehavior = choice });
                AssertEqual(choice, (await store.LoadAsync()).General.AfterLaunchBehavior, "persisted choice");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SingleInstall : IInstallationDiscoveryService
    {
        public Task<ProfileBuildResult> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProfileBuildResult
            {
                Candidates = [new InstallationProfileCandidate
                {
                    ProfileId = "test", Profile = new LaunchProfile { ProfileId = "test" },
                    State = InstallationCandidateState.Candidate,
                }],
            });

        public Task<ProfileBuildResult> DiscoverFromManualGameRootAsync(string selectedDirectory,
            string? selectedLauncherRoot = null, CancellationToken cancellationToken = default) =>
            DiscoverAsync(cancellationToken);
    }

    private static MainPageViewModel CreatePage(WindowSettingsSession session) =>
        new(session, new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter(), new SingleInstall());

    private static async Task DiscoverWithSettingsAsync(string? original)
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            var directory = Path.GetDirectoryName(store.SettingsFilePath)!;
            Directory.CreateDirectory(directory);
            var bytes = System.Text.Encoding.UTF8.GetBytes(original ?? "");
            if (original is not null)
            {
                await File.WriteAllBytesAsync(store.SettingsFilePath, bytes);
            }

            var session = new WindowSettingsSession(store);
            var page = CreatePage(session);
            await page.InitializeAsync();
            AssertEqual("test", page.SelectedProfileId, "unique install selected");
            await page.DiscoverAsync();
            var manual = await page.DiscoverFromManualRootAsync(root, null);
            Assert(manual.Succeeded, "manual discovery remains usable");
            Assert(await page.SelectProfileAsync("test"), "profile remains selectable");
            await session.SaveWindowPlacementAsync(new(50, 60, 800, 600, 0, 0, 96, false));
            if (original is null)
            {
                AssertEqual("test", (await store.LoadAsync()).SelectedProfileId, "first run persisted");
                return;
            }
            Assert(File.ReadAllBytes(store.SettingsFilePath).SequenceEqual(bytes), "original bytes changed");
            Assert(page.SettingsStatusText.Contains("只读保护"), "protection is visible");
            await AssertThrowsAsync<UserSettingsStoreException>(() => page.SaveDeveloperModeAsync(true));
            await session.RecoverUnreadableAsync(null, CancellationToken.None);
            var preserved = Directory.GetFiles(directory, "settings.unreadable-*.json").Single();
            Assert(File.ReadAllBytes(preserved).SequenceEqual(bytes), "archived original bytes changed");
            AssertEqual(null, (await store.LoadAsync()).SelectedProfileId, "reset writes defaults");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class PausedStore(IUserSettingsStore store) : IUserSettingsStore
    {
        public TaskCompletionSource Saving { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string SettingsFilePath => store.SettingsFilePath;
        public Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default) => store.LoadAsync(cancellationToken);

        public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
        {
            Saving.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            await store.SaveAsync(settings, cancellationToken);
        }
    }

    private static async Task OverlappingPreferenceSaves()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new PausedStore(new JsonUserSettingsStore(root));
            var page = CreatePage(new WindowSettingsSession(store));
            var appearance = page.SaveThemeModeAsync(ThemeMode.WarmLight);
            await store.Saving.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var developer = page.SaveDeveloperModeAsync(true);
            store.Resume.SetResult();
            await Task.WhenAll(appearance, developer);
            var saved = await store.LoadAsync();
            Assert(saved.DeveloperModeEnabled, "developer setting lost");
            AssertEqual(ThemeMode.WarmLight, saved.Appearance.ThemeMode, "appearance lost");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task QueuedPreferencesPreserveHotkeys()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new PausedStore(new JsonUserSettingsStore(root));
            var page = CreatePage(new WindowSettingsSession(store));
            var hotkeys = page.SaveHotkeySettingsAsync("F9", "F10");
            await store.Saving.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var general = page.SaveGeneralSettingsAsync(page.GeneralSettings with { LanguageTag = "zh-CN" });
            var screenshot = page.SaveScreenshotSettingsAsync(page.ScreenshotSettings with { FolderPath = root });
            store.Resume.SetResult();
            await Task.WhenAll(hotkeys, general, screenshot);
            var saved = await store.LoadAsync();
            AssertEqual("F9", saved.General.MainWindowHotkey, "general preferences preserve main shortcut");
            AssertEqual("F10", saved.Screenshot.Hotkey, "screenshot preferences preserve capture shortcut");
            AssertEqual(root, saved.Screenshot.FolderPath, "capture folder persisted");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task HotkeysRollBack(Exception failure)
    {
        var registered = "Alt+D";
        var displayed = "F10";
        var error = await AssertThrowsAsync<Exception>(() => HotkeySettingsChange.ApplyAsync(
            () =>
            {
                registered = displayed;
                return HotkeyRegistrationResult.Success("");
            },
            () => Task.FromException(failure),
            () => registered = displayed = "Alt+D"));
        Assert(ReferenceEquals(failure, error), "save outcome reaches the event boundary");
        AssertEqual("Alt+D", registered, "registration restored");
        AssertEqual("Alt+D", displayed, "display restored");
    }

    private static async Task FirstRunBackupRestoresSettings(bool useSnapshot)
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            var maintenance = new SettingsMaintenanceService(store.SettingsFilePath, Path.Combine(root, "browser"));
            var backup = useSnapshot
                ? await maintenance.CreateBackupAsync(snapshot: new UserSettings { DeveloperModeEnabled = true })
                : await maintenance.CreateBackupAsync();
            Assert(!File.Exists(store.SettingsFilePath), "backup created live settings");
            await File.WriteAllTextAsync(store.SettingsFilePath, "{broken");
            var session = new WindowSettingsSession(store);
            await AssertThrowsAsync<UserSettingsStoreException>(() => session.LoadAsync());
            await session.RecoverUnreadableAsync(backup.FilePath, CancellationToken.None);
            var restored = await store.LoadAsync();
            AssertEqual(UserSettings.CurrentSchemaVersion, restored.SchemaVersion, "restored schema");
            AssertEqual(useSnapshot, restored.DeveloperModeEnabled, "restored effective setting");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RestoreProtectedSettings()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            await store.SaveAsync(new UserSettings { DeveloperModeEnabled = true });
            var maintenance = new SettingsMaintenanceService(store.SettingsFilePath, Path.Combine(root, "browser"));
            var good = await maintenance.CreateBackupAsync();
            await File.WriteAllTextAsync(store.SettingsFilePath, "{broken");
            var bad = await maintenance.CreateBackupAsync();
            var session = new WindowSettingsSession(store);
            await AssertThrowsAsync<UserSettingsStoreException>(() => session.LoadAsync());
            await AssertThrowsAsync<JsonException>(() => session.RecoverUnreadableAsync(bad.FilePath, CancellationToken.None));
            AssertEqual("{broken", await File.ReadAllTextAsync(store.SettingsFilePath), "bad backup preserved original");
            await session.RecoverUnreadableAsync(good.FilePath, CancellationToken.None);
            Assert((await store.LoadAsync()).DeveloperModeEnabled, "restored preference");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Task NewSettingsUseAppearance()
    {
        var settings = new UserSettings();
        AssertEqual(UserSettings.CurrentSchemaVersion, settings.SchemaVersion, "schema version");
        AssertEqual(ThemeMode.WarmDark, settings.Appearance.ThemeMode, "theme mode");
        AssertEqual("Stitch", settings.Appearance.HomeSkin, "home skin");
        AssertEqual(0, settings.GalleryProfiles.Count, "gallery profile count");
        Assert(!settings.DeveloperModeEnabled, "developer mode must default off");
        return Task.CompletedTask;
    }

    private static async Task AppearanceRoundTripsThroughSettingsJson()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            await store.SaveAsync(new UserSettings
            {
                DeveloperModeEnabled = true,
                Appearance = new AppearanceSettings
                {
                    ThemeMode = ThemeMode.WarmDark,
                    AccentMode = AccentColorMode.Fixed,
                    FixedAccent = FixedAccentColor.Gold,
                    CustomAccentArgb = 0x00776655,
                    UseSerifTitles = false,
                    HomeSkin = "Cinema",
                    Motion = AppearanceMotionMode.Reduced,
                    Density = InterfaceDensity.Compact,
                    Background = new BackgroundArtSettings
                    {
                        SelectedSource = " D:\\Art\\one.png ",
                        CarouselSources = ["D:\\Art\\one.png", "D:\\Art\\two.png"],
                        CarouselEnabled = true,
                        CarouselIntervalMinutes = 30,
                        ParallaxEnabled = false,
                        HolographicCardEnabled = false,
                        MotionEnabled = true,
                        MotionSource = " D:\\Art\\loop.mp4 ",
                        MotionFpsCap = 60,
                        UseLiveBlur = true,
                        GlassIntensity = 0.55,
                        MotionPanEnabled = true,
                        MotionZoom = 1.4,
                    },
                },
            });

            var loaded = await store.LoadAsync();
            Assert(loaded.DeveloperModeEnabled, "developer mode");
            AssertEqual(ThemeMode.WarmDark, loaded.Appearance.ThemeMode, "theme mode");
            AssertEqual(AppearanceMotionMode.Reduced, loaded.Appearance.Motion, "motion mode");
            AssertEqual(InterfaceDensity.Compact, loaded.Appearance.Density, "density");
            AssertEqual(0xFF776655u, loaded.Appearance.CustomAccentArgb, "custom accent");
            AssertEqual("Cinema", loaded.Appearance.HomeSkin, "home skin");
            AssertEqual("D:\\Art\\one.png", loaded.Appearance.Background.SelectedSource, "source");
            Assert(!loaded.Appearance.Background.HolographicCardEnabled, "holographic card");
            Assert(loaded.Appearance.Background.MotionEnabled, "motion enabled");
            AssertEqual("D:\\Art\\loop.mp4", loaded.Appearance.Background.MotionSource, "motion source");
            AssertEqual(60, loaded.Appearance.Background.MotionFpsCap, "motion FPS");
            Assert(loaded.Appearance.Background.UseLiveBlur, "live blur");
            AssertEqual(0.55, loaded.Appearance.Background.GlassIntensity, "glass intensity");
            Assert(loaded.Appearance.Background.MotionPanEnabled, "motion pan");
            AssertEqual(1.4, loaded.Appearance.Background.MotionZoom, "motion zoom");

            var json = await File.ReadAllTextAsync(store.SettingsFilePath);
            Assert(
                json.Contains("\"appearance\"", StringComparison.Ordinal),
                "appearance object must be serialized");
            Assert(
                !json.Contains("\n  \"themeMode\"", StringComparison.Ordinal),
                "top-level theme authority must not be serialized");
            Assert(
                json.Contains("\"themeMode\": \"warmDark\"", StringComparison.Ordinal),
                "appearance theme must serialize as camel-case enum text");
            Assert(
                json.Contains("\"homeSkin\": \"Cinema\"", StringComparison.Ordinal),
                "home skin id must round-trip");
            Assert(
                json.Contains("\"holographicCardEnabled\": false", StringComparison.Ordinal),
                "holographic card setting must serialize");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task Schema5HolographicMigrationPreservesRollback()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            await store.SaveAsync(new UserSettings());
            var document = JsonNode.Parse(
                await File.ReadAllTextAsync(store.SettingsFilePath))!.AsObject();
            var background = document["appearance"]?["background"]?.AsObject() ??
                throw new InvalidOperationException("appearance background missing");
            document["schemaVersion"] = 5;
            background.Remove("holographicCardEnabled");
            var schema5Json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(store.SettingsFilePath, schema5Json);

            var loaded = await store.LoadAsync();

            AssertEqual(UserSettings.CurrentSchemaVersion, loaded.SchemaVersion, "migrated schema");
            Assert(
                loaded.Appearance.Background.HolographicCardEnabled,
                "missing holographic card setting should default enabled");
            Assert(File.Exists(store.MigrationRollbackFilePath), "migration rollback copy");
            AssertEqual(
                schema5Json,
                await File.ReadAllTextAsync(store.MigrationRollbackFilePath),
                "migration rollback bytes");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task WallpaperEnginePresentationRoundTripsThroughSettingsJson()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            await store.SaveAsync(new UserSettings
            {
                Appearance = new AppearanceSettings
                {
                    Background = new BackgroundArtSettings
                    {
                        SelectedSource = "C:\\Nikkiward\\WallpaperImports\\Still\\preview.jpg",
                        MotionEnabled = true,
                        MotionSource = "C:\\Nikkiward\\WallpaperImports\\Packages\\scene.pkg",
                        WallpaperEnginePresentation = WallpaperEnginePresentation.MotionBackdrop,
                        WallpaperEnginePackageSource = " C:\\Nikkiward\\WallpaperImports\\Packages\\scene.pkg ",
                    },
                },
            });

            var loaded = await store.LoadAsync();
            AssertEqual(
                WallpaperEnginePresentation.MotionBackdrop,
                loaded.Appearance.Background.WallpaperEnginePresentation,
                "Wallpaper Engine presentation");
            AssertEqual(
                "C:\\Nikkiward\\WallpaperImports\\Packages\\scene.pkg",
                loaded.Appearance.Background.WallpaperEnginePackageSource,
                "Wallpaper Engine package path");

            var json = await File.ReadAllTextAsync(store.SettingsFilePath);
            Assert(
                json.Contains("\"wallpaperEnginePresentation\": \"motionBackdrop\"", StringComparison.Ordinal),
                "Wallpaper Engine presentation must serialize as a camel-case enum");
            Assert(
                json.Contains("\"wallpaperEnginePackageSource\"", StringComparison.Ordinal),
                "Wallpaper Engine package source must serialize");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CurrentSchemaDefaultsAbsentWallpaperEngineFields()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            await store.SaveAsync(new UserSettings());
            var document = JsonNode.Parse(
                await File.ReadAllTextAsync(store.SettingsFilePath))!.AsObject();
            var background = document["appearance"]?["background"]?.AsObject() ??
                throw new InvalidOperationException("appearance background missing");
            background.Remove("wallpaperEnginePresentation");
            background.Remove("wallpaperEnginePackageSource");
            await File.WriteAllTextAsync(
                store.SettingsFilePath,
                document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var loaded = await store.LoadAsync();
            AssertEqual(
                WallpaperEnginePresentation.None,
                loaded.Appearance.Background.WallpaperEnginePresentation,
                "missing Wallpaper Engine presentation default");
            Assert(
                loaded.Appearance.Background.WallpaperEnginePackageSource is null,
                "missing Wallpaper Engine package source default");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task GalleryRootsRoundTripPerProfile()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            var firstRoot = Path.Combine(root, "gallery-a");
            var secondRoot = Path.Combine(root, "gallery-b");
            await store.SaveAsync(new UserSettings
            {
                SelectedProfileId = " profile-a ",
                GalleryProfiles =
                [
                    new GalleryProfileSettings
                    {
                        ProfileId = "profile-a",
                        RootPath = firstRoot,
                    },
                    new GalleryProfileSettings
                    {
                        ProfileId = "profile-b",
                        RootPath = secondRoot,
                    },
                ],
            });

            var loaded = await store.LoadAsync();
            AssertEqual("profile-a", loaded.SelectedProfileId, "selected profile");
            AssertEqual(2, loaded.GalleryProfiles.Count, "gallery profile count");
            AssertEqual(firstRoot, loaded.GalleryProfiles[0].RootPath, "first gallery root");
            AssertEqual(secondRoot, loaded.GalleryProfiles[1].RootPath, "second gallery root");
            Assert(
                loaded.GalleryProfiles[0].ProfileId != loaded.GalleryProfiles[1].ProfileId,
                "gallery roots must remain keyed to distinct profiles");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task Schema5WithoutChannelStoreLoadsDefaults()
    {
        foreach (var useExplicitNull in new[]
        {
            false,
            true,
        })
        {
            var root = CreateTemporaryRoot();
            try
            {
                var store = new JsonUserSettingsStore(root);
                await store.SaveAsync(new UserSettings());
                var document = JsonNode.Parse(
                    await File.ReadAllTextAsync(store.SettingsFilePath))!.AsObject();
                document["schemaVersion"] = 5;
                if (useExplicitNull)
                {
                    document["channelStore"] = null;
                }
                else
                {
                    document.Remove("channelStore");
                }

                await File.WriteAllTextAsync(
                    store.SettingsFilePath,
                    document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                var loaded = await store.LoadAsync();

                AssertEqual(UserSettings.CurrentSchemaVersion, loaded.SchemaVersion, "schema version");
                AssertEqual<string?>(null, loaded.ChannelStore.StoreRootPath, "store root");
                AssertEqual<string?>(null, loaded.ChannelStore.LastReceiptId, "receipt id");
                AssertEqual<string?>(null, loaded.ChannelStore.LastPlanSha256, "plan hash");
                AssertEqual<DateTimeOffset?>(null, loaded.ChannelStore.LastCompletedAtUtc, "completion time");
                AssertEqual(0, loaded.ChannelStore.Profiles.Count, "channel profile count");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task ChannelStoreSettingsRoundTrip()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            var completedAt = new DateTimeOffset(2026, 8, 16, 12, 34, 56, TimeSpan.Zero);
            await store.SaveAsync(new UserSettings
            {
                ChannelStore = new ChannelStoreSettings
                {
                    StoreRootPath = " E:\\NikkiwardStore ",
                    LastReceiptId = " receipt-001 ",
                    LastPlanSha256 = " 0123456789ABCDEF ",
                    LastCompletedAtUtc = completedAt,
                    Profiles =
                    [
                        new ChannelStoreProfileSettings
                        {
                            ProfileId = " official-cn ",
                            DistributionChannel = DistributionChannel.Official,
                            GameRootPath = " E:\\NikkiwardStore\\profiles\\official ",
                            LauncherRootPath = " E:\\NikkiwardStore\\runtimes\\official ",
                            XStarterPath = " E:\\NikkiwardStore\\runtimes\\official\\1.3.1\\xstarter.exe ",
                        },
                        new ChannelStoreProfileSettings
                        {
                            ProfileId = " bilibili-cn ",
                            DistributionChannel = DistributionChannel.Bilibili,
                            GameRootPath = " E:\\NikkiwardStore\\profiles\\bilibili ",
                            LauncherRootPath = " E:\\NikkiwardStore\\runtimes\\bilibili ",
                            XStarterPath = " E:\\NikkiwardStore\\runtimes\\bilibili\\1.3.1\\xstarter.exe ",
                        },
                        new ChannelStoreProfileSettings
                        {
                            ProfileId = " steam-global ",
                            DistributionChannel = DistributionChannel.Steam,
                            GameRootPath = " E:\\NikkiwardStore\\profiles\\steam ",
                            LauncherRootPath = " E:\\NikkiwardStore\\runtimes\\steam ",
                            XStarterPath = " E:\\NikkiwardStore\\runtimes\\steam\\1.3.1\\xstarter.exe ",
                        },
                    ],
                },
            });

            var loaded = await store.LoadAsync();

            AssertEqual("E:\\NikkiwardStore", loaded.ChannelStore.StoreRootPath, "store root");
            AssertEqual("receipt-001", loaded.ChannelStore.LastReceiptId, "receipt id");
            AssertEqual("0123456789ABCDEF", loaded.ChannelStore.LastPlanSha256, "plan hash");
            AssertEqual(completedAt, loaded.ChannelStore.LastCompletedAtUtc, "completion time");
            AssertEqual(3, loaded.ChannelStore.Profiles.Count, "channel profile count");
            AssertEqual("official-cn", loaded.ChannelStore.Profiles[0].ProfileId, "official profile id");
            AssertEqual(DistributionChannel.Official, loaded.ChannelStore.Profiles[0].DistributionChannel, "official channel");
            AssertEqual("E:\\NikkiwardStore\\profiles\\official", loaded.ChannelStore.Profiles[0].GameRootPath, "official root");
            AssertEqual("E:\\NikkiwardStore\\runtimes\\official", loaded.ChannelStore.Profiles[0].LauncherRootPath, "official runtime root");
            AssertEqual("E:\\NikkiwardStore\\runtimes\\official\\1.3.1\\xstarter.exe", loaded.ChannelStore.Profiles[0].XStarterPath, "official xstarter");
            AssertEqual("bilibili-cn", loaded.ChannelStore.Profiles[1].ProfileId, "Bilibili profile id");
            AssertEqual(DistributionChannel.Bilibili, loaded.ChannelStore.Profiles[1].DistributionChannel, "Bilibili channel");
            AssertEqual("E:\\NikkiwardStore\\runtimes\\bilibili", loaded.ChannelStore.Profiles[1].LauncherRootPath, "Bilibili runtime root");
            AssertEqual("steam-global", loaded.ChannelStore.Profiles[2].ProfileId, "Steam profile id");
            AssertEqual(DistributionChannel.Steam, loaded.ChannelStore.Profiles[2].DistributionChannel, "Steam channel");
            AssertEqual("E:\\NikkiwardStore\\profiles\\steam", loaded.ChannelStore.Profiles[2].GameRootPath, "Steam store root");
            AssertEqual("E:\\NikkiwardStore\\runtimes\\steam\\1.3.1\\xstarter.exe", loaded.ChannelStore.Profiles[2].XStarterPath, "Steam xstarter");

            var json = await File.ReadAllTextAsync(store.SettingsFilePath);
            Assert(json.Contains("\"channelStore\"", StringComparison.Ordinal), "channel store output");
            Assert(json.Contains("\"distributionChannel\": \"bilibili\"", StringComparison.Ordinal), "Bilibili channel output");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SettingsSchema3MigratesEverySection()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsFilePath)!);
            await File.WriteAllTextAsync(
                store.SettingsFilePath,
                """
                {
                  "schemaVersion": 3,
                  "selectedProfileId": "profile-a",
                  "themeMode": "warmDark",
                  "profiles": [
                    {
                      "profileId": "profile-a",
                      "displayName": "Primary",
                      "channel": "official",
                      "gameRootPath": "D:\\Game",
                      "launcherPath": "D:\\Launcher",
                      "xStarterPath": "D:\\Launcher\\xstarter.exe",
                      "gameExecutablePath": "D:\\Game\\InfinityNikki.exe",
                      "shippingExecutablePath": "D:\\Game\\Shipping.exe",
                      "antiCheatExecutablePath": "D:\\Game\\ACE.exe",
                      "capability": "officialAssisted"
                    }
                  ],
                  "galleryProfiles": [
                    { "profileId": "profile-a", "rootPath": "D:\\Photos" }
                  ],
                  "gamepad": {
                    "enabled": true,
                    "guideLongPressOpensMainWindow": false,
                    "guideAction": "mapKeys",
                    "guideMapKeys": "Ctrl+G",
                    "shareAction": "none"
                  }
                }
                """);

            var loaded = await store.LoadAsync();

            AssertEqual(UserSettings.CurrentSchemaVersion, loaded.SchemaVersion, "schema version");
            AssertEqual("profile-a", loaded.SelectedProfileId, "selected profile");
            AssertEqual(ThemeMode.WarmDark, loaded.Appearance.ThemeMode, "migrated theme");
            AssertEqual(1, loaded.Profiles.Count, "profile count");
            AssertEqual("Primary", loaded.Profiles[0].DisplayName, "profile display name");
            AssertEqual(LaunchCapability.OfficialAssisted, loaded.Profiles[0].Capability, "capability");
            AssertEqual(1, loaded.GalleryProfiles.Count, "gallery count");
            AssertEqual("D:\\Photos", loaded.GalleryProfiles[0].RootPath, "gallery root");
            Assert(loaded.Gamepad.Enabled, "gamepad enabled");
            Assert(!loaded.Gamepad.GuideLongPressOpensMainWindow, "guide long press");
            AssertEqual(GamepadButtonAction.MapKeys, loaded.Gamepad.GuideAction, "guide action");
            AssertEqual("Ctrl+G", loaded.Gamepad.GuideMapKeys, "guide map");
            Assert(!loaded.Appearance.Background.MotionEnabled, "migrated motion default");
            AssertEqual(30, loaded.Appearance.Background.MotionFpsCap, "migrated motion FPS");
            AssertEqual("Stitch", loaded.Appearance.HomeSkin, "home skin");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task MigratedSettingsPersist()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsFilePath)!);
            await File.WriteAllTextAsync(
                store.SettingsFilePath,
                """
                {
                  "schemaVersion": 3,
                  "selectedProfileId": "profile-a",
                  "themeMode": "warmLight",
                  "profiles": [],
                  "galleryProfiles": [],
                  "gamepad": {}
                }
                """);

            var migrated = await store.LoadAsync();
            var json = await File.ReadAllTextAsync(store.SettingsFilePath);

            Assert(json.Contains($"\"schemaVersion\": {UserSettings.CurrentSchemaVersion}", StringComparison.Ordinal), "current schema output");
            Assert(json.Contains("\"appearance\"", StringComparison.Ordinal), "appearance output");
            Assert(
                json.Contains("\"homeSkin\": \"Stitch\"", StringComparison.Ordinal),
                "default home skin output");
            Assert(!json.Contains("\n  \"themeMode\"", StringComparison.Ordinal), "legacy theme removed");
            AssertEqual(ThemeMode.WarmLight, (await store.LoadAsync()).Appearance.ThemeMode, "saved theme");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task UnsupportedSettingsSchemasFailClosed()
    {
        foreach (var version in new[] { 2, UserSettings.CurrentSchemaVersion + 1 })
        {
            var root = CreateTemporaryRoot();
            try
            {
                var store = new JsonUserSettingsStore(root);
                Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsFilePath)!);
                await File.WriteAllTextAsync(
                    store.SettingsFilePath,
                    $"{{\"schemaVersion\":{version},\"profiles\":[],\"galleryProfiles\":[],\"gamepad\":{{}}}}");

                var exception = await AssertThrowsAsync<UserSettingsStoreException>(
                    () => store.LoadAsync());
                Assert(exception.InnerException is JsonException, "inner exception must be JSON failure");
                Assert(
                    exception.InnerException!.Message.Contains($"expected {UserSettings.CurrentSchemaVersion}", StringComparison.Ordinal),
                    "schema mismatch must identify current version");
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task DamagedSettingsDocumentsFailClosed()
    {
        var documents = new[]
        {
            "not-json",
            "{}",
            "[]",
            "{\"schemaVersion\":6}",
            "{\"schemaVersion\":6,\"appearance\":{\"motion\":99},\"profiles\":[],\"galleryProfiles\":[],\"gamepad\":{}}",
            "{\"schemaVersion\":6,\"appearance\":{\"accentMode\":\"unknown\"},\"profiles\":[],\"galleryProfiles\":[],\"gamepad\":{}}",
            "{\"schemaVersion\":6,\"appearance\":{},\"profiles\":[],\"galleryProfiles\":[],\"gamepad\":{},\"unknownSection\":true}",
        };

        foreach (var document in documents)
        {
            var root = CreateTemporaryRoot();
            try
            {
                var store = new JsonUserSettingsStore(root);
                Directory.CreateDirectory(Path.GetDirectoryName(store.SettingsFilePath)!);
                await File.WriteAllTextAsync(store.SettingsFilePath, document);

                _ = await AssertThrowsAsync<UserSettingsStoreException>(
                    () => store.LoadAsync());
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task SaveRejectsInvalidSettings()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            await AssertThrowsAsync<ArgumentException>(() => store.SaveAsync(
                new UserSettings { SchemaVersion = 4 }));
            await AssertThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(
                new UserSettings
                {
                    Appearance = new AppearanceSettings
                    {
                        Motion = (AppearanceMotionMode)99,
                    },
                }));
            await AssertThrowsAsync<ArgumentOutOfRangeException>(() => store.SaveAsync(
                new UserSettings
                {
                    Appearance = new AppearanceSettings
                    {
                        Density = (InterfaceDensity)99,
                    },
                }));
            Assert(!File.Exists(store.SettingsFilePath), "invalid settings should not be written");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AtomicReplacementLeavesNoTemporaryFiles()
    {
        var root = CreateTemporaryRoot();
        try
        {
            var store = new JsonUserSettingsStore(root);
            await store.SaveAsync(new UserSettings
            {
                Appearance = new AppearanceSettings { ThemeMode = ThemeMode.WarmLight },
            });
            await store.SaveAsync(new UserSettings
            {
                Appearance = new AppearanceSettings { ThemeMode = ThemeMode.WarmDark },
            });

            var directory = Path.GetDirectoryName(store.SettingsFilePath)!;
            AssertEqual(0, Directory.GetFiles(directory, "*.tmp").Length, "temporary file count");
            AssertEqual(ThemeMode.WarmDark, (await store.LoadAsync()).Appearance.ThemeMode, "replacement");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Task ApplicationPathsPreserveDriveRoots()
    {
        var driveRoot = Path.GetPathRoot(Environment.SystemDirectory)
            ?? throw new InvalidOperationException("The Windows drive root is unavailable.");
        var normalized = ApplicationSettingsValidator.Normalize(new DownloadSettings
        {
            DefaultGameInstallPath = driveRoot,
        });

        AssertEqual(driveRoot, normalized.DefaultGameInstallPath, "drive root path");
        return Task.CompletedTask;
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Nikkiward.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
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
