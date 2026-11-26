using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Nikkiward.Features.Launcher.Skins;

public sealed class HomeButton : Button
{
    public HomeButton() => Click += OnClick;

    private void OnClick(object sender, RoutedEventArgs e)
    {
        for (DependencyObject? parent = this; parent != null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is LauncherPage page)
            {
                page.InvokeAction(this);
                return;
            }
        }
    }
}
