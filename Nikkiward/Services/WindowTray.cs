using Nikkiward.Models;

namespace Nikkiward.Services;

internal sealed class WindowTray(Action<bool> setEnabled, Action<string> reportError)
{
    internal CloseWindowBehavior CloseBehavior { get; private set; } = CloseWindowBehavior.Exit;

    internal void Open(CloseWindowBehavior behavior, Action activate)
    {
        ApplyCloseBehavior(behavior, visible: false);
        activate();
    }

    internal void ApplyCloseBehavior(CloseWindowBehavior behavior, bool visible)
    {
        CloseBehavior = behavior;
        if (visible || behavior == CloseWindowBehavior.MinimizeToTray)
        {
            TrySetEnabled(behavior == CloseWindowBehavior.MinimizeToTray);
        }
    }

    internal bool TrySetEnabled(bool enabled)
    {
        try
        {
            setEnabled(enabled);
            return true;
        }
        catch (InvalidOperationException ex) when (enabled && ex is not ObjectDisposedException)
        {
            CloseBehavior = CloseWindowBehavior.Exit;
            reportError($"托盘图标注册失败，关闭窗口将退出应用。{ex.Message}");
            return false;
        }
    }
}
