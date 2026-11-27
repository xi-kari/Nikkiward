using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nikkiward.Features.Pages;
using Nikkiward.Services;
using Windows.System;
using CoreVirtualKeyStates = Windows.UI.Core.CoreVirtualKeyStates;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter HotkeysPageSection() => new("Hotkeys", HotkeysSection.Actions,
        () => HotkeysSection.State(ViewModel.GeneralSettings, ViewModel.ScreenshotSettings),
        async (_, m) => {
            var field = SettingsValues.Field(m, "field");
            if (field is not ("main" or "screenshot")) throw SettingsValues.Invalid();
            var value = await RecordSettingsHotkeyAsync(field);
            if (value != null) await SaveWebHotkeyAsync(field, value);
        });

    private Action<string>? _captureRegisteredHotkey;

    internal bool CaptureRegisteredSettingsHotkey(bool screenshot)
    {
        if (_captureRegisteredHotkey is not { } capture || !App.MainWindow.IsForegroundActive) return false;
        capture(screenshot ? ViewModel.ScreenshotSettings.Hotkey : ViewModel.GeneralSettings.MainWindowHotkey);
        return true;
    }

    private async Task<string?> RecordSettingsHotkeyAsync(string field)
    {
        if (_captureRegisteredHotkey != null) return null;
        var dialog = new ContentDialog {
            XamlRoot = XamlRoot, Title = field == "main" ? "显示主窗口" : "游戏截图",
            Content = "请按下新的快捷键，按 Esc 取消。", CloseButtonText = "取消"
        };
        string? value = null;
        void Complete(string gesture) { value = gesture; dialog.Hide(); }
        static bool Down(VirtualKey key) =>
            InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        void KeyDown(object sender, KeyRoutedEventArgs e)
        {
            e.Handled = true;
            var key = (int)e.Key;
            if (key == 0x1B) { dialog.Hide(); return; }
            if (key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5)) return;
            var parts = new List<string>();
            if (Down(VirtualKey.Control)) parts.Add("Ctrl");
            if (Down(VirtualKey.Menu)) parts.Add("Alt");
            if (Down(VirtualKey.Shift)) parts.Add("Shift");
            if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) parts.Add("Win");
            parts.Add(key is >= 0x30 and <= 0x39 ? ((char)key).ToString() : e.Key.ToString());
            Complete(string.Join('+', parts));
        }
        void Deactivated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) dialog.Hide();
        }
        _captureRegisteredHotkey = Complete;
        dialog.PreviewKeyDown += KeyDown;
        App.MainWindow.Activated += Deactivated;
        try { await NativeSkin.ShowAsync(dialog); }
        finally
        {
            _captureRegisteredHotkey = null;
            dialog.PreviewKeyDown -= KeyDown;
            App.MainWindow.Activated -= Deactivated;
        }
        return value;
    }

    private async Task SaveWebHotkeyAsync(string field, string value)
    {
        var token = _lifetimeCancellation?.Token ?? CancellationToken.None;
        await _hotkeySaveGate.WaitAsync(token);
        try
        {
            var general = ViewModel.GeneralSettings;
            var screenshot = ViewModel.ScreenshotSettings;
            var next = HotkeysSection.Change(general, screenshot, field, value);
            var result = await HotkeySettingsChange.ApplyAsync(
                () => App.MainWindow.ApplyHotkeys(next.Main, next.Screenshot),
                () => ViewModel.SaveHotkeySettingsAsync(next.Main, next.Screenshot, token),
                () => App.MainWindow.ApplyHotkeys(general.MainWindowHotkey, screenshot.Hotkey));
            if (!result.Succeeded) throw new InvalidDataException(result.Message);
        }
        finally { _hotkeySaveGate.Release(); }
    }
}
