using Nikkiward.Models;

namespace Nikkiward.Services;

public interface IInstallationInspector
{
    Task<IReadOnlyList<ComponentVerification>> InspectAsync(
        LaunchProfile profile,
        CancellationToken cancellationToken = default);

    Task<ComponentVerification> InspectComponentAsync(
        string componentId,
        string displayName,
        string filePath,
        CancellationToken cancellationToken = default);
}
