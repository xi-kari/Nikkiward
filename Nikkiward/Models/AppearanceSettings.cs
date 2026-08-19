using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Nikkiward.Serialization;

namespace Nikkiward.Models;

public enum AccentColorMode
{
    Adaptive,
    Fixed,
}

public enum FixedAccentColor
{
    Blush,
    Gold,
    Mint,
    Lilac,
    Clay,
}

public enum AppearanceMotionMode
{
    Full,
    Reduced,
    Off,
}

public enum WallpaperEnginePresentation
{
    None,
    HolographicCard,
    MotionBackdrop,
}

public enum InterfaceDensity
{
    Compact,
    Standard,
    Comfortable,
}

public enum LauncherCapsuleStyle
{
    Original,
    Ocean,
    Klein,
    Ultraviolet,
    Chrome,
    Plus,
}

public sealed record AppearanceSettings
{
    public const string LegacyLauncherMastheadLabel = "启动契约 1.3.1";

    public const string DefaultLauncherMastheadLabel = "NIKKIWARD";

    public const string DefaultLauncherMastheadTitle = "无限暖暖";

    public const string DefaultLauncherMastheadSubtitle = "无限暖暖启动！";

    public ThemeMode ThemeMode { get; init; } = ThemeMode.WarmDark;

    public BackgroundArtSettings Background { get; init; } = new();

    public string LauncherMastheadLabel { get; init; } = DefaultLauncherMastheadLabel;

    public string LauncherMastheadTitle { get; init; } = DefaultLauncherMastheadTitle;

    public string LauncherMastheadSubtitle { get; init; } = DefaultLauncherMastheadSubtitle;

    public bool ShowLauncherUtilityPanels { get; init; }

    public LauncherCapsuleStyle LauncherCapsuleStyle { get; init; } = LauncherCapsuleStyle.Ocean;

    public AccentColorMode AccentMode { get; init; } = AccentColorMode.Adaptive;

    public FixedAccentColor FixedAccent { get; init; } = FixedAccentColor.Blush;

    public uint? CustomAccentArgb { get; init; }

    public bool UseSerifTitles { get; init; } = true;

    public AppearanceMotionMode Motion { get; init; } = AppearanceMotionMode.Full;

    public InterfaceDensity Density { get; init; } = InterfaceDensity.Standard;
}

public sealed record BackgroundArtSettings
{
    public const int DefaultCarouselIntervalMinutes = 15;

    public const int MinimumCarouselIntervalMinutes = 1;

    public const int MaximumCarouselIntervalMinutes = 24 * 60;

    public string? SelectedSource { get; init; }

    public IReadOnlyList<string> CarouselSources { get; init; } = Array.Empty<string>();

    public bool CarouselEnabled { get; init; }

    public int CarouselIntervalMinutes { get; init; } = DefaultCarouselIntervalMinutes;

    public bool ParallaxEnabled { get; init; }

    public bool HolographicCardEnabled { get; init; } = true;

    public bool MotionEnabled { get; init; }

    public string? MotionSource { get; init; }

    public WallpaperEnginePresentation WallpaperEnginePresentation { get; init; }

    public string? WallpaperEnginePackageSource { get; init; }

    public int MotionFpsCap { get; init; } = 30;

    public bool UseLiveBlur { get; init; }

    public double GlassIntensity { get; init; } = 1.0;

    public bool MotionPanEnabled { get; init; }

    public double MotionZoom { get; init; } = 1.0;
}

public readonly record struct AppearanceBackgroundPreset(
    string Id,
    string Title,
    string Source,
    LauncherCapsuleStyle CapsuleStyle,
    ThemeMode? SurfaceThemeMode);

public static class AppearanceBackgroundPresets
{
    public const string Preset1Id = "background1";

    public const string Preset2Id = "background2";

    public const string Preset1Source =
        "ms-appx:///Assets/NikkiDefaultBackground.jpg";

    public const string Preset2Source =
        "ms-appx:///Assets/NikkiPresetBackground2.jpg";

    public static IReadOnlyList<AppearanceBackgroundPreset> All { get; } =
    [
        new(
            Preset1Id,
            "默认预设背景 1",
            Preset1Source,
            LauncherCapsuleStyle.Ocean,
            ThemeMode.WarmDark),
        new(
            Preset2Id,
            "默认预设背景 2",
            Preset2Source,
            LauncherCapsuleStyle.Plus,
            ThemeMode.WarmLight),
    ];

    public static bool IsBuiltInSource(string? source) =>
        string.Equals(source, Preset1Source, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, Preset2Source, StringComparison.OrdinalIgnoreCase);

    public static bool TryGet(string? id, out AppearanceBackgroundPreset preset)
    {
        if (string.Equals(id, Preset1Id, StringComparison.Ordinal))
        {
            preset = All[0];
            return true;
        }

        if (string.Equals(id, Preset2Id, StringComparison.Ordinal))
        {
            preset = All[1];
            return true;
        }

        preset = default;
        return false;
    }
}

public readonly record struct AccentSwatch(
    FixedAccentColor Color,
    uint Argb);

public static class AppearanceAccentPalette
{
    public const uint BlushArgb = 0xFFE8A0B4;
    public const uint GoldArgb = 0xFFD9A657;
    public const uint MintArgb = 0xFF8FC9B8;
    public const uint LilacArgb = 0xFFB9A6DA;
    public const uint ClayArgb = 0xFFC77B62;

    public static IReadOnlyList<AccentSwatch> FixedColors { get; } =
    [
        new(FixedAccentColor.Blush, BlushArgb),
        new(FixedAccentColor.Gold, GoldArgb),
        new(FixedAccentColor.Mint, MintArgb),
        new(FixedAccentColor.Lilac, LilacArgb),
        new(FixedAccentColor.Clay, ClayArgb),
    ];

    public static uint ResolveFixed(AppearanceSettings? settings)
    {
        if (settings?.AccentMode != AccentColorMode.Fixed)
        {
            return BlushArgb;
        }

        if (settings.CustomAccentArgb is uint custom)
        {
            return custom | 0xFF000000;
        }

        return settings.FixedAccent switch
        {
            FixedAccentColor.Gold => GoldArgb,
            FixedAccentColor.Mint => MintArgb,
            FixedAccentColor.Lilac => LilacArgb,
            FixedAccentColor.Clay => ClayArgb,
            _ => BlushArgb,
        };
    }
}

public readonly record struct MotionProjection(
    double MicroDurationMilliseconds,
    double StandardDurationMilliseconds,
    double SurfaceDurationMilliseconds,
    double ArtDurationMilliseconds,
    double StateDurationMilliseconds,
    double PanelOpenDurationMilliseconds,
    double PanelCloseDurationMilliseconds,
    double HoverScaleDelta,
    double PressScaleDelta,
    double ButtonHoverScaleDelta,
    double ParallaxAmplitude)
{
    public static MotionProjection None => default;

    public bool IsZero =>
        MicroDurationMilliseconds == 0 &&
        StandardDurationMilliseconds == 0 &&
        SurfaceDurationMilliseconds == 0 &&
        ArtDurationMilliseconds == 0 &&
        StateDurationMilliseconds == 0 &&
        PanelOpenDurationMilliseconds == 0 &&
        PanelCloseDurationMilliseconds == 0 &&
        HoverScaleDelta == 0 &&
        PressScaleDelta == 0 &&
        ButtonHoverScaleDelta == 0 &&
        ParallaxAmplitude == 0;

    public double HoverScale => 1 + HoverScaleDelta;

    public double PressScale => 1 + PressScaleDelta;

    public double ButtonHoverScale => 1 + ButtonHoverScaleDelta;
}

public sealed record BackgroundProjection
{
    public required string Source { get; init; }

    public bool UsesFallback { get; init; }

    public bool CarouselEnabled { get; init; }

    public int CarouselIntervalMinutes { get; init; }

    public bool ParallaxEnabled { get; init; }
}

public static class AppearanceProjector
{
    public const string BuiltInBackgroundSource =
        AppearanceBackgroundPresets.Preset1Source;

    public const string BuiltInBackgroundPreset2Source =
        AppearanceBackgroundPresets.Preset2Source;

    public const string BuiltInBlurredBackgroundSource =
        "ms-appx:///Assets/NikkiDefaultBackgroundBlur.jpg";

    public static MotionProjection ProjectMotion(
        AppearanceMotionMode requestedMode,
        bool systemAnimationsEnabled)
    {
        if (!systemAnimationsEnabled)
        {
            return MotionProjection.None;
        }

        return requestedMode switch
        {
            AppearanceMotionMode.Full => new MotionProjection(
                120,
                200,
                320,
                480,
                180,
                280,
                180,
                0.06,
                -0.02,
                0.02,
                10),
            AppearanceMotionMode.Reduced => new MotionProjection(
                80,
                120,
                160,
                200,
                120,
                180,
                120,
                0,
                0,
                0,
                0),
            _ => MotionProjection.None,
        };
    }

    public static MotionProjection ProjectMotion(
        AppearanceSettings? settings,
        bool systemAnimationsEnabled) =>
        ProjectMotion(
            settings?.Motion ?? AppearanceMotionMode.Full,
            systemAnimationsEnabled);

    public static BackgroundProjection ProjectBackground(
        BackgroundArtSettings? settings,
        IEnumerable<string>? availableCustomSources,
        string? fallbackSource = null)
    {
        var fallback = NormaliseSource(fallbackSource) ?? BuiltInBackgroundSource;
        var available = new HashSet<string>(
            availableCustomSources?
                .Select(NormaliseSource)
                .Where(source => source is not null)
                .Select(source => source!) ?? [],
            StringComparer.OrdinalIgnoreCase);

        bool IsAvailableCustomSource(string source) =>
            !source.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase) &&
            available.Contains(source);

        var selected = NormaliseSource(settings?.SelectedSource);
        var hasSelectedSource = selected is not null &&
            (AppearanceBackgroundPresets.IsBuiltInSource(selected) ||
             IsAvailableCustomSource(selected));
        if (!hasSelectedSource)
        {
            selected = settings?.CarouselSources?
                .Select(NormaliseSource)
                .FirstOrDefault(source =>
                    source is not null && IsAvailableCustomSource(source));
        }

        var usesFallback = selected is null;
        var interval = settings?.CarouselIntervalMinutes ??
            BackgroundArtSettings.DefaultCarouselIntervalMinutes;
        if (interval is < BackgroundArtSettings.MinimumCarouselIntervalMinutes or
            > BackgroundArtSettings.MaximumCarouselIntervalMinutes)
        {
            interval = BackgroundArtSettings.DefaultCarouselIntervalMinutes;
        }

        var availableCarouselCount = settings?.CarouselSources?
            .Select(NormaliseSource)
            .Where(source => source is not null && IsAvailableCustomSource(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() ?? 0;

        return new BackgroundProjection
        {
            Source = selected ?? fallback,
            UsesFallback = usesFallback,
            CarouselEnabled =
                settings?.CarouselEnabled == true && availableCarouselCount > 1,
            CarouselIntervalMinutes = interval,
            ParallaxEnabled = settings?.ParallaxEnabled ?? false,
        };
    }

    internal static string? NormaliseSource(string? source) =>
        string.IsNullOrWhiteSpace(source) ? null : source.Trim();
}

public static class AppearanceSettingsValidator
{
    public static AppearanceSettings Normalize(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        EnsureDefined(settings.ThemeMode, nameof(settings.ThemeMode));
        EnsureDefined(settings.AccentMode, nameof(settings.AccentMode));
        EnsureDefined(settings.FixedAccent, nameof(settings.FixedAccent));
        EnsureDefined(settings.Motion, nameof(settings.Motion));
        EnsureDefined(settings.Density, nameof(settings.Density));
        EnsureDefined(settings.LauncherCapsuleStyle, nameof(settings.LauncherCapsuleStyle));

        var background = settings.Background ??
            throw new ArgumentException("Background settings are required.", nameof(settings));
        EnsureDefined(
            background.WallpaperEnginePresentation,
            nameof(background.WallpaperEnginePresentation));
        if (background.CarouselSources is null)
        {
            throw new ArgumentException("Carousel sources are required.", nameof(settings));
        }

        if (background.CarouselIntervalMinutes is
            < BackgroundArtSettings.MinimumCarouselIntervalMinutes or
            > BackgroundArtSettings.MaximumCarouselIntervalMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                $"Carousel interval must be between " +
                $"{BackgroundArtSettings.MinimumCarouselIntervalMinutes} and " +
                $"{BackgroundArtSettings.MaximumCarouselIntervalMinutes} minutes.");
        }

        if (!double.IsFinite(background.GlassIntensity) ||
            background.GlassIntensity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Glass intensity must be between 0 and 1.");
        }

        if (!double.IsFinite(background.MotionZoom) ||
            background.MotionZoom is < 1 or > 2.8)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "Motion zoom must be between 1.0 and 2.8.");
        }

        var normalizedSources = background.CarouselSources
            .Select(AppearanceProjector.NormaliseSource)
            .Where(source => source is not null)
            .Select(source => source!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedMotionSource = AppearanceProjector.NormaliseSource(background.MotionSource);
        var normalizedMotionEnabled = background.MotionEnabled && normalizedMotionSource is not null;
        var normalizedPackageSource = AppearanceProjector.NormaliseSource(
            background.WallpaperEnginePackageSource);
        var normalizedPackagePresentation = normalizedPackageSource is null
            ? WallpaperEnginePresentation.None
            : background.WallpaperEnginePresentation;
        var normalizedBackground = background with
        {
            SelectedSource = AppearanceProjector.NormaliseSource(background.SelectedSource),
            CarouselSources = normalizedSources,
            CarouselEnabled = background.CarouselEnabled && !normalizedMotionEnabled,
            MotionEnabled = normalizedMotionEnabled,
            MotionSource = normalizedMotionSource,
            WallpaperEnginePresentation = normalizedPackagePresentation,
            WallpaperEnginePackageSource = normalizedPackageSource,
        };

        return settings with
        {
            Background = normalizedBackground,
            LauncherMastheadLabel = NormalizeLauncherMastheadLabel(
                settings.LauncherMastheadLabel),
            LauncherMastheadTitle = NormalizeMastheadText(
                settings.LauncherMastheadTitle,
                AppearanceSettings.DefaultLauncherMastheadTitle),
            LauncherMastheadSubtitle = NormalizeOptionalMastheadText(
                settings.LauncherMastheadSubtitle),
            CustomAccentArgb = settings.CustomAccentArgb is uint custom
                ? custom | 0xFF000000
                : null,
        };
    }

    private static string NormalizeMastheadText(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= 120 ? normalized : normalized[..120];
    }

    private static string NormalizeLauncherMastheadLabel(string? value)
    {
        var normalized = NormalizeMastheadText(
            value,
            AppearanceSettings.DefaultLauncherMastheadLabel);
        return string.Equals(
            normalized,
            AppearanceSettings.LegacyLauncherMastheadLabel,
            StringComparison.Ordinal)
                ? AppearanceSettings.DefaultLauncherMastheadLabel
                : normalized;
    }

    private static string NormalizeOptionalMastheadText(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= 120 ? normalized : normalized[..120];
    }

    private static void EnsureDefined<TEnum>(TEnum value, string propertyName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                propertyName,
                value,
                "The appearance setting is not recognized.");
        }
    }
}

public static class AppearanceSettingsMigration
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false),
        },
    };

    private static readonly JsonTypeInfo<AppearanceSettings> AppearanceJsonTypeInfo =
        new NikkiwardJsonContext(SerializerOptions).AppearanceSettings;

    public static JsonObject MigrateSchema3To4(JsonObject schema3Settings)
    {
        ArgumentNullException.ThrowIfNull(schema3Settings);

        if (!schema3Settings.TryGetPropertyValue("schemaVersion", out var versionNode) ||
            versionNode is not JsonValue versionValue ||
            !versionValue.TryGetValue<int>(out var version) ||
            version != UserSettings.LegacySchemaVersion)
        {
            throw new JsonException(
                $"Appearance migration requires settings schema " +
                $"{UserSettings.LegacySchemaVersion}.");
        }

        var migrated = (JsonObject)schema3Settings.DeepClone();
        var defaults = JsonSerializer.SerializeToNode(
            new AppearanceSettings(),
            AppearanceJsonTypeInfo)?.AsObject() ??
            throw new JsonException("Appearance defaults could not be materialized.");

        if (migrated.TryGetPropertyValue("themeMode", out var themeNode) &&
            themeNode is not null)
        {
            defaults["themeMode"] = themeNode.DeepClone();
        }

        if (migrated.TryGetPropertyValue("appearance", out var appearanceNode) &&
            appearanceNode is not null)
        {
            if (appearanceNode is not JsonObject existingAppearance)
            {
                throw new JsonException(
                    "The schema 3 appearance extension must be a JSON object.");
            }

            MergeExistingValues(defaults, existingAppearance);
        }

        migrated.Remove("themeMode");
        migrated["appearance"] = defaults;
        migrated["schemaVersion"] = UserSettings.MotionBackgroundSchemaVersion;
        return migrated;
    }

    public static JsonObject MigrateSchema4To5(JsonObject schema4Settings)
    {
        ArgumentNullException.ThrowIfNull(schema4Settings);

        if (!schema4Settings.TryGetPropertyValue("schemaVersion", out var versionNode) ||
            versionNode is not JsonValue versionValue ||
            !versionValue.TryGetValue<int>(out var version) ||
            version != UserSettings.MotionBackgroundSchemaVersion)
        {
            throw new JsonException(
                $"Motion background migration requires settings schema " +
                $"{UserSettings.MotionBackgroundSchemaVersion}.");
        }

        var migrated = (JsonObject)schema4Settings.DeepClone();
        if (migrated["appearance"] is not JsonObject appearance ||
            appearance["background"] is not JsonObject background)
        {
            throw new JsonException(
                "Schema 4 settings require appearance.background before migration.");
        }

        background.TryAdd("motionEnabled", false);
        background.TryAdd("motionFpsCap", 30);
        background.TryAdd("useLiveBlur", false);
        background.TryAdd("glassIntensity", 1.0);
        background.TryAdd("motionPanEnabled", false);
        background.TryAdd("motionZoom", 1.0);
        migrated["schemaVersion"] = UserSettings.PreviousSchemaVersion;
        return migrated;
    }

    public static JsonObject MigrateSchema5To6(JsonObject schema5Settings)
    {
        ArgumentNullException.ThrowIfNull(schema5Settings);

        if (!schema5Settings.TryGetPropertyValue("schemaVersion", out var versionNode) ||
            versionNode is not JsonValue versionValue ||
            !versionValue.TryGetValue<int>(out var version) ||
            version != UserSettings.PreviousSchemaVersion)
        {
            throw new JsonException(
                $"Holographic card migration requires settings schema " +
                $"{UserSettings.PreviousSchemaVersion}.");
        }

        var migrated = (JsonObject)schema5Settings.DeepClone();
        if (migrated["appearance"] is not JsonObject appearance ||
            appearance["background"] is not JsonObject background)
        {
            throw new JsonException(
                "Schema 5 settings require appearance.background before migration.");
        }

        background.TryAdd("holographicCardEnabled", true);
        migrated["schemaVersion"] = UserSettings.HolographicCardSchemaVersion;
        return migrated;
    }

    private static void MergeExistingValues(JsonObject target, JsonObject existing)
    {
        foreach (var property in existing)
        {
            if (property.Value is JsonObject existingObject &&
                target[property.Key] is JsonObject targetObject)
            {
                MergeExistingValues(targetObject, existingObject);
                continue;
            }

            target[property.Key] = property.Value?.DeepClone();
        }
    }
}
