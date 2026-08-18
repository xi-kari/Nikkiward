using Nikkiward.Features.Settings;

internal static class AuthorProfileDepthProjectionTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("author card center remains neutral", CenterRemainsNeutral),
        ("author card corners project opposing depth layers", CornersProjectOpposingLayers),
        ("author card depth projection resets and clamps", ResetAndBoundsAreStable),
    ];

    private static Task CenterRemainsNeutral()
    {
        var state = AuthorProfileDepthProjection.Project(406d, 564d, 203d, 282d, 1d);
        AssertNear(0d, state.RotationX, "center rotation X");
        AssertNear(0d, state.RotationY, "center rotation Y");
        Assert(state.AvatarTranslation == System.Numerics.Vector3.Zero, "center avatar translation");
        AssertNear(0d, state.HeaderTranslation.Z, "header depth");
        AssertNear(0d, state.FooterTranslation.Z, "footer depth");
        AssertNear(0d, state.ShineTranslation.Z, "shine depth");
        return Task.CompletedTask;
    }

    private static Task CornersProjectOpposingLayers()
    {
        var topLeft = AuthorProfileDepthProjection.Project(406d, 564d, 0d, 0d, 1d);
        var bottomRight = AuthorProfileDepthProjection.Project(406d, 564d, 406d, 564d, 1d);

        AssertNear(-5.5d, topLeft.RotationX, "top-left rotation X");
        AssertNear(7.5d, topLeft.RotationY, "top-left rotation Y");
        AssertNear(5.5d, bottomRight.RotationX, "bottom-right rotation X");
        AssertNear(-7.5d, bottomRight.RotationY, "bottom-right rotation Y");
        Assert(topLeft.AvatarTranslation.X < 0f, "top-left avatar follows the pointer");
        Assert(topLeft.HeaderTranslation.X > 0f, "top-left header counters the pointer");
        Assert(bottomRight.AvatarTranslation.X > 0f, "bottom-right avatar follows the pointer");
        Assert(bottomRight.HeaderTranslation.X < 0f, "bottom-right header counters the pointer");
        return Task.CompletedTask;
    }

    private static Task ResetAndBoundsAreStable()
    {
        var inactive = AuthorProfileDepthProjection.Project(406d, 564d, 0d, 0d, 0d);
        var outside = AuthorProfileDepthProjection.Project(406d, 564d, 900d, -400d, 1d);
        var invalid = AuthorProfileDepthProjection.Project(0d, 564d, 0d, 0d, 1d);

        Assert(inactive == AuthorProfileDepthState.Rest, "inactive projection must reset");
        Assert(invalid == AuthorProfileDepthState.Rest, "invalid projection must reset");
        AssertNear(-5.5d, outside.RotationX, "outside rotation X clamp");
        AssertNear(-7.5d, outside.RotationY, "outside rotation Y clamp");
        return Task.CompletedTask;
    }

    private static void AssertNear(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.00001d)
        {
            throw new InvalidOperationException(
                $"{message}: expected '{expected:R}', actual '{actual:R}'.");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
