using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter ComponentsPageSection() => DiagnosticSettingsAdapter("Components");

    private PageAdapter DiagnosticSettingsAdapter(string id) => new(id, ["export", "details"], () => DeveloperSettingsState.Diagnostics(ViewModel), (op, _) => op switch {
        "details" => ShowValidationDetailsAsync(),
        "export" when ViewModel.CanExport => ExportSettingsDiagnosticsAsync(),
        _ => throw SettingsValues.Invalid()
    });
}
