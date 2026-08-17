using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Profile;

public sealed partial class ProfileDetailsView : UserControl
{
    public MainPageViewModel ViewModel { get; }

    public event EventHandler? DiscoverRequested;

    public event EventHandler? ChooseGameRootRequested;

    public event EventHandler? ChooseLauncherRootRequested;

    public event EventHandler? ChooseChannelStoreRootRequested;

    public event EventHandler? PlanChannelStoreRequested;

    public event EventHandler? BuildChannelStoreRequested;

    public event EventHandler? ActivateChannelRequested;

    public event EventHandler? RollbackActivationRequested;

    public event EventHandler<ProfileSelectedEventArgs>? ProfileSelected;

    public ProfileDetailsView(MainPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private void OnDiscoverClicked(object sender, RoutedEventArgs e) =>
        DiscoverRequested?.Invoke(this, EventArgs.Empty);

    private void OnChooseGameRootClicked(object sender, RoutedEventArgs e) =>
        ChooseGameRootRequested?.Invoke(this, EventArgs.Empty);

    private void OnChooseLauncherRootClicked(object sender, RoutedEventArgs e) =>
        ChooseLauncherRootRequested?.Invoke(this, EventArgs.Empty);

    private void OnChooseChannelStoreRootClicked(object sender, RoutedEventArgs e) =>
        ChooseChannelStoreRootRequested?.Invoke(this, EventArgs.Empty);

    private void OnPlanChannelStoreClicked(object sender, RoutedEventArgs e) =>
        PlanChannelStoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnBuildChannelStoreClicked(object sender, RoutedEventArgs e) =>
        BuildChannelStoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnActivateChannelClicked(object sender, RoutedEventArgs e) =>
        ActivateChannelRequested?.Invoke(this, EventArgs.Empty);

    private void OnRollbackActivationClicked(object sender, RoutedEventArgs e) =>
        RollbackActivationRequested?.Invoke(this, EventArgs.Empty);

    private void OnProfileCandidateClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string profileId })
        {
            ProfileSelected?.Invoke(this, new ProfileSelectedEventArgs(profileId));
        }
    }
}
