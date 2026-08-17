using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Nikkiward.Models;

namespace Nikkiward.Services;

public sealed class WindowsInstallationInspector : IInstallationInspector
{
    public async Task<IReadOnlyList<ComponentVerification>> InspectAsync(
        LaunchProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var components = new[]
        {
            new ComponentDescriptor("official-launcher", "Official launcher", profile.LauncherPath),
            new ComponentDescriptor("official-backend", "Official xstarter backend", profile.XStarterPath),
            new ComponentDescriptor("game-bootstrap", "Infinity Nikki bootstrap", profile.GameExecutablePath),
            new ComponentDescriptor("game-client", "Infinity Nikki game client", profile.ShippingExecutablePath),
            new ComponentDescriptor("anti-cheat", "Anti-cheat service", profile.AntiCheatExecutablePath),
        };

        var inspections = components.Select(component => InspectComponentAsync(
            component.ComponentId,
            component.DisplayName,
            component.FilePath,
            cancellationToken));

        return await Task.WhenAll(inspections).ConfigureAwait(false);
    }

    public async Task<ComponentVerification> InspectComponentAsync(
        string componentId,
        string displayName,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var inspectedAtUtc = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(componentId))
        {
            throw new ArgumentException("A component identifier is required.", nameof(componentId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("A component display name is required.", nameof(displayName));
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Failed(componentId, displayName, filePath, inspectedAtUtc, "No file path is configured.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failed(
                componentId,
                displayName,
                filePath,
                inspectedAtUtc,
                FormatError("path", ex));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                return Failed(
                    componentId,
                    displayName,
                    fullPath,
                    inspectedAtUtc,
                    "The configured path points to a directory, not a file.");
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new ComponentVerification
            {
                ComponentId = componentId,
                DisplayName = displayName,
                FilePath = fullPath,
                Exists = false,
                InspectionSucceeded = true,
                InspectedAtUtc = inspectedAtUtc,
            };
        }
        catch (Exception ex) when (IsFileInspectionException(ex))
        {
            return Failed(
                componentId,
                displayName,
                fullPath,
                inspectedAtUtc,
                FormatError("existence", ex));
        }

        var errors = new List<string>();
        long? fileSizeBytes = null;
        DateTimeOffset? lastWriteTimeUtc = null;
        string? fileVersion = null;
        string? productVersion = null;
        string? sha256 = null;
        var signatureStatus = AuthenticodeSignatureStatus.NotChecked;
        string? signatureStatusCode = null;

        try
        {
            var fileInfo = new FileInfo(fullPath);
            fileSizeBytes = fileInfo.Length;
            lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
        }
        catch (Exception ex) when (IsFileInspectionException(ex))
        {
            errors.Add(FormatError("metadata", ex));
        }

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(fullPath);
            fileVersion = NullIfWhiteSpace(versionInfo.FileVersion);
            productVersion = NullIfWhiteSpace(versionInfo.ProductVersion);
        }
        catch (Exception ex) when (IsFileInspectionException(ex))
        {
            errors.Add(FormatError("version", ex));
        }

        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            sha256 = Convert.ToHexString(digest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsFileInspectionException(ex) || ex is CryptographicException)
        {
            errors.Add(FormatError("sha256", ex));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var signature = WindowsAuthenticodeVerifier.Verify(fullPath);
            signatureStatus = signature.Status;
            signatureStatusCode = signature.NativeStatusCode;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            signatureStatus = AuthenticodeSignatureStatus.Error;
            errors.Add(FormatError("authenticode", ex));
        }

        return new ComponentVerification
        {
            ComponentId = componentId,
            DisplayName = displayName,
            FilePath = fullPath,
            Exists = true,
            InspectionSucceeded = errors.Count == 0,
            FileSizeBytes = fileSizeBytes,
            LastWriteTimeUtc = lastWriteTimeUtc,
            FileVersion = fileVersion,
            ProductVersion = productVersion,
            Sha256 = sha256,
            SignatureStatus = signatureStatus,
            SignatureStatusCode = signatureStatusCode,
            Error = errors.Count == 0 ? null : string.Join(" | ", errors),
            InspectedAtUtc = inspectedAtUtc,
        };
    }

    private static ComponentVerification Failed(
        string componentId,
        string displayName,
        string filePath,
        DateTimeOffset inspectedAtUtc,
        string error)
    {
        return new ComponentVerification
        {
            ComponentId = componentId,
            DisplayName = displayName,
            FilePath = filePath,
            Exists = false,
            InspectionSucceeded = false,
            Error = error,
            InspectedAtUtc = inspectedAtUtc,
        };
    }

    private static bool IsFileInspectionException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    private static string FormatError(string operation, Exception exception) =>
        $"{operation}: {exception.GetType().Name}: {exception.Message}";

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record ComponentDescriptor(string ComponentId, string DisplayName, string FilePath);
}

internal readonly record struct AuthenticodeVerification(
    AuthenticodeSignatureStatus Status,
    string NativeStatusCode);

internal static class WindowsAuthenticodeVerifier
{
    private static readonly Guid WinTrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionIgnore = 0;
    private const uint WtdRevocationCheckNone = 0x10;
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;
    private const uint WtdUiContextExecute = 0;

    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    private const int TrustESubjectNotTrusted = unchecked((int)0x800B0004);
    private const int TrustEExplicitDistrust = unchecked((int)0x800B0111);
    private const int TrustEBadDigest = unchecked((int)0x80096010);
    private const int NteBadSignature = unchecked((int)0x80090006);
    private const int CertEExpired = unchecked((int)0x800B0101);
    private const int CertEUntrustedRoot = unchecked((int)0x800B0109);
    private const int CertEChaining = unchecked((int)0x800B010A);
    private const int CertERevoked = unchecked((int)0x800B010C);

    public static AuthenticodeVerification Verify(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var filePathPointer = IntPtr.Zero;
        var fileInfoPointer = IntPtr.Zero;
        var trustDataPointer = IntPtr.Zero;

        try
        {
            filePathPointer = Marshal.StringToCoTaskMemUni(filePath);

            var fileInfo = new WinTrustFileInfo
            {
                StructureSize = checked((uint)Marshal.SizeOf<WinTrustFileInfo>()),
                FilePath = filePathPointer,
            };

            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);

            var trustData = new WinTrustData
            {
                StructureSize = checked((uint)Marshal.SizeOf<WinTrustData>()),
                UiChoice = WtdUiNone,
                RevocationChecks = WtdRevokeNone,
                UnionChoice = WtdChoiceFile,
                FileInfo = fileInfoPointer,
                StateAction = WtdStateActionIgnore,
                ProviderFlags = WtdRevocationCheckNone | WtdCacheOnlyUrlRetrieval,
                UiContext = WtdUiContextExecute,
            };

            trustDataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, trustDataPointer, false);

            var action = WinTrustActionGenericVerifyV2;
            var nativeStatus = WinVerifyTrust(new IntPtr(-1), ref action, trustDataPointer);

            return new AuthenticodeVerification(
                Classify(nativeStatus),
                $"0x{unchecked((uint)nativeStatus):X8}");
        }
        finally
        {
            if (trustDataPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(trustDataPointer);
            }

            if (fileInfoPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfoPointer);
            }

            if (filePathPointer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(filePathPointer);
            }
        }
    }

    private static AuthenticodeSignatureStatus Classify(int nativeStatus)
    {
        return nativeStatus switch
        {
            0 => AuthenticodeSignatureStatus.Valid,
            TrustENoSignature or TrustESubjectFormUnknown => AuthenticodeSignatureStatus.NotSigned,
            TrustEBadDigest or NteBadSignature => AuthenticodeSignatureStatus.Invalid,
            TrustESubjectNotTrusted or TrustEExplicitDistrust or CertEExpired or
                CertEUntrustedRoot or CertEChaining or CertERevoked =>
                AuthenticodeSignatureStatus.Untrusted,
            _ => AuthenticodeSignatureStatus.Error,
        };
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(
        IntPtr windowHandle,
        ref Guid actionId,
        IntPtr trustData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint StructureSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructureSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
