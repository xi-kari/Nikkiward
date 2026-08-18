namespace Nikkiward.Controls;

public readonly record struct CardBorderGlowState(
    double EdgeProximity,
    double AngleDegrees,
    double GlowOpacity,
    double ColorOpacity);

public static class CardBorderGlowProjection
{
    private const double ColorSensitivityOffset = 20d;

    public static CardBorderGlowState Project(
        double width,
        double height,
        double pointerX,
        double pointerY,
        double edgeSensitivity)
    {
        if (!double.IsFinite(width) ||
            !double.IsFinite(height) ||
            !double.IsFinite(pointerX) ||
            !double.IsFinite(pointerY) ||
            width <= 0 ||
            height <= 0)
        {
            return default;
        }

        var centerX = width / 2d;
        var centerY = height / 2d;
        var deltaX = pointerX - centerX;
        var deltaY = pointerY - centerY;
        var scaleX = deltaX == 0d
            ? double.PositiveInfinity
            : centerX / Math.Abs(deltaX);
        var scaleY = deltaY == 0d
            ? double.PositiveInfinity
            : centerY / Math.Abs(deltaY);
        var edgeProximity = Math.Clamp(1d / Math.Min(scaleX, scaleY), 0d, 1d);
        var angle = deltaX == 0d && deltaY == 0d
            ? 0d
            : NormalizeDegrees((Math.Atan2(deltaY, deltaX) * 180d / Math.PI) + 90d);
        var sensitivity = Math.Clamp(edgeSensitivity, 0d, 95d);
        var colorSensitivity = Math.Clamp(
            sensitivity + ColorSensitivityOffset,
            0d,
            99d);

        return new CardBorderGlowState(
            edgeProximity,
            angle,
            ProjectOpacity(edgeProximity, sensitivity),
            ProjectOpacity(edgeProximity, colorSensitivity));
    }

    private static double ProjectOpacity(double proximity, double sensitivity)
    {
        var percent = proximity * 100d;
        return Math.Clamp(
            (percent - sensitivity) / (100d - sensitivity),
            0d,
            1d);
    }

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360d;
        return normalized < 0d ? normalized + 360d : normalized;
    }
}
