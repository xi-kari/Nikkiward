using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Nikkiward.Features.Background;
using Nikkiward.Models;
using Nikkiward.Serialization;

namespace Nikkiward.Services;

public sealed class RedactedDiagnosticReportExporter : IDiagnosticReportExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static readonly JsonTypeInfo<DiagnosticReportDocument> ReportJsonTypeInfo =
        new NikkiwardJsonContext(SerializerOptions).DiagnosticReportDocument;

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly Regex TokenAssignmentPattern = new(
        @"\b(?<key>[a-z0-9_.-]*token[a-z0-9_.-]*|authorization|cookie)\b" +
        @"(?<separator>\s*[:=]\s*)(?<value>""[^""]*""|'[^']*'|[^\s,;&]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        @"\bBearer\s+[^\s,;&]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<DiagnosticReportExportResult> ExportAsync(
        LaunchProfile profile,
        LaunchSnapshot snapshot,
        string destinationDirectory,
        ArtBackdropDiagnosticState? backdropState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("A destination directory is required.", nameof(destinationDirectory));
        }

        var generatedAtUtc = DateTimeOffset.UtcNow;
        var mappings = BuildPathMappings(profile, snapshot, destinationDirectory);
        var safeProfileId = SanitizeFileName(profile.ProfileId);
        var prefix = $"nikkiward-diagnostic-{generatedAtUtc:yyyyMMdd-HHmmssfff}Z-{safeProfileId}";
        var jsonPath = Path.Combine(destinationDirectory, $"{prefix}.json");
        var textPath = Path.Combine(destinationDirectory, $"{prefix}.txt");
        var jsonTemporaryPath = $"{jsonPath}.{Guid.NewGuid():N}.tmp";
        var textTemporaryPath = $"{textPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationDirectory);

            var report = CreateReport(profile, snapshot, backdropState, generatedAtUtc, mappings);
            var json = JsonSerializer.Serialize(report, ReportJsonTypeInfo);
            var text = CreateTextReport(report);

            await File.WriteAllTextAsync(
                jsonTemporaryPath,
                json,
                Utf8WithoutBom,
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                textTemporaryPath,
                text,
                Utf8WithoutBom,
                cancellationToken).ConfigureAwait(false);

            File.Move(jsonTemporaryPath, jsonPath, overwrite: true);
            File.Move(textTemporaryPath, textPath, overwrite: true);

            return new DiagnosticReportExportResult
            {
                Succeeded = true,
                JsonFilePath = jsonPath,
                TextFilePath = textPath,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            TryDelete(jsonPath);
            TryDelete(textPath);

            return new DiagnosticReportExportResult
            {
                Succeeded = false,
                Error = RedactText($"{ex.GetType().Name}: {ex.Message}", mappings),
            };
        }
        finally
        {
            TryDelete(jsonTemporaryPath);
            TryDelete(textTemporaryPath);
        }
    }

    private static DiagnosticReportDocument CreateReport(
        LaunchProfile profile,
        LaunchSnapshot snapshot,
        ArtBackdropDiagnosticState? backdropState,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<PathMapping> mappings)
    {
        var components = snapshot.Components.Select(component => new DiagnosticComponent(
            RedactText(component.ComponentId, mappings),
            RedactText(component.DisplayName, mappings),
            RedactPath(component.FilePath, mappings),
            component.Exists,
            component.InspectionSucceeded,
            component.FileSizeBytes,
            component.LastWriteTimeUtc,
            RedactText(component.FileVersion, mappings),
            RedactText(component.ProductVersion, mappings),
            component.Sha256,
            component.SignatureStatus,
            component.SignatureStatusCode,
            RedactText(component.Error, mappings),
            component.InspectedAtUtc)).ToArray();

        return new DiagnosticReportDocument(
            SchemaVersion: 2,
            GeneratedAtUtc: generatedAtUtc,
            Profile: new DiagnosticProfile(
                RedactText(profile.ProfileId, mappings),
                RedactText(profile.DisplayName, mappings),
                RedactText(profile.Channel, mappings),
                profile.Capability,
                RedactPath(profile.GameRootPath, mappings),
                RedactPath(profile.LauncherPath, mappings),
                RedactPath(profile.XStarterPath, mappings),
                RedactPath(profile.GameExecutablePath, mappings),
                RedactPath(profile.ShippingExecutablePath, mappings),
                RedactPath(profile.AntiCheatExecutablePath, mappings)),
            Snapshot: new DiagnosticSnapshot(
                RedactText(snapshot.ProfileId, mappings),
                snapshot.State,
                snapshot.Capability,
                snapshot.CapturedAtUtc,
                RedactText(snapshot.LastFailureReason, mappings)),
            Backdrop: backdropState is null
                ? null
                : new DiagnosticBackdrop(
                    backdropState.IsReady,
                    backdropState.AccentFromFallback,
                    backdropState.DominantHueWeight,
                    backdropState.PreferredTheme),
            Components: components,
            ExcludedData: new[]
            {
                "Command lines",
                "Authentication tokens",
                "Process memory",
                "Network payloads",
            });
    }

    private static string CreateTextReport(DiagnosticReportDocument report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Nikkiward diagnostic report");
        builder.AppendLine($"Schema version: {report.SchemaVersion}");
        builder.AppendLine($"Generated at UTC: {report.GeneratedAtUtc:O}");
        builder.AppendLine();
        builder.AppendLine("Profile");
        builder.AppendLine($"  ID: {report.Profile.ProfileId}");
        builder.AppendLine($"  Name: {report.Profile.DisplayName}");
        builder.AppendLine($"  Channel: {report.Profile.Channel}");
        builder.AppendLine($"  Capability: {report.Profile.Capability}");
        builder.AppendLine($"  Game root: {report.Profile.GameRootPath}");
        builder.AppendLine($"  Launcher: {report.Profile.LauncherPath}");
        builder.AppendLine($"  Backend: {report.Profile.XStarterPath}");
        builder.AppendLine($"  Bootstrap: {report.Profile.GameExecutablePath}");
        builder.AppendLine($"  Game client: {report.Profile.ShippingExecutablePath}");
        builder.AppendLine($"  Anti-cheat: {report.Profile.AntiCheatExecutablePath}");
        builder.AppendLine();
        builder.AppendLine("Snapshot");
        builder.AppendLine($"  Profile ID: {report.Snapshot.ProfileId}");
        builder.AppendLine($"  State: {report.Snapshot.State}");
        builder.AppendLine($"  Capability: {report.Snapshot.Capability}");
        builder.AppendLine($"  Captured at UTC: {report.Snapshot.CapturedAtUtc:O}");
        builder.AppendLine($"  Last failure: {report.Snapshot.LastFailureReason ?? "(none)"}");
        builder.AppendLine();

        if (report.Backdrop is { } backdrop)
        {
            builder.AppendLine("Backdrop");
            builder.AppendLine($"  Ready: {backdrop.IsReady}");
            builder.AppendLine($"  Accent from fallback: {backdrop.AccentFromFallback}");
            builder.AppendLine($"  Dominant hue weight: {backdrop.DominantHueWeight:R}");
            builder.AppendLine($"  Preferred theme: {backdrop.PreferredTheme}");
            builder.AppendLine();
        }

        builder.AppendLine("Components");

        foreach (var component in report.Components)
        {
            builder.AppendLine($"  [{component.ComponentId}] {component.DisplayName}");
            builder.AppendLine($"    Path: {component.FilePath}");
            builder.AppendLine($"    Exists: {component.Exists}");
            builder.AppendLine($"    Inspection succeeded: {component.InspectionSucceeded}");
            builder.AppendLine($"    Size: {component.FileSizeBytes?.ToString() ?? "(unknown)"}");
            builder.AppendLine($"    Last write UTC: {component.LastWriteTimeUtc?.ToString("O") ?? "(unknown)"}");
            builder.AppendLine($"    File version: {component.FileVersion ?? "(none)"}");
            builder.AppendLine($"    Product version: {component.ProductVersion ?? "(none)"}");
            builder.AppendLine($"    SHA-256: {component.Sha256 ?? "(unavailable)"}");
            builder.AppendLine($"    Authenticode: {component.SignatureStatus} ({component.SignatureStatusCode ?? "no code"})");
            builder.AppendLine($"    Error: {component.Error ?? "(none)"}");
        }

        builder.AppendLine();
        builder.AppendLine("Excluded data");
        foreach (var excludedItem in report.ExcludedData)
        {
            builder.AppendLine($"  - {excludedItem}");
        }

        return builder.ToString();
    }

    private static IReadOnlyList<PathMapping> BuildPathMappings(
        LaunchProfile profile,
        LaunchSnapshot snapshot,
        string destinationDirectory)
    {
        var mappings = new List<PathMapping>();
        AddMapping(mappings, profile.GameRootPath, "%GAME_ROOT%");
        AddParentMapping(mappings, profile.LauncherPath, "%LAUNCHER_ROOT%");
        AddParentMapping(mappings, profile.XStarterPath, "%XSTARTER_ROOT%");
        AddMapping(mappings, destinationDirectory, "%EXPORT_ROOT%");

        for (var index = 0; index < snapshot.Components.Count; index++)
        {
            AddParentMapping(mappings, snapshot.Components[index].FilePath, $"%COMPONENT_ROOT_{index + 1}%");
        }

        AddMapping(
            mappings,
            ApplicationDataPaths.Root,
            "%NIKKIWARD_DATA%");
        AddMapping(
            mappings,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "%LOCALAPPDATA%");
        AddMapping(
            mappings,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "%USERPROFILE%");

        return mappings
            .DistinctBy(mapping => mapping.RootPath, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(mapping => mapping.RootPath.Length)
            .ToArray();
    }

    private static void AddParentMapping(List<PathMapping> mappings, string? filePath, string replacement)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            AddMapping(mappings, Path.GetDirectoryName(Path.GetFullPath(filePath)), replacement);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }

    private static void AddMapping(List<PathMapping> mappings, string? rootPath, string replacement)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                mappings.Add(new PathMapping(normalized, replacement));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }

    private static string? RedactPath(string? value, IReadOnlyList<PathMapping> mappings)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return RedactText(value, mappings);
        }

        foreach (var mapping in mappings)
        {
            if (string.Equals(fullPath, mapping.RootPath, StringComparison.OrdinalIgnoreCase))
            {
                return mapping.Replacement;
            }

            var prefix = mapping.RootPath + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = fullPath[prefix.Length..];
                return $"{mapping.Replacement}\\{relativePath}";
            }
        }

        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var fileName = Path.GetFileName(fullPath);
        return RedactText($"{root}…\\{fileName}", mappings);
    }

    private static string? RedactText(string? value, IReadOnlyList<PathMapping> mappings)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var result = value;
        foreach (var mapping in mappings)
        {
            result = result.Replace(
                mapping.RootPath,
                mapping.Replacement,
                StringComparison.OrdinalIgnoreCase);
        }

        var userName = Environment.UserName;
        if (userName.Length >= 3)
        {
            result = result.Replace(userName, "[USER]", StringComparison.OrdinalIgnoreCase);
        }

        var machineName = Environment.MachineName;
        if (machineName.Length >= 3)
        {
            result = result.Replace(machineName, "[HOST]", StringComparison.OrdinalIgnoreCase);
        }

        result = TokenAssignmentPattern.Replace(
            result,
            "${key}${separator}[REDACTED]");
        result = BearerPattern.Replace(result, "Bearer [REDACTED]");
        return result;
    }

    private static string SanitizeFileName(string value)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? "profile" : value;
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(Math.Min(candidate.Length, 40));

        foreach (var character in candidate)
        {
            if (builder.Length >= 40)
            {
                break;
            }

            builder.Append(invalidCharacters.Contains(character) ? '-' : character);
        }

        return builder.Length == 0 ? "profile" : builder.ToString();
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record PathMapping(string RootPath, string Replacement);

}

internal sealed record DiagnosticReportDocument(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    DiagnosticProfile Profile,
    DiagnosticSnapshot Snapshot,
    DiagnosticBackdrop? Backdrop,
    IReadOnlyList<DiagnosticComponent> Components,
    IReadOnlyList<string> ExcludedData);

internal sealed record DiagnosticProfile(
    string? ProfileId,
    string? DisplayName,
    string? Channel,
    LaunchCapability Capability,
    string? GameRootPath,
    string? LauncherPath,
    string? XStarterPath,
    string? GameExecutablePath,
    string? ShippingExecutablePath,
    string? AntiCheatExecutablePath);

internal sealed record DiagnosticSnapshot(
    string? ProfileId,
    LaunchState State,
    LaunchCapability Capability,
    DateTimeOffset CapturedAtUtc,
    string? LastFailureReason);

internal sealed record DiagnosticBackdrop(
    bool IsReady,
    bool AccentFromFallback,
    double DominantHueWeight,
    ArtPreferredTheme PreferredTheme);

internal sealed record DiagnosticComponent(
    string? ComponentId,
    string? DisplayName,
    string? FilePath,
    bool Exists,
    bool InspectionSucceeded,
    long? FileSizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    string? FileVersion,
    string? ProductVersion,
    string? Sha256,
    AuthenticodeSignatureStatus SignatureStatus,
    string? SignatureStatusCode,
    string? Error,
    DateTimeOffset InspectedAtUtc);
