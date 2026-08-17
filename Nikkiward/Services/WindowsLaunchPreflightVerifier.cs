using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Nikkiward.Models;

namespace Nikkiward.Services;

public interface ILaunchPreflightVerifier
{
    Task<LaunchPreflightResult> VerifyAsync(
        string launcherRootPath,
        string gameRootPath,
        string contractId,
        CancellationToken cancellationToken = default);

    Task<LaunchPreflightResult> VerifyAsync(
        InstallationProfileCandidate candidate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Static, read-only identity gate. It deliberately stops before process
/// creation; runtime ownership and observation belong to a later provider
/// layer.
/// </summary>
public sealed class WindowsLaunchPreflightVerifier : ILaunchPreflightVerifier
{
    public Task<LaunchPreflightResult> VerifyAsync(
        InstallationProfileCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (string.IsNullOrWhiteSpace(candidate.LauncherRootPath) ||
            string.IsNullOrWhiteSpace(candidate.GameRootPath) ||
            candidate.Provider is null)
        {
            return Task.FromResult(Failed(
                LaunchPreflightFailureCode.InvalidContract,
                "候选 profile 缺少 launcher/game root 或 provider binding。"));
        }

        return VerifyAsync(
            candidate.LauncherRootPath,
            candidate.GameRootPath,
            candidate.Provider.ProviderId,
            cancellationToken);
    }

    public async Task<LaunchPreflightResult> VerifyAsync(
        string launcherRootPath,
        string gameRootPath,
        string contractId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(contractId, LaunchProviderCatalog.CnWindows131ContractId, StringComparison.Ordinal))
        {
            return Failed(
                LaunchPreflightFailureCode.InvalidContract,
                "未找到不可变的 provider contract。");
        }

        if (!TryNormalizeDirectory(launcherRootPath, out var launcherRoot) ||
            !TryNormalizeDirectory(gameRootPath, out var gameRoot))
        {
            return Failed(
                LaunchPreflightFailureCode.RequiredComponentMissing,
                "launcher root 或 game root 不存在。");
        }

        var contract = LaunchProviderCatalog.CnWindows131;
        if (!contract.ArgumentList.SequenceEqual(["-skiplauncher"], StringComparer.Ordinal) ||
            !string.Equals(contract.WorkingDirectoryRole, "LauncherRoot", StringComparison.Ordinal))
        {
            return Failed(
                LaunchPreflightFailureCode.InvalidContract,
                "provider contract 的工作目录或参数 preset 不是冻结值。");
        }

        var componentResults = new List<PreflightComponentResult>();
        foreach (var requirement in contract.RequiredComponents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = string.Equals(requirement.RootRole, "GameRoot", StringComparison.Ordinal)
                ? gameRoot
                : launcherRoot;
            if (!TryResolveWithinRoot(root, requirement.RelativePath, out var filePath))
            {
                return Failed(
                    LaunchPreflightFailureCode.PathOutsideExpectedRoot,
                    $"{requirement.ComponentId} 的派生路径越出 {requirement.RootRole}。",
                    componentResults);
            }

            if (ContainsReparsePointBetween(root, filePath))
            {
                return Failed(
                    LaunchPreflightFailureCode.ReparsePointRejected,
                    $"{requirement.ComponentId} 的路径链包含 junction/symlink。",
                    componentResults);
            }

            var component = await VerifyComponentAsync(requirement, filePath, cancellationToken)
                .ConfigureAwait(false);
            componentResults.Add(component);
            if (!component.Passed)
            {
                var code = component.FailureDetail?.StartsWith("签名", StringComparison.Ordinal) == true
                    ? LaunchPreflightFailureCode.SignatureInvalid
                    : component.FailureDetail?.StartsWith("签名者", StringComparison.Ordinal) == true
                        ? LaunchPreflightFailureCode.SignerMismatch
                        : component.FailureDetail?.StartsWith("版本", StringComparison.Ordinal) == true
                            ? LaunchPreflightFailureCode.VersionMismatch
                            : component.FailureDetail?.StartsWith("文件身份", StringComparison.Ordinal) == true
                                ? LaunchPreflightFailureCode.BinaryIdentityDrift
                                : component.FailureDetail?.StartsWith("SHA-256", StringComparison.Ordinal) == true
                                    ? LaunchPreflightFailureCode.ArtifactHashMismatch
                                    : LaunchPreflightFailureCode.RequiredComponentMissing;
                return Failed(
                    code,
                    component.FailureDetail ?? "组件身份检查失败。",
                    componentResults);
            }
        }

        if (contract.ProductMarker is not null)
        {
            var markerRoot = string.Equals(contract.ProductMarker.RootRole, "GameRoot", StringComparison.Ordinal)
                ? gameRoot
                : launcherRoot;
            if (!TryResolveWithinRoot(markerRoot, contract.ProductMarker.RelativePath, out var markerPath))
            {
                return Failed(
                    LaunchPreflightFailureCode.PathOutsideExpectedRoot,
                    "product marker 路径越出预期根目录。",
                    componentResults);
            }

            var marker = ProductMarkerReader.TryRead(markerPath);
            if (marker is null)
            {
                return Failed(
                    LaunchPreflightFailureCode.MarkerMissing,
                    "product marker 不存在或不是允许的 JSON 结构。",
                    componentResults);
            }

            if (contract.ProductMarker.ExpectedName is not null &&
                !string.Equals(marker.Name, contract.ProductMarker.ExpectedName, StringComparison.OrdinalIgnoreCase))
            {
                return Failed(
                    LaunchPreflightFailureCode.MarkerMismatch,
                    "product marker 渠道身份不匹配。",
                    componentResults);
            }
        }

        var plan = new LaunchPlan
        {
            ProviderId = contract.ContractId,
            ProviderExecutablePath = Path.Combine(launcherRoot, contract.BackendRelativeExecutablePath),
            WorkingDirectory = launcherRoot,
            ArgumentList = contract.ArgumentList,
        };

        if (!contract.ExecutionEnabled)
        {
            return new LaunchPreflightResult
            {
                StaticIdentityPassed = true,
                ExecutionAllowed = false,
                FailureCode = LaunchPreflightFailureCode.ExecutionGateClosed,
                FailureDetail = "静态身份通过，但当前 contract 的执行门仍关闭。",
                Contract = contract,
                Plan = null,
                Components = componentResults,
                VerifiedAtUtc = DateTimeOffset.UtcNow,
            };
        }

        return new LaunchPreflightResult
        {
            StaticIdentityPassed = true,
            ExecutionAllowed = true,
            FailureCode = LaunchPreflightFailureCode.None,
            Contract = contract,
            Plan = plan,
            Components = componentResults,
            VerifiedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static async Task<PreflightComponentResult> VerifyComponentAsync(
        BinaryIdentityRequirement requirement,
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return FailedComponent(requirement, filePath, "必需文件不存在。");
        }

        try
        {
            var before = new FileInfo(filePath);
            var beforeLength = before.Length;
            var beforeWrite = before.LastWriteTimeUtc;
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            var after = new FileInfo(filePath);
            if (beforeLength != after.Length || beforeWrite != after.LastWriteTimeUtc)
            {
                return FailedComponent(requirement, filePath, "文件身份在读取前后发生变化。");
            }

            var sha256 = Convert.ToHexString(digest);
            if (!string.Equals(sha256, requirement.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return FailedComponent(requirement, filePath, $"SHA-256 不匹配（实际 {sha256}）。", sha256);
            }

            var version = FileVersionInfo.GetVersionInfo(filePath);
            var fileVersion = NullIfWhiteSpace(version.FileVersion);
            var productVersion = NullIfWhiteSpace(version.ProductVersion);
            if (requirement.ExpectedFileVersion is not null &&
                !string.Equals(fileVersion, requirement.ExpectedFileVersion, StringComparison.OrdinalIgnoreCase))
            {
                return FailedComponent(requirement, filePath, "版本不匹配。", sha256, fileVersion, productVersion);
            }

            if (requirement.ExpectedProductVersion is not null &&
                !string.Equals(productVersion, requirement.ExpectedProductVersion, StringComparison.OrdinalIgnoreCase))
            {
                return FailedComponent(requirement, filePath, "版本不匹配。", sha256, fileVersion, productVersion);
            }

            var signature = OperatingSystem.IsWindows()
                ? WindowsAuthenticodeVerifier.Verify(filePath)
                : new AuthenticodeVerification(AuthenticodeSignatureStatus.Error, "non-windows");
            if (signature.Status is not AuthenticodeSignatureStatus.Valid)
            {
                return FailedComponent(
                    requirement,
                    filePath,
                    $"签名状态为 {signature.Status}。",
                    sha256,
                    fileVersion,
                    productVersion,
                    signature.Status);
            }

            var signerThumbprint = SignerIdentityReader.TryGetThumbprint(filePath);
            if (requirement.ExpectedSignerThumbprint is not null &&
                !string.Equals(
                    signerThumbprint,
                    requirement.ExpectedSignerThumbprint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return FailedComponent(
                    requirement,
                    filePath,
                    "签名者身份不匹配。",
                    sha256,
                    fileVersion,
                    productVersion,
                    signature.Status,
                    signerThumbprint);
            }

            return new PreflightComponentResult
            {
                ComponentId = requirement.ComponentId,
                FilePath = filePath,
                Passed = true,
                ActualSha256 = sha256,
                ActualFileVersion = fileVersion,
                ActualProductVersion = productVersion,
                SignatureStatus = signature.Status,
                ActualSignerThumbprint = signerThumbprint,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return FailedComponent(requirement, filePath, $"读取文件失败：{ex.GetType().Name}。");
        }
    }

    private static PreflightComponentResult FailedComponent(
        BinaryIdentityRequirement requirement,
        string filePath,
        string detail,
        string? sha256 = null,
        string? fileVersion = null,
        string? productVersion = null,
        AuthenticodeSignatureStatus signatureStatus = AuthenticodeSignatureStatus.NotChecked,
        string? signerThumbprint = null) => new()
        {
            ComponentId = requirement.ComponentId,
            FilePath = filePath,
            Passed = false,
            ActualSha256 = sha256,
            ActualFileVersion = fileVersion,
            ActualProductVersion = productVersion,
            SignatureStatus = signatureStatus,
            ActualSignerThumbprint = signerThumbprint,
            FailureDetail = detail,
        };

    private static LaunchPreflightResult Failed(
        LaunchPreflightFailureCode code,
        string detail,
        IReadOnlyList<PreflightComponentResult>? components = null) => new()
        {
            StaticIdentityPassed = false,
            ExecutionAllowed = false,
            FailureCode = code,
            FailureDetail = detail,
            Components = components ?? Array.Empty<PreflightComponentResult>(),
            VerifiedAtUtc = DateTimeOffset.UtcNow,
        };

    private static bool TryNormalizeDirectory(string path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            return Directory.Exists(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryResolveWithinRoot(
        string root,
        string relativePath,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(root) || Path.IsPathRooted(relativePath))
        {
            return false;
        }

        try
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return fullPath.StartsWith(normalizedRoot, comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool ContainsReparsePointBetween(string root, string fullPath)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var current = new DirectoryInfo(Path.GetDirectoryName(fullPath)!);
            while (current is not null)
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }

                if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), normalizedRoot,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    break;
                }

                current = current.Parent;
            }

            var file = new FileInfo(fullPath);
            return (file.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

internal static class SignerIdentityReader
{
    public static string? TryGetThumbprint(string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057
            return certificate.Thumbprint;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
