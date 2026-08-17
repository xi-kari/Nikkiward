using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nikkiward.Models;

namespace Nikkiward.Services;

public sealed record ProductMarkerObservation
{
    public string? Name { get; init; }

    public string? Version { get; init; }

    public string? AdPlatformId { get; init; }
}

public static class LauncherConfigReader
{
    private static readonly Regex GameDirectoryLine = new(
        "^\\s*gameDir\\s*=\\s*(?<value>[^;#\\r\\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? TryReadGameDirectory(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines(filePath))
            {
                var value = TryReadGameDirectoryLine(line);
                if (value is not null)
                {
                    return value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    public static string? TryReadGameDirectoryText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = TryReadGameDirectoryLine(line);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? TryReadGameDirectoryLine(string line)
    {
        var match = GameDirectoryLine.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

public static class ProductMarkerReader
{
    public static ProductMarkerObservation? TryRead(string filePath, long maximumBytes = 16 * 1024)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length <= 0 || fileInfo.Length > maximumBytes)
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            return new ProductMarkerObservation
            {
                Name = ReadString(root, "name"),
                Version = ReadString(root, "version"),
                AdPlatformId = ReadString(root, "adplatid"),
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? property.ToString()
            : null;
    }
}

public static class SteamKeyValueReader
{
    private static readonly Regex ScalarLine = new(
        "^\\s*\"(?<key>[^\"]+)\"\\s+\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TokenPattern = new(
        "\"(?<value>(?:\\\\.|[^\"])*)\"|(?<open>\\{)|(?<close>\\})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyDictionary<string, string> ReadScalars(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = ScalarLine.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups["key"].Value;
            var value = Unescape(match.Groups["value"].Value);
            values.TryAdd(key, value);
        }

        return values;
    }

    public static IReadOnlyList<string> ReadInstalledDepotIds(string text)
    {
        return ReadInstalledDepots(text)
            .Select(depot => depot.DepotId)
            .ToArray();
    }

    public static IReadOnlyList<SteamInstalledDepotObservation> ReadInstalledDepots(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var tokenIndex = 0;
        var entries = ParseEntries(Tokenize(text), ref tokenIndex, stopAtClosingBrace: false);
        var installedDepots = FindFirstEntry(entries, "InstalledDepots");
        if (installedDepots is null)
        {
            return Array.Empty<SteamInstalledDepotObservation>();
        }

        var observations = new List<SteamInstalledDepotObservation>();
        var observedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var depot in installedDepots.Children)
        {
            if (string.IsNullOrWhiteSpace(depot.Key) ||
                !depot.Key.All(character => character is >= '0' and <= '9') ||
                !observedIds.Add(depot.Key))
            {
                continue;
            }

            var manifestId = depot.Value;
            long? sizeInBytes = null;
            if (depot.Children.Count > 0)
            {
                manifestId = depot.Children.FirstOrDefault(entry =>
                    string.Equals(entry.Key, "manifest", StringComparison.OrdinalIgnoreCase))?.Value;
                var sizeText = depot.Children.FirstOrDefault(entry =>
                    string.Equals(entry.Key, "size", StringComparison.OrdinalIgnoreCase))?.Value;
                if (long.TryParse(
                        sizeText,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedSize))
                {
                    sizeInBytes = parsedSize;
                }
            }

            observations.Add(new SteamInstalledDepotObservation(
                depot.Key,
                manifestId,
                sizeInBytes));
        }

        return observations;
    }

    private static IReadOnlyList<VdfToken> Tokenize(string text)
    {
        return TokenPattern.Matches(text)
            .Select(match => match.Groups["open"].Success
                ? new VdfToken(VdfTokenKind.OpenBrace, string.Empty)
                : match.Groups["close"].Success
                    ? new VdfToken(VdfTokenKind.CloseBrace, string.Empty)
                    : new VdfToken(VdfTokenKind.Value, Unescape(match.Groups["value"].Value)))
            .ToArray();
    }

    private static IReadOnlyList<VdfEntry> ParseEntries(
        IReadOnlyList<VdfToken> tokens,
        ref int tokenIndex,
        bool stopAtClosingBrace)
    {
        var entries = new List<VdfEntry>();
        while (tokenIndex < tokens.Count)
        {
            var token = tokens[tokenIndex];
            if (token.Kind is VdfTokenKind.CloseBrace)
            {
                tokenIndex++;
                if (stopAtClosingBrace)
                {
                    break;
                }

                continue;
            }

            if (token.Kind is not VdfTokenKind.Value)
            {
                tokenIndex++;
                continue;
            }

            var key = token.Value;
            tokenIndex++;
            if (tokenIndex >= tokens.Count)
            {
                break;
            }

            var valueToken = tokens[tokenIndex];
            if (valueToken.Kind is VdfTokenKind.Value)
            {
                entries.Add(new VdfEntry(key, valueToken.Value, Array.Empty<VdfEntry>()));
                tokenIndex++;
                continue;
            }

            if (valueToken.Kind is VdfTokenKind.OpenBrace)
            {
                tokenIndex++;
                var children = ParseEntries(tokens, ref tokenIndex, stopAtClosingBrace: true);
                entries.Add(new VdfEntry(key, null, children));
            }
        }

        return entries;
    }

    private static VdfEntry? FindFirstEntry(IReadOnlyList<VdfEntry> entries, string key)
    {
        foreach (var entry in entries)
        {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }

            var nested = FindFirstEntry(entry.Children, key);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static string Unescape(string value) =>
        value.Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal);

    private enum VdfTokenKind
    {
        Value,
        OpenBrace,
        CloseBrace,
    }

    private readonly record struct VdfToken(VdfTokenKind Kind, string Value);

    private sealed record VdfEntry(
        string Key,
        string? Value,
        IReadOnlyList<VdfEntry> Children);
}

public sealed record SteamInstalledDepotObservation(
    string DepotId,
    string? ManifestId,
    long? SizeInBytes);

public static class SteamLibraryVdfReader
{
    private static readonly Regex PathLine = new(
        "^\\s*\"path\"\\s+\"(?<value>(?:\\\\.|[^\"])*)\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    public static IReadOnlyList<string> ReadLibraryPaths(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return PathLine.Matches(text)
            .Select(match => match.Groups["value"].Value.Replace("\\\\", "\\", StringComparison.Ordinal))
            .Where(value => !string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public static class SteamManifestReader
{
    public static SteamManifestEvidence? TryRead(
        string manifestPath,
        string commonInstallPath,
        string stagingPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(manifestPath);
            var values = SteamKeyValueReader.ReadScalars(text);
            values.TryGetValue("appid", out var appId);
            values.TryGetValue("installdir", out var installDirectoryName);
            values.TryGetValue("StateFlags", out var stateFlags);
            values.TryGetValue("SizeOnDisk", out var sizeOnDiskText);
            values.TryGetValue("buildid", out var buildId);
            values.TryGetValue("SubID", out var subId);
            values.TryGetValue("depotid", out var depotId);
            values.TryGetValue("manifest", out var manifestId);
            var installedDepots = SteamKeyValueReader.ReadInstalledDepots(text);
            var primaryDepot = installedDepots.FirstOrDefault();

            _ = long.TryParse(
                sizeOnDiskText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var sizeOnDisk);

            return new SteamManifestEvidence
            {
                ManifestPath = Path.GetFullPath(manifestPath),
                AppId = appId,
                InstallDirectoryName = installDirectoryName,
                StateFlags = stateFlags,
                SizeOnDisk = sizeOnDiskText is null ? null : sizeOnDisk,
                BuildId = buildId,
                InstalledDepotIds = installedDepots.Select(depot => depot.DepotId).ToArray(),
                SubId = subId,
                DepotId = primaryDepot?.DepotId ?? depotId,
                ManifestId = primaryDepot?.ManifestId ?? manifestId,
                CommonInstallPath = commonInstallPath,
                StagingPath = stagingPath,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
