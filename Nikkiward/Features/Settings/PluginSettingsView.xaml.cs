using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nikkiward.Features.Settings;

public sealed partial class PluginSettingsView : UserControl
{
    private const double CompactLayoutThreshold = 620;

    public event EventHandler? ImportRequested;

    public event EventHandler? OpenRequested;

    public event EventHandler? UninstallRequested;

    public PluginSettingsView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        ApplyLayout(ActualWidth);
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

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyLayout(e.NewSize.Width);

    private void ApplyLayout(double width)
    {
        var useCompactLayout = width < CompactLayoutThreshold;
        Grid.SetRow(PluginActions, useCompactLayout ? 1 : 0);
        Grid.SetColumn(PluginActions, useCompactLayout ? 0 : 2);
        Grid.SetColumnSpan(PluginActions, useCompactLayout ? 3 : 1);
        PluginActions.Orientation = useCompactLayout
            ? Orientation.Vertical
            : Orientation.Horizontal;
        PluginActions.Margin = useCompactLayout
            ? new Thickness(0, 12, 0, 0)
            : new Thickness(0);

        var buttonAlignment = useCompactLayout
            ? HorizontalAlignment.Stretch
            : HorizontalAlignment.Left;
        PhotoPluginImportButton.HorizontalAlignment = buttonAlignment;
        PhotoPluginOpenButton.HorizontalAlignment = buttonAlignment;
        PhotoPluginUninstallButton.HorizontalAlignment = buttonAlignment;
    }
}
