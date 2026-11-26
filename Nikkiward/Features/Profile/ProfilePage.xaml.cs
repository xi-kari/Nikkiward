using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using Nikkiward.Features.Shell;

namespace Nikkiward.Features.Profile;

public sealed partial class ProfilePage : PageBase
{
    private ProfileDetailsView? _detailsView;

    public override string PageTitle => "Profile 与渠道";

    public event EventHandler? DiscoverRequested;

    public event EventHandler<ProfileSelectedEventArgs>? ProfileSelected;

    public event EventHandler? ChooseGameRootRequested;

    public event EventHandler? ChooseLauncherRootRequested;

    public event EventHandler? ChooseChannelStoreRootRequested;

    public event EventHandler? PlanChannelStoreRequested;

    public event EventHandler? BuildChannelStoreRequested;

    public event EventHandler? ActivateChannelRequested;

    public event EventHandler? RollbackActivationRequested;

    public ProfilePage()
    {
        InitializeComponent();
    }

    protected override void OnEntering(NavigationEventArgs e)
    {
        if (e.Parameter is not ProfileNavigationContext context)
        {
            throw new InvalidOperationException("Profile navigation context is required.");
        }

        if (_detailsView is null)
        {
            _detailsView = new ProfileDetailsView(context.ViewModel);
            _detailsView.DiscoverRequested += OnDiscoverRequested;
            _detailsView.ProfileSelected += OnProfileSelected;
            _detailsView.ChooseGameRootRequested += OnChooseGameRootRequested;
            _detailsView.ChooseLauncherRootRequested += OnChooseLauncherRootRequested;
            _detailsView.ChooseChannelStoreRootRequested += OnChooseChannelStoreRootRequested;
            _detailsView.PlanChannelStoreRequested += OnPlanChannelStoreRequested;
            _detailsView.BuildChannelStoreRequested += OnBuildChannelStoreRequested;
            _detailsView.ActivateChannelRequested += OnActivateChannelRequested;
            _detailsView.RollbackActivationRequested += OnRollbackActivationRequested;
            DetailsScrollViewer.Content = _detailsView;
        }

        base.OnEntering(e);
    }

    private void OnDiscoverRequested(object? sender, EventArgs e) =>
        DiscoverRequested?.Invoke(this, EventArgs.Empty);

    private void OnProfileSelected(object? sender, ProfileSelectedEventArgs e) =>
        ProfileSelected?.Invoke(this, e);

    public void ShowDetails()
    {
        DetailsScrollViewer.ChangeView(null, 0, null, true);
    }

    private void OnChooseGameRootRequested(object? sender, EventArgs e) =>
        ChooseGameRootRequested?.Invoke(this, EventArgs.Empty);

    private void OnChooseLauncherRootRequested(object? sender, EventArgs e) =>
        ChooseLauncherRootRequested?.Invoke(this, EventArgs.Empty);

    private void OnChooseChannelStoreRootRequested(object? sender, EventArgs e) =>
        ChooseChannelStoreRootRequested?.Invoke(this, EventArgs.Empty);

    private void OnPlanChannelStoreRequested(object? sender, EventArgs e) =>
        PlanChannelStoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnBuildChannelStoreRequested(object? sender, EventArgs e) =>
        BuildChannelStoreRequested?.Invoke(this, EventArgs.Empty);

    private void OnActivateChannelRequested(object? sender, EventArgs e) =>
        ActivateChannelRequested?.Invoke(this, EventArgs.Empty);

    private void OnRollbackActivationRequested(object? sender, EventArgs e) =>
        RollbackActivationRequested?.Invoke(this, EventArgs.Empty);
}

public sealed class ProfileSelectedEventArgs : EventArgs
{
    public ProfileSelectedEventArgs(string profileId)
    {
        ProfileId = profileId;
    }

    public string ProfileId { get; }
}
