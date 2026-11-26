using System.Text.Json;
using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter AboutPageSection() => new("about", AboutSection.Actions,
        () => _webAbout.Value.State(), ChangeWebAboutAsync);

    private async Task ChangeWebAboutAsync(string op, JsonElement m)
    {
        var about = _webAbout.Value;
        switch (op)
        {
            case "channel" when about.Checking: return;
            case "channel": about.SetChannel(SettingsValues.Field(m, "value")); break;
            case "check" when about.Checking || about.Version == null: return;
            case "check":
                var check = about.CheckAsync(_lifetimeCancellation?.Token ?? CancellationToken.None);
                CurrentWebPage?.Refresh();
                await check;
                break;
            case "release" when about.Release != null: await OpenSettingsLinkAsync(about.Release); break;
            case "link" when AboutSection.Links.TryGetValue(SettingsValues.Field(m, "value"), out var path):
                await OpenSettingsLinkAsync(new Uri("https://github.com/xi-kari/Nikkiward" + path)); break;
            default: throw SettingsValues.Invalid();
        }
    }
}
