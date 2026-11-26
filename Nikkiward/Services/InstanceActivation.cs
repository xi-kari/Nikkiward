using System.Globalization;

namespace Nikkiward.Services;

internal enum InstanceAction { OpenWindow, Redirect, Retry }

internal static class InstanceActivation
{
    internal const string RestartArgument = "--restart-from";

    internal static uint? GetPredecessor(IReadOnlyList<string> arguments) =>
        arguments is [RestartArgument, var value] &&
        uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid != 0
            ? pid : null;

    internal static InstanceAction Decide(bool isCurrent, uint? ownerId, uint? predecessor)
    {
        if (isCurrent)
        {
            return InstanceAction.OpenWindow;
        }

        return ownerId is null || ownerId == predecessor ? InstanceAction.Retry : InstanceAction.Redirect;
    }
}
