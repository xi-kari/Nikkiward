using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Nikkiward.Serialization;

namespace Nikkiward.ViewModels;

public sealed record LocalPluginImportRequest(
    string PluginId,
    string DisplayName,
    string Version,
    string EntryExecutablePath,
    IReadOnlyList<string> CompanionFilePaths);

public sealed record LocalPluginManifest
{
    public int SchemaVersion { get; init; } = 1;

    public string PluginId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string EntryExecutable { get; init; } = string.Empty;

    public string EntrySha256 { get; init; } = string.Empty;

    public DateTimeOffset InstalledAtUtc { get; init; }
}

public sealed record LocalPluginInstallation(
    string PluginId,
    string DisplayName,
    string Version,
    bool IsInstalled,
    bool IsBroken,
    string StatusText,
    string? InstallDirectory,
    string? EntryExecutablePath,
    string? EntrySha256);

public sealed class LocalPluginCatalog
{
    private const int ManifestSchemaVersion = 1;
    private const string ManifestFileName = "plugin.json";
    private static readonly Regex PluginIdPattern = new(
        "^[a-z0-9](?:[a-z0-9.-]{1,62}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
    private static readonly JsonTypeInfo<LocalPluginManifest> ManifestJsonTypeInfo =
        new NikkiwardJsonContext(ManifestJsonOptions).LocalPluginManifest;

    private readonly string _pluginsRoot;

    public LocalPluginCatalog(string? localApplicationDataPath = null)
    {
        var localRoot = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localApplicationDataPath;
        _pluginsRoot = Path.GetFullPath(Path.Combine(localRoot, "Nikkiward", "Plugins"));
    }

    public string PluginsRoot => _pluginsRoot;

    public async Task<LocalPluginInstallation> GetAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        var normalizedId = NormalizePluginId(pluginId);
        var installDirectory = GetInstallDirectory(normalizedId);
        var manifestPath = Path.Combine(installDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return Missing(normalizedId);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<LocalPluginManifest>(
                json,
                ManifestJsonTypeInfo);
            if (manifest is null ||
                manifest.SchemaVersion != ManifestSchemaVersion ||
                !string.Equals(manifest.PluginId, normalizedId, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.DisplayName) ||
                string.IsNullOrWhiteSpace(manifest.EntryExecutable) ||
                !string.Equals(
                    manifest.EntryExecutable,
                    Path.GetFileName(manifest.EntryExecutable),
                    StringComparison.Ordinal) ||
                !manifest.EntryExecutable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                !IsSha256(manifest.EntrySha256))
            {
                return Broken(normalizedId, installDirectory, "插件清单无效");
            }

            var entryPath = Path.GetFullPath(Path.Combine(
                installDirectory,
                manifest.EntryExecutable));
            EnsureWithinRoot(entryPath, installDirectory);
            if (!File.Exists(entryPath))
            {
                return Broken(normalizedId, installDirectory, "插件入口缺失");
            }

            var actualHash = await CalculateSha256Async(entryPath, cancellationToken);
            if (!actualHash.Equals(manifest.EntrySha256, StringComparison.OrdinalIgnoreCase))
            {
                return Broken(normalizedId, installDirectory, "插件文件已变化");
            }

            return new LocalPluginInstallation(
                manifest.PluginId,
                manifest.DisplayName,
                manifest.Version,
                IsInstalled: true,
                IsBroken: false,
                StatusText: "已安装",
                installDirectory,
                entryPath,
                actualHash);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Broken(
                normalizedId,
                installDirectory,
                $"插件读取失败：{ex.GetType().Name}");
        }
    }

    public async Task<LocalPluginInstallation> ImportAsync(
        LocalPluginImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pluginId = NormalizePluginId(request.PluginId);
        var sourceEntry = NormalizeExistingFile(request.EntryExecutablePath, ".exe");
        var companionFiles = request.CompanionFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizeExistingFile(path, expectedExtension: null))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !path.Equals(sourceEntry, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Directory.CreateDirectory(_pluginsRoot);
        EnsureDirectoryIsNotReparsePoint(_pluginsRoot);

        var installDirectory = GetInstallDirectory(pluginId);
        if (Directory.Exists(installDirectory))
        {
            await UninstallAsync(pluginId, cancellationToken);
        }

        var stagingDirectory = Path.Combine(
            _pluginsRoot,
            $".staging-{pluginId}-{Guid.NewGuid():N}");
        EnsureWithinRoot(stagingDirectory, _pluginsRoot);
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            var entryFileName = Path.GetFileName(sourceEntry);
            var stagedEntry = Path.Combine(stagingDirectory, entryFileName);
            await CopyFileAsync(sourceEntry, stagedEntry, cancellationToken);

            foreach (var companion in companionFiles)
            {
                var destination = Path.Combine(
                    stagingDirectory,
                    Path.GetFileName(companion));
                if (!destination.Equals(stagedEntry, StringComparison.OrdinalIgnoreCase))
                {
                    await CopyFileAsync(companion, destination, cancellationToken);
                }
            }

            var entryHash = await CalculateSha256Async(stagedEntry, cancellationToken);
            var manifest = new LocalPluginManifest
            {
                SchemaVersion = ManifestSchemaVersion,
                PluginId = pluginId,
                DisplayName = request.DisplayName.Trim(),
                Version = request.Version.Trim(),
                EntryExecutable = entryFileName,
                EntrySha256 = entryHash,
                InstalledAtUtc = DateTimeOffset.UtcNow,
            };
            var manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonTypeInfo);
            await File.WriteAllTextAsync(
                Path.Combine(stagingDirectory, ManifestFileName),
                manifestJson + Environment.NewLine,
                cancellationToken);

            Directory.Move(stagingDirectory, installDirectory);
            return await GetAsync(pluginId, cancellationToken);
        }
        catch
        {
            if (Directory.Exists(stagingDirectory))
            {
                DeleteVerifiedDirectory(stagingDirectory, _pluginsRoot);
            }

            throw;
        }
    }

    public async Task<bool> LaunchAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return await LaunchAsync(pluginId, [], cancellationToken);
    }

    public async Task<bool> LaunchAsync(
        string pluginId,
        IReadOnlyList<string> argumentList,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(argumentList);
        var installation = await GetAsync(pluginId, cancellationToken);
        if (!installation.IsInstalled ||
            string.IsNullOrWhiteSpace(installation.EntryExecutablePath) ||
            string.IsNullOrWhiteSpace(installation.InstallDirectory))
        {
            throw new InvalidOperationException(installation.StatusText);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = installation.EntryExecutablePath,
            WorkingDirectory = installation.InstallDirectory,
            UseShellExecute = true,
        };
        foreach (var argument in argumentList)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                throw new ArgumentException("Plugin arguments cannot be empty.", nameof(argumentList));
            }

            startInfo.ArgumentList.Add(argument);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo);
        return process is not null;
    }

    public Task UninstallAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedId = NormalizePluginId(pluginId);
        var installDirectory = GetInstallDirectory(normalizedId);
        if (Directory.Exists(installDirectory))
        {
            DeleteVerifiedDirectory(installDirectory, _pluginsRoot);
        }

        return Task.CompletedTask;
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<string> CalculateSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private string GetInstallDirectory(string pluginId)
    {
        var path = Path.GetFullPath(Path.Combine(_pluginsRoot, pluginId));
        EnsureWithinRoot(path, _pluginsRoot);
        return path;
    }

    private static string NormalizePluginId(string pluginId)
    {
        var normalized = (pluginId ?? string.Empty).Trim().ToLowerInvariant();
        if (!PluginIdPattern.IsMatch(normalized))
        {
            throw new ArgumentException("插件 ID 格式无效。", nameof(pluginId));
        }

        return normalized;
    }

    private static string NormalizeExistingFile(
        string path,
        string? expectedExtension)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("插件文件路径为空。", nameof(path));
        }

        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("插件文件不存在。", fullPath);
        }

        if (!string.IsNullOrWhiteSpace(expectedExtension) &&
            !Path.GetExtension(fullPath).Equals(
                expectedExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"插件入口必须是 {expectedExtension} 文件。");
        }

        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("插件文件不能是重解析点。");
        }

        return fullPath;
    }

    private static void DeleteVerifiedDirectory(string directory, string root)
    {
        var fullDirectory = Path.GetFullPath(directory);
        EnsureWithinRoot(fullDirectory, root);
        EnsureDirectoryIsNotReparsePoint(fullDirectory);

        foreach (var path in Directory.EnumerateFileSystemEntries(
                     fullDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("插件目录包含重解析点，已停止卸载。");
            }
        }

        Directory.Delete(fullDirectory, recursive: true);
    }

    private static void EnsureDirectoryIsNotReparsePoint(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("插件目录不能是重解析点。");
        }
    }

    private static void EnsureWithinRoot(string candidate, string root)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!fullCandidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("插件路径超出插件根目录。");
        }
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static LocalPluginInstallation Missing(string pluginId) => new(
        pluginId,
        DisplayName: "相册插件",
        Version: string.Empty,
        IsInstalled: false,
        IsBroken: false,
        StatusText: "尚未安装",
        InstallDirectory: null,
        EntryExecutablePath: null,
        EntrySha256: null);

    private static LocalPluginInstallation Broken(
        string pluginId,
        string installDirectory,
        string statusText) => new(
        pluginId,
        DisplayName: "相册插件",
        Version: string.Empty,
        IsInstalled: false,
        IsBroken: true,
        statusText,
        installDirectory,
        EntryExecutablePath: null,
        EntrySha256: null);
}
