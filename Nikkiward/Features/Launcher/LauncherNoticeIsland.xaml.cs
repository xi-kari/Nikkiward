using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Launcher;

public sealed partial class LauncherNoticeIsland : UserControl
{
    public LauncherNoticeIsland()
    {
        InitializeComponent();
    }

    public MainPageViewModel ViewModel { get; private set; } = null!;

    public event EventHandler? StatusRequested;

    public event EventHandler? DownloadRequested;

    public event EventHandler? PlayTimeRequested;

    public event EventHandler? ProfileRequested;

    public void Bind(MainPageViewModel viewModel)
    {
        ViewModel = viewModel;
        Bindings.Update();
    }

    public void ApplyJournalDuration(string text, string detail)
    {
        PlayTimeTextBlock.Text = text;
        CompactPlayTimeTextBlock.Text = text;
        ToolTipService.SetToolTip(PlayTimeButton, detail);
        ToolTipService.SetToolTip(CompactIsland, detail);
    }

    public void ApplyWideLayout() =>
        ApplyFullLayout(520, 180, 160, condensed: false);

    public void ApplyMediumLayout() =>
        ApplyFullLayout(420, 124, 112, condensed: true);

    public void ApplyCompactLayout()
    {
        Island.Visibility = Visibility.Collapsed;
        CompactIsland.Visibility = Visibility.Visible;
    }

    private void ApplyFullLayout(
        double width,
        double height,
        double bannerWidth,
        bool condensed)
    {
        CompactIsland.Visibility = Visibility.Collapsed;
        Island.Visibility = Visibility.Visible;
        Island.Width = width;
        Island.Height = height;
        BannerColumn.Width = new GridLength(bannerWidth);
        Banner.Width = bannerWidth;
        DownloadButton.Visibility = condensed ? Visibility.Collapsed : Visibility.Visible;
        ProfileButton.Visibility = condensed ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPlayTimeClicked(object sender, RoutedEventArgs e) =>
        PlayTimeRequested?.Invoke(this, EventArgs.Empty);

    private void OnStatusClicked(object sender, RoutedEventArgs e) =>
        StatusRequested?.Invoke(this, EventArgs.Empty);

    private void OnDownloadClicked(object sender, RoutedEventArgs e) =>
        DownloadRequested?.Invoke(this, EventArgs.Empty);

    private void OnProfileClicked(object sender, RoutedEventArgs e) =>
        ProfileRequested?.Invoke(this, EventArgs.Empty);
}
