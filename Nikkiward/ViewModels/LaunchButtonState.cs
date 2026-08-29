namespace Nikkiward.ViewModels;

/// <summary>
/// Honest projection of the launch gate onto the primary action button.
/// See LAUNCH_CONTRACT.md §7: the button must never disguise a blocked gate as
/// launchable, and a security refusal ("this binary cannot be attributed to
/// the official publisher") must be told apart from "the install is broken" —
/// otherwise users reinstall a game that was never the problem.
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
    /// The product marker no longer matches the selected channel (or a legacy
    /// drift code surfaced). Since the adaptive contract a routine game update
    /// no longer reaches this state — the signature gate absorbs it.
    /// </summary>
    ContractDrift,

    /// <summary>
    /// A component's Authenticode signature is invalid or its signer is not in
    /// the contract's trusted list. Deliberately fail-closed: Nikkiward never
    /// elevates a binary it cannot attribute to the official publishers.
    /// </summary>
    SignatureUntrusted,

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
