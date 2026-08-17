using System.Numerics;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Nikkiward.Features.Launcher;
using Nikkiward.Models;
using Windows.Foundation;
using Windows.UI;

namespace Nikkiward.Controls;

public sealed class LauncherCapsuleVisual : Grid
{
    private const double FluidWidthRatio = 0.69;
    private const double MinimumTextPlateWidth = 142;

    private readonly CanvasAnimatedControl _canvas;
    private readonly Border _textPlate;
    private readonly Border _sheen;
    private RectangleClip? _compositionClip;
    private PixelShaderEffect? _shader;
    private LauncherCapsuleStyle _style = LauncherCapsuleStyle.Original;
    private LauncherNebulaFrameRenderer.NebulaPreset _preset =
        LauncherNebulaFrameRenderer.ResolvePreset(LauncherCapsuleStyle.Original);
    private AppearanceMotionMode _motion = AppearanceMotionMode.Full;

    public LauncherCapsuleVisual()
    {
        IsHitTestVisible = false;

        _canvas = new CanvasAnimatedControl
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsFixedTimeStep = false,
        };
        _canvas.CreateResources += OnCreateResources;
        _canvas.Draw += OnDraw;
        Children.Add(_canvas);

        _textPlate = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
            Background = CreateTextPlateBrush(),
            CornerRadius = new CornerRadius(28, 0, 0, 28),
        };
        Children.Add(_textPlate);

        _sheen = new Border
        {
            Background = CreateSheenBrush(),
            CornerRadius = new CornerRadius(28),
            IsHitTestVisible = false,
        };
        Children.Add(_sheen);

        SizeChanged += OnSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void ApplyStyle(LauncherCapsuleStyle style)
    {
        _style = Enum.IsDefined(style) ? style : LauncherCapsuleStyle.Original;
        _preset = LauncherNebulaFrameRenderer.ResolvePreset(_style);
        _canvas.Invalidate();
    }

    public void ApplyMotion(AppearanceMotionMode mode)
    {
        _motion = mode;
        if (IsLoaded)
        {
            _canvas.Paused = mode == AppearanceMotionMode.Off;
            _canvas.Invalidate();
        }
    }

    private void OnCreateResources(
        CanvasAnimatedControl sender,
        CanvasCreateResourcesEventArgs args)
    {
        _shader?.Dispose();
        var shaderPath = Path.Combine(
            AppContext.BaseDirectory,
            "Shaders",
            "LauncherNebula.bin");
        _shader = new PixelShaderEffect(File.ReadAllBytes(shaderPath));
    }

    private void OnDraw(
        ICanvasAnimatedControl sender,
        CanvasAnimatedDrawEventArgs args)
    {
        if (_shader is null || sender.Size.Width <= 0 || sender.Size.Height <= 0)
        {
            return;
        }

        var preset = _preset;
        var resolution = new Vector2((float)sender.Size.Width, (float)sender.Size.Height);
        var elapsed = (float)args.Timing.TotalTime.TotalSeconds;
        _shader.Properties["resolution"] = resolution;
        _shader.Properties["time"] = (preset.Seed * 0.73f) + (elapsed * preset.Speed);
        _shader.Properties["seed"] = preset.Seed;
        _shader.Properties["motion"] = 0f;
        _shader.Properties["pointer"] = new Vector2(0.72f, 0.45f);
        _shader.Properties["colorA"] = preset.ColorA;
        _shader.Properties["colorB"] = preset.ColorB;
        _shader.Properties["colorC"] = preset.ColorC;
        _shader.Properties["colorD"] = preset.ColorD;

        var radius = (float)Math.Max(0, sender.Size.Height / 2);
        using var roundedClip = CanvasGeometry.CreateRoundedRectangle(
            sender,
            0,
            0,
            resolution.X,
            resolution.Y,
            radius,
            radius);
        using var layer = args.DrawingSession.CreateLayer(1, roundedClip);
        args.DrawingSession.DrawImage(_shader);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var width = Math.Max(0, args.NewSize.Width);
        var radius = Math.Max(0, args.NewSize.Height / 2);
        _canvas.Width = width * FluidWidthRatio;
        _textPlate.Width = Math.Min(
            width,
            Math.Max(MinimumTextPlateWidth, width * 0.48));
        _textPlate.CornerRadius = new CornerRadius(radius, 0, 0, radius);
        _sheen.CornerRadius = new CornerRadius(radius);
        ApplyCanvasClip(radius);
        _canvas.Invalidate();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        ApplyCanvasClip(Math.Max(0, ActualHeight / 2));
        _canvas.Paused = _motion == AppearanceMotionMode.Off;
        _canvas.Invalidate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _canvas.Paused = true;
        ElementCompositionPreview.GetElementVisual(_canvas).Clip = null;
        _compositionClip?.Dispose();
        _compositionClip = null;
    }

    private void ApplyCanvasClip(double radius)
    {
        var visual = ElementCompositionPreview.GetElementVisual(_canvas);
        _compositionClip ??= visual.Compositor.CreateRectangleClip();
        var corner = new Vector2((float)radius, (float)radius);
        _compositionClip.Left = 0;
        _compositionClip.Top = 0;
        _compositionClip.Right = (float)_canvas.Width;
        _compositionClip.Bottom = (float)ActualHeight;
        _compositionClip.TopLeftRadius = corner;
        _compositionClip.TopRightRadius = corner;
        _compositionClip.BottomLeftRadius = corner;
        _compositionClip.BottomRightRadius = corner;
        visual.Clip = _compositionClip;
    }

    private static LinearGradientBrush CreateTextPlateBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        brush.GradientStops.Add(new GradientStop
        {
            Offset = 0,
            Color = Color.FromArgb(246, 250, 248, 242),
        });
        brush.GradientStops.Add(new GradientStop
        {
            Offset = 0.72,
            Color = Color.FromArgb(220, 250, 248, 242),
        });
        brush.GradientStops.Add(new GradientStop
        {
            Offset = 1,
            Color = Color.FromArgb(0, 250, 248, 242),
        });
        return brush;
    }

    private static LinearGradientBrush CreateSheenBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.15, 0),
            EndPoint = new Point(0.86, 1),
        };
        brush.GradientStops.Add(new GradientStop
        {
            Offset = 0,
            Color = Color.FromArgb(52, 255, 255, 255),
        });
        brush.GradientStops.Add(new GradientStop
        {
            Offset = 0.44,
            Color = Color.FromArgb(10, 255, 255, 255),
        });
        brush.GradientStops.Add(new GradientStop
        {
            Offset = 1,
            Color = Color.FromArgb(0, 255, 255, 255),
        });
        return brush;
    }
}
