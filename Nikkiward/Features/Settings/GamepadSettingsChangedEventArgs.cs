using Nikkiward.Models;

namespace Nikkiward.Features.Settings;

public enum GamepadSettingsChangeKind
{
    Enabled,
    LongPress,
    GuideAction,
    ShareAction,
    GuideKeys,
    ShareKeys,
}

public sealed class GamepadSettingsChangedEventArgs : EventArgs
{
    public GamepadSettingsChangedEventArgs(
        GamepadSettings settings,
        GamepadSettingsChangeKind changeKind)
    {
        Settings = settings;
        ChangeKind = changeKind;
    }

    public GamepadSettings Settings { get; }

    public GamepadSettingsChangeKind ChangeKind { get; }
}
