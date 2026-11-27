using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nikkiward.Features.Launcher.Skins;

public sealed partial class CinemaView : UserControl
{
    private readonly LauncherPage _host;

    internal CinemaView(LauncherPage host)
    {
        _host = host;
        InitializeComponent();
    }

    private void OnCommand(object sender, RoutedEventArgs e) =>
        MainPage.FromElement(this).InvokeHomeCommand((string)((Button)sender).Tag, null);

    private void OnLaunch(object sender, RoutedEventArgs e) => _host.InvokeAction("launch");

    private void OnRetry(object sender, RoutedEventArgs e) => _host.RetryWebHome();

    private void OnProfilesClicked(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        foreach (var profile in _host.ViewModel.Profiles)
        {
            var item = new MenuFlyoutItem { Text = profile.DisplayName, IsEnabled = !profile.IsSelected };
            item.Click += (_, _) => MainPage.FromElement(this).InvokeHomeCommand("select-profile", profile.ProfileId);
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.ShowAt((Button)sender);
    }
}
