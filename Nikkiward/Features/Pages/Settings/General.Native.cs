using System.Text.Json;
using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private readonly SemaphoreSlim _generalSectionSaveGate = new(1, 1);

    private PageAdapter GeneralPageSection() => new("general", GeneralSection.Actions,
        () => GeneralSection.State(ViewModel.GeneralSettings), ChangeWebGeneralAsync);

    private async Task ChangeWebGeneralAsync(string op, JsonElement m)
    {
        if (op == "clearTime")
        {
            if (_playTime?.Sessions.Count > 0) await ClearPlayTimeAsync();
            return;
        }
        if (op == "effects") { await OpenSettingsLinkAsync(new Uri("ms-settings:easeofaccess-visualeffects")); return; }
        await _generalSectionSaveGate.WaitAsync();
        try
        {
            var next = GeneralSection.Change(ViewModel.GeneralSettings, SettingsValues.Field(m, "field"), SettingsValues.Field(m, "value"));
            await SaveAndApplyGeneralAsync(next);
        }
        finally { _generalSectionSaveGate.Release(); }
    }
}
