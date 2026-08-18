using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Nikkiward.Models;
using Nikkiward.Serialization;

namespace Nikkiward.Services;

public sealed class JsonUserSettingsStore : IUserSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false),
        },
    };

    private static readonly JsonTypeInfo<UserSettings> UserSettingsJsonTypeInfo =
        new NikkiwardJsonContext(SerializerOptions).UserSettings;

    public JsonUserSettingsStore(string? localApplicationDataPath = null)
    {
        var localDataRoot = localApplicationDataPath;
        if (string.IsNullOrWhiteSpace(localDataRoot))
        {
            localDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(localDataRoot))
        {
            throw new InvalidOperationException("The LocalApplicationData directory is unavailable.");
        }

        SettingsFilePath = Path.Combine(Path.GetFullPath(localDataRoot), "Nikkiward", "settings.json");
        MigrationRollbackFilePath = Path.Combine(
            Path.GetDirectoryName(SettingsFilePath)!,
            "settings.pre-schema6.rollback.json");
    }

    public string SettingsFilePath { get; }

    public string MigrationRollbackFilePath { get; }

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = File.GetAttributes(SettingsFilePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UserSettingsStoreException(
                $"Could not inspect Nikkiward settings at '{SettingsFilePath}'.",
                ex);
        }

        try
        {
            JsonNode? document;
            await using (var stream = new FileStream(
                SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                document = await JsonNode.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if (document is not JsonObject root)
            {
                throw new JsonException("The settings document must be a JSON object.");
            }

            var version = ReadSchemaVersion(root);
            var migrated = false;
            if (version == UserSettings.LegacySchemaVersion)
            {
                root = AppearanceSettingsMigration.MigrateSchema3To4(root);
                migrated = true;
                version = UserSettings.MotionBackgroundSchemaVersion;
            }

            if (version == UserSettings.MotionBackgroundSchemaVersion)
            {
                root = AppearanceSettingsMigration.MigrateSchema4To5(root);
                migrated = true;
                version = UserSettings.PreviousSchemaVersion;
            }

            if (version == UserSettings.PreviousSchemaVersion)
            {
                await PreserveMigrationRollbackAsync(cancellationToken).ConfigureAwait(false);
                root = AppearanceSettingsMigration.MigrateSchema5To6(root);
                migrated = true;
                version = UserSettings.CurrentSchemaVersion;
            }

            if (version != UserSettings.CurrentSchemaVersion)
            {
                throw new JsonException(
                    $"Unsupported settings schema version {version}; " +
                    $"expected {UserSettings.CurrentSchemaVersion}.");
            }

            EnsureChannelStoreSection(root);
            RequireSettingsSections(root);

            var settings = root.Deserialize(UserSettingsJsonTypeInfo);

            if (settings is null)
            {
                throw new JsonException("The settings document is empty.");
            }

            var normalized = UserSettingsValidator.Normalize(settings);
            if (migrated)
            {
                await SaveAsync(normalized, cancellationToken).ConfigureAwait(false);
            }

            return normalized;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is
            IOException or
            UnauthorizedAccessException or
            JsonException or
            ArgumentException)
        {
            throw new UserSettingsStoreException(
                $"Could not read Nikkiward settings from '{SettingsFilePath}'.",
                ex);
        }
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = UserSettingsValidator.Normalize(settings);

        var directoryPath = Path.GetDirectoryName(SettingsFilePath)
            ?? throw new InvalidOperationException("The settings directory cannot be resolved.");
        var temporaryPath = $"{SettingsFilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(directoryPath);

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    normalized,
                    UserSettingsJsonTypeInfo,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, SettingsFilePath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new UserSettingsStoreException(
                $"Could not write Nikkiward settings to '{SettingsFilePath}'.",
                ex);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void TryDeleteTemporaryFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task PreserveMigrationRollbackAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(MigrationRollbackFilePath))
        {
            return;
        }

        var temporaryPath = $"{MigrationRollbackFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var source = new FileStream(
                SettingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, MigrationRollbackFilePath, overwrite: false);
        }
        catch (IOException) when (File.Exists(MigrationRollbackFilePath))
        {
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static int ReadSchemaVersion(JsonObject root)
    {
        if (!root.TryGetPropertyValue("schemaVersion", out var versionNode) ||
            versionNode is not JsonValue versionValue ||
            !versionValue.TryGetValue<int>(out var version))
        {
            throw new JsonException("A numeric settings schemaVersion is required.");
        }

        return version;
    }

    private static void RequireSettingsSections(JsonObject root)
    {
        foreach (var section in new[]
        {
            "appearance",
            "profiles",
            "galleryProfiles",
            "gamepad",
        })
        {
            if (!root.TryGetPropertyValue(section, out var value) || value is null)
            {
                throw new JsonException(
                    $"The settings document is missing required section '{section}'.");
            }
        }
    }

    private static void EnsureChannelStoreSection(JsonObject root)
    {
        if (root.TryGetPropertyValue("channelStore", out var channelStore) &&
            channelStore is not null)
        {
            return;
        }

        root["channelStore"] = new JsonObject
        {
            ["storeRootPath"] = null,
            ["lastReceiptId"] = null,
            ["lastPlanSha256"] = null,
            ["lastCompletedAtUtc"] = null,
            ["profiles"] = new JsonArray(),
        };
    }

}

internal static class UserSettingsValidator
{
    public static UserSettings Normalize(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.SchemaVersion != UserSettings.CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"Settings schema version must be {UserSettings.CurrentSchemaVersion}.",
                nameof(settings));
        }

        if (settings.Appearance is null)
        {
            throw new ArgumentException("Appearance settings are required.", nameof(settings));
        }

        if (settings.Profiles is null ||
            settings.GalleryProfiles is null ||
            settings.ChannelStore is null ||
            settings.ChannelStore.Profiles is null ||
            settings.Gamepad is null)
        {
            throw new ArgumentException("Settings sections cannot be null.", nameof(settings));
        }

        if (!Enum.IsDefined(settings.Gamepad.GuideAction) ||
            !Enum.IsDefined(settings.Gamepad.ShareAction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "The gamepad action is not recognized.");
        }

        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in settings.Profiles)
        {
            var profileId = profile is null ? null : NormalizeOptional(profile.ProfileId);
            if (profileId is null || !profileIds.Add(profileId))
            {
                throw new ArgumentException(
                    "Each launch profile must have a unique non-empty ProfileId.",
                    nameof(settings));
            }
        }

        var galleryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var gallery in settings.GalleryProfiles)
        {
            var profileId = gallery is null ? null : NormalizeOptional(gallery.ProfileId);
            if (gallery is null ||
                profileId is null ||
                string.IsNullOrWhiteSpace(gallery.RootPath) ||
                !galleryIds.Add(profileId))
            {
                throw new ArgumentException(
                    "Each gallery profile must have a unique non-empty ProfileId and RootPath.",
                    nameof(settings));
            }
        }

        var channelStoreIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in settings.ChannelStore.Profiles)
        {
            var profileId = profile is null ? null : NormalizeOptional(profile.ProfileId);
            var hasLauncherRoot = !string.IsNullOrWhiteSpace(profile?.LauncherRootPath);
            var hasXStarter = !string.IsNullOrWhiteSpace(profile?.XStarterPath);
            if (profile is null ||
                profileId is null ||
                !Enum.IsDefined(profile.DistributionChannel) ||
                profile.DistributionChannel is DistributionChannel.Unknown ||
                string.IsNullOrWhiteSpace(profile.GameRootPath) ||
                hasLauncherRoot != hasXStarter ||
                !channelStoreIds.Add(profileId))
            {
                throw new ArgumentException(
                    "Each channel store profile must have a unique ProfileId, channel, game root, and a complete optional runtime pair.",
                    nameof(settings));
            }
        }

        return settings with
        {
            SelectedProfileId = NormalizeOptional(settings.SelectedProfileId),
            Appearance = AppearanceSettingsValidator.Normalize(settings.Appearance),
            Profiles = settings.Profiles
                .Select(item => item with { ProfileId = item.ProfileId.Trim() })
                .ToArray(),
            GalleryProfiles = settings.GalleryProfiles
                .Select(item => item with
                {
                    ProfileId = item.ProfileId.Trim(),
                    RootPath = item.RootPath.Trim(),
                })
                .ToArray(),
            ChannelStore = settings.ChannelStore with
            {
                StoreRootPath = NormalizeOptional(settings.ChannelStore.StoreRootPath),
                LastReceiptId = NormalizeOptional(settings.ChannelStore.LastReceiptId),
                LastPlanSha256 = NormalizeOptional(settings.ChannelStore.LastPlanSha256),
                Profiles = settings.ChannelStore.Profiles
                    .Select(item => item with
                    {
                        ProfileId = item.ProfileId.Trim(),
                        GameRootPath = item.GameRootPath.Trim(),
                        LauncherRootPath = NormalizeOptional(item.LauncherRootPath),
                        XStarterPath = NormalizeOptional(item.XStarterPath),
                    })
                    .ToArray(),
            },
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
