using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nikkiward.Features.Settings;

public sealed partial class SettingsHomeView : UserControl
{
    private bool _applyingDeveloperMode;

    public event EventHandler<string>? DestinationRequested;

    public event EventHandler<DeveloperModeChangedEventArgs>? DeveloperModeChanged;

    public SettingsHomeView()
    {
        InitializeComponent();
    }

    public void ApplyDeveloperMode(bool enabled)
    {
        _applyingDeveloperMode = true;
        DeveloperModeToggle.IsOn = enabled;
        DeveloperToolsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        _applyingDeveloperMode = false;
    }

    private void OnDestinationClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            DestinationRequested?.Invoke(this, tag);
        }
    }

    private void OnDeveloperModeToggled(object sender, RoutedEventArgs e)
    {
        if (_applyingDeveloperMode)
        {
            return;
        }

        DeveloperToolsPanel.Visibility = DeveloperModeToggle.IsOn
            ? Visibility.Visible
            : Visibility.Collapsed;
        DeveloperModeChanged?.Invoke(
            this,
            new DeveloperModeChangedEventArgs(DeveloperModeToggle.IsOn));
    }
}
