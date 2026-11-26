using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter RuntimeStatusPageSection() => new("Status", ["open"], () => new { status = ViewModel.HomeCheckText },
        (_, _) => { ShowStatusDrawer(); return Task.CompletedTask; });
}
