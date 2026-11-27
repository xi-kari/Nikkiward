using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nikkiward.Features.Shell;

namespace Nikkiward.Controls;

public enum GlassSurface
{
    Pill,
    Island,
    Overlay,
}

public enum GlassTint
{
    Neutral,
    Accent,
}

public sealed class GlassIsland : ContentControl
{
    private const string IslandRootPartName = "IslandRoot";
    private const string IslandBackdropPartName = "IslandBackdrop";
    private const string EdgeHighlightVisibleStateName = "EdgeHighlightVisible";
    private const string EdgeHighlightCollapsedStateName = "EdgeHighlightCollapsed";
    private const string SurfaceFlatStateName = "SurfaceFlat";
    private const string SurfaceBlurredStateName = "SurfaceBlurred";
    private const string SharedShadowResourceKey = "WarmThemeShadow";
    private const int MinElevation = 0;
    private const int MaxElevation = 5;
    private const int DefaultElevation = 2;

    private static readonly double[] ElevationDepths = [0d, 8d, 16d, 24d, 32d, 48d];

    private static readonly string?[] ElevationResourceKeys =
    [
        null,
        "ElevationCard",
        "ElevationIsland",
        "ElevationRail",
        "ElevationAction",
        "ElevationDialog",
    ];

    private static readonly CornerRadius DefaultIslandCornerRadius = new(16d);
    private static readonly Thickness DefaultIslandPadding = new(24d, 20d, 24d, 20d);
    private static ThemeShadow? s_fallbackShadow;

    public static readonly DependencyProperty IslandCornerRadiusProperty =
        DependencyProperty.Register(
            nameof(IslandCornerRadius),
            typeof(CornerRadius),
            typeof(GlassIsland),
            new PropertyMetadata(DefaultIslandCornerRadius, OnSurfacePropertyChanged));

    public static readonly DependencyProperty IslandPaddingProperty =
        DependencyProperty.Register(
            nameof(IslandPadding),
            typeof(Thickness),
            typeof(GlassIsland),
            new PropertyMetadata(DefaultIslandPadding));

    public static readonly DependencyProperty SurfaceProperty =
        DependencyProperty.Register(
            nameof(Surface),
            typeof(GlassSurface),
            typeof(GlassIsland),
            new PropertyMetadata(GlassSurface.Island, OnSurfacePropertyChanged));

    public static readonly DependencyProperty TintProperty =
        DependencyProperty.Register(
            nameof(Tint),
            typeof(GlassTint),
            typeof(GlassIsland),
            new PropertyMetadata(GlassTint.Neutral));

    public static readonly DependencyProperty ElevationProperty =
        DependencyProperty.Register(
            nameof(Elevation),
            typeof(int),
            typeof(GlassIsland),
            new PropertyMetadata(DefaultElevation, OnElevationChanged));

    public static readonly DependencyProperty ShowEdgeHighlightProperty =
        DependencyProperty.Register(
            nameof(ShowEdgeHighlight),
            typeof(bool),
            typeof(GlassIsland),
            new PropertyMetadata(true, OnShowEdgeHighlightChanged));

    private FrameworkElement? _islandRoot;
    private FrameworkElement? _islandBackdrop;
    private BackdropBlurAttachment? _blur;
    private bool _capabilitiesSubscribed;
    private bool _applyingSurface;

    public GlassIsland()
    {
        DefaultStyleKey = typeof(GlassIsland);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public CornerRadius IslandCornerRadius
    {
        get => (CornerRadius)GetValue(IslandCornerRadiusProperty);
        set => SetValue(IslandCornerRadiusProperty, value);
    }

    public Thickness IslandPadding
    {
        get => (Thickness)GetValue(IslandPaddingProperty);
        set => SetValue(IslandPaddingProperty, value);
    }

    public GlassSurface Surface
    {
        get => (GlassSurface)GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public GlassTint Tint
    {
        get => (GlassTint)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public int Elevation
    {
        get => (int)GetValue(ElevationProperty);
        set => SetValue(ElevationProperty, value);
    }

    public bool ShowEdgeHighlight
    {
        get => (bool)GetValue(ShowEdgeHighlightProperty);
        set => SetValue(ShowEdgeHighlightProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        _blur?.Dispose();
        _blur = null;

        base.OnApplyTemplate();

        _islandRoot = GetTemplateChild(IslandRootPartName) as FrameworkElement;
        _islandBackdrop = GetTemplateChild(IslandBackdropPartName) as FrameworkElement;

        ApplySurface();
        ApplyElevation();
        ApplyEdgeHighlight();
    }

    private static void OnElevationChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var island = (GlassIsland)sender;
        var requested = (int)args.NewValue;
        var clamped = Math.Clamp(requested, MinElevation, MaxElevation);
        if (clamped != requested)
        {
            island.Elevation = clamped;
            return;
        }

        island.ApplySurface();
        island.ApplyElevation();
    }

    private static void OnShowEdgeHighlightChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args) =>
        ((GlassIsland)sender).ApplyEdgeHighlight();

    private static void OnSurfacePropertyChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args) =>
        ((GlassIsland)sender).ApplySurface();

    private static Shadow ResolveShadow()
    {
        if (Application.Current?.Resources.TryGetValue(
                SharedShadowResourceKey,
                out var resource) == true &&
            resource is Shadow shared)
        {
            return shared;
        }

        return s_fallbackShadow ??= new ThemeShadow();
    }

    private void ApplyElevation()
    {
        if (_islandRoot is null)
        {
            return;
        }

        var depth = (float)ResolveElevationDepth(Elevation);
        _islandRoot.Translation = new Vector3(0f, 0f, depth);
        if (_blur?.HasCompositorShadow == true || depth <= 0f)
        {
            _islandRoot.ClearValue(UIElement.ShadowProperty);
        }
        else
        {
            _islandRoot.Shadow = ResolveShadow();
        }
    }

    private static double ResolveElevationDepth(int elevation)
    {
        var index = Math.Clamp(elevation, MinElevation, MaxElevation);
        var resourceKey = ElevationResourceKeys[index];
        if (resourceKey is not null &&
            Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true &&
            value is double depth)
        {
            return depth;
        }

        return ElevationDepths[index];
    }

    private void ApplyEdgeHighlight()
    {
        VisualStateManager.GoToState(
            this,
            ShowEdgeHighlight ? EdgeHighlightVisibleStateName : EdgeHighlightCollapsedStateName,
            false);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_capabilitiesSubscribed)
        {
            GlassCapabilities.Current.TierChanged += OnGlassTierChanged;
            _capabilitiesSubscribed = true;
        }

        ApplySurface();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_capabilitiesSubscribed)
        {
            GlassCapabilities.Current.TierChanged -= OnGlassTierChanged;
            _capabilitiesSubscribed = false;
        }

        _blur?.Dispose();
        _blur = null;
    }

    private void OnGlassTierChanged(object? sender, EventArgs e) => ApplySurface();

    private void ApplySurface()
    {
        if (_applyingSurface || _islandBackdrop is null)
        {
            return;
        }

        _applyingSurface = true;
        try
        {
            _blur?.Dispose();
            _blur = null;

            var intensity = Math.Clamp(GlassCapabilities.Current.GlassIntensity, 0, 1);
            var sigmaKey = Surface switch
            {
                GlassSurface.Pill => "GlassBlurSigmaPill",
                GlassSurface.Overlay => "GlassBlurSigmaOverlay",
                _ => "GlassBlurSigmaIsland",
            };
            if (intensity > 0)
            {
                var sigma = (float)(ReadDoubleResource(sigmaKey, 22) * intensity);
                var saturation = (float)(1 +
                    ((ReadDoubleResource("GlassSaturation", 1.22) - 1) * intensity));
                var brightness = (float)(1 +
                    ((ReadDoubleResource("GlassBrightness", 1.04) - 1) * intensity));
                _blur = BackdropBlurAttachment.TryCreate(
                    _islandBackdrop,
                    sigma,
                    saturation,
                    brightness,
                    (float)Math.Max(0, IslandCornerRadius.TopLeft),
                    Elevation);
            }

            VisualStateManager.GoToState(
                this,
                _blur is null ? SurfaceFlatStateName : SurfaceBlurredStateName,
                false);
            ApplyElevation();
        }
        finally
        {
            _applyingSurface = false;
        }
    }

    private static double ReadDoubleResource(string key, double fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true &&
        value is double resolved
            ? resolved
            : fallback;
}
