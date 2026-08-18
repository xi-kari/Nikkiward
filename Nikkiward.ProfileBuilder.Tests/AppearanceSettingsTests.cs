using System.Text.Json;
using System.Text.Json.Nodes;
using Nikkiward.Models;

internal static class AppearanceSettingsTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("appearance defaults target schema 6", DefaultsTargetSchema6),
        ("launcher masthead defaults are customizable", LauncherMastheadDefaultsAreCustomizable),
        ("launcher masthead text normalization restores safe defaults", LauncherMastheadTextNormalizationRestoresDefaults),
        ("appearance choices expose the planned mode counts", ChoicesExposePlannedModeCounts),
        ("fixed accents match the five brand colors", FixedAccentsMatchBrandColors),
        ("custom fixed accent is projected opaque", CustomFixedAccentIsProjectedOpaque),
        ("schema 3 migration preserves existing settings sections", MigrationPreservesExistingSections),
        ("schema 3 migration moves the existing theme into appearance", MigrationMovesExistingTheme),
        ("schema 3 migration preserves appearance extension values", MigrationPreservesExtensionValues),
        ("schema 4 migration adds motion background defaults", Schema4MigrationAddsMotionDefaults),
        ("schema 4 migration preserves existing motion values", Schema4MigrationPreservesMotionValues),
        ("schema 5 migration adds holographic card defaults", Schema5MigrationAddsHolographicCardDefaults),
        ("appearance migration rejects unsupported schemas", MigrationRejectsUnsupportedSchemas),
        ("appearance normalization trims and deduplicates sources", NormalizationTrimsAndDeduplicatesSources),
        ("appearance validation rejects undefined choices", ValidationRejectsUndefinedChoices),
        ("motion background validation rejects invalid parameters", MotionValidationRejectsInvalidParameters),
        ("system animation disablement zeros every motion channel", SystemAnimationDisablementZerosMotion),
        ("motion levels remain ordered and off is zero", MotionLevelsRemainOrdered),
        ("missing background projects to shipped artwork", MissingBackgroundUsesShippedArtwork),
        ("available background remains selected", AvailableBackgroundRemainsSelected),
    ];

    private static Task DefaultsTargetSchema6()
    {
        var userSettings = new UserSettings();
        var settings = userSettings.Appearance;

        AssertEqual(6, UserSettings.CurrentSchemaVersion, "schema version");
        AssertEqual(ThemeMode.FollowArtwork, settings.ThemeMode, "theme mode");
        AssertEqual(AccentColorMode.Adaptive, settings.AccentMode, "accent mode");
        AssertEqual(FixedAccentColor.Blush, settings.FixedAccent, "fixed accent");
        Assert(settings.CustomAccentArgb is null, "custom accent should be absent");
        Assert(settings.UseSerifTitles, "serif titles should be enabled");
        AssertEqual(AppearanceMotionMode.Full, settings.Motion, "motion mode");
        AssertEqual(InterfaceDensity.Standard, settings.Density, "density");
        AssertEqual(
            LauncherCapsuleStyle.Ocean,
            settings.LauncherCapsuleStyle,
            "launcher capsule style");
        Assert(settings.Background.SelectedSource is null, "selected background");
        AssertEqual(0, settings.Background.CarouselSources.Count, "carousel sources");
        Assert(!settings.Background.CarouselEnabled, "carousel should be disabled");
        AssertEqual(15, settings.Background.CarouselIntervalMinutes, "carousel interval");
        Assert(!settings.Background.ParallaxEnabled, "parallax should be disabled");
        Assert(settings.Background.HolographicCardEnabled, "holographic card should be enabled");
        Assert(!settings.Background.MotionEnabled, "motion should be disabled by default");
        Assert(settings.Background.MotionSource is null, "motion source should be absent");
        AssertEqual(30, settings.Background.MotionFpsCap, "motion FPS cap");
        Assert(!settings.Background.UseLiveBlur, "live blur should be disabled by default");
        AssertEqual(1d, settings.Background.GlassIntensity, "glass intensity");
        Assert(!settings.Background.MotionPanEnabled, "motion pan should be disabled by default");
        AssertEqual(1d, settings.Background.MotionZoom, "motion zoom");
        return Task.CompletedTask;
    }

    private static Task LauncherMastheadDefaultsAreCustomizable()
    {
        var settings = new AppearanceSettings();

        AssertEqual(
            AppearanceSettings.DefaultLauncherMastheadLabel,
            settings.LauncherMastheadLabel,
            "masthead label default");
        AssertEqual(
            AppearanceSettings.DefaultLauncherMastheadTitle,
            settings.LauncherMastheadTitle,
            "masthead title default");
        AssertEqual(
            AppearanceSettings.DefaultLauncherMastheadSubtitle,
            settings.LauncherMastheadSubtitle,
            "masthead subtitle default");
        AssertEqual("无限暖暖启动！", settings.LauncherMastheadSubtitle, "stable launcher subtitle");
        Assert(!settings.ShowLauncherUtilityPanels, "launcher notice panel should be hidden by default");
        return Task.CompletedTask;
    }

    private static Task LauncherMastheadTextNormalizationRestoresDefaults()
    {
        var normalized = AppearanceSettingsValidator.Normalize(new AppearanceSettings
        {
            LauncherMastheadLabel = "  ",
            LauncherMastheadTitle = "  自定义标题  ",
            LauncherMastheadSubtitle = "  ",
        });

        AssertEqual(
            AppearanceSettings.DefaultLauncherMastheadLabel,
            normalized.LauncherMastheadLabel,
            "blank masthead label");
        AssertEqual("自定义标题", normalized.LauncherMastheadTitle, "trimmed masthead title");
        AssertEqual(string.Empty, normalized.LauncherMastheadSubtitle, "blank masthead subtitle");

        var limited = AppearanceSettingsValidator.Normalize(new AppearanceSettings
        {
            LauncherMastheadSubtitle = new string('x', 160),
        });
        AssertEqual(120, limited.LauncherMastheadSubtitle.Length, "masthead subtitle length");
        return Task.CompletedTask;
    }

    private static Task ChoicesExposePlannedModeCounts()
    {
        AssertEqual(3, Enum.GetValues<ThemeMode>().Length, "theme choices");
        AssertEqual(2, Enum.GetValues<AccentColorMode>().Length, "accent modes");
        AssertEqual(5, Enum.GetValues<FixedAccentColor>().Length, "fixed accents");
        AssertEqual(3, Enum.GetValues<AppearanceMotionMode>().Length, "motion choices");
        AssertEqual(3, Enum.GetValues<InterfaceDensity>().Length, "density choices");
        AssertEqual(6, Enum.GetValues<LauncherCapsuleStyle>().Length, "launcher capsule styles");
        Assert(
            Enum.GetNames<LauncherCapsuleStyle>().SequenceEqual(
                ["Original", "Ocean", "Klein", "Ultraviolet", "Chrome", "Plus"]),
            "launcher capsule styles must retain the six reference values");
        return Task.CompletedTask;
    }

    private static Task FixedAccentsMatchBrandColors()
    {
        var expected = new (FixedAccentColor Color, uint Argb)[]
        {
            (FixedAccentColor.Blush, 0xFFE8A0B4),
            (FixedAccentColor.Gold, 0xFFD9A657),
            (FixedAccentColor.Mint, 0xFF8FC9B8),
            (FixedAccentColor.Lilac, 0xFFB9A6DA),
            (FixedAccentColor.Clay, 0xFFC77B62),
        };

        AssertEqual(expected.Length, AppearanceAccentPalette.FixedColors.Count, "palette size");
        for (var index = 0; index < expected.Length; index++)
        {
            AssertEqual(expected[index].Color, AppearanceAccentPalette.FixedColors[index].Color, $"color {index}");
            AssertEqual(expected[index].Argb, AppearanceAccentPalette.FixedColors[index].Argb, $"ARGB {index}");
        }

        return Task.CompletedTask;
    }

    private static Task CustomFixedAccentIsProjectedOpaque()
    {
        var settings = new AppearanceSettings
        {
            AccentMode = AccentColorMode.Fixed,
            FixedAccent = FixedAccentColor.Gold,
            CustomAccentArgb = 0x00776655,
        };

        AssertEqual(0xFF776655u, AppearanceAccentPalette.ResolveFixed(settings), "custom accent");
        AssertEqual(
            AppearanceAccentPalette.BlushArgb,
            AppearanceAccentPalette.ResolveFixed(new AppearanceSettings()),
            "adaptive projection fallback");
        return Task.CompletedTask;
    }

    private static Task MigrationPreservesExistingSections()
    {
        var source = ParseObject("""
            {
              "schemaVersion": 3,
              "selectedProfileId": "profile-a",
              "themeMode": "warmDark",
              "profiles": [{ "profileId": "profile-a", "displayName": "A" }],
              "galleryProfiles": [{ "profileId": "profile-a", "rootPath": "D:\\Photos" }],
              "gamepad": { "enabled": true }
            }
            """);
        var sourceBefore = source.ToJsonString();

        var migrated = AppearanceSettingsMigration.MigrateSchema3To4(source);

        AssertEqual(sourceBefore, source.ToJsonString(), "migration input");
        AssertEqual(4, migrated["schemaVersion"]!.GetValue<int>(), "migrated schema");
        AssertEqual(
            source["profiles"]!.ToJsonString(),
            migrated["profiles"]!.ToJsonString(),
            "profiles");
        AssertEqual(
            source["galleryProfiles"]!.ToJsonString(),
            migrated["galleryProfiles"]!.ToJsonString(),
            "gallery profiles");
        AssertEqual(
            source["gamepad"]!.ToJsonString(),
            migrated["gamepad"]!.ToJsonString(),
            "gamepad");
        return Task.CompletedTask;
    }

    private static Task MigrationMovesExistingTheme()
    {
        var migrated = AppearanceSettingsMigration.MigrateSchema3To4(
            ParseObject("""{ "schemaVersion": 3, "themeMode": "warmDark" }"""));
        var appearance = migrated["appearance"]!.AsObject();

        AssertEqual("warmDark", appearance["themeMode"]!.GetValue<string>(), "appearance theme");
        Assert(!migrated.ContainsKey("themeMode"), "legacy theme authority should be removed");
        AssertEqual("adaptive", appearance["accentMode"]!.GetValue<string>(), "accent default");
        AssertEqual("full", appearance["motion"]!.GetValue<string>(), "motion default");
        AssertEqual("standard", appearance["density"]!.GetValue<string>(), "density default");
        return Task.CompletedTask;
    }

    private static Task MigrationPreservesExtensionValues()
    {
        var source = ParseObject("""
            {
              "schemaVersion": 3,
              "themeMode": "warmLight",
              "appearance": {
                "motion": "reduced",
                "background": { "parallaxEnabled": false }
              }
            }
            """);

        var migrated = AppearanceSettingsMigration.MigrateSchema3To4(source);
        var appearance = migrated["appearance"]!.AsObject();
        var background = appearance["background"]!.AsObject();

        AssertEqual("reduced", appearance["motion"]!.GetValue<string>(), "motion extension");
        Assert(!background["parallaxEnabled"]!.GetValue<bool>(), "parallax extension");
        AssertEqual(15, background["carouselIntervalMinutes"]!.GetValue<int>(), "background default");
        return Task.CompletedTask;
    }

    private static Task Schema4MigrationAddsMotionDefaults()
    {
        var source = ParseObject("""
            {
              "schemaVersion": 4,
              "appearance": {
                "background": {
                  "selectedSource": "D:\\Art\\still.png",
                  "carouselSources": [],
                  "carouselEnabled": false,
                  "carouselIntervalMinutes": 15,
                  "parallaxEnabled": true
                }
              }
            }
            """);
        var sourceBefore = source.ToJsonString();

        var migrated = AppearanceSettingsMigration.MigrateSchema4To5(source);
        var background = migrated["appearance"]!["background"]!.AsObject();

        AssertEqual(sourceBefore, source.ToJsonString(), "schema 4 migration input");
        AssertEqual(5, migrated["schemaVersion"]!.GetValue<int>(), "migrated schema");
        AssertEqual("D:\\Art\\still.png", background["selectedSource"]!.GetValue<string>(), "still source");
        Assert(!background["motionEnabled"]!.GetValue<bool>(), "motion default");
        AssertEqual(30, background["motionFpsCap"]!.GetValue<int>(), "FPS default");
        Assert(!background["useLiveBlur"]!.GetValue<bool>(), "live blur default");
        AssertEqual(1d, background["glassIntensity"]!.GetValue<double>(), "glass default");
        Assert(!background["motionPanEnabled"]!.GetValue<bool>(), "motion pan default");
        AssertEqual(1d, background["motionZoom"]!.GetValue<double>(), "motion zoom default");
        Assert(!background.ContainsKey("motionSource"), "null motion source should remain omitted");
        return Task.CompletedTask;
    }

    private static Task Schema4MigrationPreservesMotionValues()
    {
        var migrated = AppearanceSettingsMigration.MigrateSchema4To5(ParseObject("""
            {
              "schemaVersion": 4,
              "appearance": {
                "background": {
                  "motionEnabled": true,
                  "motionSource": "D:\\Art\\loop.mp4",
                  "motionFpsCap": 60,
                  "useLiveBlur": true,
                  "glassIntensity": 0.55,
                  "motionPanEnabled": true,
                  "motionZoom": 1.4
                }
              }
            }
            """));
        var background = migrated["appearance"]!["background"]!.AsObject();

        Assert(background["motionEnabled"]!.GetValue<bool>(), "motion value");
        AssertEqual("D:\\Art\\loop.mp4", background["motionSource"]!.GetValue<string>(), "motion source");
        AssertEqual(60, background["motionFpsCap"]!.GetValue<int>(), "motion FPS");
        Assert(background["useLiveBlur"]!.GetValue<bool>(), "live blur value");
        AssertEqual(0.55, background["glassIntensity"]!.GetValue<double>(), "glass value");
        Assert(background["motionPanEnabled"]!.GetValue<bool>(), "motion pan value");
        AssertEqual(1.4, background["motionZoom"]!.GetValue<double>(), "motion zoom value");
        return Task.CompletedTask;
    }

    private static Task Schema5MigrationAddsHolographicCardDefaults()
    {
        var source = ParseObject("""
            {
              "schemaVersion": 5,
              "appearance": {
                "background": {
                  "motionEnabled": false
                }
              }
            }
            """);
        var sourceBefore = source.ToJsonString();
        var migrated = AppearanceSettingsMigration.MigrateSchema5To6(source);
        var background = migrated["appearance"]!["background"]!.AsObject();

        AssertEqual(sourceBefore, source.ToJsonString(), "schema 5 migration input");
        AssertEqual(6, migrated["schemaVersion"]!.GetValue<int>(), "migrated schema");
        Assert(background["holographicCardEnabled"]!.GetValue<bool>(), "holographic default");

        var explicitOff = AppearanceSettingsMigration.MigrateSchema5To6(ParseObject("""
            {
              "schemaVersion": 5,
              "appearance": {
                "background": {
                  "holographicCardEnabled": false
                }
              }
            }
            """));
        Assert(
            !explicitOff["appearance"]!["background"]!["holographicCardEnabled"]!.GetValue<bool>(),
            "existing holographic choice");
        return Task.CompletedTask;
    }

    private static Task MigrationRejectsUnsupportedSchemas()
    {
        AssertThrows<JsonException>(() =>
            AppearanceSettingsMigration.MigrateSchema3To4(
                ParseObject("""{ "schemaVersion": 2 }""")));
        AssertThrows<JsonException>(() =>
            AppearanceSettingsMigration.MigrateSchema3To4(
                ParseObject("""{ "schemaVersion": 4 }""")));
        AssertThrows<JsonException>(() =>
            AppearanceSettingsMigration.MigrateSchema3To4(
                ParseObject("""{ "schemaVersion": 3, "appearance": "invalid" }""")));
        AssertThrows<JsonException>(() =>
            AppearanceSettingsMigration.MigrateSchema4To5(
                ParseObject("""{ "schemaVersion": 3 }""")));
        AssertThrows<JsonException>(() =>
            AppearanceSettingsMigration.MigrateSchema4To5(
                ParseObject("""{ "schemaVersion": 5 }""")));
        AssertThrows<JsonException>(() =>
            AppearanceSettingsMigration.MigrateSchema4To5(
                ParseObject("""{ "schemaVersion": 4, "appearance": {} }""")));
        AssertThrows<JsonException>(() =>
            AppearanceSettingsMigration.MigrateSchema5To6(
                ParseObject("""{ "schemaVersion": 4 }""")));
        AssertThrows<JsonException>(() =>
            AppearanceSettingsMigration.MigrateSchema5To6(
                ParseObject("""{ "schemaVersion": 5, "appearance": {} }""")));
        return Task.CompletedTask;
    }

    private static Task NormalizationTrimsAndDeduplicatesSources()
    {
        var normalized = AppearanceSettingsValidator.Normalize(new AppearanceSettings
        {
            CustomAccentArgb = 0x00112233,
                Background = new BackgroundArtSettings
                {
                    SelectedSource = "  D:\\Art\\one.png  ",
                    MotionSource = "  D:\\Art\\loop.mp4  ",
                    MotionEnabled = true,
                    CarouselEnabled = true,
                CarouselSources =
                [
                    "D:\\Art\\one.png",
                    " d:\\art\\ONE.png ",
                    " ",
                    "D:\\Art\\two.png",
                ],
            },
        });

        AssertEqual("D:\\Art\\one.png", normalized.Background.SelectedSource, "selected source");
        AssertEqual("D:\\Art\\loop.mp4", normalized.Background.MotionSource, "motion source");
        Assert(normalized.Background.MotionEnabled, "motion should remain enabled with a source");
        Assert(!normalized.Background.CarouselEnabled, "motion and still carousel must be mutually exclusive");
        AssertEqual(2, normalized.Background.CarouselSources.Count, "unique source count");
        AssertEqual(0xFF112233u, normalized.CustomAccentArgb, "opaque accent");
        return Task.CompletedTask;
    }

    private static Task MotionValidationRejectsInvalidParameters()
    {
        foreach (var legacyFpsValue in new[] { -1, 0, 24, 30, 60, 120, 1000 })
        {
            var normalized = AppearanceSettingsValidator.Normalize(new AppearanceSettings
            {
                Background = new BackgroundArtSettings
                {
                    MotionFpsCap = legacyFpsValue,
                },
            });
            AssertEqual(
                legacyFpsValue,
                normalized.Background.MotionFpsCap,
                "legacy FPS value must not impose a runtime cap");
        }

        foreach (var intensity in new[] { double.NaN, double.NegativeInfinity, -0.01, 1.01 })
        {
            AssertThrows<ArgumentOutOfRangeException>(() =>
                AppearanceSettingsValidator.Normalize(new AppearanceSettings
                {
                    Background = new BackgroundArtSettings { GlassIntensity = intensity },
                }));
        }

        foreach (var zoom in new[] { double.NaN, double.PositiveInfinity, 0.99, 2.81 })
        {
            AssertThrows<ArgumentOutOfRangeException>(() =>
                AppearanceSettingsValidator.Normalize(new AppearanceSettings
                {
                    Background = new BackgroundArtSettings { MotionZoom = zoom },
                }));
        }

        return Task.CompletedTask;
    }

    private static Task ValidationRejectsUndefinedChoices()
    {
        AssertThrows<ArgumentOutOfRangeException>(() =>
            AppearanceSettingsValidator.Normalize(new AppearanceSettings
            {
                Motion = (AppearanceMotionMode)99,
            }));
        AssertThrows<ArgumentOutOfRangeException>(() =>
            AppearanceSettingsValidator.Normalize(new AppearanceSettings
            {
                LauncherCapsuleStyle = (LauncherCapsuleStyle)99,
            }));
        AssertThrows<ArgumentOutOfRangeException>(() =>
            AppearanceSettingsValidator.Normalize(new AppearanceSettings
            {
                Background = new BackgroundArtSettings
                {
                    CarouselIntervalMinutes = 0,
                },
            }));
        return Task.CompletedTask;
    }

    private static Task SystemAnimationDisablementZerosMotion()
    {
        foreach (var mode in Enum.GetValues<AppearanceMotionMode>())
        {
            var projection = AppearanceProjector.ProjectMotion(mode, systemAnimationsEnabled: false);
            Assert(projection.IsZero, $"{mode} should project to zero");
            AssertEqual(1d, projection.HoverScale, $"{mode} hover scale");
            AssertEqual(1d, projection.PressScale, $"{mode} press scale");
        }

        return Task.CompletedTask;
    }

    private static Task MotionLevelsRemainOrdered()
    {
        var full = AppearanceProjector.ProjectMotion(
            AppearanceMotionMode.Full,
            systemAnimationsEnabled: true);
        var reduced = AppearanceProjector.ProjectMotion(
            AppearanceMotionMode.Reduced,
            systemAnimationsEnabled: true);
        var off = AppearanceProjector.ProjectMotion(
            AppearanceMotionMode.Off,
            systemAnimationsEnabled: true);

        Assert(full.ArtDurationMilliseconds > reduced.ArtDurationMilliseconds, "full art duration");
        Assert(reduced.ArtDurationMilliseconds > 0, "reduced mode should retain brief fades");
        AssertEqual(180d, full.StateDurationMilliseconds, "full state duration");
        AssertEqual(280d, full.PanelOpenDurationMilliseconds, "full panel open duration");
        AssertEqual(180d, full.PanelCloseDurationMilliseconds, "full panel close duration");
        AssertEqual(120d, reduced.StateDurationMilliseconds, "reduced state duration");
        AssertEqual(180d, reduced.PanelOpenDurationMilliseconds, "reduced panel open duration");
        AssertEqual(120d, reduced.PanelCloseDurationMilliseconds, "reduced panel close duration");
        Assert(full.ParallaxAmplitude > 0, "full parallax");
        AssertEqual(0d, reduced.ParallaxAmplitude, "reduced parallax");
        Assert(off.IsZero, "off projection");
        return Task.CompletedTask;
    }

    private static Task MissingBackgroundUsesShippedArtwork()
    {
        var background = new BackgroundArtSettings
        {
            SelectedSource = "D:\\Missing\\wallpaper.png",
            CarouselEnabled = true,
            CarouselSources = ["D:\\Missing\\one.png", "D:\\Missing\\two.png"],
        };

        var projection = AppearanceProjector.ProjectBackground(background, []);

        AssertEqual(AppearanceProjector.BuiltInBackgroundSource, projection.Source, "background source");
        Assert(projection.UsesFallback, "fallback marker");
        Assert(!projection.CarouselEnabled, "missing carousel");
        AssertEqual(15, projection.CarouselIntervalMinutes, "safe interval");
        Assert(!projection.ParallaxEnabled, "fallback parallax preference");
        return Task.CompletedTask;
    }

    private static Task AvailableBackgroundRemainsSelected()
    {
        var sources = new[] { "D:\\Art\\one.png", "D:\\Art\\two.png" };
        var background = new BackgroundArtSettings
        {
            SelectedSource = "  d:\\art\\ONE.png  ",
            CarouselEnabled = true,
            CarouselIntervalMinutes = 30,
            CarouselSources = sources,
            ParallaxEnabled = false,
        };

        var projection = AppearanceProjector.ProjectBackground(background, sources);

        AssertEqual("d:\\art\\ONE.png", projection.Source, "selected source");
        Assert(!projection.UsesFallback, "custom source marker");
        Assert(projection.CarouselEnabled, "carousel projection");
        AssertEqual(30, projection.CarouselIntervalMinutes, "carousel interval");
        Assert(!projection.ParallaxEnabled, "parallax preference");
        return Task.CompletedTask;
    }

    private static JsonObject ParseObject(string json) =>
        JsonNode.Parse(json)?.AsObject() ?? throw new InvalidOperationException("JSON object required.");

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
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
