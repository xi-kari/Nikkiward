using Microsoft.UI.Xaml.Controls;
using Nikkiward.Models;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Launcher;

public sealed partial class LauncherMasthead : UserControl
{
    private bool _showSubtitleForLayout = true;

    public LauncherMasthead()
    {
        InitializeComponent();
        MastheadGlow.DepthTarget = Island;
    }

    public MainPageViewModel ViewModel { get; private set; } = null!;

    public void Bind(MainPageViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    public void ApplySettings(AppearanceSettings settings)
    {
        EyebrowText.Text = string.IsNullOrWhiteSpace(settings.LauncherMastheadLabel)
            ? AppearanceSettings.DefaultLauncherMastheadLabel
            : settings.LauncherMastheadLabel;
        TitleText.Text = string.IsNullOrWhiteSpace(settings.LauncherMastheadTitle)
            ? AppearanceSettings.DefaultLauncherMastheadTitle
            : settings.LauncherMastheadTitle;
        SubtitleText.Text = settings.LauncherMastheadSubtitle?.Trim() ?? string.Empty;
        MastheadGlow.ApplyMotion(settings.Motion);
        UpdateSubtitleVisibility();
    }

    public void ApplyWideLayout()
    {
        ApplyLayout(520, 40, true);
    }

    public void ApplyMediumLayout()
    {
        ApplyLayout(440, 36, false);
    }

    public void ApplyCompactLayout()
    {
        ApplyLayout(320, 32, false);
    }

    private void ApplyLayout(double maxWidth, double titleSize, bool showSubtitle)
    {
        MastheadGlow.MaxWidth = maxWidth;
        TitleText.FontSize = titleSize;
        _showSubtitleForLayout = showSubtitle;
        UpdateSubtitleVisibility();
    }

    private void UpdateSubtitleVisibility()
    {
        SubtitleText.Visibility = _showSubtitleForLayout &&
            !string.IsNullOrWhiteSpace(SubtitleText.Text)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
    }
}
