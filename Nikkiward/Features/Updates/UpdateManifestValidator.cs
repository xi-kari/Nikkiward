using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace Nikkiward.Features.Updates;

internal static partial class UpdateManifestValidator
{
    private const string RepositoryPath = "/xi-kari/Nikkiward/";

    public static ValidatedUpdate Validate(
        UpdateManifest manifest,
        SelectedRelease selected)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw Invalid("Unsupported update manifest schema.");
        }
        if (!string.Equals(manifest.Product, "Nikkiward", StringComparison.Ordinal))
        {
            throw Invalid("The update manifest product does not match Nikkiward.");
        }
        if (!NuGetVersion.TryParse(manifest.Version, out var manifestVersion) || manifestVersion is null ||
            !VersionComparer.VersionReleaseMetadata.Equals(manifestVersion, selected.Version))
        {
            throw Invalid("The update manifest version does not match its GitHub Release tag.");
        }
        if (!string.Equals(manifest.Tag, selected.Release.TagName, StringComparison.Ordinal) ||
            !string.Equals(manifest.Tag, $"v{manifest.Version}", StringComparison.Ordinal))
        {
            throw Invalid("The update manifest tag does not match its GitHub Release.");
        }

        var expectedChannel = selected.Release.Prerelease ? "preview" : "stable";
        if (!string.Equals(manifest.Channel, expectedChannel, StringComparison.Ordinal))
        {
            throw Invalid("The update manifest channel does not match the GitHub prerelease state.");
        }
        if (!CommitShaRegex().IsMatch(manifest.CommitSha))
        {
            throw Invalid("The update manifest commit is not a full SHA-1 identifier.");
        }
        if (!NuGetVersion.TryParse(manifest.MinimumSupportedVersion, out var minimumVersion) || minimumVersion is null ||
            VersionComparer.VersionRelease.Compare(minimumVersion, manifestVersion) > 0)
        {
            throw Invalid("The minimum supported version is invalid.");
        }
        if (manifest.PublishedAtUtc == default || manifest.PublishedAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid("The update manifest publication time must be UTC.");
        }

        ValidateSignatureShape(manifest.Signature);
        ValidatePackageShape(manifest.Package);

        var matchingAssets = selected.Release.Assets
            .Where(asset => string.Equals(asset.Name, manifest.Package.FileName, StringComparison.Ordinal))
            .ToArray();
        if (matchingAssets.Length != 1)
        {
            throw Invalid("The GitHub Release must contain exactly one matching package asset.");
        }

        var packageAsset = matchingAssets[0];
        if (packageAsset.Size != manifest.Package.Size)
        {
            throw Invalid("The package size differs between GitHub and the update manifest.");
        }
        ValidateDigest(packageAsset.Digest, manifest.Package.Sha256, "package");

        var releaseUri = EnsureTrustedGitHubUri(selected.Release.HtmlUrl, "releases/tag/");
        _ = EnsureTrustedGitHubUri(packageAsset.BrowserDownloadUrl, "releases/download/");
        return new ValidatedUpdate(manifestVersion, releaseUri, packageAsset);
    }

    public static Uri EnsureTrustedGitHubUri(string value, string releasePath)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(RepositoryPath + releasePath, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw Invalid("The GitHub Release URL is outside the trusted repository path.");
        }

        return uri;
    }

    public static void ValidateDigest(string? digest, string expectedSha256, string assetName)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return;
        }

        var expectedDigest = $"sha256:{expectedSha256.ToLowerInvariant()}";
        if (!string.Equals(digest, expectedDigest, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid($"The GitHub {assetName} digest does not match the update manifest.");
        }
    }

    private static void ValidatePackageShape(UpdatePackageManifest package)
    {
        if (!string.Equals(package.FileName, "Nikkiward-win-x64.zip", StringComparison.Ordinal) ||
            !string.Equals(package.RuntimeIdentifier, "win-x64", StringComparison.Ordinal) ||
            !string.Equals(package.Format, "zip", StringComparison.Ordinal) ||
            package.Size <= 0 ||
            !Sha256Regex().IsMatch(package.Sha256))
        {
            throw Invalid("The update package description is invalid.");
        }
    }

    private static void ValidateSignatureShape(UpdateManifestSignature? signature)
    {
        if (signature is not null &&
            (!string.Equals(signature.Algorithm, "ed25519", StringComparison.Ordinal) ||
             string.IsNullOrWhiteSpace(signature.KeyId) ||
             string.IsNullOrWhiteSpace(signature.Value)))
        {
            throw Invalid("The update manifest signature description is invalid.");
        }
    }

    private static InvalidDataException Invalid(string message) => new(message);

    [GeneratedRegex("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaRegex();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}

internal sealed record ValidatedUpdate(
    NuGetVersion Version,
    Uri ReleaseUri,
    GitHubReleaseAsset PackageAsset);
