using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Nikkiward.Features.Shell;
using Nikkiward.Models;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Launcher;

public sealed partial class LauncherPage : PageBase
{
    private const double ShellRailWidth = 56;
    private const double MediumWindowBreakpoint = 780;
    private const double WideWindowBreakpoint = 1100;

    private bool _xamlInitialized;

    public LauncherPage()
    {
        InitializeComponent();
        _xamlInitialized = true;
        AppearanceRuntimeValues.ApplyScaleTransition(PrimaryActionSurface);
        SizeChanged += OnPageSizeChanged;
    }

    public MainPageViewModel ViewModel { get; private set; } = null!;

    public override string PageTitle => "启动管理";

    public FrameworkElement OnArtHost => LauncherRoot;

    public event EventHandler? DownloadStatusRequested;

    public event EventHandler? PlayTimeRequested;

    public event EventHandler? OfficialFlowRequested;

    public event EventHandler? CloseGameRequested;

    public event EventHandler? LaunchSettingsRequested;

    public event EventHandler? JournalRequested;

    public event EventHandler? GalleryRequested;

    public event EventHandler? BackgroundResetRequested;

    public event EventHandler? ProfileRequested;

    protected override void OnEntering(NavigationEventArgs e)
    {
        if (e.Parameter is not LauncherNavigationContext context)
        {
            throw new InvalidOperationException("Launcher navigation context is required.");
        }

        ViewModel = context.ViewModel;
        Bindings.Update();
        UpdatePrimaryActionState();
        MastheadHost.Bind(ViewModel);
        ApplyAppearanceSettings(ViewModel.AppearanceSettings);
        NoticeHost.Bind(ViewModel);
        ApplyResponsiveLayout(ActualWidth);
        base.OnEntering(e);
    }

    public void ApplyAppearanceSettings(AppearanceSettings settings)
    {
        MastheadHost.ApplySettings(settings);
        NoticeHost.Visibility = Visibility.Collapsed;
        ActionCluster.Visibility = Visibility.Visible;
        PrimaryCapsuleVisual.ApplyStyle(settings.LauncherCapsuleStyle);
        UtilityCapsuleVisual.ApplyStyle(settings.LauncherCapsuleStyle);
        PrimaryCapsuleVisual.ApplyMotion(settings.Motion);
        UtilityCapsuleVisual.ApplyMotion(settings.Motion);
        var identity = ResolveCapsuleIdentity(settings.LauncherCapsuleStyle);
        LauncherCapsuleCode.Text = $"{identity.Code} /";
        LauncherCapsuleName.Text = identity.Name;
        PrimaryActionCodeText.Text = identity.Code;
    }

    public void ApplyJournalDuration(string text, string detail)
    {
        NoticeHost.ApplyJournalDuration(text, detail);
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var windowWidth = width + ShellRailWidth;
        if (windowWidth < MediumWindowBreakpoint)
        {
            MastheadHost.ApplyCompactLayout();
            NoticeHost.ApplyCompactLayout();
        }
        else if (windowWidth < WideWindowBreakpoint)
        {
            MastheadHost.ApplyMediumLayout();
            NoticeHost.ApplyMediumLayout();
        }
        else
        {
            MastheadHost.ApplyWideLayout();
            NoticeHost.ApplyWideLayout();
        }
    }

    private void OnNoticeDownloadRequested(object? sender, EventArgs e) =>
        DownloadStatusRequested?.Invoke(this, EventArgs.Empty);

    private void OnNoticePlayTimeRequested(object? sender, EventArgs e) =>
        PlayTimeRequested?.Invoke(this, EventArgs.Empty);

    private void OnNoticeProfileRequested(object? sender, EventArgs e) =>
        ProfileRequested?.Invoke(this, EventArgs.Empty);

    private void OnOfficialFlowClicked(object sender, RoutedEventArgs e) =>
        OfficialFlowRequested?.Invoke(this, EventArgs.Empty);

    private void OnCloseGameClicked(object sender, RoutedEventArgs e) =>
        CloseGameRequested?.Invoke(this, EventArgs.Empty);

    private void OnLaunchSettingsClicked(object sender, RoutedEventArgs e) =>
        LaunchSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnPrimaryActionPointerEntered(object sender, PointerRoutedEventArgs e) =>
        SetPrimaryActionScaleIfEnabled(
            AppearanceRuntimeValues.ReadScale("ButtonHoverScale"));

    private void OnPrimaryActionPointerExited(object sender, PointerRoutedEventArgs e) =>
        SetPrimaryActionScale(1f);

    private void OnPrimaryActionPointerPressed(object sender, PointerRoutedEventArgs e) =>
        SetPrimaryActionScaleIfEnabled(
            AppearanceRuntimeValues.ReadScale("PressScale"));

    private void OnPrimaryActionPointerReleased(object sender, PointerRoutedEventArgs e) =>
        SetPrimaryActionScaleIfEnabled(
            AppearanceRuntimeValues.ReadScale("ButtonHoverScale"));

    private void OnPrimaryActionIsEnabledChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (_xamlInitialized)
        {
            UpdatePrimaryActionState();
        }
    }

    private void SetPrimaryActionScaleIfEnabled(float scale) =>
        SetPrimaryActionScale(PrimaryActionButton.IsEnabled ? scale : 1f);

    private void UpdatePrimaryActionState()
    {
        var enabled = PrimaryActionButton.IsEnabled;
        PrimaryActionDisabledPlate.Visibility = enabled
            ? Visibility.Collapsed
            : Visibility.Visible;
        var foreground = (Brush)Application.Current.Resources[
            enabled ? "InkOnAccentBrush" : "InkSecondaryBrush"];
        PrimaryActionLabel.Foreground = foreground;
        PrimaryActionCodeText.Foreground = foreground;
        LauncherCapsuleStudy.Foreground = foreground;
        PrimaryActionProgressRing.Foreground = foreground;
        if (!enabled)
        {
            SetPrimaryActionScale(1f);
        }
    }

    private void SetPrimaryActionScale(float scale)
    {
        PrimaryActionSurface.CenterPoint = new Vector3(
            (float)PrimaryActionSurface.ActualWidth / 2f,
            (float)PrimaryActionSurface.ActualHeight / 2f,
            0f);
        PrimaryActionSurface.Scale = new Vector3(scale, scale, 1f);
    }

    private void OnJournalClicked(object sender, RoutedEventArgs e) =>
        JournalRequested?.Invoke(this, EventArgs.Empty);

    private void OnProfileClicked(object sender, RoutedEventArgs e) =>
        ProfileRequested?.Invoke(this, EventArgs.Empty);

    private void OnGalleryClicked(object sender, RoutedEventArgs e) =>
        GalleryRequested?.Invoke(this, EventArgs.Empty);

    private void OnBackgroundResetClicked(object sender, RoutedEventArgs e) =>
        BackgroundResetRequested?.Invoke(this, EventArgs.Empty);

    private static (string Code, string Name) ResolveCapsuleIdentity(
        LauncherCapsuleStyle style) => style switch
    {
        LauncherCapsuleStyle.Ocean => ("NC-02", "OCEAN"),
        LauncherCapsuleStyle.Klein => ("NC-03", "KLEIN"),
        LauncherCapsuleStyle.Ultraviolet => ("NC-04", "ULTRAVIOLET"),
        LauncherCapsuleStyle.Chrome => ("NC-05", "CHROME"),
        LauncherCapsuleStyle.Plus => ("NC-06", "PLUS"),
        _ => ("NC-01", "ORIGINAL"),
    };
}
