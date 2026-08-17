namespace Nikkiward.ViewModels;

/// <summary>
/// Honest projection of the launch gate onto the primary action button.
/// See LAUNCH_CONTRACT.md §7: the button must never disguise a blocked gate as
/// launchable, and "contract drift after a game update" must be told apart from
/// "the launcher is broken" — otherwise users report a bug that is really a
/// patch day.
/// </summary>
public enum LaunchButtonState
{
    /// <summary>Preflight has not run yet.</summary>
    Checking,

    /// <summary>No install candidate was discovered.</summary>
    NotInstalled,

    /// <summary>An install exists but its channel/region has no frozen contract.</summary>
    ChannelUnsupported,

    /// <summary>
    /// Binary identity no longer matches the frozen contract — almost always
    /// because the game or launcher was updated. Recoverable by refreshing the
    /// contract (<c>--emit-contract</c>).
    /// </summary>
    ContractDrift,

    /// <summary>Static identity failed for a reason other than drift.</summary>
    Blocked,

    /// <summary>A submitted root or bootstrap remains and must be closed before retrying.</summary>
    CleanupRequired,

    /// <summary>
    /// The normal launchable state. The static gate is deliberately closed and
    /// the coordinator synthesises a transient plan on click.
    /// </summary>
    Ready,

    /// <summary>A launch attempt is in flight.</summary>
    Launching,

    /// <summary>The target process tree is already running.</summary>
    Running,
}
