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
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Nikkiward.Features.Shell;
using Nikkiward.Models;
using Windows.UI;

namespace Nikkiward.Controls;

public sealed class CardBorderGlow : Grid
{
    private const double DefaultGlowRadius = 34d;
    private const double MaximumDepthOffset = 3d;
    private const double MinimumCanvasSize = 1d;

    private readonly Microsoft.UI.Xaml.Controls.Canvas _canvasHost;
    private readonly CanvasControl _canvas;
    private DispatcherQueueTimer? _animationTimer;
    private AppearanceMotionMode _motion = AppearanceMotionMode.Full;
    private CardBorderGlowState _pointerState;
    private double _renderGlowOpacity;
    private double _renderColorOpacity;
    private double _renderAngle = 110d;
    private double _pointerNormalizedX;
    private double _pointerNormalizedY;
    private long _lastFrameTimestamp;
    private long _introStartTimestamp;
    private bool _pointerInside;
    private bool _introActive;
    private bool _introPlayed;
    private bool _glowEnabled = true;
    private bool _decorationsEnabled = true;
    private bool _animationsEnabled = true;

    public CardBorderGlow()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        _canvasHost = new Microsoft.UI.Xaml.Controls.Canvas
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false,
        };
        _canvas = new CanvasControl
        {
            ClearColor = Colors.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        _canvas.Draw += OnDraw;
        _canvasHost.Children.Add(_canvas);
        Children.Add(_canvasHost);
        Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(_canvasHost, 100);

        AppearanceRuntimeValues.ApplyScaleTransition(this);
        AppearanceRuntimeValues.ApplyTranslationTransition(this, "MotionStateDuration");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        PointerEntered += OnPointerEntered;
        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
        PointerCanceled += OnPointerCanceled;
        PointerCaptureLost += OnPointerCaptureLost;
    }

    public double EdgeSensitivity { get; set; } = 30d;

    public double GlowCornerRadius { get; set; } = 16d;

    public double GlowIntensity { get; set; } = 0.9d;

    public double GlowRadius { get; set; } = DefaultGlowRadius;

    public bool IsIntroAnimationEnabled { get; set; } = true;

    public bool IsDirectPointerTrackingEnabled { get; set; }

    public bool IsLiftEnabled { get; set; } = true;

    public UIElement? DepthTarget
    {
        get => field;
        set
        {
            if (ReferenceEquals(field, value))
            {
                return;
            }

            if (field is not null)
            {
                field.Translation = Vector3.Zero;
            }

            field = value;
            if (field is not null)
            {
                AppearanceRuntimeValues.ApplyTranslationTransition(
                    field,
                    "MotionStateDuration");
            }
        }
    }

    public void SetGlowEnabled(bool enabled)
    {
        if (_glowEnabled == enabled)
        {
            return;
        }

        _glowEnabled = enabled;
        _pointerInside = false;
        _introActive = false;
        _renderGlowOpacity = 0d;
        _renderColorOpacity = 0d;
        _animationTimer?.Stop();
        ApplyLiftState();
        RefreshPlatformPolicy();
        if (IsLoaded)
        {
            _canvas.Invalidate();
        }
    }

    public void ApplyMotion(AppearanceMotionMode motion)
    {
        _motion = Enum.IsDefined(motion) ? motion : AppearanceMotionMode.Full;
        RefreshPlatformPolicy();
        ApplyLiftState();
        if (IsLoaded)
        {
            StartIntroIfEligible();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        GlassCapabilities.Current.TierChanged += OnGlassTierChanged;
        ActualThemeChanged += OnActualThemeChanged;
        _animationTimer ??= CreateAnimationTimer();
        RefreshPlatformPolicy();
        UpdateCanvasLayout(ActualWidth, ActualHeight);
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
        StartRendering();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) =>
        StartRendering();

    private void RefreshPlatformPolicy()
    {
        var signals = GlassCapabilities.Current.ReadSignals();
        _decorationsEnabled = _glowEnabled && !signals.HighContrast;
        _animationsEnabled =
            _motion != AppearanceMotionMode.Off &&
            signals.AnimationsEnabled &&
            !signals.EnergySaverOn &&
            !signals.RemoteSession &&
            !signals.WindowOccluded;
        if (_decorationsEnabled)
        {
            return;
        }

        _introActive = false;
        _renderGlowOpacity = 0d;
        _renderColorOpacity = 0d;
        _animationTimer?.Stop();
        _canvas.Invalidate();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        CenterPoint = new Vector3(
            (float)(args.NewSize.Width / 2d),
            (float)(args.NewSize.Height / 2d),
            0f);
        UpdateCanvasLayout(args.NewSize.Width, args.NewSize.Height);
        StartRendering();
    }

    private void UpdateCanvasLayout(double width, double height)
    {
        var extent = ResolveGlowRadius();
        _canvas.Width = Math.Max(MinimumCanvasSize, width + (extent * 2d));
        _canvas.Height = Math.Max(MinimumCanvasSize, height + (extent * 2d));
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(_canvas, -extent);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(_canvas, -extent);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (!_decorationsEnabled)
        {
            return;
        }

        _pointerInside = true;
        UpdatePointerState(args);
        ApplyLiftState();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!_decorationsEnabled)
        {
            return;
        }

        _pointerInside = true;
        UpdatePointerState(args);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs args) =>
        ResetPointerState();

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs args) =>
        ResetPointerState();

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args) =>
        ResetPointerState();

    private void UpdatePointerState(PointerRoutedEventArgs args)
    {
        if (!_decorationsEnabled)
        {
            return;
        }

        var point = args.GetCurrentPoint(this).Position;
        _pointerState = CardBorderGlowProjection.Project(
            ActualWidth,
            ActualHeight,
            point.X,
            point.Y,
            EdgeSensitivity);
        _pointerNormalizedX = Math.Clamp(
            ((point.X / Math.Max(1d, ActualWidth)) * 2d) - 1d,
            -1d,
            1d);
        _pointerNormalizedY = Math.Clamp(
            ((point.Y / Math.Max(1d, ActualHeight)) * 2d) - 1d,
            -1d,
            1d);
        ApplyLiftState();
        if (IsDirectPointerTrackingEnabled)
        {
            _introActive = false;
            _renderGlowOpacity = _pointerState.GlowOpacity;
            _renderColorOpacity = _pointerState.ColorOpacity;
            _renderAngle = _pointerState.AngleDegrees;
            _animationTimer?.Stop();
            _canvas.Invalidate();
            return;
        }

        StartRendering();
    }

    private void ResetPointerState()
    {
        _pointerInside = false;
        ApplyLiftState();
        if (IsDirectPointerTrackingEnabled)
        {
            _introActive = false;
            _renderGlowOpacity = 0d;
            _renderColorOpacity = 0d;
            _animationTimer?.Stop();
            _canvas.Invalidate();
            return;
        }

        StartRendering();
    }

    private void ResetVisualState()
    {
        _animationTimer?.Stop();
        _pointerState = default;
        _pointerInside = false;
        _pointerNormalizedX = 0d;
        _pointerNormalizedY = 0d;
        _introActive = false;
        _introPlayed = false;
        _introStartTimestamp = 0;
        _lastFrameTimestamp = 0;
        _renderGlowOpacity = 0d;
        _renderColorOpacity = 0d;
        _renderAngle = 110d;
        Scale = Vector3.One;
        Translation = Vector3.Zero;
        if (DepthTarget is not null)
        {
            DepthTarget.Translation = Vector3.Zero;
        }
    }

    private void ApplyLiftState()
    {
        if (!IsLiftEnabled || !_animationsEnabled || _motion == AppearanceMotionMode.Off)
        {
            Scale = Vector3.One;
            Translation = Vector3.Zero;
            if (DepthTarget is not null)
            {
                DepthTarget.Translation = Vector3.Zero;
            }
            return;
        }

        var scale = _pointerInside
            ? _motion == AppearanceMotionMode.Full ? 1.012f : 1.005f
            : 1f;
        var offset = _pointerInside && _motion == AppearanceMotionMode.Full ? -2f : 0f;
        var depth = _motion == AppearanceMotionMode.Full
            ? MaximumDepthOffset
            : MaximumDepthOffset * 0.4d;
        Scale = new Vector3(scale, scale, 1f);
        Translation = new Vector3(0f, offset, 0f);
        if (DepthTarget is not null)
        {
            DepthTarget.Translation = _pointerInside
                ? new Vector3(
                    (float)(-_pointerNormalizedX * depth),
                    (float)(-_pointerNormalizedY * depth * 0.72d),
                    0f)
                : Vector3.Zero;
        }
    }

    private void StartIntroIfEligible()
    {
        if (_introPlayed ||
            !IsIntroAnimationEnabled ||
            !_decorationsEnabled ||
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

        _lastFrameTimestamp = _lastFrameTimestamp == 0
            ? Stopwatch.GetTimestamp()
            : _lastFrameTimestamp;
        _canvas.Invalidate();
        if (!IsDirectPointerTrackingEnabled || _introActive)
        {
            _animationTimer?.Start();
        }
    }

    private void OnDraw(
        CanvasControl sender,
        CanvasDrawEventArgs args)
    {
        args.DrawingSession.Clear(Colors.Transparent);
        if (!_decorationsEnabled || ActualWidth <= 0d || ActualHeight <= 0d)
        {
            _animationTimer?.Stop();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        UpdateRenderState(now);
        if (_renderGlowOpacity > 0.001d || _renderColorOpacity > 0.001d)
        {
            DrawGlow(sender, args, _renderAngle);
        }

        if (!NeedsAnotherFrame())
        {
            _animationTimer?.Stop();
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
        if (!IsLoaded ||
            !_decorationsEnabled ||
            (IsDirectPointerTrackingEnabled && !_introActive))
        {
            sender.Stop();
            return;
        }

        _canvas.Invalidate();
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
            var envelope = Math.Sin(Math.PI * progress);
            _renderAngle = 110d + (355d * eased);
            _renderGlowOpacity = Math.Max(_renderGlowOpacity, envelope * 0.92d);
            _renderColorOpacity = Math.Max(_renderColorOpacity, envelope * 0.78d);
            if (progress < 1d)
            {
                return;
            }

            _introActive = false;
        }

        if (IsDirectPointerTrackingEnabled)
        {
            _renderGlowOpacity = _pointerInside ? _pointerState.GlowOpacity : 0d;
            _renderColorOpacity = _pointerInside ? _pointerState.ColorOpacity : 0d;
            _renderAngle = _pointerState.AngleDegrees;
            return;
        }

        var targetGlow = _pointerInside ? _pointerState.GlowOpacity : 0d;
        var targetColor = _pointerInside ? _pointerState.ColorOpacity : 0d;
        if (!_animationsEnabled)
        {
            _renderGlowOpacity = targetGlow;
            _renderColorOpacity = targetColor;
            _renderAngle = _pointerState.AngleDegrees;
            return;
        }

        var response = _pointerInside ? 0.07d : 0.24d;
        var blend = 1d - Math.Exp(-elapsedSeconds / response);
        _renderGlowOpacity += (targetGlow - _renderGlowOpacity) * blend;
        _renderColorOpacity += (targetColor - _renderColorOpacity) * blend;
        _renderAngle = InterpolateAngle(
            _renderAngle,
            _pointerState.AngleDegrees,
            1d - Math.Exp(-elapsedSeconds / 0.05d));
    }

    private bool NeedsAnotherFrame()
    {
        if (_introActive)
        {
            return true;
        }

        if (IsDirectPointerTrackingEnabled)
        {
            return false;
        }

        var targetGlow = _pointerInside ? _pointerState.GlowOpacity : 0d;
        var targetColor = _pointerInside ? _pointerState.ColorOpacity : 0d;
        return Math.Abs(_renderGlowOpacity - targetGlow) > 0.002d ||
            Math.Abs(_renderColorOpacity - targetColor) > 0.002d ||
            AngleDistance(_renderAngle, _pointerState.AngleDegrees) > 0.25d;
    }

    private void DrawGlow(
        ICanvasResourceCreator resourceCreator,
        CanvasDrawEventArgs args,
        double angle)
    {
        var extent = (float)ResolveGlowRadius();
        var width = (float)ActualWidth;
        var height = (float)ActualHeight;
        var cornerRadius = (float)Math.Clamp(
            GlowCornerRadius,
            0d,
            Math.Min(width, height) / 2d);
        using var border = CanvasGeometry.CreateRoundedRectangle(
            resourceCreator,
            extent,
            extent,
            width,
            height,
            cornerRadius,
            cornerRadius);

        var direction = DirectionFromAngle(angle);
        var edgePoint = FindEdgePoint(direction, width, height) + new Vector2(extent);
        var tangent = new Vector2(-direction.Y, direction.X);
        var locality = Math.Max(82f, Math.Min(width, height) * 0.8f);
        var intensity = Math.Clamp(GlowIntensity, 0.1d, 2d);
        var blush = ResolveColor("AccentBlushBrush", Color.FromArgb(255, 232, 160, 180));
        var gold = ResolveColor("AccentGoldBrush", Color.FromArgb(255, 217, 166, 87));
        var mint = ResolveColor("AccentMintBrush", Color.FromArgb(255, 143, 201, 184));
        var accent = ResolveColor("DerivedAccentBrush", blush);

        using (var fill = CreateRadialBrush(
                   resourceCreator,
                   edgePoint - (direction * 18f),
                   locality * 1.35f,
                   accent,
                   _renderColorOpacity * intensity * 0.16d))
        {
            args.DrawingSession.FillGeometry(border, fill);
        }

        using (var sheen = CreateRadialBrush(
                   resourceCreator,
                   edgePoint - (direction * 8f),
                   locality * 0.68f,
                   Colors.White,
                   _renderColorOpacity * intensity * 0.13d))
        {
            args.DrawingSession.FillGeometry(border, sheen);
        }

        DrawBand(args, border, resourceCreator, edgePoint, tangent, locality, blush, gold, mint, 26f, _renderGlowOpacity * intensity * 0.07d);
        DrawBand(args, border, resourceCreator, edgePoint, tangent, locality, blush, gold, mint, 13f, _renderGlowOpacity * intensity * 0.13d);
        DrawBand(args, border, resourceCreator, edgePoint, tangent, locality, blush, gold, mint, 5f, _renderGlowOpacity * intensity * 0.24d);
        DrawBand(args, border, resourceCreator, edgePoint, tangent, locality, blush, gold, mint, 1.5f, _renderGlowOpacity * intensity * 0.92d);
    }

    private static void DrawBand(
        CanvasDrawEventArgs args,
        CanvasGeometry border,
        ICanvasResourceCreator resourceCreator,
        Vector2 edgePoint,
        Vector2 tangent,
        float locality,
        Color blush,
        Color gold,
        Color mint,
        float strokeWidth,
        double opacity)
    {
        var offset = locality * 0.36f;
        DrawBandColor(args, border, resourceCreator, edgePoint - (tangent * offset), locality, blush, strokeWidth, opacity);
        DrawBandColor(args, border, resourceCreator, edgePoint, locality * 0.82f, gold, strokeWidth, opacity);
        DrawBandColor(args, border, resourceCreator, edgePoint + (tangent * offset), locality, mint, strokeWidth, opacity);
    }

    private static void DrawBandColor(
        CanvasDrawEventArgs args,
        CanvasGeometry border,
        ICanvasResourceCreator resourceCreator,
        Vector2 center,
        float radius,
        Color color,
        float strokeWidth,
        double opacity)
    {
        using var brush = CreateRadialBrush(
            resourceCreator,
            center,
            radius,
            color,
            opacity);
        args.DrawingSession.DrawGeometry(border, brush, strokeWidth);
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

    private static Vector2 DirectionFromAngle(double angle)
    {
        var radians = (angle - 90d) * Math.PI / 180d;
        return Vector2.Normalize(new Vector2(
            (float)Math.Cos(radians),
            (float)Math.Sin(radians)));
    }

    private static Vector2 FindEdgePoint(Vector2 direction, float width, float height)
    {
        var center = new Vector2(width / 2f, height / 2f);
        var scaleX = Math.Abs(direction.X) < 0.0001f
            ? float.PositiveInfinity
            : center.X / Math.Abs(direction.X);
        var scaleY = Math.Abs(direction.Y) < 0.0001f
            ? float.PositiveInfinity
            : center.Y / Math.Abs(direction.Y);
        return center + (direction * Math.Min(scaleX, scaleY));
    }

    private static double InterpolateAngle(double current, double target, double amount)
    {
        var delta = ((target - current + 540d) % 360d) - 180d;
        return (current + (delta * amount) + 360d) % 360d;
    }

    private static double AngleDistance(double first, double second) =>
        Math.Abs(((second - first + 540d) % 360d) - 180d);

    private double ResolveGlowRadius() =>
        Math.Clamp(GlowRadius, 8d, 64d);

    private static TimeSpan ResolveIntroDuration()
    {
        var artDuration = AppearanceRuntimeValues.ReadDuration("MotionArt");
        return artDuration > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(artDuration.TotalMilliseconds * 2.2d)
            : TimeSpan.FromMilliseconds(1050d);
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
