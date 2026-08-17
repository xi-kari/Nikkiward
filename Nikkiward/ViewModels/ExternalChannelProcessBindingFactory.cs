using Nikkiward.Models;

namespace Nikkiward.ViewModels;

internal static class ExternalChannelProcessBindingFactory
{
    private static readonly string[] BilibiliAuxiliaryRelativePaths =
    [
        Path.Combine(
            "X6Game",
            "Plugins",
            "PaperSDK",
            "PSDKChannelBili",
            "Source",
            "ThirdParty",
            "PSDKChannelBiliLibrary",
            "x64",
            "Release",
            "BLPlatform64",
            "PCGamePlatform.exe"),
        Path.Combine(
            "X6Game",
            "Plugins",
            "PaperSDK",
            "PSDKChannelBili",
            "Source",
            "ThirdParty",
            "PSDKChannelBiliLibrary",
            "x64",
            "Release",
            "BLPlatform64",
            "game_security_protection.exe"),
    ];

    public static bool TryCreate(
        InstallationProfileCandidate candidate,
        ChannelLaunchReceipt receipt,
        out OfficialAssistedProcessBinding binding)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(receipt);
        binding = new OfficialAssistedProcessBinding();

        if (candidate.Profile is null ||
            candidate.State is not (
                InstallationCandidateState.Candidate or
                InstallationCandidateState.ReadyForStaticVerification) ||
            candidate.Identity.DistributionChannel is not (
                DistributionChannel.Bilibili or DistributionChannel.Steam) ||
            receipt is not
            {
                Succeeded: true,
                AttemptId: var attemptId,
                SubmittedProcessId: > 0,
                SubmittedProcessStartTimeUtc: not null,
            } ||
            attemptId == Guid.Empty ||
            !string.Equals(receipt.ProfileId, candidate.ProfileId, StringComparison.Ordinal) ||
            receipt.DistributionChannel != candidate.Identity.DistributionChannel)
        {
            return false;
        }

        if (!TryNormalizeDirectory(candidate.LauncherRootPath, out var launcherRootPath) ||
            !TryNormalizeDirectory(candidate.GameRootPath, out var gameRootPath) ||
            !TryNormalizeExistingFile(candidate.Profile.XStarterPath, out var rootExecutablePath) ||
            !TryNormalizeExistingFile(candidate.Profile.GameExecutablePath, out var bootstrapPath) ||
            !TryNormalizeExistingFile(candidate.Profile.ShippingExecutablePath, out var shippingPath) ||
            !IsUnderRoot(rootExecutablePath, launcherRootPath) ||
            !string.Equals(
                bootstrapPath,
                Path.Combine(gameRootPath, "InfinityNikki.exe"),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                shippingPath,
                Path.Combine(gameRootPath, "X6Game", "Binaries", "Win64", "X6Game-Win64-Shipping.exe"),
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bootstrapPath, shippingPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var auxiliaryPaths = Array.Empty<string>();
        if (candidate.Identity.DistributionChannel is DistributionChannel.Bilibili)
        {
            auxiliaryPaths = BilibiliAuxiliaryRelativePaths
                .Select(relativePath => Path.Combine(gameRootPath, relativePath))
                .ToArray();
            if (auxiliaryPaths.Any(path =>
                    !TryNormalizeExistingFile(path, out var normalizedPath) ||
                    !string.Equals(path, normalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        binding = new OfficialAssistedProcessBinding
        {
            ProfileId = candidate.ProfileId,
            AttemptId = receipt.AttemptId,
            RequestedAtUtc = receipt.SubmittedAtUtc,
            RootProcessId = receipt.SubmittedProcessId.Value,
            RootProcessStartTimeUtc = receipt.SubmittedProcessStartTimeUtc.Value,
            RootExecutablePath = rootExecutablePath,
            GameProcessPaths = [bootstrapPath, shippingPath],
            RunningProcessPath = shippingPath,
            AuxiliaryProcessPaths = auxiliaryPaths,
        };
        return true;
    }

    private static bool TryNormalizeDirectory(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.IsPathFullyQualified(normalized) && Directory.Exists(normalized);
        }
        catch (Exception ex) when (ex is
            ArgumentException or
            IOException or
            NotSupportedException or
            PathTooLongException or
            UnauthorizedAccessException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static bool TryNormalizeExistingFile(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(path);
            return Path.IsPathFullyQualified(normalized) && File.Exists(normalized);
        }
        catch (Exception ex) when (ex is
            ArgumentException or
            IOException or
            NotSupportedException or
            PathTooLongException or
            UnauthorizedAccessException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    private static bool IsUnderRoot(string path, string rootPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, path);
        return !Path.IsPathFullyQualified(relativePath) &&
            relativePath is not ".." &&
            !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
