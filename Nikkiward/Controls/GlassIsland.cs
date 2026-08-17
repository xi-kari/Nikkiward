using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nikkiward.Features.Shell;

namespace Nikkiward.Controls;

public enum GlassIslandReadabilityRegion
{
    Global,
    Masthead,
    Notice,
    Cta,
    Pill,
}

public enum GlassSurface
{
    Pill,
    Island,
    Overlay,
}

public enum GlassLocalScrim
{
    None,
    Bottom,
    Top,
    Radial,
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
    private const string IslandLocalScrimPartName = "IslandLocalScrim";
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

    public static readonly DependencyProperty LocalScrimBrushProperty =
        DependencyProperty.Register(
            nameof(LocalScrimBrush),
            typeof(Brush),
            typeof(GlassIsland),
            new PropertyMetadata(null, OnLocalScrimBrushChanged));

    public static readonly DependencyProperty LocalScrimOpacityProperty =
        DependencyProperty.Register(
            nameof(LocalScrimOpacity),
            typeof(double),
            typeof(GlassIsland),
            new PropertyMetadata(1d, OnLocalScrimOpacityChanged));

    public static readonly DependencyProperty SurfaceProperty =
        DependencyProperty.Register(
            nameof(Surface),
            typeof(GlassSurface),
            typeof(GlassIsland),
            new PropertyMetadata(GlassSurface.Island, OnSurfacePropertyChanged));

    public static readonly DependencyProperty LocalScrimProperty =
        DependencyProperty.Register(
            nameof(LocalScrim),
            typeof(GlassLocalScrim),
            typeof(GlassIsland),
            new PropertyMetadata(GlassLocalScrim.None, OnLocalScrimChanged));

    public static readonly DependencyProperty ScrimExtentProperty =
        DependencyProperty.Register(
            nameof(ScrimExtent),
            typeof(double),
            typeof(GlassIsland),
            new PropertyMetadata(0.55d, OnScrimExtentChanged));

    public static readonly DependencyProperty TintProperty =
        DependencyProperty.Register(
            nameof(Tint),
            typeof(GlassTint),
            typeof(GlassIsland),
            new PropertyMetadata(GlassTint.Neutral));

    public static readonly DependencyProperty ReadabilityRegionProperty =
        DependencyProperty.Register(
            nameof(ReadabilityRegion),
            typeof(GlassIslandReadabilityRegion),
            typeof(GlassIsland),
            new PropertyMetadata(GlassIslandReadabilityRegion.Global));

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
    private Border? _islandLocalScrim;
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

    public Brush? LocalScrimBrush
    {
        get => GetValue(LocalScrimBrushProperty) as Brush;
        set => SetValue(LocalScrimBrushProperty, value);
    }

    public double LocalScrimOpacity
    {
        get => (double)GetValue(LocalScrimOpacityProperty);
        set => SetValue(LocalScrimOpacityProperty, value);
    }

    public GlassSurface Surface
    {
        get => (GlassSurface)GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public GlassLocalScrim LocalScrim
    {
        get => (GlassLocalScrim)GetValue(LocalScrimProperty);
        set => SetValue(LocalScrimProperty, value);
    }

    public double ScrimExtent
    {
        get => (double)GetValue(ScrimExtentProperty);
        set => SetValue(ScrimExtentProperty, value);
    }

    public GlassTint Tint
    {
        get => (GlassTint)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public GlassIslandReadabilityRegion ReadabilityRegion
    {
        get => (GlassIslandReadabilityRegion)GetValue(ReadabilityRegionProperty);
        set => SetValue(ReadabilityRegionProperty, value);
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
        if (_islandRoot is not null)
        {
            _islandRoot.SizeChanged -= OnIslandRootSizeChanged;
        }

        base.OnApplyTemplate();

        _islandRoot = GetTemplateChild(IslandRootPartName) as FrameworkElement;
        _islandBackdrop = GetTemplateChild(IslandBackdropPartName) as FrameworkElement;
        _islandLocalScrim = GetTemplateChild(IslandLocalScrimPartName) as Border;
        if (_islandRoot is not null)
        {
            _islandRoot.SizeChanged += OnIslandRootSizeChanged;
        }

        ApplySurface();
        ApplyElevation();
        ApplyEdgeHighlight();
        ApplyLocalScrimLayout();
        ApplyLocalScrim(LocalScrimOpacity, LocalScrimOpacity);
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

    private static void OnLocalScrimOpacityChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        var island = (GlassIsland)sender;
        var requested = (double)args.NewValue;
        var clamped = double.IsNaN(requested)
            ? 1d
            : requested <= 0
                ? 0d
                : Math.Clamp(requested, 0.60d, 1.40d);
        if (!requested.Equals(clamped))
        {
            island.LocalScrimOpacity = clamped;
            return;
        }

        island.ApplyLocalScrim((double)args.OldValue, clamped);
    }

    private static void OnLocalScrimBrushChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        var island = (GlassIsland)sender;
        island.ApplyLocalScrimLayout();
        island.ApplyLocalScrim(island.LocalScrimOpacity, island.LocalScrimOpacity);
    }

    private static void OnLocalScrimChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args) =>
        ((GlassIsland)sender).ApplyLocalScrimLayout();

    private static void OnSurfacePropertyChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args) =>
        ((GlassIsland)sender).ApplySurface();

    private static void OnScrimExtentChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args)
    {
        var island = (GlassIsland)sender;
        var requested = (double)args.NewValue;
        var clamped = double.IsNaN(requested) ? 0.55d : Math.Clamp(requested, 0.20d, 1d);
        if (!requested.Equals(clamped))
        {
            island.ScrimExtent = clamped;
            return;
        }

        island.ApplyLocalScrimLayout();
    }

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

    private void ApplyLocalScrim(double previous, double requested)
    {
        if (_islandLocalScrim is null)
        {
            return;
        }

        if (LocalScrimBrush is null)
        {
            _islandLocalScrim.Background = null;
            _islandLocalScrim.Opacity = 0;
            return;
        }

        var alphaScale = Math.Max(0.01, Math.Max(previous, requested));
        _islandLocalScrim.Background = CloneWithScaledAlpha(LocalScrimBrush, alphaScale);
        var from = (float)Math.Clamp(previous / alphaScale, 0, 1);
        var to = (float)Math.Clamp(requested / alphaScale, 0, 1);
        _islandLocalScrim.Opacity = to;
        AppearanceRuntimeValues.StartOpacityAnimation(
            _islandLocalScrim,
            from,
            to,
            "MotionStateDuration",
            "EaseGlass");
    }

    private static Brush CloneWithScaledAlpha(Brush source, double scale)
    {
        switch (source)
        {
            case LinearGradientBrush linear:
            {
                var clone = new LinearGradientBrush
                {
                    StartPoint = linear.StartPoint,
                    EndPoint = linear.EndPoint,
                    Opacity = linear.Opacity,
                };
                CopyStops(
                    linear.GradientStops,
                    clone.GradientStops.Add,
                    scale);
                return clone;
            }
            case RadialGradientBrush radial:
            {
                var clone = new RadialGradientBrush
                {
                    Center = radial.Center,
                    GradientOrigin = radial.GradientOrigin,
                    RadiusX = radial.RadiusX,
                    RadiusY = radial.RadiusY,
                    Opacity = radial.Opacity,
                };
                CopyStops(
                    radial.GradientStops,
                    clone.GradientStops.Add,
                    scale);
                return clone;
            }
            case SolidColorBrush solid:
                return new SolidColorBrush(ScaleAlpha(solid.Color, scale))
                {
                    Opacity = solid.Opacity,
                };
            default:
                return source;
        }
    }

    private static void CopyStops(
        IEnumerable<GradientStop> source,
        Action<GradientStop> add,
        double scale)
    {
        foreach (var stop in source)
        {
            add(new GradientStop
            {
                Offset = stop.Offset,
                Color = ScaleAlpha(stop.Color, scale),
            });
        }
    }

    private static void CopyStops(
        Windows.Foundation.Collections.IObservableVector<GradientStop> source,
        Windows.Foundation.Collections.IObservableVector<GradientStop> destination,
        double scale)
    {
        foreach (var stop in source)
        {
            destination.Add(new GradientStop
            {
                Offset = stop.Offset,
                Color = ScaleAlpha(stop.Color, scale),
            });
        }
    }

    private static Windows.UI.Color ScaleAlpha(Windows.UI.Color color, double scale) =>
        Windows.UI.Color.FromArgb(
            (byte)Math.Clamp(Math.Round(color.A * scale), 0, byte.MaxValue),
            color.R,
            color.G,
            color.B);

    private void OnIslandRootSizeChanged(object sender, SizeChangedEventArgs args) =>
        ApplyLocalScrimLayout();

    private void ApplyLocalScrimLayout()
    {
        if (_islandLocalScrim is null || _islandRoot is null)
        {
            return;
        }

        var visible = LocalScrimBrush is not null && LocalScrim != GlassLocalScrim.None;
        _islandLocalScrim.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            return;
        }

        if (LocalScrim == GlassLocalScrim.Radial)
        {
            _islandLocalScrim.ClearValue(FrameworkElement.HeightProperty);
            _islandLocalScrim.VerticalAlignment = VerticalAlignment.Stretch;
            return;
        }

        _islandLocalScrim.Height = Math.Max(0, _islandRoot.ActualHeight * ScrimExtent);
        _islandLocalScrim.VerticalAlignment = LocalScrim == GlassLocalScrim.Top
            ? VerticalAlignment.Top
            : VerticalAlignment.Bottom;
    }

    private static double ReadDoubleResource(string key, double fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true &&
        value is double resolved
            ? resolved
            : fallback;
}
