namespace Nikkiward.Features.Background;

public enum StillArtworkFitMode
{
    Fill,
    Contain,
}

public readonly record struct StillArtworkLayout(
    StillArtworkFitMode FitMode,
    double Width,
    double Height,
    double SourceRetention)
{
    public bool IsValid =>
        Width > 0d &&
        Height > 0d &&
        double.IsFinite(Width) &&
        double.IsFinite(Height);

    public bool UsesBoundedSurface => FitMode == StillArtworkFitMode.Contain;
}

public readonly record struct HolographicPointerState(
    bool IsValid,
    double NormalizedX,
    double NormalizedY,
    double FoilAngleDegrees,
    double GlarePosition);

public static class HolographicBackdropProjection
{
    public static StillArtworkLayout ProjectLayout(
        double sourceWidth,
        double sourceHeight,
        double viewportWidth,
        double viewportHeight)
    {
        if (!IsPositiveFinite(viewportWidth) || !IsPositiveFinite(viewportHeight))
        {
            return default;
        }

        if (!IsPositiveFinite(sourceWidth) || !IsPositiveFinite(sourceHeight))
        {
            return new StillArtworkLayout(
                StillArtworkFitMode.Fill,
                viewportWidth,
                viewportHeight,
                1d);
        }

        var scale = Math.Min(
            viewportWidth / sourceWidth,
            viewportHeight / sourceHeight);
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        var fillsViewport =
            Math.Abs(width - viewportWidth) <= 0.000001d &&
            Math.Abs(height - viewportHeight) <= 0.000001d;
        return new StillArtworkLayout(
            fillsViewport ? StillArtworkFitMode.Fill : StillArtworkFitMode.Contain,
            width,
            height,
            1d);
    }

    public static HolographicPointerState ProjectPointer(
        double normalizedX,
        double normalizedY)
    {
        if (!double.IsFinite(normalizedX) || !double.IsFinite(normalizedY))
        {
            return default;
        }

        var x = Math.Clamp(normalizedX, -1d, 1d);
        var y = Math.Clamp(normalizedY, -1d, 1d);
        return new HolographicPointerState(
            true,
            x,
            y,
            118d + (x * 22d) - (y * 14d),
            Math.Clamp(0.5d + (x * 0.27d) + (y * 0.13d), 0.12d, 0.88d));
    }

    private static bool IsPositiveFinite(double value) =>
        double.IsFinite(value) && value > 0d;
}
