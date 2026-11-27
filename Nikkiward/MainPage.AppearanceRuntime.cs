using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Nikkiward.Features.Background;
using Nikkiward.Features.Shell;
using Nikkiward.Models;

namespace Nikkiward;

public sealed partial class MainPage
{
    private bool _refreshingThemeResources;

    private void ApplyAppearanceSettings(AppearanceSettings settings)
    {
        ApplyHomeSkin(settings.HomeSkin);
        ApplyThemeMode(settings.ThemeMode);
        if (settings.AccentMode == AccentColorMode.Fixed)
        {
            ApplyFixedAccent(settings);
        }
        else
        {
            ApplyAccentForActualTheme();
        }

        ApplyTypographyPreference(settings.UseSerifTitles);
        ApplyDensityPreference(settings.Density);
        ApplyMotionPreference(settings.Motion);
        LauncherBackground.ConfigureAppearance(settings);
        ConfigureBackgroundCarousel(settings.Background);
        _hostedLauncherPage?.ApplyAppearanceSettings(settings);
    }

    private void ApplyFixedAccent(AppearanceSettings settings)
    {
        if (settings.AccentMode != AccentColorMode.Fixed)
        {
            return;
        }

        var argb = AppearanceAccentPalette.ResolveFixed(settings);
        _backdrop.ApplyAccentOverride(argb);
    }

    private static void ApplyTypographyPreference(bool useSerifTitles)
    {
        if (Application.Current?.Resources is not { } resources ||
            resources[useSerifTitles ? "DisplayFontFamilySerif" : "UIFontFamily"] is not FontFamily family)
        {
            return;
        }

        resources["DisplayFontFamily"] = family;
    }

    private static void ApplyDensityPreference(InterfaceDensity density)
    {
        if (Application.Current?.Resources is not { } resources)
        {
            return;
        }

        var (content, card, island, controlHeight) = density switch
        {
            InterfaceDensity.Compact =>
                (new Thickness(16, 12, 16, 12), new Thickness(12, 12, 12, 12), new Thickness(18, 14, 18, 14), 40d),
            InterfaceDensity.Comfortable =>
                (new Thickness(32, 24, 32, 24), new Thickness(20, 20, 20, 20), new Thickness(28, 24, 28, 24), 48d),
            _ =>
                (new Thickness(24, 16, 24, 16), new Thickness(16, 16, 16, 16), new Thickness(24, 20, 24, 20), 44d),
        };
        resources["ContentPaddingThickness"] = content;
        resources["CardPaddingThickness"] = card;
        resources["IslandPaddingThickness"] = island;
        resources["ControlHeight"] = controlHeight;
    }

    private void ApplyMotionPreference(AppearanceMotionMode mode)
    {
        if (Application.Current?.Resources is not { } resources)
        {
            return;
        }

        var projection = AppearanceProjector.ProjectMotion(mode, ReadSystemAnimationsEnabled());
        resources["MotionMicro"] = new Duration(TimeSpan.FromMilliseconds(
            projection.MicroDurationMilliseconds));
        resources["MotionStandard"] = new Duration(TimeSpan.FromMilliseconds(
            projection.StandardDurationMilliseconds));
        resources["MotionSurface"] = new Duration(TimeSpan.FromMilliseconds(
            projection.SurfaceDurationMilliseconds));
        resources["MotionArt"] = new Duration(TimeSpan.FromMilliseconds(
            projection.ArtDurationMilliseconds));
        resources["MotionStateDuration"] = projection.StateDurationMilliseconds;
        resources["MotionPanelOpen"] = projection.PanelOpenDurationMilliseconds;
        resources["MotionPanelClose"] = projection.PanelCloseDurationMilliseconds;
        resources["HoverScale"] = projection.HoverScale;
        resources["PressScale"] = projection.PressScale;
        resources["ButtonHoverScale"] = projection.ButtonHoverScale;
        resources["ParallaxAmplitude"] = projection.ParallaxAmplitude;
        RefreshThemeResourceBindings();
        AppearanceRuntimeValues.RefreshTransitions(RootGrid);
        if (projection.IsZero)
        {
            ResetInteractiveScales(RootGrid);
        }
    }

    private void RefreshThemeResourceBindings()
    {
        var target = RequestedTheme;
        _refreshingThemeResources = true;
        try
        {
            RequestedTheme = ActualTheme == ElementTheme.Dark
                ? ElementTheme.Light
                : ElementTheme.Dark;
            RequestedTheme = target;
        }
        finally
        {
            _refreshingThemeResources = false;
        }

        ApplyAccentForActualTheme();
    }

    private static void ResetInteractiveScales(DependencyObject root)
    {
        if (root is UIElement { ScaleTransition: not null } element)
        {
            element.Scale = System.Numerics.Vector3.One;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            ResetInteractiveScales(VisualTreeHelper.GetChild(root, index));
        }
    }

    private void DetachAppearanceRuntime()
    {
        if (_motionUiSettingsSubscribed &&
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _motionUiSettings.AnimationsEnabledChanged -= OnSystemAnimationsEnabledChanged;
            _motionUiSettingsSubscribed = false;
        }

        _backgroundCarouselTimer?.Stop();
        LauncherBackground.Detach();
    }

    private void ConfigureBackgroundCarousel(BackgroundArtSettings settings)
    {
        _backgroundCarouselTimer?.Stop();
        if (settings.MotionEnabled)
        {
            return;
        }

        var available = settings.CarouselSources
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!settings.CarouselEnabled || available.Length < 2)
        {
            if (!IsAvailableBackgroundSource(_currentBackgroundSource))
            {
                RestoreDefaultBackground();
            }

            return;
        }

        _backgroundCarouselTimer ??= new DispatcherTimer();
        _backgroundCarouselTimer.Tick -= OnBackgroundCarouselTick;
        _backgroundCarouselTimer.Tick += OnBackgroundCarouselTick;
        _backgroundCarouselTimer.Interval = TimeSpan.FromMinutes(
            settings.CarouselIntervalMinutes);
        _backgroundCarouselIndex = Array.FindIndex(
            available,
            source => string.Equals(
                source,
                _currentBackgroundSource,
                StringComparison.OrdinalIgnoreCase));
        _backgroundCarouselTimer.Start();
    }

    private async void OnBackgroundCarouselTick(object? sender, object e)
    {
        var sources = ViewModel.AppearanceSettings.Background.CarouselSources
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sources.Length < 2)
        {
            _backgroundCarouselTimer?.Stop();
            if (!IsAvailableBackgroundSource(_currentBackgroundSource))
            {
                RestoreDefaultBackground();
            }

            return;
        }

        _backgroundCarouselIndex = (_backgroundCarouselIndex + 1) % sources.Length;
        var source = sources[_backgroundCarouselIndex];
        SetCurrentBackgroundSource(source);
        var cancellationToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
        var analyzed = await AnalyzeBackdropAsync(source, cancellationToken);
        if (!analyzed)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await DisableBrokenCarouselSourceAsync(source);
            return;
        }

    }

    private async Task DisableBrokenCarouselSourceAsync(string failedSource)
    {
        var cancellationToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
        await _appearanceSaveGate.WaitAsync(cancellationToken);
        try
        {
            _backgroundCarouselTimer?.Stop();
            RestoreDefaultBackground();

            var appearance = ViewModel.AppearanceSettings;
            var remaining = appearance.Background.CarouselSources
                .Where(source => !string.Equals(
                    source,
                    failedSource,
                    StringComparison.OrdinalIgnoreCase))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            try
            {
                await ViewModel.SaveAppearanceSettingsAsync(
                    appearance with
                    {
                        Background = appearance.Background with
                        {
                            SelectedSource = null,
                            CarouselEnabled = false,
                            CarouselSources = remaining,
                        },
                    },
                    cancellationToken);
                ApplyAppearanceSettings(ViewModel.AppearanceSettings);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ViewModel.ReportUiError(
                    $"轮播状态保存失败：{ex.GetType().Name}: {ex.Message}");
            }
        }
        finally
        {
            _appearanceSaveGate.Release();
        }
    }

    private static bool IsAvailableBackgroundSource(string source) =>
        source.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase) ||
        File.Exists(source);

    private static bool ReadSystemAnimationsEnabled()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return true;
        }
    }

    private void SetCurrentBackgroundSource(string source)
    {
        ViewModel.ShowArtwork(source);
        if (string.Equals(
            source,
            _currentBackgroundSource,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var image = new BitmapImage(new Uri(source));
        LauncherBackground.Source = image;
        _currentBackgroundSource = source;
    }

    private static Windows.UI.Color ToColor(uint argb) => Windows.UI.Color.FromArgb(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));
}
