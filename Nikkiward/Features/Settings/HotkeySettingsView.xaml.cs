using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.Models;

namespace Nikkiward.Features.Settings;

public sealed class HotkeySettingsChangedEventArgs : EventArgs
{
    public HotkeySettingsChangedEventArgs(string mainWindowHotkey, string screenshotHotkey)
    {
        MainWindowHotkey = mainWindowHotkey;
        ScreenshotHotkey = screenshotHotkey;
    }

    public string MainWindowHotkey { get; }

    public string ScreenshotHotkey { get; }
}

public sealed partial class HotkeySettingsView : UserControl
{
    private bool _loading;

    public event EventHandler<HotkeySettingsChangedEventArgs>? SettingsChanged;

    public HotkeySettingsView()
    {
        InitializeComponent();
    }

    public void ApplySettings(GeneralSettings general, ScreenshotSettings screenshot)
    {
        _loading = true;
        try
        {
            ShowMainWindowHotkeyTextBox.Text = general.MainWindowHotkey;
            ScreenshotHotkeyTextBox.Text = screenshot.Hotkey;
        }
        finally
        {
            _loading = false;
        }
    }

    public void ApplyRegistrationStatus(string message) =>
        RegistrationStatusText.Text = message;

    private void OnHotkeyLostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            SettingsChanged?.Invoke(
                this,
                new HotkeySettingsChangedEventArgs(
                    ShowMainWindowHotkeyTextBox.Text,
                    ScreenshotHotkeyTextBox.Text));
        }
    }
}
