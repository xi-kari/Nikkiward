using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter AdvancedPageSection() => new("Advanced", ["change", "profile"], () => new {
        developer = ViewModel.DeveloperModeEnabled, profiles = DeveloperSettingsState.ChannelTools(ViewModel),
        contract = DeveloperSettingsState.Contract(ViewModel)
    }, async (op, message) => {
        if (op == "profile" && ViewModel.DeveloperModeEnabled) { await RunChannelToolAsync(message); return; }
        if (op != "change" || SettingsValues.Field(message, "field") != "developer") throw SettingsValues.Invalid();
        await ViewModel.SaveDeveloperModeAsync(SettingsValues.Toggle(SettingsValues.Field(message, "value")),
            _lifetimeCancellation?.Token ?? CancellationToken.None);
    });

    // Each tool calls the handler the profile details page used before the redesign, unchanged.
    private async Task RunChannelToolAsync(System.Text.Json.JsonElement message)
    {
        switch (SettingsValues.Field(message, "tool"))
        {
            case "discover" when ViewModel.CanDiscover: await DiscoverProfilesAsync(); break;
            case "game" when ViewModel.CanDiscover: OnChooseGameRootClicked(this, new Microsoft.UI.Xaml.RoutedEventArgs()); break;
            case "launcher" when ViewModel.CanDiscover: OnChooseLauncherRootClicked(this, new Microsoft.UI.Xaml.RoutedEventArgs()); break;
            case "select":
                var id = SettingsValues.Field(message, "id");
                if (!ViewModel.Profiles.Any(p => p.ProfileId == id)) throw SettingsValues.Invalid();
                await SelectProfileAsync(id);
                break;
            case "root" when ViewModel.CanPlanChannelStore: OnProfileChooseChannelStoreRootRequested(this, EventArgs.Empty); break;
            case "plan" when ViewModel.CanPlanChannelStore: OnProfilePlanChannelStoreRequested(this, EventArgs.Empty); break;
            case "build" when ViewModel.CanBuildChannelStore: OnProfileBuildChannelStoreRequested(this, EventArgs.Empty); break;
            case "activate" when ViewModel.CanActivateSelectedChannel: OnProfileActivateChannelRequested(this, EventArgs.Empty); break;
            case "rollback" when ViewModel.CanRollbackChannelActivation: OnProfileRollbackActivationRequested(this, EventArgs.Empty); break;
            default: throw SettingsValues.Invalid();
        }
    }
}
