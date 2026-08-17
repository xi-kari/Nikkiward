namespace Nikkiward.Models;

public sealed record LaunchSnapshot
{
    public string ProfileId { get; init; } = string.Empty;

    public LaunchState State { get; init; } = LaunchState.NotInstalled;

    public LaunchCapability Capability { get; init; } = LaunchCapability.NotVerified;

    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<ComponentVerification> Components { get; init; } =
        Array.Empty<ComponentVerification>();

    public string? LastFailureReason { get; init; }
}
