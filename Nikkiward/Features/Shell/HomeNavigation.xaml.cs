using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.Features.Launcher.Skins;

namespace Nikkiward.Features.Shell;

public sealed partial class HomeNavigation : UserControl
{
    public event EventHandler<string>? Invoked;
    public IEnumerable<FrameworkElement> InteractiveRegions => Items.Children.OfType<RadioButton>()
        .Where(item => item.Visibility == Visibility.Visible);

    public event EventHandler? RegionsChanged;
    private void OnNavigationScrolled(object sender, ScrollViewerViewChangedEventArgs e) => RegionsChanged?.Invoke(this, EventArgs.Empty);

    private string _selected = "launcher";
    public HomeNavigation() => InitializeComponent();

    public void Apply(HomeSkin skin, ResourceDictionary tokens)
    {
        Brand.ContentTemplate = (DataTemplate)tokens["HomeBrandTemplate"];
        Items.Orientation = skin.LeftEdge ? Orientation.Vertical : Orientation.Horizontal;
        VerticalAlignment = skin.LeftEdge ? VerticalAlignment.Center : VerticalAlignment.Top;
        foreach (var item in Items.Children.OfType<RadioButton>())
            item.Template = (ControlTemplate)tokens["HomeNavTemplate"];
    }

    public void Fit(double windowWidth)
    {
        if (windowWidth <= 0) return;
        MaxWidth = Math.Max(1, windowWidth - 160);
        Brand.Visibility = windowWidth >= 760
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Select(string tag)
    {
        _selected = tag;
        foreach (var item in Items.Children.OfType<RadioButton>())
            item.IsChecked = Equals(item.Tag, tag);
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        var tag = (string)((RadioButton)sender).Tag;
        Select(_selected);
        Invoked?.Invoke(this, tag);
    }
}
