using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.Models;

namespace Nikkiward.Features.Settings;

public sealed class GeneralSettingsChangedEventArgs : EventArgs
{
    public GeneralSettingsChangedEventArgs(GeneralSettings settings)
    {
        Settings = settings;
    }

    public GeneralSettings Settings { get; }
}

public sealed partial class CommonSettingsView : UserControl
{
    private bool _loading;
    private GeneralSettings _settings = new();

    public event EventHandler<GeneralSettingsChangedEventArgs>? SettingsChanged;

    public event EventHandler? VisualEffectsRequested;

    public CommonSettingsView()
    {
        InitializeComponent();
    }

    public void ApplySettings(GeneralSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loading = true;
        try
        {
            _settings = settings;
            LanguageComboBox.SelectedItem = LanguageComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag as string,
                    settings.LanguageTag,
                    StringComparison.OrdinalIgnoreCase))
                ?? LanguageComboBox.Items.OfType<ComboBoxItem>().First();
            MinimizeToTrayRadio.IsChecked = settings.CloseWindowBehavior is CloseWindowBehavior.MinimizeToTray;
            ExitRadio.IsChecked = settings.CloseWindowBehavior is CloseWindowBehavior.Exit;
            ProfileSwitcherToggle.IsOn = settings.EnableProfileQuickSwitcher;
        }
        finally
        {
            _loading = false;
        }
    }

    private GeneralSettings ReadSettings()
    {
        var language = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "zh-CN";
        var closeBehavior = MinimizeToTrayRadio.IsChecked is true
            ? CloseWindowBehavior.MinimizeToTray
            : CloseWindowBehavior.Exit;
        return _settings with
        {
            LanguageTag = language,
            CloseWindowBehavior = closeBehavior,
            EnableProfileQuickSwitcher = ProfileSwitcherToggle.IsOn,
        };
    }

    private void RaiseChanged()
    {
        if (!_loading)
        {
            _settings = ReadSettings();
            SettingsChanged?.Invoke(this, new GeneralSettingsChangedEventArgs(_settings));
        }
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e) => RaiseChanged();

    private void OnCloseBehaviorChecked(object sender, RoutedEventArgs e) => RaiseChanged();

    private void OnProfileSwitcherToggled(object sender, RoutedEventArgs e) => RaiseChanged();

    private void OnVisualEffectsClicked(object sender, RoutedEventArgs e) =>
        VisualEffectsRequested?.Invoke(this, EventArgs.Empty);
}
