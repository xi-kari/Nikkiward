using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Nikkiward.Serialization;

namespace Nikkiward.Features.Gallery;

public sealed record NikkiGalleryToolState(
    string StatusText,
    string? ExecutablePath,
    bool IsRegistered,
    bool IsAvailable);

public sealed record NikkiGalleryToolRegistration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string ExecutablePath { get; init; }

    public required string Sha256 { get; init; }

    public string? FileVersion { get; init; }

    public DateTimeOffset RegisteredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class NikkiGalleryToolRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonTypeInfo<NikkiGalleryToolRegistration> RegistrationJsonTypeInfo =
        new NikkiwardJsonContext(SerializerOptions).NikkiGalleryToolRegistration;

    public NikkiGalleryToolRegistry(string? localApplicationDataPath = null)
    {
        var localRoot = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localApplicationDataPath;
        if (string.IsNullOrWhiteSpace(localRoot))
        {
            throw new InvalidOperationException("LocalApplicationData is unavailable.");
        }

        AssociationFilePath = Path.Combine(
            Path.GetFullPath(localRoot),
            "Nikkiward",
            "ExternalTools",
            "nikkigallery.json");
    }

    public string AssociationFilePath { get; }

    public async Task<NikkiGalleryToolState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AssociationFilePath))
        {
            return new NikkiGalleryToolState("尚未关联", null, false, false);
        }

        try
        {
            var registration = await LoadRegistrationAsync(cancellationToken);
            var executablePath = NormalizeExecutablePath(
                registration.ExecutablePath,
                requireExists: false);
            if (!File.Exists(executablePath))
            {
                return new NikkiGalleryToolState(
                    "已关联，但程序不存在",
                    executablePath,
                    true,
                    false);
            }

            ValidateExistingExecutable(executablePath);
            var currentHash = await ComputeSha256Async(executablePath, cancellationToken);
            if (!string.Equals(
                    currentHash,
                    registration.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new NikkiGalleryToolState(
                    "程序已发生变化，请重新关联",
                    executablePath,
                    true,
                    false);
            }

            var versionSuffix = string.IsNullOrWhiteSpace(registration.FileVersion)
                ? string.Empty
                : $" · {registration.FileVersion}";
            return new NikkiGalleryToolState(
                $"已关联{versionSuffix}",
                executablePath,
                true,
                true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidDataException
            or JsonException)
        {
            return new NikkiGalleryToolState(
                $"关联记录不可用：{ex.GetType().Name}",
                null,
                true,
                false);
        }
    }

    public async Task<NikkiGalleryToolState> RegisterAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = NormalizeExecutablePath(executablePath, requireExists: true);
        ValidateExistingExecutable(normalizedPath);

        var registration = new NikkiGalleryToolRegistration
        {
            ExecutablePath = normalizedPath,
            Sha256 = await ComputeSha256Async(normalizedPath, cancellationToken),
            FileVersion = ReadFileVersion(normalizedPath),
        };
        await SaveRegistrationAsync(registration, cancellationToken);

        var versionSuffix = string.IsNullOrWhiteSpace(registration.FileVersion)
            ? string.Empty
            : $" · {registration.FileVersion}";
        return new NikkiGalleryToolState(
            $"已关联{versionSuffix}",
            normalizedPath,
            true,
            true);
    }

    public async Task<bool> LaunchAsync(CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(cancellationToken);
        if (!state.IsAvailable || string.IsNullOrWhiteSpace(state.ExecutablePath))
        {
            throw new InvalidOperationException(state.StatusText);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = state.ExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(state.ExecutablePath),
            UseShellExecute = true,
        });
        return process is not null;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(AssociationFilePath))
        {
            File.Delete(AssociationFilePath);
        }

        return Task.CompletedTask;
    }

    private async Task<NikkiGalleryToolRegistration> LoadRegistrationAsync(
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            AssociationFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var registration = await JsonSerializer.DeserializeAsync(
            stream,
            RegistrationJsonTypeInfo,
            cancellationToken);
        if (registration is null ||
            registration.SchemaVersion != NikkiGalleryToolRegistration.CurrentSchemaVersion ||
            string.IsNullOrWhiteSpace(registration.ExecutablePath) ||
            string.IsNullOrWhiteSpace(registration.Sha256) ||
            registration.Sha256.Length != 64 ||
            !registration.Sha256.All(char.IsAsciiHexDigit))
        {
            throw new InvalidDataException("The NikkiGallery association document is invalid.");
        }

        return registration;
    }

    private async Task SaveRegistrationAsync(
        NikkiGalleryToolRegistration registration,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(AssociationFilePath)
            ?? throw new InvalidOperationException("Integration directory is unavailable.");
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Integration directory cannot be a reparse point.");
        }

        var temporaryPath = $"{AssociationFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    registration,
                    RegistrationJsonTypeInfo,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, AssociationFilePath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static string NormalizeExecutablePath(string path, bool requireExists)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException("NikkiGallery path is empty.");
        }

        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        if (!Path.GetExtension(fullPath).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileNameWithoutExtension(fullPath).Contains(
                "NikkiGallery",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Selected file is not a NikkiGallery executable.");
        }

        if (requireExists && !File.Exists(fullPath))
        {
            throw new FileNotFoundException("NikkiGallery executable was not found.", fullPath);
        }

        return fullPath;
    }

    private static void ValidateExistingExecutable(string executablePath)
    {
        if ((File.GetAttributes(executablePath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("NikkiGallery executable cannot be a reparse point.");
        }
    }

    private static async Task<string> ComputeSha256Async(
        string executablePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            executablePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string? ReadFileVersion(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return string.IsNullOrWhiteSpace(info.FileVersion)
                ? null
                : info.FileVersion;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
