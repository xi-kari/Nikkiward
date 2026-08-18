using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.Models;
using Nikkiward.Services;

namespace Nikkiward.Features.Settings;

public enum UserDataFolderAction
{
    Choose,
    Open,
}

public sealed class UserDataFolderRequestedEventArgs : EventArgs
{
    public UserDataFolderRequestedEventArgs(UserDataFolderAction action, string path)
    {
        Action = action;
        Path = path;
    }

    public UserDataFolderAction Action { get; }

    public string Path { get; }
}

public sealed class FileManagementSettingsViewState
{
    public required string DataFolderPath { get; init; }

    public required SettingsCacheStatistics CacheStatistics { get; init; }

    public string? LastBackupPath { get; init; }

    public DateTimeOffset? LastBackupAtUtc { get; init; }

    public bool ClearLauncherBackgroundFiles { get; init; }

    public bool IsBusy { get; init; }

    public string? StatusText { get; init; }
}

public sealed partial class FileManagementSettingsView : UserControl
{
    private bool _loading;
    private string _dataFolderPath = string.Empty;

    public event EventHandler<UserDataFolderRequestedEventArgs>? DataFolderRequested;

    public event EventHandler? BackupRequested;

    public event EventHandler? OpenBackupRequested;

    public event EventHandler? DeleteAllSettingsRequested;

    public event EventHandler? OpenLogsRequested;

    public event EventHandler? ClearCacheRequested;

    public event EventHandler<bool>? ClearLauncherBackgroundChanged;

    public FileManagementSettingsView()
    {
        InitializeComponent();
    }

    public void ApplySettings(FileManagementSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loading = true;
        try
        {
            ClearLauncherBackgroundCheckBox.IsChecked = settings.ClearLauncherBackgroundFiles;
            LastBackupText.Text = settings.LastBackupAtUtc is { } time
                ? $"最近备份 {time.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                : "尚无备份";
            OpenBackupButton.IsEnabled = !string.IsNullOrWhiteSpace(settings.LastBackupPath) &&
                                          File.Exists(settings.LastBackupPath);
        }
        finally
        {
            _loading = false;
        }
    }

    public void ApplyState(FileManagementSettingsViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _loading = true;
        try
        {
            _dataFolderPath = state.DataFolderPath;
            DataFolderTextBox.Text = state.DataFolderPath;
            ToolTipService.SetToolTip(DataFolderTextBox, state.DataFolderPath);
            ClearLauncherBackgroundCheckBox.IsChecked = state.ClearLauncherBackgroundFiles;
            LogCacheSizeText.Text = FormatBytes(state.CacheStatistics.LogBytes);
            ImageCacheSizeText.Text = FormatBytes(state.CacheStatistics.ImageBytes);
            BrowserCacheSizeText.Text = FormatBytes(state.CacheStatistics.BrowserBytes);
            GameCacheSizeText.Text = FormatBytes(state.CacheStatistics.GameResourceBytes);
            LauncherBackgroundSizeText.Text = FormatBytes(state.CacheStatistics.LauncherBackgroundBytes);
            LastBackupText.Text = state.LastBackupAtUtc is { } time
                ? $"最近备份 {time.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
                : "尚无备份";
            OpenBackupButton.IsEnabled = !string.IsNullOrWhiteSpace(state.LastBackupPath) &&
                                          File.Exists(state.LastBackupPath);
            BackupButton.IsEnabled = !state.IsBusy;
            ClearCacheButton.IsEnabled = !state.IsBusy;
            DeleteAllSettingsButton.IsEnabled = !state.IsBusy;
            ClearLauncherBackgroundCheckBox.IsEnabled = !state.IsBusy;
            BackupProgressRing.Visibility = state.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            BackupProgressRing.IsActive = state.IsBusy;
            ClearCacheProgressRing.Visibility = state.IsBusy ? Visibility.Visible : Visibility.Collapsed;
            ClearCacheProgressRing.IsActive = state.IsBusy;
            StatusText.Text = state.StatusText ?? string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        var value = (double)Math.Max(0, bytes);
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return index == 0 ? $"{value:0} {units[index]}" : $"{value:0.##} {units[index]}";
    }

    private void OnChooseDataFolderClicked(object sender, RoutedEventArgs e) =>
        DataFolderRequested?.Invoke(
            this,
            new UserDataFolderRequestedEventArgs(UserDataFolderAction.Choose, _dataFolderPath));

    private void OnOpenDataFolderClicked(object sender, RoutedEventArgs e) =>
        DataFolderRequested?.Invoke(
            this,
            new UserDataFolderRequestedEventArgs(UserDataFolderAction.Open, _dataFolderPath));

    private void OnBackupClicked(object sender, RoutedEventArgs e) => BackupRequested?.Invoke(this, EventArgs.Empty);

    private void OnOpenBackupClicked(object sender, RoutedEventArgs e) => OpenBackupRequested?.Invoke(this, EventArgs.Empty);

    private void OnDeleteAllSettingsClicked(object sender, RoutedEventArgs e) => DeleteAllSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnOpenLogsClicked(object sender, RoutedEventArgs e) => OpenLogsRequested?.Invoke(this, EventArgs.Empty);

    private void OnClearCacheClicked(object sender, RoutedEventArgs e) => ClearCacheRequested?.Invoke(this, EventArgs.Empty);

    private void OnClearLauncherBackgroundChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            ClearLauncherBackgroundChanged?.Invoke(
                this,
                ClearLauncherBackgroundCheckBox.IsChecked is true);
        }
    }
}
