using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Nikkiward.Features.GamepadControl;
using Nikkiward.Models;

namespace Nikkiward.Features.Settings;

public sealed partial class GamepadSettingsView : UserControl
{
    private bool _loading;

    public event EventHandler<GamepadSettingsChangedEventArgs>? SettingsChanged;

    public event EventHandler? RuntimeDownloadRequested;

    public GamepadSettingsView()
    {
        InitializeComponent();
    }

    public void ApplyState(GamepadSettings settings, GamepadRuntimeViewState runtime)
    {
        _loading = true;
        try
        {
            EnableToggle.IsOn = settings.Enabled;
            LongPressToggle.IsOn = settings.GuideLongPressOpensMainWindow;
            SelectAction(GuideActionCombo, settings.GuideAction);
            SelectAction(ShareActionCombo, settings.ShareAction);
            GuideKeysBox.Text = settings.GuideMapKeys ?? string.Empty;
            ShareKeysBox.Text = settings.ShareMapKeys ?? string.Empty;
            EnableToggle.IsEnabled = LongPressToggle.IsEnabled = runtime.CanEdit;
            GuideActionCombo.IsEnabled = ShareActionCombo.IsEnabled = runtime.CanEdit;
            GuideKeysBox.IsEnabled = ShareKeysBox.IsEnabled = runtime.CanEdit;
            StatusText.Text = runtime.StatusText;
            RedistPanel.Visibility = runtime.RuntimeMissing
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateMappingVisibility();
        }
        finally
        {
            _loading = false;
        }
    }

    private GamepadSettings CurrentSettings() => new()
    {
        Enabled = EnableToggle.IsOn,
        GuideLongPressOpensMainWindow = LongPressToggle.IsOn,
        GuideAction = ReadAction(GuideActionCombo),
        ShareAction = ReadAction(ShareActionCombo),
        GuideMapKeys = GuideKeysBox.Text,
        ShareMapKeys = ShareKeysBox.Text,
    };

    private static void SelectAction(ComboBox comboBox, GamepadButtonAction action)
    {
        var tag = action is GamepadButtonAction.MapKeys ? "mapKeys" : "none";
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
    }

    private static GamepadButtonAction ReadAction(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag as string is "mapKeys"
            ? GamepadButtonAction.MapKeys
            : GamepadButtonAction.None;

    private void UpdateMappingVisibility()
    {
        GuideKeysBox.Visibility = ReadAction(GuideActionCombo) is GamepadButtonAction.MapKeys
            ? Visibility.Visible
            : Visibility.Collapsed;
        ShareKeysBox.Visibility = ReadAction(ShareActionCombo) is GamepadButtonAction.MapKeys
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RaiseSettingsChanged(GamepadSettingsChangeKind kind)
    {
        if (!_loading)
        {
            SettingsChanged?.Invoke(this, new GamepadSettingsChangedEventArgs(CurrentSettings(), kind));
        }
    }

    private void OnEnableToggled(object sender, RoutedEventArgs e) =>
        RaiseSettingsChanged(GamepadSettingsChangeKind.Enabled);

    private void OnLongPressToggled(object sender, RoutedEventArgs e) =>
        RaiseSettingsChanged(GamepadSettingsChangeKind.LongPress);

    private void OnGuideActionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateMappingVisibility();
        RaiseSettingsChanged(GamepadSettingsChangeKind.GuideAction);
    }

    private void OnShareActionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateMappingVisibility();
        RaiseSettingsChanged(GamepadSettingsChangeKind.ShareAction);
    }

    private void OnGuideKeysCommitted(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (!GamepadKeyNames.TryParse(GuideKeysBox.Text, out var result, out _, out _))
        {
            ShowMappingError("导航键", result);
            return;
        }

        HideMappingError();
        GuideKeysBox.Text = result ?? string.Empty;
        RaiseSettingsChanged(GamepadSettingsChangeKind.GuideKeys);
    }

    private void OnShareKeysCommitted(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (!GamepadKeyNames.TryParse(ShareKeysBox.Text, out var result, out _, out _))
        {
            ShowMappingError("分享键", result);
            return;
        }

        HideMappingError();
        ShareKeysBox.Text = result ?? string.Empty;
        RaiseSettingsChanged(GamepadSettingsChangeKind.ShareKeys);
    }

    private void ShowMappingError(string buttonName, string? unrecognizedKey)
    {
        MappingErrorText.Text =
            $"{buttonName}映射未保存：无法识别按键“{unrecognizedKey}”。请输入键盘按键上的文字，多个按键用空格分隔。";
        MappingErrorText.Visibility = Visibility.Visible;
    }

    private void HideMappingError()
    {
        MappingErrorText.Text = string.Empty;
        MappingErrorText.Visibility = Visibility.Collapsed;
    }

    private void OnRuntimeDownloadClicked(object sender, RoutedEventArgs e) =>
        RuntimeDownloadRequested?.Invoke(this, EventArgs.Empty);
}
