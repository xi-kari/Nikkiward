using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Nikkiward.Features.Pages;

public sealed partial class WebPageErrorView : Page
{
    internal sealed record Failure(string Reason, Action Retry, Action Home);
    private Failure _failure = null!;

    public WebPageErrorView() { InitializeComponent(); NativeSkin.Bind(this, Heading); }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _failure = (Failure)e.Parameter;
        Reason.Text = _failure.Reason;
    }

    private void OnRetry(object sender, RoutedEventArgs e) => _failure.Retry();
    private void OnHome(object sender, RoutedEventArgs e) => _failure.Home();
}
