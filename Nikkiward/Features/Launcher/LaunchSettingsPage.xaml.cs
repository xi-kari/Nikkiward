using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Nikkiward.Features.Background;
using Nikkiward.Models;
using Nikkiward.Features.Shell;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Launcher;

public sealed partial class LaunchSettingsPage : PageBase
{
    private const double CompactLayoutWidth = 720;

    public MainPageViewModel ViewModel { get; private set; } = null!;

    public override string PageTitle => "游戏设置";

    public override FrameworkElement? MastheadInteractionRegion => CloseButton;

    public FrameworkElement OnArtHost => SurfaceRoot;

    public event EventHandler? CloseRequested;
    public event EventHandler? ChooseGameRootRequested;
    public event EventHandler<WallpaperImportRequestedEventArgs>? WallpaperImportRequested;
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

    public void ApplyAppearanceSettings(AppearanceSettings settings)
    {
        MastheadSubtitleBox.Text = settings.LauncherMastheadSubtitle;
        var motionMode = settings.Background.WallpaperEnginePresentation switch
        {
            WallpaperEnginePresentation.MotionBackdrop => true,
            WallpaperEnginePresentation.HolographicCard => false,
            _ => settings.Background.MotionEnabled,
        };
        MotionImportModeButton.IsChecked = motionMode;
        HolographicImportModeButton.IsChecked = !motionMode;
    }

    public void ResetToBasic() =>
        ShowSection(BasicSection, BasicNavigationItem);

    private void OnCloseClicked(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void OnChooseGameRootClicked(object sender, RoutedEventArgs e) =>
        ChooseGameRootRequested?.Invoke(this, EventArgs.Empty);

    private void OnChooseWallpaperClicked(object sender, RoutedEventArgs e) =>
        WallpaperImportRequested?.Invoke(
            this,
            new WallpaperImportRequestedEventArgs(
                MotionImportModeButton.IsChecked == true
                    ? WallpaperImportMode.MotionBackdrop
                    : WallpaperImportMode.HolographicCard));

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

    private void OnLayoutRootLoaded(object sender, RoutedEventArgs e) =>
        ApplyLayoutState(LayoutRoot.ActualWidth);

    private void OnLayoutRootSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyLayoutState(e.NewSize.Width);

    private void ApplyLayoutState(double width)
    {
        var useCompactLayout = width < CompactLayoutWidth;
        VisualStateManager.GoToState(
            this,
            useCompactLayout
                ? "NarrowLaunchSettings"
                : "WideLaunchSettings",
            false);

        SurfaceRoot.CornerRadius = new CornerRadius(useCompactLayout ? 8 : 16);
        LaunchSettingsHeader.Padding = useCompactLayout
            ? new Thickness(14, 12, 10, 8)
            : new Thickness(28, 22, 18, 14);
        LaunchSettingsTitle.FontSize = useCompactLayout ? 20 : 25;
        LaunchSettingsNavigationView.PaneDisplayMode = useCompactLayout
            ? NavigationViewPaneDisplayMode.LeftCompact
            : NavigationViewPaneDisplayMode.Left;
        LaunchSettingsNavigationView.IsPaneOpen = !useCompactLayout;
        LaunchSettingsNavigationView.IsPaneToggleButtonVisible = useCompactLayout;
        LaunchSettingsContent.Margin = useCompactLayout
            ? new Thickness(14, 8, 14, 20)
            : new Thickness(28, 8, 28, 28);

        Grid.SetRow(LocateGameButton, useCompactLayout ? 1 : 0);
        Grid.SetColumn(LocateGameButton, useCompactLayout ? 0 : 2);
        Grid.SetColumnSpan(LocateGameButton, useCompactLayout ? 3 : 1);
        LocateGameButton.HorizontalAlignment = useCompactLayout
            ? HorizontalAlignment.Stretch
            : HorizontalAlignment.Right;
        LocateGameButton.Margin = useCompactLayout
            ? new Thickness(0, 10, 0, 0)
            : new Thickness(0);

        BasicDetailsSecondColumn.Width = useCompactLayout
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(LaunchCapabilityPanel, useCompactLayout ? 1 : 0);
        Grid.SetColumn(LaunchCapabilityPanel, useCompactLayout ? 0 : 1);
        Grid.SetRow(StaticIdentityPanel, useCompactLayout ? 2 : 1);
        Grid.SetColumn(StaticIdentityPanel, 0);
        Grid.SetRow(ExecutionGatePanel, useCompactLayout ? 3 : 1);
        Grid.SetColumn(ExecutionGatePanel, useCompactLayout ? 0 : 1);

        ArgumentsSecondColumn.Width = useCompactLayout
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(ArgumentsCapabilityPanel, useCompactLayout ? 1 : 0);
        Grid.SetColumn(ArgumentsCapabilityPanel, useCompactLayout ? 0 : 1);

        BackgroundPreviewSecondColumn.Width = useCompactLayout
            ? new GridLength(0)
            : new GridLength(1, GridUnitType.Star);
        BackgroundPreviewGrid.Height = useCompactLayout ? 324 : 156;
        Grid.SetRow(CurrentBackgroundPreview, useCompactLayout ? 1 : 0);
        Grid.SetColumn(CurrentBackgroundPreview, useCompactLayout ? 0 : 1);
        BackgroundActions.Orientation = useCompactLayout
            ? Orientation.Vertical
            : Orientation.Horizontal;
    }

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

public sealed class WallpaperImportRequestedEventArgs(WallpaperImportMode mode) : EventArgs
{
    public WallpaperImportMode Mode { get; } = mode;
}
