using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.Models;

namespace Nikkiward.Features.Settings;

public enum ScreenshotFolderAction
{
    Choose,
    Open,
}

public sealed class ScreenshotSettingsChangedEventArgs : EventArgs
{
    public ScreenshotSettingsChangedEventArgs(ScreenshotSettings settings)
    {
        Settings = settings;
    }

    public ScreenshotSettings Settings { get; }
}

public sealed class ScreenshotFolderRequestedEventArgs : EventArgs
{
    public ScreenshotFolderRequestedEventArgs(ScreenshotFolderAction action, string? path)
    {
        Action = action;
        Path = path;
    }

    public ScreenshotFolderAction Action { get; }

    public string? Path { get; }
}

public sealed partial class ScreenshotSettingsView : UserControl
{
    private bool _loading;
    private ScreenshotSettings _settings = new();

    public event EventHandler<ScreenshotSettingsChangedEventArgs>? SettingsChanged;

    public event EventHandler<ScreenshotFolderRequestedEventArgs>? FolderRequested;

    public event EventHandler? TestCaptureRequested;

    public event EventHandler? ClearThumbnailCacheRequested;

    public ScreenshotSettingsView()
    {
        InitializeComponent();
    }

    public void ApplySettings(ScreenshotSettings settings, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loading = true;
        try
        {
            _settings = settings;
            ScreenshotFolderTextBox.Text = folderPath;
            ToolTipService.SetToolTip(ScreenshotFolderTextBox, folderPath);
            OpenScreenshotFolderButton.IsEnabled = Directory.Exists(folderPath);
            HotkeyTextBox.Text = settings.Hotkey;
            PngFormatRadio.IsChecked = settings.Format is ScreenshotImageFormat.Png;
            AvifFormatRadio.IsChecked = settings.Format is ScreenshotImageFormat.Avif;
            JpegXlFormatRadio.IsChecked = settings.Format is ScreenshotImageFormat.JpegXl;
            MediumQualityRadio.IsChecked = settings.Quality is ScreenshotImageQuality.Medium;
            HighQualityRadio.IsChecked = settings.Quality is ScreenshotImageQuality.High;
            LosslessQualityRadio.IsChecked = settings.Quality is ScreenshotImageQuality.Lossless;
            QualityPanel.Visibility = settings.Format is ScreenshotImageFormat.Png
                ? Visibility.Collapsed
                : Visibility.Visible;
            ColorManagementToggle.IsOn = settings.EnableColorManagement;
            AutoCopyToggle.IsOn = settings.AutoCopyToClipboard;
            AutoConvertSdrToggle.IsOn = settings.AutoConvertHdrToSdr;
        }
        finally
        {
            _loading = false;
        }
    }

    public void ApplyStatus(string message) => StatusText.Text = message;

    private void RaiseChanged()
    {
        if (_loading)
        {
            return;
        }

        _settings = _settings with
        {
            Hotkey = HotkeyTextBox.Text,
            Format = ReadFormat(),
            Quality = ReadQuality(),
            EnableColorManagement = ColorManagementToggle.IsOn,
            AutoCopyToClipboard = AutoCopyToggle.IsOn,
            AutoConvertHdrToSdr = AutoConvertSdrToggle.IsOn,
        };
        SettingsChanged?.Invoke(this, new ScreenshotSettingsChangedEventArgs(_settings));
    }

    private ScreenshotImageFormat ReadFormat() =>
        AvifFormatRadio.IsChecked is true
            ? ScreenshotImageFormat.Avif
            : JpegXlFormatRadio.IsChecked is true
                ? ScreenshotImageFormat.JpegXl
                : ScreenshotImageFormat.Png;

    private ScreenshotImageQuality ReadQuality() =>
        MediumQualityRadio.IsChecked is true
            ? ScreenshotImageQuality.Medium
            : LosslessQualityRadio.IsChecked is true
                ? ScreenshotImageQuality.Lossless
                : ScreenshotImageQuality.High;

    private void OnChooseFolderClicked(object sender, RoutedEventArgs e) =>
        FolderRequested?.Invoke(
            this,
            new ScreenshotFolderRequestedEventArgs(
                ScreenshotFolderAction.Choose,
                _settings.FolderPath));

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e) =>
        FolderRequested?.Invoke(
            this,
            new ScreenshotFolderRequestedEventArgs(
                ScreenshotFolderAction.Open,
                ScreenshotFolderTextBox.Text));

    private void OnHotkeyLostFocus(object sender, RoutedEventArgs e) => RaiseChanged();

    private void OnFormatChecked(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            QualityPanel.Visibility = ReadFormat() is ScreenshotImageFormat.Png
                ? Visibility.Collapsed
                : Visibility.Visible;
            RaiseChanged();
        }
    }

    private void OnQualityChecked(object sender, RoutedEventArgs e) => RaiseChanged();

    private void OnToggleChanged(object sender, RoutedEventArgs e) => RaiseChanged();

    private void OnTestCaptureClicked(object sender, RoutedEventArgs e) => TestCaptureRequested?.Invoke(this, EventArgs.Empty);

    private void OnClearThumbnailCacheClicked(object sender, RoutedEventArgs e) => ClearThumbnailCacheRequested?.Invoke(this, EventArgs.Empty);
}
