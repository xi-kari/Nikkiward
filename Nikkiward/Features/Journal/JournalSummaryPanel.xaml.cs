using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Journal;

public sealed partial class JournalSummaryPanel : UserControl
{
    private const double CompactBreakpoint = 760;

    public event EventHandler? OpenRequested;

    public JournalSummaryPanel()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
    }

    public void ApplySnapshot(JournalSnapshot snapshot, string durationText, string durationSourceText)
    {
        LoginDaysTile.Value = snapshot.LoginDays ?? string.Empty;
        DurationTile.Value = durationText ?? string.Empty;
        DurationSourceText.Text = durationSourceText;
        OutfitCountTile.Value = snapshot.OutfitCount ?? string.Empty;
        MomoCloakCountTile.Value = snapshot.MomoCloakCount ?? string.Empty;
        SketchCountTile.Value = snapshot.SketchCount ?? string.Empty;
        LastSyncedText.Text = $"最后同步 {snapshot.CapturedAtUtc.ToLocalTime():MM/dd HH:mm}";
    }

    public void ResetState(string durationSourceText)
    {
        LoginDaysTile.Value = string.Empty;
        DurationTile.Value = string.Empty;
        DurationSourceText.Text = durationSourceText;
        OutfitCountTile.Value = string.Empty;
        MomoCloakCountTile.Value = string.Empty;
        SketchCountTile.Value = string.Empty;
        LastSyncedText.Text = string.Empty;
    }

    public void SetSyncInProgress(bool isInProgress)
    {
        StatisticsGrid.Visibility = isInProgress ? Visibility.Collapsed : Visibility.Visible;
        StatisticsSkeleton.Visibility = isInProgress ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        ApplyResponsiveLayout(ActualWidth);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var compact = width < CompactBreakpoint;
        StatisticsGrid.ColumnDefinitions.Clear();
        StatisticsGrid.RowDefinitions.Clear();

        var columnCount = compact ? 6 : 5;
        for (var index = 0; index < columnCount; index++)
        {
            StatisticsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        StatisticsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (compact)
        {
            StatisticsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var tiles = new[] { LoginDaysTile, DurationTile, OutfitCountTile, MomoCloakCountTile, SketchCountTile };
        for (var index = 0; index < tiles.Length; index++)
        {
            var tile = tiles[index];
            var row = compact && index >= 3 ? 1 : 0;
            var column = compact
                ? index < 3 ? index * 2 : (index - 3) * 3
                : index;
            var span = compact ? (index < 3 ? 2 : 3) : 1;
            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, column);
            Grid.SetColumnSpan(tile, span);
            tile.BorderThickness = column == 0 ? new Thickness(0) : new Thickness(1, 0, 0, 0);
        }

        StatisticsIsland.MinHeight = compact ? 224 : 120;
        Grid.SetRow(SyncButton, compact ? 1 : 0);
        Grid.SetColumn(SyncButton, compact ? 0 : 2);
        Grid.SetColumnSpan(SyncButton, compact ? 3 : 1);
        Grid.SetRow(LastSyncedText, compact ? 1 : 0);
        Grid.SetColumn(LastSyncedText, compact ? 1 : 1);
        SyncButton.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        SyncButton.Margin = compact ? new Thickness(0, 8, 0, 0) : new Thickness(0);
    }
}
