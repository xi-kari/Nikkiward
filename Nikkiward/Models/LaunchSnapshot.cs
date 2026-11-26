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

    internal static string? ResolveFailureReason(
        LaunchPreflightResult? preflight, LaunchState readiness, bool external, string? componentFailure)
    {
        if (preflight is { StaticIdentityPassed: false })
        {
            var reason = $"预检未通过：{preflight.FailureCode} — {preflight.FailureDetail}";
            return componentFailure is null ? reason : $"{reason} | {componentFailure}";
        }

        return readiness is LaunchState.Ready && !external &&
            preflight?.FailureCode is LaunchPreflightFailureCode.ExecutionGateClosed
            ? "静态身份检查完成，但 Nikkiward 当前执行能力仍未启用。"
            : componentFailure;
    }

    internal LaunchSnapshot WithPreflightFailure(LaunchPreflightResult failure) => this with
    {
        State = LaunchState.Failed,
        LastFailureReason = ResolveFailureReason(failure, LaunchState.Failed, false, null),
    };
}
