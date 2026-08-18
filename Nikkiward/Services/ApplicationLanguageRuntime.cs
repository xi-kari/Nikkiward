using System.Globalization;
using Nikkiward.Models;

namespace Nikkiward.Services;

public static class ApplicationLanguageRuntime
{
    public static CultureInfo? ResolveCulture(string? languageTag) =>
        ApplicationSettingsValidator.NormalizeLanguageTag(languageTag) ==
        ApplicationSettingsValidator.SystemLanguageTag
            ? null
            : CultureInfo.GetCultureInfo(
                ApplicationSettingsValidator.SimplifiedChineseLanguageTag);

    public static void Apply(string? languageTag)
    {
        var culture = ResolveCulture(languageTag);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
