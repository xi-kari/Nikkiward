using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter AdvancedPageSection() => new("Advanced", ["change"], () => new {
        developer = ViewModel.DeveloperModeEnabled, contract = DeveloperSettingsState.Contract(ViewModel)
    }, async (_, message) => {
        if (SettingsValues.Field(message, "field") != "developer") throw SettingsValues.Invalid();
        await ViewModel.SaveDeveloperModeAsync(SettingsValues.Toggle(SettingsValues.Field(message, "value")),
            _lifetimeCancellation?.Token ?? CancellationToken.None);
        _hostedSettingsPage?.ApplyDeveloperMode(ViewModel.DeveloperModeEnabled);
    });
}
