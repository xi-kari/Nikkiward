using System.Text.Json;

namespace Nikkiward.Features.Pages;

internal sealed record PageAdapter(string Module, string[] Actions, Func<object> State,
    Func<string, JsonElement, Task> Execute)
{
    internal void PostState(string page, string skin, string tone, Action<string> post, Action<string> fail)
    {
        try
        {
            var message = new { type = "state", page, module = Module, skin, tone, state = State() };
            post(JsonSerializer.Serialize(message));
        }
        catch (Exception ex)
        {
            fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }
}

internal static class PageRoutes
{
    internal static Dictionary<string, PageAdapter> Register(params (string Route, PageAdapter Adapter)[] pages) =>
        pages.ToDictionary(page => page.Route, page => page.Adapter, StringComparer.Ordinal);

    internal static string? NavigationTag(string route) => route switch
    {
        "home" => "launcher", "journal" => "library", "wish" => "resonance",
        "favorites" => "gallery-favorites", "gallery" or "settings" => route, _ => null
    };

    internal static string? NavigationDestination(JsonElement message, string? current, bool ready)
    {
        if (message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String || !action.ValueEquals("navigate") ||
            !message.TryGetProperty("to", out var to) || to.ValueKind != JsonValueKind.String) return null;
        if (message.TryGetProperty("page", out var page) &&
            (!ready || page.ValueKind != JsonValueKind.String || page.GetString() != current)) return null;
        var route = to.GetString()!;
        return NavigationTag(route) == null ? null : route;
    }

    internal static bool Allows(string? current, string page, PageAdapter adapter, string action) =>
        current == page && adapter.Actions.Contains(action, StringComparer.Ordinal);
}

