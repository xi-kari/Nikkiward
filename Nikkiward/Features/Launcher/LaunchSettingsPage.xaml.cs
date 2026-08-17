using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Nikkiward.Models;
using Nikkiward.Features.Shell;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Launcher;

public sealed partial class LaunchSettingsPage : PageBase
{
    public MainPageViewModel ViewModel { get; private set; } = null!;

    public override string PageTitle => "游戏设置";

    public override FrameworkElement? MastheadInteractionRegion => CloseButton;

    public FrameworkElement OnArtHost => SurfaceRoot;

    public event EventHandler? CloseRequested;
    public event EventHandler? ChooseGameRootRequested;
    public event EventHandler? BackgroundChooseRequested;
    public event EventHandler? MotionBackgroundChooseRequested;
    public event EventHandler? BackgroundResetRequested;
    public event EventHandler<MastheadSubtitleChangedEventArgs>? MastheadSubtitleSaveRequested;

    public LaunchSettingsPage()
    {
        InitializeComponent();
        CloseButton.Loaded += OnMastheadLoaded;
        CloseButton.SizeChanged += OnMastheadSizeChanged;
    }

    protected override void OnEntering(NavigationEventArgs e)
    {
        if (e.Parameter is not LaunchSettingsNavigationContext context)
        {
            throw new InvalidOperationException("Launch settings navigation context is required.");
        }

        ViewModel = context.ViewModel;
        BackgroundPreviewImage.Source = context.CurrentBackgroundSource;
        ApplyAppearanceSettings(ViewModel.AppearanceSettings);
        Bindings.Update();
        ResetToBasic();
        base.OnEntering(e);
    }

    public void ApplyBackgroundPreview(ImageSource? source) =>
        BackgroundPreviewImage.Source = source;

    public void ApplyAppearanceSettings(AppearanceSettings settings) =>
        MastheadSubtitleBox.Text = settings.LauncherMastheadSubtitle;

    public void ResetToBasic() =>
        ShowSection(BasicSection, BasicNavigationItem);

    private void OnCloseClicked(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnChooseGameRootClicked(object sender, RoutedEventArgs e) =>
        ChooseGameRootRequested?.Invoke(this, EventArgs.Empty);

    private void OnChooseBackgroundClicked(object sender, RoutedEventArgs e) =>
        BackgroundChooseRequested?.Invoke(this, EventArgs.Empty);

    private void OnChooseMotionBackgroundClicked(object sender, RoutedEventArgs e) =>
        MotionBackgroundChooseRequested?.Invoke(this, EventArgs.Empty);

    private void OnResetBackgroundClicked(object sender, RoutedEventArgs e) =>
        BackgroundResetRequested?.Invoke(this, EventArgs.Empty);

    private void OnSaveMastheadSubtitleClicked(object sender, RoutedEventArgs e) =>
        MastheadSubtitleSaveRequested?.Invoke(
            this,
            new MastheadSubtitleChangedEventArgs(MastheadSubtitleBox.Text));

    private void OnResetMastheadSubtitleClicked(object sender, RoutedEventArgs e)
    {
        MastheadSubtitleBox.Text = AppearanceSettings.DefaultLauncherMastheadSubtitle;
        MastheadSubtitleSaveRequested?.Invoke(
            this,
            new MastheadSubtitleChangedEventArgs(MastheadSubtitleBox.Text));
    }

    private void OnMastheadLoaded(object sender, RoutedEventArgs e) =>
        NotifyMastheadInteractionRegionChanged();

    private void OnMastheadSizeChanged(object sender, SizeChangedEventArgs e) =>
        NotifyMastheadInteractionRegionChanged();

    private void OnNavigationItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is not NavigationViewItem { Tag: string tag } item)
        {
            return;
        }

        var target = tag switch
        {
            "basic" => BasicSection,
            "arguments" => ArgumentsSection,
            "background" => BackgroundSection,
            "package" => PackageSection,
            _ => null,
        };

        if (target is not null)
        {
            ShowSection(target, item);
        }
    }

    private void ShowSection(
        FrameworkElement section,
        NavigationViewItem navigationItem)
    {
        FrameworkElement[] sections =
        [
            BasicSection,
            ArgumentsSection,
            BackgroundSection,
            PackageSection,
        ];

        foreach (var candidate in sections)
        {
            candidate.Visibility = ReferenceEquals(candidate, section)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        LaunchSettingsNavigationView.SelectedItem = navigationItem;
        ContentScrollViewer.ChangeView(null, 0, null, true);
    }
}

public sealed class MastheadSubtitleChangedEventArgs(string subtitle) : EventArgs
{
    public string Subtitle { get; } = subtitle;
}
