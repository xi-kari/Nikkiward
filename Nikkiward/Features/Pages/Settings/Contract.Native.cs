using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter ContractPageSection() => new("Contract", [], () => DeveloperSettingsState.Contract(ViewModel), (_, _) => throw SettingsValues.Invalid());
}
