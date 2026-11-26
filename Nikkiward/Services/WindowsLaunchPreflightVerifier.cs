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
        string? versionDirectoryName = null,
        CancellationToken cancellationToken = default);

    Task<LaunchPreflightResult> VerifyAsync(
        InstallationProfileCandidate candidate,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The leaf certificate identity of the first Authenticode signature on a
/// file. Read separately from WinVerifyTrust, which only reports chain status.
/// </summary>
internal readonly record struct SignerIdentity(
    string? Thumbprint,
    string? SubjectCommonName,
    string? SubjectSerialNumber);

/// <summary>
/// Injection seam for the two signature primitives so the trust gate itself is
/// testable against stub files; production always uses the WinVerifyTrust +
/// CreateFromSignedFile implementation.
/// </summary>
internal interface IAuthenticodeIdentityProbe
{
    AuthenticodeVerification VerifySignature(string filePath);

    SignerIdentity? ReadSignerIdentity(string filePath);

    string? ReadOriginalFilename(string filePath);
}

internal sealed class WindowsAuthenticodeIdentityProbe : IAuthenticodeIdentityProbe
{
    public static WindowsAuthenticodeIdentityProbe Instance { get; } = new();

    public AuthenticodeVerification VerifySignature(string filePath) =>
        OperatingSystem.IsWindows()
            ? WindowsAuthenticodeVerifier.Verify(filePath)
            : new AuthenticodeVerification(AuthenticodeSignatureStatus.Error, "non-windows");

    public SignerIdentity? ReadSignerIdentity(string filePath) =>
        SignerIdentityReader.TryRead(filePath);

    public string? ReadOriginalFilename(string filePath)
    {
        try
        {
            var value = FileVersionInfo.GetVersionInfo(filePath).OriginalFilename;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}

/// <summary>
/// Static, read-only identity gate. It deliberately stops before process
/// creation; runtime ownership and observation belong to a later provider
/// layer. Since ContractVersion 2 the per-component gate is structural +
/// publisher identity (file exists, stable while read, Authenticode Valid,
/// signer in the contract's trusted list); hashes and file versions are
/// recorded on the receipts as diagnostics but no longer block launching,
/// so a game update does not require a new contract.
/// </summary>
public sealed class WindowsLaunchPreflightVerifier : ILaunchPreflightVerifier
{
    private readonly IAuthenticodeIdentityProbe _identityProbe;

    public WindowsLaunchPreflightVerifier()
        : this(WindowsAuthenticodeIdentityProbe.Instance)
    {
    }

    internal WindowsLaunchPreflightVerifier(IAuthenticodeIdentityProbe identityProbe)
    {
        ArgumentNullException.ThrowIfNull(identityProbe);
        _identityProbe = identityProbe;
    }

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
            TryGetVersionDirectoryName(candidate.Provider.BackendExecutablePath),
            cancellationToken);
    }

    public async Task<LaunchPreflightResult> VerifyAsync(
        string launcherRootPath,
        string gameRootPath,
        string contractId,
        string? versionDirectoryName = null,
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

        // The contract must be materialized for the same version directory the
        // discovery layer bound, otherwise the coordinator's path-derivation
        // equality check fails closed.
        var version = versionDirectoryName ?? LaunchProviderCatalog.CnLauncherVersion;
        if (!LaunchProviderCatalog.TryGet(DistributionChannel.Official, version, out var contract))
        {
            return Failed(
                LaunchPreflightFailureCode.InvalidContract,
                $"版本目录 {version} 无法实例化 provider contract。");
        }

        if (!contract.ArgumentList.SequenceEqual(["-skiplauncher"], StringComparer.Ordinal) ||
            !string.Equals(contract.WorkingDirectoryRole, "LauncherRoot", StringComparison.Ordinal))
        {
            return Failed(
                LaunchPreflightFailureCode.InvalidContract,
                "provider contract 的工作目录或参数 preset 不是冻结值。");
        }

        var identity = await VerifyComponentsAsync(contract.RequiredComponents, launcherRoot, gameRoot, cancellationToken)
            .ConfigureAwait(false);
        if (!identity.StaticIdentityPassed)
        {
            return identity;
        }

        var componentResults = identity.Components;
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

    internal async Task<LaunchPreflightResult> VerifyComponentsAsync(
        IReadOnlyList<BinaryIdentityRequirement> requirements,
        string launcherRoot,
        string gameRoot,
        CancellationToken cancellationToken)
    {
        var componentResults = new List<PreflightComponentResult>();
        foreach (var requirement in requirements)
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
                var code = component.FailureCode is LaunchPreflightFailureCode.None
                    ? LaunchPreflightFailureCode.RequiredComponentMissing
                    : component.FailureCode;
                return Failed(
                    code,
                    component.FailureDetail ?? "组件身份检查失败。",
                    componentResults);
            }
        }

        return new LaunchPreflightResult { StaticIdentityPassed = true, Components = componentResults };
    }

    internal static string? TryGetVersionDirectoryName(string? backendExecutablePath)
    {
        if (string.IsNullOrWhiteSpace(backendExecutablePath))
        {
            return null;
        }

        try
        {
            var name = Path.GetFileName(Path.GetDirectoryName(backendExecutablePath));
            return LaunchProviderCatalog.IsCnVersionDirectoryName(name) ? name : null;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return null;
        }
    }

    internal async Task<PreflightComponentResult> VerifyComponentAsync(
        BinaryIdentityRequirement requirement,
        string filePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return FailedComponent(
                requirement,
                filePath,
                LaunchPreflightFailureCode.RequiredComponentMissing,
                "必需文件不存在。");
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
                return FailedComponent(
                    requirement,
                    filePath,
                    LaunchPreflightFailureCode.BinaryIdentityDrift,
                    "文件身份在读取前后发生变化。");
            }

            // Recorded for diagnostics only; content pinning is gone on purpose
            // so a game update no longer disables launching.
            var sha256 = Convert.ToHexString(digest);
            var version = FileVersionInfo.GetVersionInfo(filePath);
            var fileVersion = NullIfWhiteSpace(version.FileVersion);
            var productVersion = NullIfWhiteSpace(version.ProductVersion);

            var signature = _identityProbe.VerifySignature(filePath);
            if (signature.Status is not AuthenticodeSignatureStatus.Valid)
            {
                return FailedComponent(
                    requirement,
                    filePath,
                    LaunchPreflightFailureCode.SignatureInvalid,
                    $"签名状态为 {signature.Status}。",
                    sha256,
                    fileVersion,
                    productVersion,
                    signature.Status);
            }

            var identity = _identityProbe.ReadSignerIdentity(filePath);
            if (identity is not { } signer || string.IsNullOrWhiteSpace(signer.Thumbprint))
            {
                return FailedComponent(
                    requirement,
                    filePath,
                    LaunchPreflightFailureCode.SignerMismatch,
                    "无法读取签名者证书身份。",
                    sha256,
                    fileVersion,
                    productVersion,
                    signature.Status);
            }

            if (!IsTrustedSigner(requirement.TrustedSigners, signer))
            {
                return FailedComponent(
                    requirement,
                    filePath,
                    LaunchPreflightFailureCode.SignerMismatch,
                    $"签名者不在可信列表：指纹 {signer.Thumbprint}，CN={signer.SubjectCommonName ?? "未知"}。",
                    sha256,
                    fileVersion,
                    productVersion,
                    signature.Status,
                    signer);
            }

            // Secondary anchor after the publisher gate: stops a *different*
            // binary from the same trusted publisher being renamed into an
            // elevated slot. Only launcher-side components pin this.
            if (requirement.ExpectedOriginalFilename is not null)
            {
                var originalFilename = _identityProbe.ReadOriginalFilename(filePath);
                if (!string.Equals(
                        originalFilename,
                        requirement.ExpectedOriginalFilename,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return FailedComponent(
                        requirement,
                        filePath,
                        LaunchPreflightFailureCode.SignerMismatch,
                        $"组件 OriginalFilename 与契约不符（实际 {originalFilename ?? "空"}）；该槽位放置的不是官方 {requirement.ExpectedOriginalFilename}。",
                        sha256,
                        fileVersion,
                        productVersion,
                        signature.Status,
                        signer);
                }
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
                ActualSignerThumbprint = signer.Thumbprint,
                ActualSignerSubjectCommonName = signer.SubjectCommonName,
                ActualSignerSubjectSerialNumber = signer.SubjectSerialNumber,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return FailedComponent(
                requirement,
                filePath,
                LaunchPreflightFailureCode.RequiredComponentMissing,
                $"读取文件失败：{ex.GetType().Name}。");
        }
    }

    /// <summary>
    /// Thumbprint hit is the fast path; a whole-value subject match (CN plus
    /// serialNumber when pinned) keeps installs working across a publisher
    /// certificate rotation. An empty trusted list fails closed. Shared with
    /// the --emit-contract signature audit so both report the same verdict.
    /// </summary>
    internal static bool IsTrustedSigner(
        IReadOnlyList<TrustedSignerRequirement> trustedSigners,
        SignerIdentity signer)
    {
        foreach (var trusted in trustedSigners)
        {
            if (signer.Thumbprint is not null &&
                trusted.Thumbprints.Contains(signer.Thumbprint, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (trusted.SubjectCommonName is not null &&
                string.Equals(trusted.SubjectCommonName, signer.SubjectCommonName, StringComparison.OrdinalIgnoreCase) &&
                (trusted.SubjectSerialNumber is null ||
                 string.Equals(trusted.SubjectSerialNumber, signer.SubjectSerialNumber, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static PreflightComponentResult FailedComponent(
        BinaryIdentityRequirement requirement,
        string filePath,
        LaunchPreflightFailureCode failureCode,
        string detail,
        string? sha256 = null,
        string? fileVersion = null,
        string? productVersion = null,
        AuthenticodeSignatureStatus signatureStatus = AuthenticodeSignatureStatus.NotChecked,
        SignerIdentity? signer = null) => new()
        {
            ComponentId = requirement.ComponentId,
            FilePath = filePath,
            Passed = false,
            ActualSha256 = sha256,
            ActualFileVersion = fileVersion,
            ActualProductVersion = productVersion,
            SignatureStatus = signatureStatus,
            ActualSignerThumbprint = signer?.Thumbprint,
            ActualSignerSubjectCommonName = signer?.SubjectCommonName,
            ActualSignerSubjectSerialNumber = signer?.SubjectSerialNumber,
            FailureCode = failureCode,
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

    internal static bool ContainsReparsePointBetween(string root, string fullPath)
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
    private const string SubjectCommonNameOid = "2.5.4.3";
    private const string SubjectSerialNumberOid = "2.5.4.5";

    public static SignerIdentity? TryRead(string filePath)
    {
        try
        {
            // X509CertificateLoader cannot read the Authenticode certificate out
            // of a PE; CreateFromSignedFile is the only in-box route. It returns
            // the leaf certificate of the first signature.
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057
            // The CN is read from the exact 2.5.4.3 RDN rather than
            // GetNameInfo(SimpleName), whose CN→OU→O→Email fallback would let a
            // certificate without any CN match a pinned CommonName via its O=.
            return new SignerIdentity(
                certificate.Thumbprint,
                TryGetSubjectRdnValue(certificate, SubjectCommonNameOid),
                TryGetSubjectRdnValue(certificate, SubjectSerialNumberOid));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? TryGetThumbprint(string filePath) => TryRead(filePath)?.Thumbprint;

    private static string? TryGetSubjectRdnValue(X509Certificate2 certificate, string oid)
    {
        try
        {
            foreach (var rdn in certificate.SubjectName.EnumerateRelativeDistinguishedNames())
            {
                if (rdn.HasMultipleElements)
                {
                    continue;
                }

                if (string.Equals(rdn.GetSingleElementType().Value, oid, StringComparison.Ordinal))
                {
                    return NullIfWhiteSpace(rdn.GetSingleElementValue());
                }
            }
        }
        catch (CryptographicException)
        {
        }

        return null;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
