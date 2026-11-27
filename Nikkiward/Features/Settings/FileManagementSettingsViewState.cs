using Nikkiward.Services;

namespace Nikkiward.Features.Settings;

public sealed class FileManagementSettingsViewState
{
    public required string DataFolderPath { get; init; }

    public required SettingsCacheStatistics CacheStatistics { get; init; }

    public string? LastBackupPath { get; init; }

    public DateTimeOffset? LastBackupAtUtc { get; init; }

    public bool ClearLauncherBackgroundFiles { get; init; }

    public bool IsBusy { get; init; }

    public string? ProtectionMessage { get; init; }

}
