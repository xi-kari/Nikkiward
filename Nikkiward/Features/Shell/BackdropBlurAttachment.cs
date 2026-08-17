using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Nikkiward.Features.Shell;

internal sealed class BackdropBlurAttachment : IDisposable
{
    private const string SourceParameterName = "source";

    private readonly FrameworkElement _host;
    private readonly LayerVisual _root;
    private readonly SpriteVisual _sprite;
    private readonly CompositionRoundedRectangleGeometry _clipGeometry;
    private readonly CompositionEffectBrush _brush;
    private readonly DropShadow? _shadow;

    private BackdropBlurAttachment(
        FrameworkElement host,
        float sigma,
        float saturation,
        float brightness,
        float cornerRadius,
        int elevation)
    {
        _host = host;
        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        var graph = new ExposureEffect
        {
            Exposure = MathF.Log2(Math.Max(0.01f, brightness)),
            Source = new SaturationEffect
            {
                Saturation = saturation,
                Source = new GaussianBlurEffect
                {
                    Name = "blur",
                    BlurAmount = sigma,
                    BorderMode = EffectBorderMode.Hard,
                    Optimization = EffectOptimization.Speed,
                    Source = new CompositionEffectSourceParameter(SourceParameterName),
                },
            },
        };

        using var factory = compositor.CreateEffectFactory(graph, ["blur.BlurAmount"]);
        _brush = factory.CreateBrush();
        _brush.SetSourceParameter(SourceParameterName, compositor.CreateBackdropBrush());

        _clipGeometry = compositor.CreateRoundedRectangleGeometry();
        _clipGeometry.CornerRadius = new Vector2(cornerRadius);
        _sprite = compositor.CreateSpriteVisual();
        _sprite.Brush = _brush;
        _sprite.Clip = compositor.CreateGeometricClip(_clipGeometry);

        _root = compositor.CreateLayerVisual();
        _root.Children.InsertAtTop(_sprite);
        if (elevation is 1 or 2)
        {
            _shadow = compositor.CreateDropShadow();
            _shadow.BlurRadius = ReadFloatResource("GlassShadowBlur", 60f);
            _shadow.Offset = new Vector3(0, ReadFloatResource("GlassShadowOffsetY", 18f), 0);
            _shadow.Opacity = ReadFloatResource("GlassShadowOpacity", 0.44f);
            _root.Shadow = _shadow;
        }

        ElementCompositionPreview.SetElementChildVisual(host, _root);
        host.SizeChanged += OnHostSizeChanged;
        Resize((float)host.ActualWidth, (float)host.ActualHeight);
    }

    public bool HasCompositorShadow => _shadow is not null;

    public static BackdropBlurAttachment? TryCreate(
        FrameworkElement host,
        float sigma,
        float saturation,
        float brightness,
        float cornerRadius,
        int elevation)
    {
        if (!GlassCapabilities.Current.AllowsLiveBlur)
        {
            return null;
        }

        try
        {
            return new BackdropBlurAttachment(
                host,
                sigma,
                saturation,
                brightness,
                cornerRadius,
                elevation);
        }
        catch (Exception ex) when (ex is
            NotSupportedException or
            TypeLoadException or
            ArgumentException or
            COMException)
        {
            GlassCapabilities.Current.ReportBlurFailure();
            return null;
        }
    }

    public void SetCornerRadius(float radius) =>
        _clipGeometry.CornerRadius = new Vector2(Math.Max(0, radius));

    public void SetBlurAmount(float sigma) =>
        _brush.Properties.InsertScalar("blur.BlurAmount", Math.Max(0, sigma));

    public void Dispose()
    {
        _host.SizeChanged -= OnHostSizeChanged;
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        _shadow?.Dispose();
        _root.Dispose();
        _sprite.Dispose();
        _clipGeometry.Dispose();
        _brush.Dispose();
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs args) =>
        Resize((float)args.NewSize.Width, (float)args.NewSize.Height);

    private void Resize(float width, float height)
    {
        var size = new Vector2(Math.Max(0, width), Math.Max(0, height));
        _root.Size = size;
        _sprite.Size = size;
        _clipGeometry.Size = size;
    }

    private static float ReadFloatResource(string key, float fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true &&
        value is double resolved
            ? (float)resolved
            : fallback;
}
