using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using NuGet.Versioning;

namespace Nikkiward.Features.Updates;

public sealed class GitHubReleaseUpdateService
{
    private const int MaximumReleaseResponseBytes = 2 * 1024 * 1024;
    private const int MaximumManifestBytes = 256 * 1024;
    private const string ManifestAssetName = "Nikkiward-update.json";
    private static readonly Uri ReleasesUri = new(
        "https://api.github.com/repos/xi-kari/Nikkiward/releases?per_page=20");
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateService()
        : this(SharedHttpClient)
    {
    }

    internal GitHubReleaseUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        UpdateChannel channel,
        NuGetVersion currentVersion,
        CancellationToken cancellationToken = default)
    {
        using var releasesResponse = await _httpClient.GetAsync(
            ReleasesUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (releasesResponse.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.NoPublishedRelease,
                currentVersion,
                null,
                null);
        }

        releasesResponse.EnsureSuccessStatusCode();
        var releaseBytes = await ReadBoundedAsync(
            releasesResponse.Content,
            MaximumReleaseResponseBytes,
            cancellationToken);
        var releases = JsonSerializer.Deserialize(
            releaseBytes,
            UpdateJsonContext.Default.GitHubReleaseArray) ?? [];
        var selected = ReleaseSelector.Select(releases, channel);
        if (selected is null)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.NoPublishedRelease,
                currentVersion,
                null,
                null);
        }

        var manifestAssets = selected.Release.Assets
            .Where(asset => string.Equals(asset.Name, ManifestAssetName, StringComparison.Ordinal))
            .ToArray();
        if (manifestAssets.Length != 1)
        {
            throw new InvalidDataException("The selected GitHub Release must contain exactly one update manifest.");
        }

        var manifestAsset = manifestAssets[0];
        var manifestUri = UpdateManifestValidator.EnsureTrustedGitHubUri(
            manifestAsset.BrowserDownloadUrl,
            "releases/download/");
        using var manifestResponse = await _httpClient.GetAsync(
            manifestUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        manifestResponse.EnsureSuccessStatusCode();
        var manifestBytes = await ReadBoundedAsync(
            manifestResponse.Content,
            MaximumManifestBytes,
            cancellationToken);
        if (manifestAsset.Size != manifestBytes.LongLength)
        {
            throw new InvalidDataException("The GitHub manifest asset size does not match the downloaded manifest.");
        }

        var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes));
        UpdateManifestValidator.ValidateDigest(manifestAsset.Digest, manifestHash, "manifest");
        var manifest = JsonSerializer.Deserialize(
            manifestBytes,
            UpdateJsonContext.Default.UpdateManifest) ??
            throw new InvalidDataException("The update manifest is empty.");
        var validated = UpdateManifestValidator.Validate(manifest, selected);

        var status = VersionComparer.VersionRelease.Compare(validated.Version, currentVersion) > 0
            ? UpdateCheckStatus.UpdateAvailable
            : UpdateCheckStatus.UpToDate;
        return new UpdateCheckResult(
            status,
            currentVersion,
            validated.Version,
            validated.ReleaseUri);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength && contentLength > maximumBytes)
        {
            throw new InvalidDataException("The update response is larger than the accepted limit.");
        }

        var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > maximumBytes)
        {
            throw new InvalidDataException("The update response is larger than the accepted limit.");
        }

        return bytes;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Nikkiward", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }
}
