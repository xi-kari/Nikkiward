using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter PluginsPageSection() => new("Plugins", PluginsSection.Actions,
        () => PluginsSection.State(_photoPluginInstallation, _photoPluginOperationInProgress), async (op, _) => {
            if (!PluginsSection.Allowed(op, _photoPluginInstallation, _photoPluginOperationInProgress)) throw SettingsValues.Invalid();
            _photoPluginOperationInProgress = true;
            var token = _lifetimeCancellation?.Token ?? CancellationToken.None;
            try
            {
                CurrentWebPage?.Refresh();
                switch (op)
                {
                    case "import": await ImportPhotoPluginAsync(confirmReplacement: true); break;
                    case "open":
                        if (!await _pluginCatalog.LaunchAsync(PhotoAlbumPluginId, token))
                            throw new IOException("未能打开相册插件。");
                        break;
                    case "uninstall":
                        if (await ConfirmWebSettingsAsync("卸载相册插件？", "将删除启动器中安装的插件和随附文件，保留相册照片。", "卸载"))
                            await _pluginCatalog.UninstallAsync(PhotoAlbumPluginId, token);
                        break;
                }
            }
            finally
            {
                _photoPluginOperationInProgress = false;
                await RefreshPhotoPluginStateAsync(token);
            }
        });
}
