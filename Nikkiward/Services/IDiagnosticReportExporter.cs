using Nikkiward.Features.Background;
using Nikkiward.Models;

namespace Nikkiward.Services;

public interface IDiagnosticReportExporter
{
    Task<DiagnosticReportExportResult> ExportAsync(
        LaunchProfile profile,
        LaunchSnapshot snapshot,
        string destinationDirectory,
        ArtBackdropDiagnosticState? backdropState = null,
        CancellationToken cancellationToken = default);
}

public sealed record DiagnosticReportExportResult
{
    public bool Succeeded { get; init; }

    public string? JsonFilePath { get; init; }

    public string? TextFilePath { get; init; }

    public string? Error { get; init; }
}
