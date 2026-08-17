using Nikkiward.ViewModels;

namespace Nikkiward.Features.Settings;

public enum SettingsDestination
{
    Overview,
    General,
    Gallery,
    Journal,
    Files,
    Plugins,
    Components,
    Hotkeys,
    Gamepad,
    Status,
    Diagnostics,
    Contract,
    About,
}

public sealed record SettingsStoragePaths(
    string JournalWebViewDataPath,
    string JournalSnapshotPath,
    string JournalAssetsPath);

public sealed record SettingsNavigationContext(
    MainPageViewModel ViewModel,
    SettingsDestination InitialDestination,
    SettingsStoragePaths StoragePaths,
    bool DeveloperModeEnabled);

public sealed class SettingsDestinationEventArgs : EventArgs
{
    public SettingsDestinationEventArgs(SettingsDestination destination)
    {
        Destination = destination;
    }

    public SettingsDestination Destination { get; }
}

public sealed class DeveloperModeChangedEventArgs : EventArgs
{
    public DeveloperModeChangedEventArgs(bool enabled)
    {
        Enabled = enabled;
    }

    public bool Enabled { get; }
}
