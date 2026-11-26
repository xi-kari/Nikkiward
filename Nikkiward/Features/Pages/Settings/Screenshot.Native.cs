using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.Pages;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter ScreenshotPageSection() => new("Screenshot", ScreenshotSection.Actions,
        () => ScreenshotSection.State(ViewModel.ScreenshotSettings, ResolveScreenshotFolderPath()), ChangeWebScreenshotAsync);

    private async Task ChangeWebScreenshotAsync(string op, JsonElement m)
    {
        var token = _lifetimeCancellation?.Token ?? CancellationToken.None;
        switch (op)
        {
            case "test":
                var result = await _gameScreenshotService.CaptureTestAsync(WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow),
                    ViewModel.ScreenshotSettings, ResolveScreenshotFolderPath(), () => ViewModel.GalleryReadOnlyRoots, token);
                if (!result.Succeeded) throw new IOException(result.Message);
                await CopyScreenshotToClipboardAsync(result);
                await TryShowDialogAsync("测试截图完成", result.Message);
                return;
            case "clear": await ClearWebThumbnailsAsync(token); return;
            case "open" when Directory.Exists(ResolveScreenshotFolderPath()):
                var target = await StorageFolder.GetFolderFromPathAsync(ResolveScreenshotFolderPath());
                if (!await Launcher.LaunchFolderAsync(target)) throw new IOException("未能打开截图文件夹。");
                return;
            case "change" or "choose": break;
            default: throw SettingsValues.Invalid();
        }
        var folder = op == "choose" ? await PickFolderAsync("选择截图文件夹", PickerLocationId.PicturesLibrary) : null;
        if (op == "choose" && folder == null) return;
        await _hotkeySaveGate.WaitAsync(token);
        try
        {
            var current = ViewModel.ScreenshotSettings;
            var next = folder != null ? current with { FolderPath = folder }
                : ScreenshotSection.Change(current, SettingsValues.Field(m, "field"), SettingsValues.Field(m, "value"));
            await ViewModel.SaveScreenshotSettingsAsync(next, token);
        }
        finally { _hotkeySaveGate.Release(); }
    }

    private async Task ClearWebThumbnailsAsync(CancellationToken token)
    {
        if (await ConfirmWebSettingsAsync("清除缩略图缓存？", "仅删除缓存的缩略图。下次浏览相册时会重新生成，原图会保留。", "清除"))
        {
            await ClearGalleryThumbnailsAsync(token);
        }
    }

    private async Task<bool> ConfirmWebSettingsAsync(string title, string message, string button)
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = message,
            PrimaryButtonText = button, CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
