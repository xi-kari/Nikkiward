using System.Diagnostics;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Nikkiward.Features.Background;
using Nikkiward.Features.Shell;
using Nikkiward.Models;
using Windows.UI;

namespace Nikkiward.Controls;

public sealed class HolographicBackdropOverlay : Grid
{
    private const double IdleStrength = 0.28d;
    private const double MinimumCanvasSize = 1d;

    private readonly CanvasControl _canvas;
    private DispatcherQueueTimer? _animationTimer;
    private AppearanceMotionMode _motion = AppearanceMotionMode.Full;
    private double _targetX;
    private double _targetY;
    private double _targetStrength = IdleStrength;
    private double _renderX;
    private double _renderY;
    private double _renderStrength = IdleStrength;
    private double _introSweep;
    private double _introEnvelope;
    private long _lastFrameTimestamp;
    private long _introStartTimestamp;
    private bool _pointerActive;
    private bool _introActive;
    private bool _introPlayed;
    private bool _materialEnabled = true;
    private bool _interactionEnabled = true;
    private bool _decorationsEnabled = true;
    private bool _animationsEnabled = true;

    public HolographicBackdropOverlay()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        IsHitTestVisible = false;
        _canvas = new CanvasControl
        {
            ClearColor = Colors.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        _canvas.Draw += OnDraw;
        Children.Add(_canvas);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    public double Intensity { get; set; } = 1.16d;

    public double MaterialCornerRadius { get; set; } = 20d;

    public bool IsIntroAnimationEnabled { get; set; } = true;

    public bool IsSurfaceShadingEnabled { get; set; } = true;

    public void SetMaterialEnabled(bool enabled)
    {
        if (_materialEnabled == enabled)
        {
            return;
        }

        _materialEnabled = enabled;
        _pointerActive = false;
        _introActive = false;
        _introEnvelope = 0d;
        _targetX = 0d;
        _targetY = 0d;
        _targetStrength = enabled ? IdleStrength : 0d;
        if (!enabled)
        {
            _renderX = 0d;
            _renderY = 0d;
            _renderStrength = 0d;
            _animationTimer?.Stop();
        }

        RefreshPlatformPolicy();
        if (enabled && IsLoaded)
        {
            StartIntroIfEligible();
        }
    }

    public void SetInteractionEnabled(bool enabled)
    {
        if (_interactionEnabled == enabled)
        {
            return;
        }

        _interactionEnabled = enabled;
        _pointerActive = false;
        _introActive = false;
        _introEnvelope = 0d;
        _targetX = 0d;
        _targetY = 0d;
        _targetStrength = _materialEnabled ? IdleStrength : 0d;
        _renderX = 0d;
        _renderY = 0d;
        if (!enabled)
        {
            _animationTimer?.Stop();
        }

        RefreshPlatformPolicy();
    }

    public void ApplyMotion(AppearanceMotionMode motion)
    {
        _motion = Enum.IsDefined(motion) ? motion : AppearanceMotionMode.Full;
        RefreshPlatformPolicy();
        if (IsLoaded)
        {
            StartIntroIfEligible();
        }
    }

    public void SetPointer(double normalizedX, double normalizedY)
    {
        if (!_materialEnabled || !_interactionEnabled)
        {
            return;
        }

        var state = HolographicBackdropProjection.ProjectPointer(
            normalizedX,
            normalizedY);
        if (!state.IsValid)
        {
            ResetPointer();
            return;
        }

        _pointerActive = true;
        _introActive = false;
        _introEnvelope = 0d;
        _targetX = state.NormalizedX;
        _targetY = state.NormalizedY;
        _targetStrength = 1d;
        if (!_animationsEnabled)
        {
            ApplyStaticState();
        }

        StartRendering();
    }

    public void ResetPointer()
    {
        _pointerActive = false;
        _targetX = 0d;
        _targetY = 0d;
        _targetStrength = _materialEnabled ? IdleStrength : 0d;
        if (!_animationsEnabled)
        {
            ApplyStaticState();
        }

        if (_materialEnabled && _interactionEnabled)
        {
            StartRendering();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        GlassCapabilities.Current.TierChanged += OnGlassTierChanged;
        ActualThemeChanged += OnActualThemeChanged;
        _animationTimer ??= CreateAnimationTimer();
        _targetStrength = _materialEnabled ? IdleStrength : 0d;
        _renderStrength = _targetStrength;
        UpdateCanvasLayout(ActualWidth, ActualHeight);
        RefreshPlatformPolicy();
        StartIntroIfEligible();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        GlassCapabilities.Current.TierChanged -= OnGlassTierChanged;
        ActualThemeChanged -= OnActualThemeChanged;
        ResetVisualState();
        if (_animationTimer is not null)
        {
            _animationTimer.Tick -= OnAnimationTick;
            _animationTimer = null;
        }
    }

    private void OnGlassTierChanged(object? sender, EventArgs args)
    {
        RefreshPlatformPolicy();
        StartIntroIfEligible();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) =>
        StartRendering();

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        UpdateCanvasLayout(args.NewSize.Width, args.NewSize.Height);
        StartRendering();
    }

    private void UpdateCanvasLayout(double width, double height)
    {
        _canvas.Width = Math.Max(MinimumCanvasSize, width);
        _canvas.Height = Math.Max(MinimumCanvasSize, height);
    }

    private void RefreshPlatformPolicy()
    {
        var signals = GlassCapabilities.Current.ReadSignals();
        _decorationsEnabled = _materialEnabled && !signals.HighContrast;
        _animationsEnabled =
            _interactionEnabled &&
            _motion == AppearanceMotionMode.Full &&
            signals.AnimationsEnabled &&
            !signals.EnergySaverOn &&
            !signals.RemoteSession &&
            !signals.WindowOccluded;
        if (!_decorationsEnabled)
        {
            _introActive = false;
            _introEnvelope = 0d;
            _renderStrength = 0d;
            _animationTimer?.Stop();
            _canvas.Invalidate();
            return;
        }

        if (!_animationsEnabled)
        {
            _introActive = false;
            _introEnvelope = 0d;
            ApplyStaticState();
        }

        StartRendering();
    }

    private void ApplyStaticState()
    {
        _renderX = 0d;
        _renderY = 0d;
        _renderStrength = _motion == AppearanceMotionMode.Off
            ? 0.14d
            : 0.22d;
    }

    private void ResetVisualState()
    {
        _animationTimer?.Stop();
        _pointerActive = false;
        _introActive = false;
        _introPlayed = false;
        _targetX = 0d;
        _targetY = 0d;
        _targetStrength = 0d;
        _renderX = 0d;
        _renderY = 0d;
        _renderStrength = 0d;
        _introSweep = 0d;
        _introEnvelope = 0d;
        _lastFrameTimestamp = 0;
        _introStartTimestamp = 0;
        Scale = Vector3.One;
        Translation = Vector3.Zero;
    }

    private void StartIntroIfEligible()
    {
        if (!IsIntroAnimationEnabled ||
            _introPlayed ||
            !_decorationsEnabled ||
            !_interactionEnabled ||
            !_animationsEnabled ||
            _motion != AppearanceMotionMode.Full)
        {
            return;
        }

        _introPlayed = true;
        _introActive = true;
        _introStartTimestamp = Stopwatch.GetTimestamp();
        _lastFrameTimestamp = _introStartTimestamp;
        StartRendering();
    }

    private void StartRendering()
    {
        if (!IsLoaded || !_decorationsEnabled)
        {
            return;
        }

        if (_lastFrameTimestamp == 0)
        {
            _lastFrameTimestamp = Stopwatch.GetTimestamp();
        }

        _canvas.Invalidate();
        if (_animationsEnabled && NeedsAnotherFrame())
        {
            _animationTimer?.Start();
        }
    }

    private DispatcherQueueTimer CreateAnimationTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16d);
        timer.IsRepeating = true;
        timer.Tick += OnAnimationTick;
        return timer;
    }

    private void OnAnimationTick(DispatcherQueueTimer sender, object args)
    {
        if (!IsLoaded || !_decorationsEnabled)
        {
            sender.Stop();
            return;
        }

        _canvas.Invalidate();
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        args.DrawingSession.Clear(Colors.Transparent);
        if (!_decorationsEnabled || sender.Size.Width <= 0 || sender.Size.Height <= 0)
        {
            _animationTimer?.Stop();
            return;
        }

        UpdateRenderState(Stopwatch.GetTimestamp());
        DrawMaterial(sender, args);
        if (!NeedsAnotherFrame())
        {
            _animationTimer?.Stop();
        }
    }

    private void UpdateRenderState(long now)
    {
        var elapsedSeconds = _lastFrameTimestamp == 0
            ? 0d
            : Math.Clamp(
                (now - _lastFrameTimestamp) / (double)Stopwatch.Frequency,
                0d,
                0.05d);
        _lastFrameTimestamp = now;

        if (_introActive)
        {
            var duration = ResolveIntroDuration().TotalSeconds;
            var progress = Math.Clamp(
                (now - _introStartTimestamp) / (Stopwatch.Frequency * duration),
                0d,
                1d);
            var eased = 1d - Math.Pow(1d - progress, 3d);
            _introSweep = -1d + (2d * eased);
            _introEnvelope = Math.Sin(Math.PI * progress);
            if (progress >= 1d)
            {
                _introActive = false;
                _introEnvelope = 0d;
            }
        }

        if (!_animationsEnabled)
        {
            return;
        }

        var response = _pointerActive ? 0.032d : 0.12d;
        var blend = 1d - Math.Exp(-elapsedSeconds / response);
        _renderX += (_targetX - _renderX) * blend;
        _renderY += (_targetY - _renderY) * blend;
        _renderStrength += (_targetStrength - _renderStrength) * blend;
    }

    private bool NeedsAnotherFrame() =>
        _introActive ||
        Math.Abs(_renderX - _targetX) > 0.002d ||
        Math.Abs(_renderY - _targetY) > 0.002d ||
        Math.Abs(_renderStrength - _targetStrength) > 0.002d;

    private void DrawMaterial(
        ICanvasResourceCreator resourceCreator,
        CanvasDrawEventArgs args)
    {
        var width = (float)ActualWidth;
        var height = (float)ActualHeight;
        if (width <= 0f || height <= 0f)
        {
            return;
        }

        var radius = (float)Math.Clamp(
            MaterialCornerRadius,
            0d,
            Math.Min(width, height) / 2d);
        using var surface = CanvasGeometry.CreateRoundedRectangle(
            resourceCreator,
            0f,
            0f,
            width,
            height,
            radius,
            radius);
        using var layer = args.DrawingSession.CreateLayer(1f, surface);

        var pointerX = _introActive ? _introSweep : _renderX;
        var pointerY = _introActive ? -0.18d : _renderY;
        var strength = Math.Clamp(
            Math.Max(_renderStrength, _introEnvelope * 0.96d),
            0d,
            1d);
        var projection = HolographicBackdropProjection.ProjectPointer(
            pointerX,
            pointerY);
        var intensity = Math.Clamp(Intensity, 0d, 1.5d);
        var blush = ResolveColor(
            "AccentBlushBrush",
            Color.FromArgb(255, 232, 160, 180));
        var gold = ResolveColor(
            "AccentGoldBrush",
            Color.FromArgb(255, 217, 166, 87));
        var mint = ResolveColor(
            "AccentMintBrush",
            Color.FromArgb(255, 143, 201, 184));
        var lilac = ResolveColor(
            "AccentLilacBrush",
            Color.FromArgb(255, 185, 166, 218));

        DrawFoil(
            resourceCreator,
            args,
            surface,
            projection,
            width,
            height,
            strength * intensity,
            blush,
            gold,
            mint,
            lilac);
        DrawRaster(
            args,
            width,
            height,
            projection,
            strength * intensity,
            blush,
            gold,
            mint,
            lilac);
        DrawGlare(
            resourceCreator,
            args,
            surface,
            projection,
            width,
            height,
            strength * intensity,
            gold,
            mint);
        DrawSpecular(
            resourceCreator,
            args,
            surface,
            projection,
            width,
            height,
            strength * intensity,
            lilac);
        if (IsSurfaceShadingEnabled)
        {
            DrawClearcoat(
                resourceCreator,
                args,
                surface,
                projection,
                width,
                height,
                strength * intensity);
        }
        DrawBevel(
            resourceCreator,
            args,
            surface,
            projection,
            width,
            height,
            radius,
            strength * intensity);
    }

    private static void DrawFoil(
        ICanvasResourceCreator resourceCreator,
        CanvasDrawEventArgs args,
        CanvasGeometry surface,
        HolographicPointerState projection,
        float width,
        float height,
        double strength,
        Color blush,
        Color gold,
        Color mint,
        Color lilac)
    {
        var direction = DirectionFromAngle(projection.FoilAngleDegrees);
        var tangent = new Vector2(-direction.Y, direction.X);
        var diagonal = MathF.Sqrt((width * width) + (height * height));
        var center = new Vector2(width / 2f, height / 2f) +
            (tangent * (float)(projection.NormalizedY * diagonal * 0.08d));
        var halfLength = diagonal * 0.66f;
        var stops = new CanvasGradientStop[]
        {
            Stop(0f, Colors.Transparent),
            Stop(0.12f, WithOpacity(mint, strength * 0.030d)),
            Stop(0.28f, WithOpacity(lilac, strength * 0.090d)),
            Stop(0.44f, WithOpacity(blush, strength * 0.120d)),
            Stop(0.57f, WithOpacity(gold, strength * 0.145d)),
            Stop(0.72f, WithOpacity(mint, strength * 0.110d)),
            Stop(0.88f, WithOpacity(lilac, strength * 0.050d)),
            Stop(1f, Colors.Transparent),
        };
        using var brush = new CanvasLinearGradientBrush(resourceCreator, stops)
        {
            StartPoint = center - (direction * halfLength),
            EndPoint = center + (direction * halfLength),
        };
        args.DrawingSession.FillGeometry(surface, brush);
    }

    private static void DrawRaster(
        CanvasDrawEventArgs args,
        float width,
        float height,
        HolographicPointerState projection,
        double strength,
        Color blush,
        Color gold,
        Color mint,
        Color lilac)
    {
        const float spacing = 6f;
        var phase = (float)((projection.NormalizedX + 1d) * spacing * 0.5d);
        var lean = (float)(0.012d + (projection.NormalizedX * 0.008d));
        var shift = height * lean;
        var start = -MathF.Abs(shift) - spacing + phase;
        var colors = new[] { mint, lilac, gold, blush };
        var index = 0;
        for (var x = start; x <= width + MathF.Abs(shift) + spacing; x += spacing)
        {
            var shimmer = 0.72d +
                (0.28d * Math.Sin((index * 0.83d) + (projection.NormalizedX * 2.4d)));
            var opacity = (0.014d + (strength * 0.035d)) * shimmer;
            args.DrawingSession.DrawLine(
                new Vector2(x - (shift / 2f), 0f),
                new Vector2(x + (shift / 2f), height),
                WithOpacity(colors[index % colors.Length], opacity),
                0.8f);
            args.DrawingSession.DrawLine(
                new Vector2(x - (shift / 2f) + 1.6f, 0f),
                new Vector2(x + (shift / 2f) + 1.6f, height),
                WithOpacity(
                    Colors.Black,
                    (0.008d + (strength * 0.020d)) * shimmer),
                0.55f);
            index++;
        }
    }

    private static void DrawGlare(
        ICanvasResourceCreator resourceCreator,
        CanvasDrawEventArgs args,
        CanvasGeometry surface,
        HolographicPointerState projection,
        float width,
        float height,
        double strength,
        Color gold,
        Color mint)
    {
        var centerStop = (float)projection.GlarePosition;
        var direction = DirectionFromAngle(126d);
        var center = new Vector2(width / 2f, height / 2f);
        var diagonal = MathF.Sqrt((width * width) + (height * height));
        var halfLength = diagonal * 0.66f;
        var stops = new CanvasGradientStop[]
        {
            Stop(0f, Colors.Transparent),
            Stop(centerStop - 0.10f, Colors.Transparent),
            Stop(centerStop - 0.055f, WithOpacity(gold, strength * 0.050d)),
            Stop(centerStop - 0.018f, WithOpacity(Colors.White, strength * 0.135d)),
            Stop(centerStop, WithOpacity(Colors.White, strength * 0.310d)),
            Stop(centerStop + 0.025f, WithOpacity(Colors.White, strength * 0.155d)),
            Stop(centerStop + 0.07f, WithOpacity(mint, strength * 0.055d)),
            Stop(centerStop + 0.12f, Colors.Transparent),
            Stop(1f, Colors.Transparent),
        };
        using var brush = new CanvasLinearGradientBrush(resourceCreator, stops)
        {
            StartPoint = center - (direction * halfLength),
            EndPoint = center + (direction * halfLength),
        };
        args.DrawingSession.FillGeometry(surface, brush);
    }

    private static void DrawSpecular(
        ICanvasResourceCreator resourceCreator,
        CanvasDrawEventArgs args,
        CanvasGeometry surface,
        HolographicPointerState projection,
        float width,
        float height,
        double strength,
        Color accent)
    {
        var center = new Vector2(
            (float)((projection.NormalizedX + 1d) * 0.5d * width),
            (float)((projection.NormalizedY + 1d) * 0.5d * height));
        var radius = Math.Max(100f, Math.Min(width, height) * 0.62f);
        using (var color = CreateRadialBrush(
                   resourceCreator,
                   center,
                   radius * 1.15f,
                   accent,
                   strength * 0.075d))
        {
            args.DrawingSession.FillGeometry(surface, color);
        }

        using var white = CreateRadialBrush(
            resourceCreator,
            center,
            radius,
            Colors.White,
            strength * 0.145d);
        args.DrawingSession.FillGeometry(surface, white);
    }

    private static void DrawClearcoat(
        ICanvasResourceCreator resourceCreator,
        CanvasDrawEventArgs args,
        CanvasGeometry surface,
        HolographicPointerState projection,
        float width,
        float height,
        double strength)
    {
        var direction = ResolveLightDirection(projection);
        var center = new Vector2(width / 2f, height / 2f);
        var halfLength = MathF.Sqrt((width * width) + (height * height)) * 0.58f;
        var stops = new CanvasGradientStop[]
        {
            Stop(0f, WithOpacity(Colors.Black, strength * 0.070d)),
            Stop(0.34f, WithOpacity(Colors.Black, strength * 0.020d)),
            Stop(0.52f, Colors.Transparent),
            Stop(0.78f, WithOpacity(Colors.White, strength * 0.035d)),
            Stop(1f, WithOpacity(Colors.White, strength * 0.095d)),
        };
        using var brush = new CanvasLinearGradientBrush(resourceCreator, stops)
        {
            StartPoint = center - (direction * halfLength),
            EndPoint = center + (direction * halfLength),
        };
        args.DrawingSession.FillGeometry(surface, brush);
    }

    private static void DrawBevel(
        ICanvasResourceCreator resourceCreator,
        CanvasDrawEventArgs args,
        CanvasGeometry surface,
        HolographicPointerState projection,
        float width,
        float height,
        float radius,
        double strength)
    {
        var direction = ResolveLightDirection(projection);
        var center = new Vector2(width / 2f, height / 2f);
        var halfLength = MathF.Sqrt((width * width) + (height * height)) * 0.58f;
        var brightOpacity = Math.Clamp(0.24d + (strength * 0.48d), 0d, 0.82d);
        var darkOpacity = Math.Clamp(0.16d + (strength * 0.28d), 0d, 0.54d);
        var stops = new CanvasGradientStop[]
        {
            Stop(0f, WithOpacity(Colors.Black, darkOpacity)),
            Stop(0.28f, WithOpacity(Colors.Black, darkOpacity * 0.42d)),
            Stop(0.48f, Colors.Transparent),
            Stop(0.70f, WithOpacity(Colors.White, brightOpacity * 0.38d)),
            Stop(1f, WithOpacity(Colors.White, brightOpacity)),
        };
        using (var edgeBrush = new CanvasLinearGradientBrush(resourceCreator, stops)
               {
                   StartPoint = center - (direction * halfLength),
                   EndPoint = center + (direction * halfLength),
               })
        {
            args.DrawingSession.DrawGeometry(surface, edgeBrush, 3.2f);
        }

        var inset = MathF.Min(2.6f, MathF.Min(width, height) * 0.08f);
        if (width <= inset * 2f || height <= inset * 2f)
        {
            return;
        }

        using var inner = CanvasGeometry.CreateRoundedRectangle(
            resourceCreator,
            inset,
            inset,
            width - (inset * 2f),
            height - (inset * 2f),
            Math.Max(0f, radius - inset),
            Math.Max(0f, radius - inset));
        using (var neutralBrush = new CanvasSolidColorBrush(
                   resourceCreator,
                   WithOpacity(
                       Colors.White,
                       Math.Clamp(0.065d + (strength * 0.085d), 0d, 0.19d))))
        {
            args.DrawingSession.DrawGeometry(inner, neutralBrush, 0.9f);
        }

        using var innerBrush = new CanvasLinearGradientBrush(resourceCreator, stops)
        {
            StartPoint = center + (direction * halfLength),
            EndPoint = center - (direction * halfLength),
            Opacity = 0.34f,
        };
        args.DrawingSession.DrawGeometry(inner, innerBrush, 1.15f);
    }

    private static CanvasRadialGradientBrush CreateRadialBrush(
        ICanvasResourceCreator resourceCreator,
        Vector2 center,
        float radius,
        Color color,
        double opacity) =>
        new(
            resourceCreator,
            WithOpacity(color, opacity),
            Color.FromArgb(0, color.R, color.G, color.B))
        {
            Center = center,
            RadiusX = Math.Max(1f, radius),
            RadiusY = Math.Max(1f, radius),
        };

    private static CanvasGradientStop Stop(float position, Color color) =>
        new()
        {
            Position = position,
            Color = color,
        };

    private static Vector2 DirectionFromAngle(double angle)
    {
        var radians = (angle - 90d) * Math.PI / 180d;
        return Vector2.Normalize(new Vector2(
            (float)Math.Cos(radians),
            (float)Math.Sin(radians)));
    }

    private static Vector2 ResolveLightDirection(HolographicPointerState projection)
    {
        var direction = new Vector2(
            (float)projection.NormalizedX,
            (float)projection.NormalizedY);
        return direction.LengthSquared() > 0.01f
            ? Vector2.Normalize(direction)
            : Vector2.Normalize(new Vector2(-0.55f, -0.84f));
    }

    private static TimeSpan ResolveIntroDuration()
    {
        var artDuration = AppearanceRuntimeValues.ReadDuration("MotionArt");
        return artDuration > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(artDuration.TotalMilliseconds * 2.4d)
            : TimeSpan.FromMilliseconds(1150d);
    }

    private static Color ResolveColor(string resourceKey, Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true)
        {
            if (value is SolidColorBrush brush)
            {
                return brush.Color;
            }

            if (value is Color color)
            {
                return color;
            }
        }

        return fallback;
    }

    private static Color WithOpacity(Color color, double opacity) =>
        Color.FromArgb(
            (byte)Math.Clamp(Math.Round(255d * opacity), 0d, 255d),
            color.R,
            color.G,
            color.B);
}
