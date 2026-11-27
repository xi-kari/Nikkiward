using System.Text.Json;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private bool _webGallerySettingsBusy;
    private bool _webGallerySettingsLoaded;
    private string _webStudioExportRoot = "正在读取";

    private PageAdapter GalleryPageSection() => new("Gallery", GallerySection.Actions,
        () => GallerySection.State(CreateGallerySettingsState(), _webStudioExportRoot, _webGallerySettingsLoaded,
            _webGallerySettingsBusy, ViewModel.ProfileDisplayName), ChangeWebGallerySettingsAsync);

    private async Task ChangeWebGallerySettingsAsync(string op, JsonElement m)
    {
        if (_webGallerySettingsBusy) return;
        if ((!_webGallerySettingsLoaded && op != "refresh") || !GallerySection.Allowed(op, CreateGallerySettingsState()))
            throw SettingsValues.Invalid();
        _webGallerySettingsBusy = true;
        var token = _lifetimeCancellation?.Token ?? CancellationToken.None;
        try
        {
            CurrentWebPage?.Refresh();
            switch (op)
            {
                case "refresh": _webGallerySettingsLoaded = false; break;
                case "choose": await GalleryRootChooseAsync(); return;
                case "reset": await GalleryRootResetAsync(); return;
                case "open": await GalleryOpenAsync(); return;
                case "exports": await ChooseStudioExportsAsync(); break;
                case "change":
                    await SetGalleryProtectionAsync(GallerySection.ProtectionValue(SettingsValues.Field(m, "field"), SettingsValues.Field(m, "value")));
                    return;
                case "chooseProtection": await GalleryProtectionRootChooseAsync(); return;
                case "openProtection": await GalleryProtectionRootOpenAsync(); return;
                case "verify": await GalleryProtectionVerifyAsync(); return;
                case "clean": await GalleryProtectionCleanAsync(); return;
                case "clearCache": await ClearWebThumbnailsAsync(token); break;
                case "associate": await NikkiGalleryRegisterAsync(); return;
                case "openTool": await NikkiGalleryOpenAsync(); return;
                case "disconnect": await NikkiGalleryDisconnectAsync(); return;
                default: throw SettingsValues.Invalid();
            }
            _webStudioExportRoot = PhotoStudioView.Store.ExportRoot;
            await LoadGallerySettingsStateAsync(token);
            _webGallerySettingsLoaded = true;
        }
        finally { _webGallerySettingsBusy = false; }
    }
}
