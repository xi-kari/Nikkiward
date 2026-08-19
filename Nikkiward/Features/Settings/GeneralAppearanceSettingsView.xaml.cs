using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Nikkiward.Models;
using Windows.UI;

namespace Nikkiward.Features.Settings;

public sealed partial class GeneralAppearanceSettingsView : UserControl
{
    private const double NarrowBreakpoint = 620;
    private bool _uiLoading;
    private AppearanceSettings _settings = new();
    private uint? _pendingCustomAccentArgb;

    public event EventHandler? ChooseBackgroundRequested;

    public event EventHandler? ResetBackgroundRequested;

    public event EventHandler<AppearanceSettingsChangedEventArgs>? AppearanceSettingsChanged;

    public GeneralAppearanceSettingsView()
    {
        InitializeComponent();
        ApplySettings(new AppearanceSettings());
    }

    public ImageSource? DefaultBackgroundSource
    {
        get => DefaultBackgroundPreview.Source;
        set => DefaultBackgroundPreview.Source = value;
    }

    public ImageSource? CurrentBackgroundSource
    {
        get => CurrentBackgroundPreview.Source;
        set => CurrentBackgroundPreview.Source = value;
    }

    public string BackgroundStatus
    {
        get => BackgroundStatusText.Text;
        set => BackgroundStatusText.Text = value;
    }

    public string AppearanceStatus
    {
        get => AppearanceStatusText.Text;
        set => AppearanceStatusText.Text = value;
    }

    public void ApplySettings(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _uiLoading = true;
        try
        {
            _settings = settings;
            LauncherMastheadLabelBox.Text = settings.LauncherMastheadLabel;
            LauncherMastheadTitleBox.Text = settings.LauncherMastheadTitle;
            LauncherMastheadSubtitleBox.Text = settings.LauncherMastheadSubtitle;
            LauncherUtilityPanelsToggle.IsOn = settings.ShowLauncherUtilityPanels;
            ApplyCapsuleStyleSelection(settings.LauncherCapsuleStyle);
            ApplyBackgroundPresetSelection(settings.Background.SelectedSource);
            FollowArtworkThemeButton.IsChecked =
                settings.ThemeMode == ThemeMode.FollowArtwork;
            WarmLightThemeButton.IsChecked =
                settings.ThemeMode == ThemeMode.WarmLight;
            WarmDarkThemeButton.IsChecked =
                settings.ThemeMode == ThemeMode.WarmDark;

            CarouselToggle.IsOn = settings.Background.CarouselEnabled;
            CarouselIntervalNumberBox.Value =
                settings.Background.CarouselIntervalMinutes;
            CarouselIntervalNumberBox.IsEnabled = settings.Background.CarouselEnabled;
            ParallaxToggle.IsOn = settings.Background.ParallaxEnabled;
            HolographicCardToggle.IsOn = settings.Background.HolographicCardEnabled;
            MotionToggle.IsOn = settings.Background.MotionEnabled;
            LiveBlurToggle.IsOn = settings.Background.UseLiveBlur;
            GlassIntensitySlider.Value = settings.Background.GlassIntensity;
            MotionPanToggle.IsOn = settings.Background.MotionPanEnabled;
            MotionZoomSlider.Value = settings.Background.MotionZoom;
            UpdateMotionControlState(settings.Background);

            AdaptiveAccentButton.IsChecked =
                settings.AccentMode == AccentColorMode.Adaptive;
            FixedAccentButton.IsChecked =
                settings.AccentMode == AccentColorMode.Fixed;
            ApplyFixedAccentSelection(settings.FixedAccent);
            SetFixedAccentEnabled(settings.AccentMode == AccentColorMode.Fixed);
            _pendingCustomAccentArgb = settings.CustomAccentArgb;
            ApplyCustomAccentPreview(settings.CustomAccentArgb);

            SerifTitlesToggle.IsOn = settings.UseSerifTitles;
            SelectComboItem(MotionModeComboBox, settings.Motion switch
            {
                AppearanceMotionMode.Reduced => "reduced",
                AppearanceMotionMode.Off => "off",
                _ => "full",
            });
            SelectComboItem(DensityComboBox, settings.Density switch
            {
                InterfaceDensity.Compact => "compact",
                InterfaceDensity.Comfortable => "comfortable",
                _ => "standard",
            });
        }
        finally
        {
            _uiLoading = false;
        }
    }

    public void ApplyThemeMode(ThemeMode mode) =>
        ApplySettings(_settings with { ThemeMode = mode });

    private void OnThemeModeChecked(object sender, RoutedEventArgs e)
    {
        if (_uiLoading || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        Commit(_settings with
        {
            ThemeMode = tag switch
            {
                "warmLight" => ThemeMode.WarmLight,
                "warmDark" => ThemeMode.WarmDark,
                _ => ThemeMode.FollowArtwork,
            },
        });
    }

    private void OnChooseClicked(object sender, RoutedEventArgs e) =>
        ChooseBackgroundRequested?.Invoke(this, EventArgs.Empty);

    private void OnPresetBackgroundClicked(object sender, RoutedEventArgs e)
    {
        if (_uiLoading ||
            sender is not Button { Tag: string id } ||
            !AppearanceBackgroundPresets.TryGet(id, out var preset))
        {
            return;
        }

        var settings = _settings with
        {
            ThemeMode = preset.SurfaceThemeMode ?? _settings.ThemeMode,
            LauncherCapsuleStyle = preset.CapsuleStyle,
            Background = new BackgroundArtSettings
            {
                SelectedSource = preset.Source,
            },
        };
        ApplySettings(settings);
        Commit(settings, forceStaticBackgroundReload: true);
    }

    private void OnResetClicked(object sender, RoutedEventArgs e) =>
        ResetBackgroundRequested?.Invoke(this, EventArgs.Empty);

    private void OnMastheadCopyLostFocus(object sender, RoutedEventArgs e)
    {
        if (_uiLoading || sender is not TextBox { Tag: string tag } textBox)
        {
            return;
        }

        var value = tag == "subtitle"
            ? textBox.Text.Trim()
            : string.IsNullOrWhiteSpace(textBox.Text)
                ? tag == "title"
                    ? AppearanceSettings.DefaultLauncherMastheadTitle
                    : AppearanceSettings.DefaultLauncherMastheadLabel
                : textBox.Text.Trim();

        Commit(tag switch
        {
            "title" => _settings with { LauncherMastheadTitle = value },
            "subtitle" => _settings with { LauncherMastheadSubtitle = value },
            _ => _settings with { LauncherMastheadLabel = value },
        });
    }

    private void OnLauncherUtilityPanelsToggled(object sender, RoutedEventArgs e)
    {
        if (!_uiLoading)
        {
            Commit(_settings with
            {
                ShowLauncherUtilityPanels = LauncherUtilityPanelsToggle.IsOn,
            });
        }
    }

    private void OnLauncherCapsuleStyleChecked(object sender, RoutedEventArgs e)
    {
        if (_uiLoading || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        Commit(_settings with
        {
            LauncherCapsuleStyle = tag switch
            {
                "ocean" => LauncherCapsuleStyle.Ocean,
                "klein" => LauncherCapsuleStyle.Klein,
                "ultraviolet" => LauncherCapsuleStyle.Ultraviolet,
                "chrome" => LauncherCapsuleStyle.Chrome,
                "plus" => LauncherCapsuleStyle.Plus,
                _ => LauncherCapsuleStyle.Original,
            },
        });
    }

    private void OnResetLauncherMastheadClicked(object sender, RoutedEventArgs e)
    {
        var settings = _settings with
        {
            LauncherMastheadLabel = AppearanceSettings.DefaultLauncherMastheadLabel,
            LauncherMastheadTitle = AppearanceSettings.DefaultLauncherMastheadTitle,
            LauncherMastheadSubtitle = AppearanceSettings.DefaultLauncherMastheadSubtitle,
        };
        ApplySettings(settings);
        Commit(settings);
    }

    private void OnBackgroundOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_uiLoading)
        {
            return;
        }

        _uiLoading = true;
        try
        {
            if (ReferenceEquals(sender, CarouselToggle) && CarouselToggle.IsOn)
            {
                MotionToggle.IsOn = false;
            }
            else if (ReferenceEquals(sender, MotionToggle) && MotionToggle.IsOn)
            {
                CarouselToggle.IsOn = false;
            }
        }
        finally
        {
            _uiLoading = false;
        }

        CarouselIntervalNumberBox.IsEnabled = CarouselToggle.IsOn;
        UpdateMotionControlState(_settings.Background with
        {
            MotionEnabled = MotionToggle.IsOn,
        });
        Commit(_settings with
        {
            Background = _settings.Background with
            {
                CarouselEnabled = CarouselToggle.IsOn,
                ParallaxEnabled = ParallaxToggle.IsOn,
                HolographicCardEnabled = HolographicCardToggle.IsOn,
                MotionEnabled = MotionToggle.IsOn,
                UseLiveBlur = LiveBlurToggle.IsOn,
                MotionPanEnabled = MotionPanToggle.IsOn,
            },
        });
    }

    private void UpdateMotionControlState(BackgroundArtSettings settings)
    {
        var sourceAvailable = !string.IsNullOrWhiteSpace(settings.MotionSource) &&
            File.Exists(settings.MotionSource);
        MotionToggle.IsEnabled = sourceAvailable;
        var motionControlsEnabled = sourceAvailable && MotionToggle.IsOn;
        MotionPanToggle.IsEnabled = motionControlsEnabled;
        MotionZoomSlider.IsEnabled = motionControlsEnabled;
    }

    private void OnGlassIntensityChanged(
        object sender,
        RangeBaseValueChangedEventArgs args)
    {
        if (_uiLoading)
        {
            return;
        }

        Commit(_settings with
        {
            Background = _settings.Background with
            {
                GlassIntensity = Math.Clamp(args.NewValue, 0, 1),
            },
        });
    }

    private void OnMotionZoomChanged(
        object sender,
        RangeBaseValueChangedEventArgs args)
    {
        if (_uiLoading)
        {
            return;
        }

        Commit(_settings with
        {
            Background = _settings.Background with
            {
                MotionZoom = Math.Clamp(args.NewValue, 1, 2.8),
            },
        });
    }

    private void OnCarouselIntervalChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        if (_uiLoading || double.IsNaN(args.NewValue))
        {
            return;
        }

        var interval = Math.Clamp(
            (int)Math.Round(args.NewValue),
            BackgroundArtSettings.MinimumCarouselIntervalMinutes,
            BackgroundArtSettings.MaximumCarouselIntervalMinutes);
        Commit(_settings with
        {
            Background = _settings.Background with
            {
                CarouselIntervalMinutes = interval,
            },
        });
    }

    private void OnAccentModeChecked(object sender, RoutedEventArgs e)
    {
        if (_uiLoading || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        var fixedMode = tag == "fixed";
        SetFixedAccentEnabled(fixedMode);
        Commit(_settings with
        {
            AccentMode = fixedMode
                ? AccentColorMode.Fixed
                : AccentColorMode.Adaptive,
        });
    }

    private void OnFixedAccentChecked(object sender, RoutedEventArgs e)
    {
        if (_uiLoading || sender is not RadioButton { Tag: string tag })
        {
            return;
        }

        Commit(_settings with
        {
            AccentMode = AccentColorMode.Fixed,
            FixedAccent = tag switch
            {
                "gold" => FixedAccentColor.Gold,
                "mint" => FixedAccentColor.Mint,
                "lilac" => FixedAccentColor.Lilac,
                "clay" => FixedAccentColor.Clay,
                _ => FixedAccentColor.Blush,
            },
            CustomAccentArgb = null,
        });
    }

    private void OnCustomAccentClicked(object sender, RoutedEventArgs e)
    {
        var argb = _settings.CustomAccentArgb ??
            AppearanceAccentPalette.ResolveFixed(_settings);
        _pendingCustomAccentArgb = argb;
        CustomAccentPicker.Color = ToColor(argb);
        FlyoutBase.ShowAttachedFlyout(CustomAccentButton);
    }

    private void OnCustomAccentColorChanged(
        ColorPicker sender,
        ColorChangedEventArgs args)
    {
        _pendingCustomAccentArgb = ToArgb(args.NewColor);
        ApplyCustomAccentPreview(_pendingCustomAccentArgb);
    }

    private void OnApplyCustomAccentClicked(object sender, RoutedEventArgs e)
    {
        if (_pendingCustomAccentArgb is uint custom)
        {
            Commit(_settings with
            {
                AccentMode = AccentColorMode.Fixed,
                CustomAccentArgb = custom,
            });
        }

        CustomAccentFlyout.Hide();
    }

    private void OnCustomAccentFlyoutClosed(object sender, object e)
    {
        _pendingCustomAccentArgb = _settings.CustomAccentArgb;
        ApplyCustomAccentPreview(_settings.CustomAccentArgb);
    }

    private void OnSerifTitlesToggled(object sender, RoutedEventArgs e)
    {
        if (!_uiLoading)
        {
            Commit(_settings with { UseSerifTitles = SerifTitlesToggle.IsOn });
        }
    }

    private void OnMotionModeSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_uiLoading ||
            MotionModeComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        Commit(_settings with
        {
            Motion = tag switch
            {
                "reduced" => AppearanceMotionMode.Reduced,
                "off" => AppearanceMotionMode.Off,
                _ => AppearanceMotionMode.Full,
            },
        });
    }

    private void OnDensitySelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_uiLoading ||
            DensityComboBox.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        Commit(_settings with
        {
            Density = tag switch
            {
                "compact" => InterfaceDensity.Compact,
                "comfortable" => InterfaceDensity.Comfortable,
                _ => InterfaceDensity.Standard,
            },
        });
    }

    private void Commit(
        AppearanceSettings settings,
        bool forceStaticBackgroundReload = false)
    {
        _settings = settings;
        AppearanceSettingsChanged?.Invoke(
            this,
            new AppearanceSettingsChangedEventArgs(
                settings,
                forceStaticBackgroundReload));
    }

    private void ApplyFixedAccentSelection(FixedAccentColor accent)
    {
        BlushAccentButton.IsChecked = accent == FixedAccentColor.Blush;
        GoldAccentButton.IsChecked = accent == FixedAccentColor.Gold;
        MintAccentButton.IsChecked = accent == FixedAccentColor.Mint;
        LilacAccentButton.IsChecked = accent == FixedAccentColor.Lilac;
        ClayAccentButton.IsChecked = accent == FixedAccentColor.Clay;
    }

    private void SetFixedAccentEnabled(bool enabled)
    {
        BlushAccentButton.IsEnabled = enabled;
        GoldAccentButton.IsEnabled = enabled;
        MintAccentButton.IsEnabled = enabled;
        LilacAccentButton.IsEnabled = enabled;
        ClayAccentButton.IsEnabled = enabled;
        CustomAccentButton.IsEnabled = enabled;
    }

    private void ApplyCapsuleStyleSelection(LauncherCapsuleStyle style)
    {
        OriginalCapsuleStyleButton.IsChecked = style == LauncherCapsuleStyle.Original;
        OceanCapsuleStyleButton.IsChecked = style == LauncherCapsuleStyle.Ocean;
        KleinCapsuleStyleButton.IsChecked = style == LauncherCapsuleStyle.Klein;
        UltravioletCapsuleStyleButton.IsChecked = style == LauncherCapsuleStyle.Ultraviolet;
        ChromeCapsuleStyleButton.IsChecked = style == LauncherCapsuleStyle.Chrome;
        PlusCapsuleStyleButton.IsChecked = style == LauncherCapsuleStyle.Plus;
    }

    private void ApplyBackgroundPresetSelection(string? selectedSource)
    {
        var source = selectedSource?.Trim();
        Preset1SelectionBorder.Visibility =
            string.IsNullOrWhiteSpace(source) ||
            string.Equals(
                source,
                AppearanceBackgroundPresets.Preset1Source,
                StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        Preset2SelectionBorder.Visibility =
            string.Equals(
                source,
                AppearanceBackgroundPresets.Preset2Source,
                StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ApplyCustomAccentPreview(uint? argb)
    {
        if (argb is uint value)
        {
            CustomAccentPreview.Background = new SolidColorBrush(ToColor(value));
            return;
        }

        CustomAccentPreview.Background =
            Application.Current.Resources["DerivedAccentBrush"] as Brush;
    }

    private static void SelectComboItem(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .First(item => string.Equals(
                item.Tag as string,
                tag,
                StringComparison.Ordinal));
    }

    private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < NarrowBreakpoint;
        ApplyGridState(ThemeChoiceGrid, narrow, 3);
        ApplyGridState(BackgroundPreviewGrid, narrow, 3);
    }

    private static void ApplyGridState(Grid grid, bool narrow, int itemCount)
    {
        for (var index = 0; index < itemCount; index++)
        {
            var child = grid.Children[index];
            child.SetValue(Grid.ColumnProperty, narrow ? 0 : index);
            child.SetValue(Grid.RowProperty, narrow ? index : 0);
            child.SetValue(Grid.ColumnSpanProperty, narrow ? grid.ColumnDefinitions.Count : 1);
        }
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    private static uint ToArgb(Color color) =>
        ((uint)color.A << 24) |
        ((uint)color.R << 16) |
        ((uint)color.G << 8) |
        color.B;
}

public sealed class AppearanceSettingsChangedEventArgs : EventArgs
{
    public AppearanceSettingsChangedEventArgs(
        AppearanceSettings settings,
        bool forceStaticBackgroundReload = false)
    {
        Settings = settings;
        ForceStaticBackgroundReload = forceStaticBackgroundReload;
    }

    public AppearanceSettings Settings { get; }

    public bool ForceStaticBackgroundReload { get; }
}
