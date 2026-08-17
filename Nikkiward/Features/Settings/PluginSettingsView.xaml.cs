using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nikkiward.Features.Settings;

public sealed partial class PluginSettingsView : UserControl
{
    public event EventHandler? ImportRequested;

    public event EventHandler? OpenRequested;

    public event EventHandler? UninstallRequested;

    public PluginSettingsView()
    {
        InitializeComponent();
    }

    public void ApplyState(
        string statusText,
        bool canImport,
        bool canOpen,
        bool canUninstall)
    {
        PhotoPluginSettingsStatusText.Text = statusText;
        PhotoPluginImportButton.IsEnabled = canImport;
        PhotoPluginOpenButton.IsEnabled = canOpen;
        PhotoPluginUninstallButton.IsEnabled = canUninstall;
    }

    private void OnImportClicked(object sender, RoutedEventArgs e) =>
        ImportRequested?.Invoke(this, EventArgs.Empty);

    private void OnOpenClicked(object sender, RoutedEventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnUninstallClicked(object sender, RoutedEventArgs e) =>
        UninstallRequested?.Invoke(this, EventArgs.Empty);
}
