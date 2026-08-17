namespace Nikkiward.Models;

public sealed record LaunchProfile
{
    public string ProfileId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Channel { get; init; } = string.Empty;

    public string GameRootPath { get; init; } = string.Empty;

    public string LauncherPath { get; init; } = string.Empty;

    public string XStarterPath { get; init; } = string.Empty;

    public string GameExecutablePath { get; init; } = string.Empty;

    public string ShippingExecutablePath { get; init; } = string.Empty;

    public string AntiCheatExecutablePath { get; init; } = string.Empty;

    public LaunchCapability Capability { get; init; } = LaunchCapability.NotVerified;
}
