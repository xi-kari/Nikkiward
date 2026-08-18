using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Nikkiward.Serialization;
using Nikkiward.Services;

namespace Nikkiward.ViewModels;

public sealed class ResonanceHistorySnapshot
{
    public int SchemaVersion { get; set; } = ResonanceHistoryCache.CurrentSchemaVersion;

    public DateTimeOffset CapturedAtUtc { get; set; }

    public string SourcePagePath { get; set; } = string.Empty;

    public List<ResonanceBannerSnapshot> Banners { get; set; } = [];
}

public sealed class ResonanceBannerSnapshot
{
    public string? PoolId { get; set; }

    public string? PoolName { get; set; }

    public string PatchTitle { get; set; } = string.Empty;

    public string OutfitTitle { get; set; } = string.Empty;

    public string AveragePulls { get; set; } = string.Empty;

    public string TotalPulls { get; set; } = string.Empty;

    public string CompletionText { get; set; } = string.Empty;

    public string RemainingText { get; set; } = string.Empty;

    public int? Rarity { get; set; }

    public string CoverImageUrl { get; set; } = string.Empty;

    public List<string> CoverImageUrls { get; set; } = [];

    public string? LocalCoverFilePath { get; set; }

    public string? CoverCacheStatus { get; set; }

    public List<ResonanceItemSnapshot> Items { get; set; } = [];

    [JsonIgnore]
    public string CoverPreviewUri => ResonanceHistoryCache.ToPreviewUri(LocalCoverFilePath);
}

public sealed class ResonanceItemSnapshot
{
    public string? StableId { get; set; }

    public DateTimeOffset? TimestampUtc { get; set; }

    public string? PoolName { get; set; }

    public string? ItemId { get; set; }

    public string? ItemName { get; set; }

    public int? Rarity { get; set; }

    public int? PullNumber { get; set; }

    public string? ImageUri
    {
        get => ImageUrl;
        set => ImageUrl = value ?? string.Empty;
    }

    public int SlotIndex { get; set; }

    public int ObtainCount { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    public string? StatusText { get; set; }

    public string? ResourceRole { get; set; }

    public string? LocalFilePath { get; set; }

    public string? CacheStatus { get; set; }

    [JsonIgnore]
    public string PreviewUri => ResonanceHistoryCache.ToPreviewUri(LocalFilePath);
}

public sealed class ResonanceHistoryCache
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumBannerCount = 256;
    public const int MaximumItemCount = 3000;
    public const long MaximumImageBytes = 4L * 1024 * 1024;
    public const long MaximumTotalCacheBytes = 512L * 1024 * 1024;

    private const int MaximumRedirectCount = 5;
    private const int DownloadConcurrency = 4;
    private const int MaximumTextLength = 512;
    private const int MaximumSourcePathLength = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private static readonly JsonTypeInfo<ResonanceHistorySnapshot> ResonanceHistoryJsonTypeInfo =
        new NikkiwardJsonContext(JsonOptions).ResonanceHistorySnapshot;

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public ResonanceHistoryCache(string? localApplicationDataPath = null)
    {
        var applicationRoot = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? ApplicationDataPaths.Root
            : Path.Combine(
                Path.GetFullPath(localApplicationDataPath),
                "Nikkiward");
        RootPath = Path.Combine(applicationRoot, "JournalCache");
        AssetsPath = Path.Combine(RootPath, "ResonanceAssets");
        SnapshotPath = Path.Combine(RootPath, "resonance-history.json");

        EnsureChildPath(RootPath, AssetsPath);
        EnsureChildPath(RootPath, SnapshotPath);
    }

    public string RootPath { get; }

    public string AssetsPath { get; }

    public string SnapshotPath { get; }

    public async Task<ResonanceHistorySnapshot?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(SnapshotPath))
        {
            return null;
        }

        EnsureSafeStoragePath(SnapshotPath, expectDirectory: false);
        try
        {
            await using var stream = new FileStream(
                SnapshotPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<ResonanceHistorySnapshot>(
                stream,
                ResonanceHistoryJsonTypeInfo,
                cancellationToken);
            if (snapshot?.SchemaVersion != CurrentSchemaVersion)
            {
                return null;
            }

            return CreateSafeSnapshot(snapshot, preserveCachedPaths: true);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public async Task<ResonanceHistorySnapshot> DownloadAndSaveAsync(
        ResonanceHistorySnapshot snapshot,
        CancellationToken cancellationToken = default,
        Func<bool>? commitGuard = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCommitAllowed(commitGuard, cancellationToken);

        var safeSnapshot = CreateSafeSnapshot(snapshot, preserveCachedPaths: false);
        Directory.CreateDirectory(RootPath);
        EnsureSafeStoragePath(RootPath, expectDirectory: true);
        Directory.CreateDirectory(AssetsPath);
        EnsureSafeStoragePath(AssetsPath, expectDirectory: true);

        var existingBytes = CalculateExistingCacheBytes();
        if (existingBytes > MaximumTotalCacheBytes)
        {
            throw new InvalidDataException("共鸣图片缓存已经超过 512 MB 上限。");
        }

        var urls = EnumerateImageUrls(safeSnapshot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var budget = new CacheBudget(MaximumTotalCacheBytes - existingBytes);
        using var gate = new SemaphoreSlim(DownloadConcurrency, DownloadConcurrency);
        var cacheTasks = urls.Select(async url =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return new KeyValuePair<string, CacheImageResult>(
                    url,
                    await CacheImageAsync(url, budget, cancellationToken));
            }
            finally
            {
                gate.Release();
            }
        }).ToArray();

        var cacheResults = (await Task.WhenAll(cacheTasks))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        ApplyCacheResults(safeSnapshot, cacheResults);
        EnsureCommitAllowed(commitGuard, cancellationToken);
        await SaveSnapshotAsync(safeSnapshot, cancellationToken, commitGuard);
        return safeSnapshot;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (File.Exists(SnapshotPath))
        {
            EnsureSafeStoragePath(SnapshotPath, expectDirectory: false);
            File.Delete(SnapshotPath);
        }

        if (Directory.Exists(AssetsPath))
        {
            EnsureSafeStoragePath(AssetsPath, expectDirectory: true);
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         AssetsPath,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureChildPath(AssetsPath, entry);
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                    (attributes & FileAttributes.Directory) != 0)
                {
                    throw new IOException("共鸣资源目录包含非预期的目录或重解析点。");
                }

                File.Delete(entry);
            }

            Directory.Delete(AssetsPath, recursive: false);
        }

        return Task.CompletedTask;
    }

    internal static string ToPreviewUri(string? localFilePath)
    {
        if (string.IsNullOrWhiteSpace(localFilePath))
        {
            return string.Empty;
        }

        try
        {
            var fullPath = Path.GetFullPath(localFilePath);
            if (!File.Exists(fullPath) ||
                (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                return string.Empty;
            }

            return new Uri(fullPath, UriKind.Absolute).AbsoluteUri;
        }
        catch (Exception ex) when (ex is ArgumentException or
                                   IOException or
                                   NotSupportedException or
                                   PathTooLongException or
                                   UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private ResonanceHistorySnapshot CreateSafeSnapshot(
        ResonanceHistorySnapshot snapshot,
        bool preserveCachedPaths)
    {
        var banners = snapshot.Banners ?? [];
        if (banners.Count > MaximumBannerCount)
        {
            throw new InvalidDataException(
                $"共鸣卡池数量超过 {MaximumBannerCount} 个的缓存上限。");
        }

        var totalItems = 0;
        var safeBanners = new List<ResonanceBannerSnapshot>(banners.Count);
        foreach (var banner in banners)
        {
            var items = banner.Items ?? [];
            totalItems = checked(totalItems + items.Count);
            if (totalItems > MaximumItemCount)
            {
                throw new InvalidDataException(
                    $"共鸣历史数量超过 {MaximumItemCount} 条的缓存上限。");
            }

            var normalizedCoverUrl = NormalizePublicUrl(banner.CoverImageUrl);
            var safeBanner = new ResonanceBannerSnapshot
            {
                PoolId = NormalizeOptionalText(banner.PoolId),
                PoolName = NormalizeOptionalText(banner.PoolName),
                PatchTitle = NormalizeText(banner.PatchTitle, nameof(banner.PatchTitle)),
                OutfitTitle = NormalizeText(banner.OutfitTitle, nameof(banner.OutfitTitle)),
                AveragePulls = NormalizeText(banner.AveragePulls, nameof(banner.AveragePulls)),
                TotalPulls = NormalizeText(banner.TotalPulls, nameof(banner.TotalPulls)),
                CompletionText = NormalizeText(banner.CompletionText, nameof(banner.CompletionText)),
                RemainingText = NormalizeText(banner.RemainingText, nameof(banner.RemainingText)),
                Rarity = banner.Rarity is >= 1 and <= 5 ? banner.Rarity : null,
                CoverImageUrl = normalizedCoverUrl,
                CoverImageUrls = (banner.CoverImageUrls ?? [])
                    .Select(NormalizePublicUrl)
                    .Where(url => url.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToList(),
                LocalCoverFilePath = preserveCachedPaths
                    ? GetSafeCachedAssetPath(banner.LocalCoverFilePath)
                    : null,
                CoverCacheStatus = preserveCachedPaths
                    ? NormalizeCacheStatus(banner.CoverCacheStatus)
                    : null,
                Items = new List<ResonanceItemSnapshot>(items.Count),
            };

            if (!string.IsNullOrWhiteSpace(banner.CoverImageUrl) && normalizedCoverUrl.Length == 0)
            {
                safeBanner.CoverCacheStatus = "未缓存：非官方公开图片地址";
            }
            else if (safeBanner.CoverImageUrls.Count == 0 && normalizedCoverUrl.Length > 0)
            {
                safeBanner.CoverImageUrls.Add(normalizedCoverUrl);
            }
            else if (preserveCachedPaths &&
                     !string.IsNullOrWhiteSpace(banner.LocalCoverFilePath) &&
                     safeBanner.LocalCoverFilePath is null)
            {
                safeBanner.CoverCacheStatus = "未缓存：本地文件不可用";
            }

            foreach (var item in items)
            {
                var normalizedImageUrl = NormalizePublicUrl(item.ImageUrl);
                var safeItem = new ResonanceItemSnapshot
                {
                    StableId = NormalizeOptionalText(item.StableId),
                    TimestampUtc = item.TimestampUtc?.ToUniversalTime(),
                    PoolName = NormalizeOptionalText(item.PoolName),
                    ItemId = NormalizeOptionalText(item.ItemId),
                    ItemName = NormalizeOptionalText(item.ItemName),
                    Rarity = item.Rarity is >= 1 and <= 5 ? item.Rarity : null,
                    PullNumber = item.PullNumber is > 0 and <= 9999 ? item.PullNumber : null,
                    SlotIndex = item.SlotIndex,
                    ObtainCount = item.ObtainCount,
                    ImageUrl = normalizedImageUrl,
                    StatusText = NormalizeOptionalText(item.StatusText),
                    ResourceRole = NormalizeOptionalText(item.ResourceRole),
                    LocalFilePath = preserveCachedPaths
                        ? GetSafeCachedAssetPath(item.LocalFilePath)
                        : null,
                    CacheStatus = preserveCachedPaths
                        ? NormalizeCacheStatus(item.CacheStatus)
                        : null,
                };

                if (!string.IsNullOrWhiteSpace(item.ImageUrl) && normalizedImageUrl.Length == 0)
                {
                    safeItem.CacheStatus = "未缓存：非官方公开图片地址";
                }
                else if (preserveCachedPaths &&
                         !string.IsNullOrWhiteSpace(item.LocalFilePath) &&
                         safeItem.LocalFilePath is null)
                {
                    safeItem.CacheStatus = "未缓存：本地文件不可用";
                }

                safeBanner.Items.Add(safeItem);
            }

            safeBanners.Add(safeBanner);
        }

        return new ResonanceHistorySnapshot
        {
            SchemaVersion = CurrentSchemaVersion,
            CapturedAtUtc = snapshot.CapturedAtUtc.ToUniversalTime(),
            SourcePagePath = NormalizeSourcePagePath(snapshot.SourcePagePath),
            Banners = safeBanners,
        };
    }

    private async Task SaveSnapshotAsync(
        ResonanceHistorySnapshot snapshot,
        CancellationToken cancellationToken,
        Func<bool>? commitGuard = null)
    {
        EnsureCommitAllowed(commitGuard, cancellationToken);
        Directory.CreateDirectory(RootPath);
        EnsureSafeStoragePath(RootPath, expectDirectory: true);
        var temporaryPath = Path.Combine(
            RootPath,
            $".{Path.GetFileName(SnapshotPath)}.{Guid.NewGuid():N}.tmp");
        EnsureChildPath(RootPath, temporaryPath);

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    ResonanceHistoryJsonTypeInfo,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            EnsureCommitAllowed(commitGuard, cancellationToken);
            File.Move(temporaryPath, SnapshotPath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static void EnsureCommitAllowed(
        Func<bool>? commitGuard,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (commitGuard?.Invoke() == false)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private async Task<CacheImageResult> CacheImageAsync(
        string normalizedUrl,
        CacheBudget budget,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri) ||
            !IsAllowedPublicUri(uri))
        {
            return new CacheImageResult(null, "未缓存：非官方公开图片地址");
        }

        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl)))
            .ToLowerInvariant();
        var extension = GetSafeExtension(uri);
        var finalPath = Path.Combine(AssetsPath, $"{hash}{extension}");
        EnsureChildPath(AssetsPath, finalPath);

        if (File.Exists(finalPath))
        {
            var existingPath = GetSafeCachedAssetPath(finalPath);
            if (existingPath is not null)
            {
                return new CacheImageResult(existingPath, "已缓存");
            }

            return new CacheImageResult(null, "未缓存：现有本地文件不可用");
        }

        var temporaryPath = Path.Combine(
            AssetsPath,
            $".{hash}.{Guid.NewGuid():N}.tmp");
        EnsureChildPath(AssetsPath, temporaryPath);
        var reservedBytes = 0L;
        var committed = false;
        try
        {
            using var response = await GetAllowedResponseAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new CacheImageResult(
                    null,
                    $"未缓存：HTTP {(int)response.StatusCode}");
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(mediaType) ||
                !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return new CacheImageResult(null, "未缓存：响应不是图片");
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is <= 0 or > MaximumImageBytes)
            {
                return new CacheImageResult(null, "未缓存：图片大小超限");
            }

            reservedBytes = contentLength ?? MaximumImageBytes;
            if (!budget.TryReserve(reservedBytes))
            {
                return new CacheImageResult(null, "未缓存：本地资源上限");
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            var written = 0L;
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                var nextWritten = checked(written + read);
                if (nextWritten > MaximumImageBytes)
                {
                    return new CacheImageResult(null, "未缓存：图片大小超限");
                }

                if (nextWritten > reservedBytes)
                {
                    var additionalBytes = nextWritten - reservedBytes;
                    if (!budget.TryReserve(additionalBytes))
                    {
                        return new CacheImageResult(null, "未缓存：本地资源上限");
                    }

                    reservedBytes += additionalBytes;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written = nextWritten;
            }

            if (written == 0)
            {
                return new CacheImageResult(null, "未缓存：图片内容为空");
            }

            await output.FlushAsync(cancellationToken);
            output.Close();
            File.Move(temporaryPath, finalPath, overwrite: false);
            committed = true;
            budget.Release(reservedBytes - written);
            return new CacheImageResult(finalPath, "已缓存");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or
                                   IOException or
                                   UnauthorizedAccessException)
        {
            return new CacheImageResult(null, $"未缓存：{ex.GetType().Name}");
        }
        finally
        {
            if (!committed && reservedBytes > 0)
            {
                budget.Release(reservedBytes);
            }

            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static async Task<HttpResponseMessage> GetAllowedResponseAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var currentUri = NormalizeAllowedUri(initialUri)
            ?? throw new HttpRequestException("图片地址不属于允许的官方 HTTPS 域名。");
        for (var redirectCount = 0; redirectCount <= MaximumRedirectCount; redirectCount++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirectCount == MaximumRedirectCount)
            {
                throw new HttpRequestException("官方图片重定向无效或次数过多。");
            }

            var redirectedUri = location.IsAbsoluteUri
                ? location
                : new Uri(currentUri, location);
            currentUri = NormalizeAllowedUri(redirectedUri)
                ?? throw new HttpRequestException("图片重定向离开了允许的官方 HTTPS 域名。");
        }

        throw new HttpRequestException("官方图片重定向次数过多。");
    }

    private static IEnumerable<string> EnumerateImageUrls(ResonanceHistorySnapshot snapshot)
    {
        foreach (var banner in snapshot.Banners)
        {
            if (banner.CoverImageUrl.Length > 0)
            {
                yield return banner.CoverImageUrl;
            }

            foreach (var coverUrl in banner.CoverImageUrls)
            {
                if (coverUrl.Length > 0)
                {
                    yield return coverUrl;
                }
            }

            foreach (var item in banner.Items)
            {
                if (item.ImageUrl.Length > 0)
                {
                    yield return item.ImageUrl;
                }
            }
        }
    }

    private static void ApplyCacheResults(
        ResonanceHistorySnapshot snapshot,
        IReadOnlyDictionary<string, CacheImageResult> results)
    {
        foreach (var banner in snapshot.Banners)
        {
            if (banner.CoverImageUrl.Length > 0 &&
                results.TryGetValue(banner.CoverImageUrl, out var coverResult))
            {
                banner.LocalCoverFilePath = coverResult.LocalFilePath;
                banner.CoverCacheStatus = coverResult.Status;
            }

            foreach (var item in banner.Items)
            {
                if (item.ImageUrl.Length > 0 &&
                    results.TryGetValue(item.ImageUrl, out var itemResult))
                {
                    item.LocalFilePath = itemResult.LocalFilePath;
                    item.CacheStatus = itemResult.Status;
                }
            }
        }
    }

    private long CalculateExistingCacheBytes()
    {
        var total = 0L;
        foreach (var path in Directory.EnumerateFiles(
                     AssetsPath,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            EnsureChildPath(AssetsPath, path);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("共鸣资源目录包含非预期的重解析点。");
            }

            total = checked(total + new FileInfo(path).Length);
        }

        return total;
    }

    private string? GetSafeCachedAssetPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var fullPath = Path.GetFullPath(value);
            EnsureChildPath(AssetsPath, fullPath);
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var attributes = File.GetAttributes(fullPath);
            var length = new FileInfo(fullPath).Length;
            return (attributes & FileAttributes.ReparsePoint) == 0 &&
                   length is > 0 and <= MaximumImageBytes
                ? fullPath
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or
                                   IOException or
                                   NotSupportedException or
                                   PathTooLongException or
                                   UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string NormalizeSourcePagePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        Uri? uri;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            if (!IsAllowedPublicUri(absoluteUri))
            {
                return string.Empty;
            }

            uri = absoluteUri;
        }
        else
        {
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var queryOrFragmentIndex = trimmed.IndexOfAny(['?', '#']);
            if (queryOrFragmentIndex >= 0)
            {
                trimmed = trimmed[..queryOrFragmentIndex];
            }

            if (!trimmed.StartsWith('/'))
            {
                trimmed = $"/{trimmed}";
            }

            if (!Uri.TryCreate(new Uri("https://myl.nuanpaper.com"), trimmed, out uri))
            {
                return string.Empty;
            }
        }

        var path = uri.AbsolutePath;
        return path.Length <= MaximumSourcePathLength ? path : string.Empty;
    }

    private static string NormalizePublicUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        return NormalizeAllowedUri(uri)?.AbsoluteUri ?? string.Empty;
    }

    private static Uri? NormalizeAllowedUri(Uri uri)
    {
        if (!IsAllowedPublicUri(uri))
        {
            return null;
        }

        return new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri;
    }

    private static bool IsAllowedPublicUri(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort)
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.');
        return IsHostOrSubdomain(host, "papegames.com") ||
               IsHostOrSubdomain(host, "nuanpaper.com");
    }

    private static bool IsHostOrSubdomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase);

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static string GetSafeExtension(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath);
        return extension.ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".svg" =>
                extension.ToLowerInvariant(),
            _ => ".img",
        };
    }

    private static string NormalizeText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length > MaximumTextLength)
        {
            throw new InvalidDataException($"{fieldName} 超过允许的文本长度。");
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeCacheStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 128 ? normalized : null;
    }

    private void EnsureSafeStoragePath(string path, bool expectDirectory)
    {
        EnsureChildPath(RootPath, path, allowSamePath: true);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            expectDirectory != ((attributes & FileAttributes.Directory) != 0))
        {
            throw new IOException("共鸣缓存路径类型无效或包含重解析点。");
        }
    }

    private static void EnsureChildPath(
        string rootPath,
        string candidatePath,
        bool allowSamePath = false)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (allowSamePath && candidate.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var rootedPrefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("共鸣缓存路径越出了本地缓存根目录。");
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Nikkiward", "1.0"));
        return client;
    }

    private sealed class CacheBudget
    {
        private readonly object _sync = new();
        private long _remainingBytes;

        public CacheBudget(long remainingBytes)
        {
            _remainingBytes = remainingBytes;
        }

        public bool TryReserve(long bytes)
        {
            lock (_sync)
            {
                if (bytes <= 0 || bytes > _remainingBytes)
                {
                    return false;
                }

                _remainingBytes -= bytes;
                return true;
            }
        }

        public void Release(long bytes)
        {
            if (bytes <= 0)
            {
                return;
            }

            lock (_sync)
            {
                _remainingBytes = Math.Min(
                    MaximumTotalCacheBytes,
                    _remainingBytes + bytes);
            }
        }
    }

    private sealed record CacheImageResult(string? LocalFilePath, string Status);
}
