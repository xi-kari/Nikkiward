using System.Numerics;

namespace Nikkiward.Features.Settings;

internal readonly record struct AuthorProfileDepthState(
    double RotationX,
    double RotationY,
    Vector3 AvatarTranslation,
    Vector3 ShineTranslation,
    Vector3 HeaderTranslation,
    Vector3 FooterTranslation)
{
    public static AuthorProfileDepthState Rest { get; } = new(
        0d,
        0d,
        Vector3.Zero,
        Vector3.Zero,
        Vector3.Zero,
        Vector3.Zero);
}

internal static class AuthorProfileDepthProjection
{
    private const double MaximumRotationX = 5.5d;
    private const double MaximumRotationY = 7.5d;

    public static AuthorProfileDepthState Project(
        double width,
        double height,
        double pointerX,
        double pointerY,
        double intensity)
    {
        var amount = Math.Clamp(intensity, 0d, 1d);
        if (amount <= 0d ||
            !double.IsFinite(width) ||
            !double.IsFinite(height) ||
            width <= 0d ||
            height <= 0d)
        {
            return AuthorProfileDepthState.Rest;
        }

        var normalizedX = (float)Math.Clamp(((pointerX / width) * 2d) - 1d, -1d, 1d);
        var normalizedY = (float)Math.Clamp(((pointerY / height) * 2d) - 1d, -1d, 1d);
        return new AuthorProfileDepthState(
            RotationX: normalizedY * MaximumRotationX * amount,
            RotationY: -normalizedX * MaximumRotationY * amount,
            AvatarTranslation: new Vector3(
                normalizedX * 1.4f * (float)amount,
                normalizedY * 0.9f * (float)amount,
                0f),
            ShineTranslation: new Vector3(
                -normalizedX * 1.4f * (float)amount,
                -normalizedY * 1f * (float)amount,
                0f),
            HeaderTranslation: new Vector3(
                -normalizedX * 3f * (float)amount,
                -normalizedY * 2.2f * (float)amount,
                0f),
            FooterTranslation: new Vector3(
                -normalizedX * 4f * (float)amount,
                -normalizedY * 3f * (float)amount,
                0f));
    }
}
