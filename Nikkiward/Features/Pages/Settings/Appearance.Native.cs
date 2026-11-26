using System.Text.Json;
using Nikkiward.Features.Pages;
using Nikkiward.Features.Launcher.Skins;

namespace Nikkiward;

public sealed partial class MainPage
{
    private bool _settingsWallpaperPickerOpen;

    private PageAdapter AppearancePageSection() => new("appearance", AppearanceSection.Actions,
        () => AppearanceSection.State(ViewModel.AppearanceSettings), ChangeWebAppearanceAsync);

    private async Task ChangeWebAppearanceAsync(string op, JsonElement m)
    {
        if (op == "choose")
        {
            if (_settingsWallpaperPickerOpen) return;
            _settingsWallpaperPickerOpen = true;
            try { await ChooseWallpaperAsync(null); }
            finally { _settingsWallpaperPickerOpen = false; }
            return;
        }
        if (op == "restore") { await SaveAndApplyAppearanceAsync(current => current with { Background = new() }, restoreDefaultBackground: true); return; }
        var field = SettingsValues.Field(m, "field");
        var value = SettingsValues.Field(m, "value");
        if (field == "skin" && !HomeSkins.Available.Any(s => s.Id == value)) throw SettingsValues.Invalid();
        await SaveAndApplyAppearanceAsync(current => AppearanceSection.Change(current, field, value), forceStaticBackgroundReload: field == "preset");
    }
}
