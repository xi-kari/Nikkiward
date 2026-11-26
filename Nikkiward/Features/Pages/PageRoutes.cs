using System.Text.Json;

namespace Nikkiward.Features.Pages;

internal sealed record PageAdapter(string Module, string[] Actions, Func<object> State,
    Func<string, JsonElement, Task> Execute);

internal static class PageRoutes
{
    internal static Dictionary<string, PageAdapter> Register(params (string Route, PageAdapter Adapter)[] pages) =>
        pages.ToDictionary(page => page.Route, page => page.Adapter, StringComparer.Ordinal);

    internal static string? NavigationTag(string route) => route switch
    {
        "home" => "launcher", "journal" => "library", "wish" => "resonance",
        "favorites" => "gallery-favorites", "gallery" or "settings" => route, _ => null
    };

    internal static bool Allows(string? current, string page, PageAdapter adapter, string action) =>
        current == page && adapter.Actions.Contains(action, StringComparer.Ordinal);
}

