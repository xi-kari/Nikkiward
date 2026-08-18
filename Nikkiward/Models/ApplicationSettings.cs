using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Nikkiward.Models;

public enum CloseWindowBehavior
{
    MinimizeToTray,
    Exit,
}

public sealed record GeneralSettings
{
    public string LanguageTag { get; init; } = "zh-CN";

    public string MainWindowHotkey { get; init; } = "Alt+S";

    public CloseWindowBehavior CloseWindowBehavior { get; init; } = CloseWindowBehavior.Exit;

    public bool EnableProfileQuickSwitcher { get; init; } = true;
}

public sealed record DownloadSettings
{
    public string? DefaultGameInstallPath { get; init; }

    public bool EnableHardLinks { get; init; } = true;

    public int SpeedLimitKbps { get; init; }
}

public sealed record FileManagementSettings
{
    public string? UserDataFolderPath { get; init; }

    public bool ClearLauncherBackgroundFiles { get; init; }

    public string? LastBackupPath { get; init; }

    public DateTimeOffset? LastBackupAtUtc { get; init; }
}

public enum ScreenshotImageFormat
{
    Png,
    Avif,
    JpegXl,
}

public enum ScreenshotImageQuality
{
    Medium,
    High,
    Lossless,
}

public sealed record ScreenshotSettings
{
    public string? FolderPath { get; init; }

    public string Hotkey { get; init; } = "Alt+D";

    public ScreenshotImageFormat Format { get; init; } = ScreenshotImageFormat.Png;

    public ScreenshotImageQuality Quality { get; init; } = ScreenshotImageQuality.High;

    public bool EnableColorManagement { get; init; } = true;

    public bool AutoCopyToClipboard { get; init; } = true;

    public bool AutoConvertHdrToSdr { get; init; } = true;
}

public static class ApplicationSettingsValidator
{
    public const string SystemLanguageTag = "system";

    public const string SimplifiedChineseLanguageTag = "zh-CN";

    public static GeneralSettings Normalize(GeneralSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureDefined(settings.CloseWindowBehavior, nameof(settings.CloseWindowBehavior));
        var languageTag = NormalizeLanguageTag(settings.LanguageTag);

        var mainWindowHotkey = string.IsNullOrWhiteSpace(settings.MainWindowHotkey)
            ? "Alt+S"
            : settings.MainWindowHotkey.Trim();
        if (mainWindowHotkey.Length > 64)
        {
            mainWindowHotkey = mainWindowHotkey[..64];
        }

        return settings with
        {
            LanguageTag = languageTag,
            MainWindowHotkey = mainWindowHotkey,
        };
    }

    public static DownloadSettings Normalize(DownloadSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SpeedLimitKbps < 0 || settings.SpeedLimitKbps > 10_000_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings.SpeedLimitKbps),
                settings.SpeedLimitKbps,
                "Download speed limit must be between 0 and 10,000,000 KB/s.");
        }

        return settings with
        {
            DefaultGameInstallPath = NormalizePath(settings.DefaultGameInstallPath),
        };
    }

    public static string NormalizeLanguageTag(string? languageTag) =>
        string.Equals(
            languageTag?.Trim(),
            SystemLanguageTag,
            StringComparison.OrdinalIgnoreCase)
            ? SystemLanguageTag
            : SimplifiedChineseLanguageTag;

    public static string? ResolveDefaultChannelStoreRoot(DownloadSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var installPath = NormalizePath(settings.DefaultGameInstallPath);
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        return string.Equals(
            Path.GetFileName(installPath),
            "NikkiwardStore",
            StringComparison.OrdinalIgnoreCase)
            ? installPath
            : Path.Combine(installPath, "NikkiwardStore");
    }

    public static FileManagementSettings Normalize(FileManagementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings with
        {
            UserDataFolderPath = NormalizePath(settings.UserDataFolderPath),
            LastBackupPath = NormalizePath(settings.LastBackupPath),
            LastBackupAtUtc = settings.LastBackupAtUtc?.ToUniversalTime(),
        };
    }

    public static ScreenshotSettings Normalize(ScreenshotSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureDefined(settings.Format, nameof(settings.Format));
        EnsureDefined(settings.Quality, nameof(settings.Quality));
        var hotkey = string.IsNullOrWhiteSpace(settings.Hotkey)
            ? "Alt+D"
            : settings.Hotkey.Trim();
        if (hotkey.Length > 64)
        {
            hotkey = hotkey[..64];
        }

        return settings with
        {
            FolderPath = NormalizePath(settings.FolderPath),
            Hotkey = hotkey,
        };
    }

    private static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Path.TrimEndingDirectorySeparator(value.Trim());
    }

    private static void EnsureDefined<TEnum>(TEnum value, string propertyName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(propertyName, value, "The setting is not recognized.");
        }
    }
}

public static class ApplicationSettingsMigration
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false),
        },
    };

    public static JsonObject MigrateSchema6To7(JsonObject schema6Settings)
    {
        ArgumentNullException.ThrowIfNull(schema6Settings);
        if (!schema6Settings.TryGetPropertyValue("schemaVersion", out var versionNode) ||
            versionNode is not JsonValue versionValue ||
            !versionValue.TryGetValue<int>(out var version) ||
            version != UserSettings.HolographicCardSchemaVersion)
        {
            throw new JsonException(
                $"Application settings migration requires schema {UserSettings.HolographicCardSchemaVersion}.");
        }

        var migrated = (JsonObject)schema6Settings.DeepClone();
        migrated.TryAdd("general", SerializeDefaults(new GeneralSettings()));
        migrated.TryAdd("download", SerializeDefaults(new DownloadSettings()));
        migrated.TryAdd("fileManagement", SerializeDefaults(new FileManagementSettings()));
        migrated.TryAdd("screenshot", SerializeDefaults(new ScreenshotSettings()));
        migrated["schemaVersion"] = UserSettings.CurrentSchemaVersion;
        return migrated;
    }

    private static JsonObject SerializeDefaults<T>(T value) =>
        JsonSerializer.SerializeToNode(value, SerializerOptions)?.AsObject() ??
        throw new JsonException($"Defaults for {typeof(T).Name} could not be materialized.");
}
