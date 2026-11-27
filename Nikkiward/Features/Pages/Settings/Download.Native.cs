using System.Text.Json;
using Nikkiward.Features.Pages;
using Windows.Storage.Pickers;

namespace Nikkiward;

public sealed partial class MainPage
{
    private readonly SemaphoreSlim _webDownloadSave = new(1, 1);
    private bool _downloadPickerOpen;

    private PageAdapter DownloadPageSection() => new("Download", ["choose", "open", "clear", "change"],
        () => DownloadSection.State(ViewModel.DownloadSettings), ChangeWebDownloadAsync);

    private async Task ChangeWebDownloadAsync(string op, JsonElement m)
    {
        if (op == "open")
        {
            await OpenSettingsFolderAsync(ViewModel.DownloadSettings.DefaultGameInstallPath ?? throw SettingsValues.Invalid());
            return;
        }
        string? path = null;
        if (op == "choose")
        {
            if (_downloadPickerOpen) return;
            _downloadPickerOpen = true;
            try { path = await PickFolderAsync("选择默认游戏安装路径", PickerLocationId.ComputerFolder); }
            finally { _downloadPickerOpen = false; }
            if (path == null) return;
        }
        await _webDownloadSave.WaitAsync();
        try
        {
            var current = ViewModel.DownloadSettings;
            var next = op == "change"
                ? DownloadSection.Change(current, SettingsValues.Field(m, "field"), SettingsValues.Field(m, "value"))
                : current with { DefaultGameInstallPath = path };
            await ViewModel.SaveDownloadSettingsAsync(next, _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        finally { _webDownloadSave.Release(); }
    }
}
