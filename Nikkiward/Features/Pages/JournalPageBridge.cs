using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.ViewModels;
using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter JournalPageAdapter() => new("journal", JournalPageData.Actions,
        () => JournalPageData.State(_journalSnapshot, ViewModel.GeneralSettings.RecordLocalPlayTime,
            _playTime?.Sessions, _playTimeError, Planner, DateTimeOffset.Now, TimeZoneInfo.Local),
        HandleJournalPageActionAsync);

    private void RefreshWebJournal(object? sender, EventArgs args)
    {
        if (CurrentWebPage is { CurrentPage: "journal" } view) view.Refresh();
    }

    private async Task HandleJournalPageActionAsync(string action, JsonElement message)
    {
        switch (action)
        {
            case "official":
                await OpenJournalAsync();
                break;
            case "time-settings":
                ShowSettingsPage("general");
                break;
            case "preview-image":
                await ShowJournalImageAsync(message);
                break;
            default:
                var command = JournalPageData.ReadCommand(action, message);
                try { await PerformPlannerAction(command); }
                catch (InvalidOperationException ex) { throw new InvalidDataException(ex.Message, ex); }
                break;
        }
    }
    private async Task ShowJournalImageAsync(JsonElement message)
    {
        if (!message.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) return;
        var name = id.GetString();
        var image = _journalSnapshot?.Resources.FirstOrDefault(r =>
            r.LocalFilePath != null && Path.GetFileName(r.LocalFilePath) == name);
        if (image == null || !File.Exists(image.LocalFilePath)) throw new InvalidDataException("这张图片已不在本地缓存中。");
        if (!_dialogGate.Wait(0)) return;
        try
        {
            var dialog = new ContentDialog {
                XamlRoot = XamlRoot, Title = image.AltText ?? "快照图片", CloseButtonText = "关闭",
                Content = new Image { Source = PreviewImageSource.Create(image.PreviewUri),
                    MaxHeight = XamlRoot.Size.Height * 0.65 }
            };
            await NativeSkin.ShowAsync(dialog);
        }
        finally { _dialogGate.Release(); }
    }
}
