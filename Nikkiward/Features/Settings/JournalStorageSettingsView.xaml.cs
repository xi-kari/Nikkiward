using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nikkiward.Features.Settings;

public sealed partial class JournalStorageSettingsView : UserControl
{
    public event EventHandler? JournalOpenRequested;

    public event EventHandler? JournalCacheClearRequested;

    public JournalStorageSettingsView()
    {
        InitializeComponent();
    }

    public void ApplyPaths(SettingsStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        JournalWebViewDataPathTextBox.Text = paths.JournalWebViewDataPath;
        JournalSnapshotPathTextBox.Text = paths.JournalSnapshotPath;
        JournalAssetsPathTextBox.Text = paths.JournalAssetsPath;
    }

    public void ApplyPaths(
        string journalWebViewDataPath,
        string journalSnapshotPath,
        string journalAssetsPath) =>
        ApplyPaths(new SettingsStoragePaths(
            journalWebViewDataPath,
            journalSnapshotPath,
            journalAssetsPath));

    private void OnOpenClicked(object sender, RoutedEventArgs e) =>
        JournalOpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnClearCacheClicked(object sender, RoutedEventArgs e) =>
        JournalCacheClearRequested?.Invoke(this, EventArgs.Empty);
}
