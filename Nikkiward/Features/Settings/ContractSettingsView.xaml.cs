using Microsoft.UI.Xaml.Controls;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Settings;

public sealed partial class ContractSettingsView : UserControl
{
    public MainPageViewModel ViewModel { get; }

    public ContractSettingsView(MainPageViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }
}
