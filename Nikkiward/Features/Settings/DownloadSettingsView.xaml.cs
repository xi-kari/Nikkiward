using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.Models;

namespace Nikkiward.Features.Settings;

public enum DownloadPathAction
{
    Choose,
    Open,
    Clear,
}

public sealed class DownloadSettingsChangedEventArgs : EventArgs
{
    public DownloadSettingsChangedEventArgs(DownloadSettings settings)
    {
        Settings = settings;
    }

    public DownloadSettings Settings { get; }
}

public sealed class DownloadPathRequestedEventArgs : EventArgs
{
    public DownloadPathRequestedEventArgs(DownloadPathAction action, string? path)
    {
        Action = action;
        Path = path;
    }

    public DownloadPathAction Action { get; }

    public string? Path { get; }
}

public sealed partial class DownloadSettingsView : UserControl
{
    private bool _loading;
    private DownloadSettings _settings = new();

    public event EventHandler<DownloadSettingsChangedEventArgs>? SettingsChanged;

    public event EventHandler<DownloadPathRequestedEventArgs>? PathRequested;

    public DownloadSettingsView()
    {
        InitializeComponent();
    }

    public void ApplySettings(DownloadSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loading = true;
        try
        {
            _settings = settings;
            var path = settings.DefaultGameInstallPath;
            InstallPathTextBox.Text = string.IsNullOrWhiteSpace(path) ? "尚未选择" : path;
            ToolTipService.SetToolTip(
                InstallPathTextBox,
                string.IsNullOrWhiteSpace(path) ? null : path);
            var hasPath = !string.IsNullOrWhiteSpace(path);
            OpenInstallPathButton.IsEnabled = hasPath;
            ClearInstallPathButton.IsEnabled = hasPath;
            HardLinksToggle.IsOn = settings.EnableHardLinks;
            SpeedLimitNumberBox.Value = settings.SpeedLimitKbps;
        }
        finally
        {
            _loading = false;
        }
    }

    private void RaiseChanged()
    {
        if (!_loading)
        {
            _settings = _settings with
            {
                EnableHardLinks = HardLinksToggle.IsOn,
                SpeedLimitKbps = double.IsNaN(SpeedLimitNumberBox.Value)
                    ? 0
                    : Math.Clamp((int)Math.Round(SpeedLimitNumberBox.Value), 0, 10_000_000),
            };
            SettingsChanged?.Invoke(this, new DownloadSettingsChangedEventArgs(_settings));
        }
    }

    private void OnChooseInstallPathClicked(object sender, RoutedEventArgs e) =>
        PathRequested?.Invoke(
            this,
            new DownloadPathRequestedEventArgs(
                DownloadPathAction.Choose,
                _settings.DefaultGameInstallPath));

    private void OnOpenInstallPathClicked(object sender, RoutedEventArgs e) =>
        PathRequested?.Invoke(
            this,
            new DownloadPathRequestedEventArgs(
                DownloadPathAction.Open,
                _settings.DefaultGameInstallPath));

    private void OnClearInstallPathClicked(object sender, RoutedEventArgs e) =>
        PathRequested?.Invoke(
            this,
            new DownloadPathRequestedEventArgs(DownloadPathAction.Clear, null));

    private void OnHardLinksToggled(object sender, RoutedEventArgs e) => RaiseChanged();

    private void OnSpeedLimitChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => RaiseChanged();
}
