namespace Nikkiward.Services;

internal static class ScreenshotFrameHandoff
{
    public static void Draw<TFrame, TTarget, TSession>(
        TFrame frame, Func<TFrame, TTarget?> createTarget, Func<TTarget, TSession> openSession,
        Action<TFrame, TSession> draw, Func<TTarget, bool> deliver)
        where TFrame : IDisposable where TTarget : class, IDisposable where TSession : IDisposable
    {
        TTarget? target = null;
        try
        {
            using (frame)
            {
                target = createTarget(frame);
                if (target is null) return;
                using var session = openSession(target);
                draw(frame, session);
            }
            if (deliver(target)) target = null;
        }
        finally
        {
            target?.Dispose();
        }
    }
}
