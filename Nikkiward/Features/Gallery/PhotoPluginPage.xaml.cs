using Microsoft.UI.Xaml;
using Nikkiward.Features.Shell;

namespace Nikkiward.Features.Gallery;

public sealed partial class PhotoPluginPage : PageBase
{
    public override string PageTitle => "相册插件";

    public event EventHandler? OpenRequested;

    public event EventHandler? SettingsRequested;

    public PhotoPluginPage()
    {
        InitializeComponent();
    }

    public void UpdateState(string statusText, string? version, bool canOpen)
    {
        StatusText.Text = statusText;
        VersionText.Text = string.IsNullOrWhiteSpace(version)
            ? string.Empty
            : $"v{version}";
        OpenButton.IsEnabled = canOpen;
    }

    public void UpdateStatus(string statusText)
    {
        StatusText.Text = statusText;
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }
}
