using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter LocalFilesPageSection() => StorageSettingsAdapter("Files");
}
