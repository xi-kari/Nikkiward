namespace Nikkiward.Models;

public enum LaunchState
{
    NotInstalled,
    Ready,
    PreparingBackend,
    Launching,
    Running,
    Returning,
    Exited,
    Failed,
}
