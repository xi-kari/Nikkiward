using System.Text.Json.Serialization;
using NuGet.Versioning;

namespace Nikkiward.Features.Updates;

public enum UpdateChannel
{
    Stable,
    Preview,
}

public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    NoPublishedRelease,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    NuGetVersion CurrentVersion,
    NuGetVersion? LatestVersion,
    Uri? ReleaseUri);

public sealed record AppVersionInfo(
    NuGetVersion Version,
    string DisplayVersion,
    string RuntimeIdentifier,
    string DistributionKind,
    string? CommitSha);

internal sealed class UpdateManifest
{
    public int SchemaVersion { get; init; }
    public string Product { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public string CommitSha { get; init; } = string.Empty;
    public string MinimumSupportedVersion { get; init; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; init; }
    public UpdatePackageManifest Package { get; init; } = new();
    public UpdateManifestSignature? Signature { get; init; }
}

internal sealed class UpdatePackageManifest
{
    public string FileName { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Size { get; init; }
    public string RuntimeIdentifier { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
}

internal sealed class UpdateManifestSignature
{
    public string Algorithm { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; init; } = string.Empty;

    [JsonPropertyName("draft")]
    public bool Draft { get; init; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; init; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; init; }

    [JsonPropertyName("assets")]
    public GitHubReleaseAsset[] Assets { get; init; } = [];
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("digest")]
    public string? Digest { get; init; }

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; init; } = string.Empty;
}

internal sealed record SelectedRelease(GitHubRelease Release, NuGetVersion Version);
