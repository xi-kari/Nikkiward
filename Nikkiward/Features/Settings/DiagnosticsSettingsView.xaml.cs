using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Settings;

public sealed partial class DiagnosticsSettingsView : UserControl
{
    public MainPageViewModel ViewModel { get; }

    public event EventHandler? ProviderDetailsRequested;

    public event EventHandler? ExportRequested;

    public DiagnosticsSettingsView(MainPageViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }

    private void OnProviderDetailsClicked(object sender, RoutedEventArgs e) =>
        ProviderDetailsRequested?.Invoke(this, EventArgs.Empty);

    private void OnExportClicked(object sender, RoutedEventArgs e) =>
        ExportRequested?.Invoke(this, EventArgs.Empty);
}
