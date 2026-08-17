using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Nikkiward.Models;

namespace Nikkiward.Services;

public interface IChannelActivationService
{
    Task<ChannelActivationPlan> CreatePlanAsync(
        ChannelActivationRequest request,
        CancellationToken cancellationToken = default);

    Task<ChannelActivationReceipt> ActivateAsync(
        ChannelActivationRequest request,
        string expectedPlanSha256,
        CancellationToken cancellationToken = default);

    Task<ChannelActivationReceipt> RollbackAsync(
        ChannelActivationReceipt receipt,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsChannelActivationService : IChannelActivationService
{
    private static readonly Regex GameDirectoryLine = new(
        "^(?<prefix>\\s*gameDir\\s*=\\s*)(?<value>[^;#\\r\\n]*)(?<suffix>.*)$",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Multiline);

    private readonly string localApplicationDataPath;

    public WindowsChannelActivationService(string? localApplicationDataPath = null)
    {
        this.localApplicationDataPath = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.GetFullPath(localApplicationDataPath);
    }

    public Task<ChannelActivationPlan> CreatePlanAsync(
        ChannelActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => CreatePlan(request, cancellationToken), cancellationToken);
    }

    public async Task<ChannelActivationReceipt> ActivateAsync(
        ChannelActivationRequest request,
        string expectedPlanSha256,
        CancellationToken cancellationToken = default)
    {
        var plan = await CreatePlanAsync(request, cancellationToken).ConfigureAwait(false);
        if (!plan.CanActivate)
        {
            return Failure(plan, plan.FailureCode, plan.FailureDetail);
        }

        if (!VariantHash.IsSha256(expectedPlanSha256) ||
            !string.Equals(plan.PlanSha256, expectedPlanSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                plan,
                ChannelActivationFailureCode.PlanChanged,
                "The fresh activation plan does not match the approved plan digest.");
        }

        if (plan.CreatesLauncherConfig)
        {
            var createdConfigPath = plan.LauncherConfigPath!;
            if (File.Exists(createdConfigPath))
            {
                return Failure(
                    plan,
                    ChannelActivationFailureCode.ConfigChanged,
                    "Launcher config appeared after planning.");
            }

            try
            {
                await CreateGameDirectoryConfigAsync(
                        createdConfigPath,
                        plan.TargetGameRootPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failure(
                    plan,
                    ChannelActivationFailureCode.WriteFailed,
                    $"Failed to create launcher config: {ex.GetType().Name}.");
            }

            var createdRoot = LauncherConfigReader.TryReadGameDirectory(createdConfigPath);
            if (!PathEquals(createdRoot, plan.TargetGameRootPath))
            {
                return Failure(
                    plan,
                    ChannelActivationFailureCode.VerificationFailed,
                    "Created launcher gameDir did not match the target.");
            }

            var createdHash = await ComputeFileSha256Async(createdConfigPath, cancellationToken)
                .ConfigureAwait(false);
            return Success(
                plan,
                configChanged: true,
                beforeHash: null,
                afterHash: createdHash,
                configCreated: true);
        }

        if (PathEquals(plan.PreviousGameRootPath, plan.TargetGameRootPath))
        {
            return Success(plan, configChanged: false, null, null);
        }

        var configPath = plan.LauncherConfigPath!;
        var beforeHash = await ComputeFileSha256Async(configPath, cancellationToken).ConfigureAwait(false);
        var freshPreviousRoot = LauncherConfigReader.TryReadGameDirectory(configPath);
        if (!PathEquals(freshPreviousRoot, plan.PreviousGameRootPath))
        {
            return Failure(
                plan,
                ChannelActivationFailureCode.ConfigChanged,
                "Launcher gameDir changed after planning.");
        }

        try
        {
            await WriteGameDirectoryAsync(configPath, plan.TargetGameRootPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return Failure(
                plan,
                ChannelActivationFailureCode.WriteFailed,
                $"Failed to update launcher gameDir: {ex.GetType().Name}.");
        }

        var observedRoot = LauncherConfigReader.TryReadGameDirectory(configPath);
        if (!PathEquals(observedRoot, plan.TargetGameRootPath))
        {
            return Failure(
                plan,
                ChannelActivationFailureCode.VerificationFailed,
                "Launcher gameDir did not match the target after activation.");
        }

        var afterHash = await ComputeFileSha256Async(configPath, cancellationToken).ConfigureAwait(false);
        return Success(plan, configChanged: true, beforeHash, afterHash);
    }

    public async Task<ChannelActivationReceipt> RollbackAsync(
        ChannelActivationReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!receipt.Succeeded || !receipt.ConfigChanged ||
            string.IsNullOrWhiteSpace(receipt.LauncherConfigPath) ||
            (!receipt.LauncherConfigCreated && string.IsNullOrWhiteSpace(receipt.PreviousGameRootPath)))
        {
            return receipt with
            {
                Succeeded = false,
                FailureCode = ChannelActivationFailureCode.RollbackFailed,
                FailureDetail = "The receipt does not contain a reversible launcher config change.",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        var currentRoot = LauncherConfigReader.TryReadGameDirectory(receipt.LauncherConfigPath);
        if (!PathEquals(currentRoot, receipt.TargetGameRootPath))
        {
            return receipt with
            {
                Succeeded = false,
                FailureCode = ChannelActivationFailureCode.ConfigChanged,
                FailureDetail = "Launcher gameDir changed after activation; rollback did not overwrite it.",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        if (receipt.LauncherConfigCreated)
        {
            var currentHash = await ComputeFileSha256Async(
                    receipt.LauncherConfigPath,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    currentHash,
                    receipt.ConfigSha256After,
                    StringComparison.OrdinalIgnoreCase))
            {
                return receipt with
                {
                    Succeeded = false,
                    FailureCode = ChannelActivationFailureCode.ConfigChanged,
                    FailureDetail = "Created launcher config changed after activation; rollback kept it.",
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                };
            }

            try
            {
                File.Delete(receipt.LauncherConfigPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return receipt with
                {
                    Succeeded = false,
                    FailureCode = ChannelActivationFailureCode.RollbackFailed,
                    FailureDetail = $"Failed to remove created launcher config: {ex.GetType().Name}.",
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                };
            }

            return receipt with
            {
                Succeeded = !File.Exists(receipt.LauncherConfigPath),
                FailureCode = File.Exists(receipt.LauncherConfigPath)
                    ? ChannelActivationFailureCode.RollbackFailed
                    : ChannelActivationFailureCode.None,
                FailureDetail = File.Exists(receipt.LauncherConfigPath)
                    ? "Created launcher config still exists after rollback."
                    : null,
                ConfigSha256After = null,
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        try
        {
            await WriteGameDirectoryAsync(
                    receipt.LauncherConfigPath,
                    receipt.PreviousGameRootPath!,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return receipt with
            {
                Succeeded = false,
                FailureCode = ChannelActivationFailureCode.RollbackFailed,
                FailureDetail = $"Failed to restore launcher gameDir: {ex.GetType().Name}.",
                CompletedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        var restoredRoot = LauncherConfigReader.TryReadGameDirectory(receipt.LauncherConfigPath);
        var restoredHash = await ComputeFileSha256Async(
                receipt.LauncherConfigPath,
                cancellationToken)
            .ConfigureAwait(false);
        return receipt with
        {
            Succeeded = PathEquals(restoredRoot, receipt.PreviousGameRootPath),
            FailureCode = PathEquals(restoredRoot, receipt.PreviousGameRootPath)
                ? ChannelActivationFailureCode.None
                : ChannelActivationFailureCode.RollbackFailed,
            FailureDetail = PathEquals(restoredRoot, receipt.PreviousGameRootPath)
                ? null
                : "Restored launcher gameDir did not match the previous path.",
            ConfigSha256After = restoredHash,
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private ChannelActivationPlan CreatePlan(
        ChannelActivationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Candidate is null || string.IsNullOrWhiteSpace(request.TargetGameRootPath))
        {
            return Rejected(ChannelActivationFailureCode.InvalidRequest, "Candidate and target root are required.");
        }

        var candidate = request.Candidate;
        if (candidate.Profile is null || candidate.State is not (
                InstallationCandidateState.Candidate or
                InstallationCandidateState.ReadyForStaticVerification))
        {
            return Rejected(
                ChannelActivationFailureCode.CandidateNotSelectable,
                "The selected candidate is not selectable.",
                candidate);
        }

        var targetRoot = NormalizeExistingDirectory(request.TargetGameRootPath);
        if (targetRoot is null)
        {
            return Rejected(
                ChannelActivationFailureCode.TargetRootMissing,
                "The target game root does not exist.",
                candidate);
        }

        if (!HasGameLayout(targetRoot))
        {
            return Rejected(
                ChannelActivationFailureCode.TargetLayoutMismatch,
                "The target root does not contain the required game layout.",
                candidate,
                targetRoot);
        }

        var expectedMarkerName = ExpectedMarkerName(candidate.Identity.DistributionChannel);
        if (expectedMarkerName is null)
        {
            return Rejected(
                ChannelActivationFailureCode.UnsupportedChannel,
                "The selected distribution channel is not supported by activation.",
                candidate,
                targetRoot);
        }

        var marker = ProductMarkerReader.TryRead(Path.Combine(targetRoot, "product.db"));
        if (marker is null)
        {
            return Rejected(
                ChannelActivationFailureCode.MarkerMissing,
                "The target product.db marker is missing or invalid.",
                candidate,
                targetRoot,
                expectedMarkerName);
        }

        if (!string.Equals(marker.Name, expectedMarkerName, StringComparison.OrdinalIgnoreCase))
        {
            return Rejected(
                ChannelActivationFailureCode.MarkerMismatch,
                "The target product.db marker belongs to another channel.",
                candidate,
                targetRoot,
                expectedMarkerName);
        }

        if (candidate.Identity.DistributionChannel is DistributionChannel.Steam &&
            !PathEquals(candidate.GameRootPath, targetRoot))
        {
            return Rejected(
                ChannelActivationFailureCode.SteamRootMismatch,
                "Steam activation must retain the selected game root.",
                candidate,
                targetRoot,
                expectedMarkerName);
        }

        var configPath = GetLauncherConfigPath(candidate.Identity.DistributionChannel);
        string? previousRoot = null;
        var createsConfig = false;
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return Rejected(
                ChannelActivationFailureCode.LauncherConfigMissing,
                "The channel launcher config was not found.",
                candidate,
                targetRoot,
                expectedMarkerName,
                configPath);
        }

        if (!File.Exists(configPath))
        {
            if (candidate.DiscoverySource is not ProfileDiscoverySource.ChannelStoreReceipt)
            {
                return Rejected(
                    ChannelActivationFailureCode.LauncherConfigMissing,
                    "The channel launcher config was not found.",
                    candidate,
                    targetRoot,
                    expectedMarkerName,
                    configPath);
            }

            createsConfig = true;
        }
        else
        {
            previousRoot = LauncherConfigReader.TryReadGameDirectory(configPath);
            if (previousRoot is null)
            {
                return Rejected(
                    ChannelActivationFailureCode.LauncherConfigInvalid,
                    "The channel launcher config does not contain a valid gameDir.",
                    candidate,
                    targetRoot,
                    expectedMarkerName,
                    configPath);
            }
        }

        var plan = new ChannelActivationPlan
        {
            CanActivate = true,
            DistributionChannel = candidate.Identity.DistributionChannel,
            ProfileId = candidate.ProfileId,
            TargetGameRootPath = targetRoot,
            LauncherConfigPath = configPath,
            PreviousGameRootPath = previousRoot,
            CreatesLauncherConfig = createsConfig,
            ExpectedMarkerName = expectedMarkerName,
        };
        return plan with { PlanSha256 = ComputePlanSha256(plan) };
    }

    private string? GetLauncherConfigPath(DistributionChannel channel) => channel switch
    {
        DistributionChannel.Official => Path.Combine(
            localApplicationDataPath,
            "InfinityNikki Launcher",
            "config.ini"),
        DistributionChannel.Bilibili => Path.Combine(
            localApplicationDataPath,
            "InfinityNikkiBili Launcher",
            "config.ini"),
        DistributionChannel.Steam => Path.Combine(
            localApplicationDataPath,
            "InfinityNikkiSteam Launcher",
            "config.ini"),
        _ => null,
    };

    private static string? ExpectedMarkerName(DistributionChannel channel) => channel switch
    {
        DistributionChannel.Official => "InfinityNikki Launcher",
        DistributionChannel.Bilibili => "InfinityNikkiBili Launcher",
        DistributionChannel.Steam => "InfinityNikkiSteam Launcher",
        _ => null,
    };

    private static bool HasGameLayout(string root) =>
        File.Exists(Path.Combine(root, "InfinityNikki.exe")) &&
        File.Exists(Path.Combine(
            root,
            "X6Game",
            "Binaries",
            "Win64",
            "X6Game-Win64-Shipping.exe"));

    private static string? NormalizeExistingDirectory(string path)
    {
        try
        {
            var normalized = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Directory.Exists(normalized) ? normalized : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static async Task WriteGameDirectoryAsync(
        string configPath,
        string gameRootPath,
        CancellationToken cancellationToken)
    {
        var document = await EncodedTextDocument.ReadAsync(configPath, cancellationToken)
            .ConfigureAwait(false);
        if (!GameDirectoryLine.IsMatch(document.Text))
        {
            throw new IOException("Launcher config does not contain gameDir.");
        }

        var normalizedValue = gameRootPath.Replace('\\', '/');
        var updated = GameDirectoryLine.Replace(
            document.Text,
            match => match.Groups["prefix"].Value + normalizedValue + match.Groups["suffix"].Value,
            count: 1);
        var temporaryPath = configPath + ".nikkiward-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await document.WriteAsync(temporaryPath, updated, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, configPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task CreateGameDirectoryConfigAsync(
        string configPath,
        string gameRootPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var normalizedValue = gameRootPath.Replace('\\', '/');
        var temporaryPath = configPath + ".nikkiward-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var document = new EncodedTextDocument(
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                [],
                string.Empty);
            await document.WriteAsync(
                    temporaryPath,
                    $"gameDir={normalizedValue}\r\n",
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, configPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ComputePlanSha256(ChannelActivationPlan plan)
    {
        var canonical = string.Join(
            "\n",
            ((int)plan.DistributionChannel).ToString(CultureInfo.InvariantCulture),
            plan.ProfileId,
            plan.TargetGameRootPath,
            plan.LauncherConfigPath ?? string.Empty,
            plan.PreviousGameRootPath ?? string.Empty,
            plan.CreatesLauncherConfig ? "1" : "0",
            plan.ExpectedMarkerName);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static async Task<string> ComputeFileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static bool PathEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right);
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static ChannelActivationPlan Rejected(
        ChannelActivationFailureCode code,
        string detail,
        InstallationProfileCandidate? candidate = null,
        string? targetRoot = null,
        string? expectedMarker = null,
        string? configPath = null) => new()
        {
            FailureCode = code,
            FailureDetail = detail,
            DistributionChannel = candidate?.Identity.DistributionChannel ?? DistributionChannel.Unknown,
            ProfileId = candidate?.ProfileId ?? string.Empty,
            TargetGameRootPath = targetRoot ?? string.Empty,
            LauncherConfigPath = configPath,
            ExpectedMarkerName = expectedMarker ?? string.Empty,
        };

    private static ChannelActivationReceipt Success(
        ChannelActivationPlan plan,
        bool configChanged,
        string? beforeHash,
        string? afterHash,
        bool configCreated = false) => new()
        {
            Succeeded = true,
            DistributionChannel = plan.DistributionChannel,
            ProfileId = plan.ProfileId,
            TargetGameRootPath = plan.TargetGameRootPath,
            LauncherConfigPath = plan.LauncherConfigPath,
            PreviousGameRootPath = plan.PreviousGameRootPath,
            ConfigSha256Before = beforeHash,
            ConfigSha256After = afterHash,
            PlanSha256 = plan.PlanSha256,
            ConfigChanged = configChanged,
            LauncherConfigCreated = configCreated,
        };

    private static ChannelActivationReceipt Failure(
        ChannelActivationPlan plan,
        ChannelActivationFailureCode code,
        string? detail) => new()
        {
            FailureCode = code,
            FailureDetail = detail,
            DistributionChannel = plan.DistributionChannel,
            ProfileId = plan.ProfileId,
            TargetGameRootPath = plan.TargetGameRootPath,
            LauncherConfigPath = plan.LauncherConfigPath,
            PreviousGameRootPath = plan.PreviousGameRootPath,
            PlanSha256 = plan.PlanSha256,
        };

    private sealed record EncodedTextDocument(
        Encoding Encoding,
        byte[] Preamble,
        string Text)
    {
        public static async Task<EncodedTextDocument> ReadAsync(
            string path,
            CancellationToken cancellationToken)
        {
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var (encoding, preambleLength) = DetectEncoding(bytes);
            var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            return new EncodedTextDocument(encoding, bytes[..preambleLength], text);
        }

        public async Task WriteAsync(
            string path,
            string text,
            CancellationToken cancellationToken)
        {
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            if (Preamble.Length > 0)
            {
                await stream.WriteAsync(Preamble, cancellationToken).ConfigureAwait(false);
            }

            var bytes = Encoding.GetBytes(text);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
        {
            if (bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()))
            {
                return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true), 3);
            }

            if (bytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()))
            {
                return (new UnicodeEncoding(false, true, true), 2);
            }

            if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.GetPreamble()))
            {
                return (new UnicodeEncoding(true, true, true), 2);
            }

            return (new UTF8Encoding(false, true), 0);
        }
    }
}
