using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Nikkiward.Services;

public interface IWindowsInstallationPathSource
{
    IReadOnlyList<string> GetOfficialLauncherRootCandidates();

    IReadOnlyList<string> GetBilibiliLauncherRootCandidates() => Array.Empty<string>();

    IReadOnlyList<string> GetSteamRootCandidates();
}

/// <summary>
/// Reads only installation-location metadata. It never executes an uninstall
/// string, launches Steam, or scans arbitrary disks.
/// </summary>
public sealed class WindowsInstallationPathSource : IWindowsInstallationPathSource
{
    private const string OfficialLauncherName = "InfinityNikki Launcher";
    private const string BilibiliLauncherName = "InfinityNikkiBili Launcher";

    public IReadOnlyList<string> GetOfficialLauncherRootCandidates() =>
        GetLauncherRootCandidates(OfficialLauncherName);

    public IReadOnlyList<string> GetBilibiliLauncherRootCandidates() =>
        GetLauncherRootCandidates(BilibiliLauncherName);

    private static IReadOnlyList<string> GetLauncherRootCandidates(string launcherName)
    {
        var roots = new List<string>();
        AddDirectory(roots, GetEnvironmentPath("SystemDrive", "C:\\") + launcherName);
        AddDirectory(roots, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            launcherName));
        AddDirectory(roots, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            launcherName));

        foreach (var uninstallPath in EnumerateUninstallKeys())
        {
            var displayName = ReadString(uninstallPath, "DisplayName");
            if (!string.Equals(displayName, launcherName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddDirectory(roots, ParseDirectory(ReadString(uninstallPath, "InstallLocation")));
            AddFileDirectory(roots, ParseDisplayIcon(ReadString(uninstallPath, "DisplayIcon")));
            AddFileDirectory(roots, ParseUninstallCommand(ReadString(uninstallPath, "UninstallString")));
        }

        return DistinctPaths(roots);
    }

    public IReadOnlyList<string> GetSteamRootCandidates()
    {
        var roots = new List<string>();
        AddDirectory(roots, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam"));
        AddDirectory(roots, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Steam"));

        foreach (var candidate in new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default, "Software\\Valve\\Steam", "SteamPath"),
            (RegistryHive.LocalMachine, RegistryView.Registry32, "SOFTWARE\\Valve\\Steam", "InstallPath"),
            (RegistryHive.LocalMachine, RegistryView.Registry64, "SOFTWARE\\Valve\\Steam", "InstallPath"),
        })
        {
            AddDirectory(roots, ReadRegistryString(
                candidate.Item1,
                candidate.Item2,
                candidate.Item3,
                candidate.Item4));
        }

        return DistinctPaths(roots);
    }

    private static IReadOnlyList<string> EnumerateUninstallKeys()
    {
        var paths = new List<string>();
        var locations = new[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default,
                "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall"),
            (RegistryHive.LocalMachine, RegistryView.Registry32,
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall"),
            (RegistryHive.LocalMachine, RegistryView.Registry64,
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall"),
        };

        foreach (var location in locations)
        {
            RegistryKey? root = null;
            RegistryKey? uninstall = null;
            try
            {
                root = RegistryKey.OpenBaseKey(location.Item1, location.Item2);
                uninstall = root.OpenSubKey(location.Item3);
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    paths.Add($"{location.Item3}\\{name}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or PlatformNotSupportedException)
            {
                // Registry access is optional; discovery remains available via
                // manual selection and known paths.
            }
            finally
            {
                uninstall?.Dispose();
                root?.Dispose();
            }
        }

        return paths;
    }

    private static string GetEnvironmentPath(string variable, string fallback) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))
            ? fallback
            : Environment.GetEnvironmentVariable(variable)! + Path.DirectorySeparatorChar;

    private static string? ReadString(string keyPath, string valueName)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32, RegistryView.Default })
            {
                var value = ReadRegistryString(hive, view, keyPath, valueName);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? ReadRegistryString(
        RegistryHive hive,
        RegistryView view,
        string keyPath,
        string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(keyPath);
            var value = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value is string text ? Environment.ExpandEnvironmentVariables(text) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    internal static string? ParseDirectory(string? value)
    {
        var path = value?.Trim();
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return path.Length >= 2 && path[0] == '"' && path[^1] == '"' ? path[1..^1] : path;
    }

    internal static string? ParseDisplayIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var comma = value.LastIndexOf(',');
        return ParseDirectory(comma >= 0 && int.TryParse(value[(comma + 1)..], out _) ? value[..comma] : value);
    }

    internal static string? ParseUninstallCommand(string? value)
    {
        var command = value?.Trim();
        if (string.IsNullOrEmpty(command))
        {
            return null;
        }

        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        var match = Regex.Match(command, @"^.+?\.exe(?=\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Value : null;
    }

    private static void AddFileDirectory(List<string> roots, string? path)
    {
        if (File.Exists(path))
        {
            AddDirectory(roots, Path.GetDirectoryName(path));
        }
    }

    private static void AddDirectory(List<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                roots.Add(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }

    private static IReadOnlyList<string> DistinctPaths(IEnumerable<string> paths) =>
        paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
