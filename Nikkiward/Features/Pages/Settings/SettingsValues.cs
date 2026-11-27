using System.Globalization;
using System.Text.Json;

namespace Nikkiward.Features.Pages;

internal static class SettingsValues
{
    internal static InvalidDataException Invalid() => new("设置项或取值无效，请重新选择。");

    internal static string Field(JsonElement message, string name) =>
        message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : throw Invalid();

    internal static T Choice<T>(string value) where T : struct, Enum =>
        Enum.GetNames<T>().Contains(value, StringComparer.Ordinal) ? Enum.Parse<T>(value) : throw Invalid();

    internal static bool Toggle(string value) => value switch
    {
        "true" => true, "false" => false, _ => throw Invalid()
    };

    internal static int Integer(string value, int min, int max) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max ? n : throw Invalid();

    internal static double Number(string value, double min, double max) =>
        double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max ? n : throw Invalid();
}
