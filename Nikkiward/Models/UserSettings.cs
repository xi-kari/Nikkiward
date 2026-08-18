namespace Nikkiward.Models;

public sealed record UserSettings
{
    public const int LegacySchemaVersion = 3;

    public const int MotionBackgroundSchemaVersion = 4;

    public const int PreviousSchemaVersion = 5;

    public const int HolographicCardSchemaVersion = 6;

    public const int CurrentSchemaVersion = 7;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string? SelectedProfileId { get; init; }

    public bool DeveloperModeEnabled { get; init; }

    public AppearanceSettings Appearance { get; init; } = new();

    public GeneralSettings General { get; init; } = new();

    public DownloadSettings Download { get; init; } = new();

    public FileManagementSettings FileManagement { get; init; } = new();

    public ScreenshotSettings Screenshot { get; init; } = new();

    public IReadOnlyList<LaunchProfile> Profiles { get; init; } = Array.Empty<LaunchProfile>();

    public IReadOnlyList<GalleryProfileSettings> GalleryProfiles { get; init; } =
        Array.Empty<GalleryProfileSettings>();

    public ChannelStoreSettings ChannelStore { get; init; } = new();

    public GamepadSettings Gamepad { get; init; } = new();
}

public sealed record ChannelStoreSettings
{
    public string? StoreRootPath { get; init; }

    public string? LastReceiptId { get; init; }

    public string? LastPlanSha256 { get; init; }

    public DateTimeOffset? LastCompletedAtUtc { get; init; }

    public IReadOnlyList<ChannelStoreProfileSettings> Profiles { get; init; } =
        Array.Empty<ChannelStoreProfileSettings>();
}

public sealed record ChannelStoreProfileSettings
{
    public required string ProfileId { get; init; }

    public required DistributionChannel DistributionChannel { get; init; }

    public required string GameRootPath { get; init; }

    public string? LauncherRootPath { get; init; }

    public string? XStarterPath { get; init; }
}

public sealed record GalleryProfileSettings
{
    public required string ProfileId { get; init; }

    public required string RootPath { get; init; }
}

public enum ThemeMode
{
    FollowArtwork,
    WarmLight,
    WarmDark,
}

public enum GamepadButtonAction
{
    None,
    MapKeys,
}

public sealed record GamepadSettings
{
    public bool Enabled { get; init; }

    /// <summary>
    /// When set, a Guide press is held for 600ms to tell a long press (open the
    /// main window) from a short press (<see cref="GuideAction"/>). Clear it to
    /// fire the mapped action immediately at the cost of losing the gesture.
    /// </summary>
    public bool GuideLongPressOpensMainWindow { get; init; } = true;

    public GamepadButtonAction GuideAction { get; init; }

    public string? GuideMapKeys { get; init; }

    public GamepadButtonAction ShareAction { get; init; }

    public string? ShareMapKeys { get; init; }
}
