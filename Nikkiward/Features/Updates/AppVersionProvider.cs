using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace Nikkiward.Features.Updates;

public static partial class AppVersionProvider
{
    public static AppVersionInfo GetCurrent()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersionProvider).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?
            .Trim();

        if (string.IsNullOrWhiteSpace(informationalVersion) ||
            !NuGetVersion.TryParse(informationalVersion, out var version) ||
            version is null)
        {
            throw new InvalidOperationException("The application informational version is not valid semantic version text.");
        }

        var displayVersion = informationalVersion.Split('+', 2)[0];
        var commitMatch = CommitShaRegex().Match(version.Metadata ?? string.Empty);
        var commitSha = commitMatch.Success
            ? commitMatch.Groups["sha"].Value.ToLowerInvariant()
            : null;

        return new AppVersionInfo(
            version,
            displayVersion,
            GetRuntimeIdentifier(),
            "便携版",
            commitSha);
    }

    private static string GetRuntimeIdentifier() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.X86 => "win-x86",
        Architecture.Arm64 => "win-arm64",
        _ => $"win-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}",
    };

    [GeneratedRegex("(?:^|[.])(?<sha>[0-9a-fA-F]{40})(?:$|[.])", RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaRegex();
}
