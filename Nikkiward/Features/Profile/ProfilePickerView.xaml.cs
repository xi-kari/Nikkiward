using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Profile;

public sealed partial class ProfilePickerView : UserControl
{
    private bool _isProfileCollectionSubscribed;

    public MainPageViewModel ViewModel { get; }

    public ObservableCollection<ProfileServerOption> ServerOptions { get; } = [];

    public event EventHandler? DiscoverRequested;

    public event EventHandler<ProfileSelectedEventArgs>? ProfileSelected;

    public event EventHandler? DetailsRequested;

    public event EventHandler? CloseRequested;

    public ProfilePickerView(MainPageViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isProfileCollectionSubscribed)
        {
            ViewModel.Profiles.CollectionChanged += OnProfilesChanged;
            _isProfileCollectionSubscribed = true;
        }

        ProfileDetailsButton.Visibility = ViewModel.DeveloperModeEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        RebuildServerOptions();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isProfileCollectionSubscribed)
        {
            return;
        }

        ViewModel.Profiles.CollectionChanged -= OnProfilesChanged;
        _isProfileCollectionSubscribed = false;
    }

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildServerOptions();

    private void RebuildServerOptions()
    {
        var options = ViewModel.Profiles
            .Select(ProfileServerOption.Create)
            .GroupBy(option => option.ServerName, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group
                .OrderByDescending(option => option.IsSelected)
                .ThenBy(option => option.ProfileId, StringComparer.Ordinal)
                .First())
            .OrderBy(option => option.SortOrder)
            .ThenBy(option => option.ServerName, StringComparer.CurrentCulture)
            .ToArray();

        ServerOptions.Clear();
        foreach (var option in options)
        {
            ServerOptions.Add(option);
        }

        var hasProfiles = ServerOptions.Count > 0;
        ServerOptionsList.Visibility = hasProfiles ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasProfiles ? Visibility.Collapsed : Visibility.Visible;
        ServerCountText.Text = $"{ServerOptions.Count} 个服务器";
    }

    private void OnDiscoverClicked(object sender, RoutedEventArgs e) =>
        DiscoverRequested?.Invoke(this, EventArgs.Empty);

    private void OnDetailsClicked(object sender, RoutedEventArgs e) =>
        DetailsRequested?.Invoke(this, EventArgs.Empty);

    private void OnBackdropTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPanelTapped(object sender, TappedRoutedEventArgs e) =>
        e.Handled = true;

    private void OnProfileClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string profileId })
        {
            ProfileSelected?.Invoke(this, new ProfileSelectedEventArgs(profileId));
        }
    }
}

public sealed class ProfileServerOption
{
    private ProfileServerOption(
        string profileId,
        string serverName,
        int sortOrder,
        bool isSelected)
    {
        ProfileId = profileId;
        ServerName = serverName;
        SortOrder = sortOrder;
        IsSelected = isSelected;
        SelectionVisibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        AutomationName = isSelected
            ? $"{serverName}，当前服务器"
            : $"切换到{serverName}";
    }

    public string ProfileId { get; }

    public string ServerName { get; }

    public int SortOrder { get; }

    public bool IsSelected { get; }

    public Visibility SelectionVisibility { get; }

    public string AutomationName { get; }

    public static ProfileServerOption Create(LaunchProfileItemViewModel profile)
    {
        var identity = $"{profile.Channel} {profile.DisplayName}";
        if (identity.Contains("Steam", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("国际服", StringComparison.Ordinal))
        {
            return new ProfileServerOption(
                profile.ProfileId,
                "国际服",
                1,
                profile.IsSelected);
        }

        if (identity.Contains("Bilibili", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("B服", StringComparison.Ordinal))
        {
            return new ProfileServerOption(
                profile.ProfileId,
                "哔哩哔哩",
                2,
                profile.IsSelected);
        }

        if (identity.Contains("CN", StringComparison.OrdinalIgnoreCase) ||
            identity.Contains("国服", StringComparison.Ordinal) ||
            identity.Contains("Official", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileServerOption(
                profile.ProfileId,
                "中国服",
                0,
                profile.IsSelected);
        }

        return new ProfileServerOption(
            profile.ProfileId,
            profile.DisplayName,
            3,
            profile.IsSelected);
    }
}
