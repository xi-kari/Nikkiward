using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using Nikkiward.Features.Shell;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Diagnostics;

public sealed partial class StatusPage : PageBase
{
    public MainPageViewModel ViewModel { get; private set; } = null!;

    public override string PageTitle => "能力与生命周期";

    public override FrameworkElement? MastheadInteractionRegion => CloseButton;

    public event EventHandler? CloseRequested;

    public event EventHandler? RefreshRequested;

    public event EventHandler? OfficialFlowRequested;

    public event EventHandler? ExportRequested;

    public StatusPage()
    {
        InitializeComponent();
        CloseButton.Loaded += OnMastheadChanged;
        CloseButton.SizeChanged += OnMastheadSizeChanged;
    }

    protected override void OnEntering(NavigationEventArgs e)
    {
        if (e.Parameter is not StatusNavigationContext context)
        {
            throw new InvalidOperationException("Status navigation context is required.");
        }

        ViewModel = context.ViewModel;
        Bindings.Update();
        ResetView();
        base.OnEntering(e);
    }

    public void ResetView()
    {
        TechnicalDetailsExpander.IsExpanded = false;
        StatusScrollViewer.ChangeView(null, 0, null, true);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnRefreshClicked(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void OnOfficialFlowClicked(object sender, RoutedEventArgs e) =>
        OfficialFlowRequested?.Invoke(this, EventArgs.Empty);

    private void OnExportClicked(object sender, RoutedEventArgs e) =>
        ExportRequested?.Invoke(this, EventArgs.Empty);

    private void OnMastheadChanged(object sender, RoutedEventArgs e) =>
        NotifyMastheadInteractionRegionChanged();

    private void OnMastheadSizeChanged(object sender, SizeChangedEventArgs e) =>
        NotifyMastheadInteractionRegionChanged();
}
