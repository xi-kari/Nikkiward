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

public sealed class JournalSnapshot
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public DateTimeOffset CapturedAtUtc { get; set; }

    public string PageTitle { get; set; } = string.Empty;

    public string SourcePagePath { get; set; } = string.Empty;

    public string? LoginDays { get; set; }

    public string? LoginDaysSource { get; set; }

    public string? GameHours { get; set; }

    public string? GameHoursSource { get; set; }

    public string? OutfitCount { get; set; }

    public string? OutfitCountSource { get; set; }

    public string? MomoCloakCount { get; set; }

    public string? MomoCloakCountSource { get; set; }

    public string? SketchCount { get; set; }

    public string? SketchCountSource { get; set; }

    public string? SummaryText { get; set; }

    public string? SummarySource { get; set; }

    public List<JournalSectionSnapshot> Sections { get; set; } = [];

    public List<JournalResourceSnapshot> Resources { get; set; } = [];

    public List<JournalContentBlockSnapshot> ContentBlocks { get; set; } = [];

    public List<string> SanitizedVisibleText { get; set; } = [];
}

public sealed class JournalSectionSnapshot
{
    public string SectionKey { get; set; } = JournalSectionKey.Unknown;

    public string? Source { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string? Route { get; set; }

    public string? ImageUrl { get; set; }

    public List<JournalMetricSnapshot> Metrics { get; set; } = [];

    public List<JournalContentBlockSnapshot> Blocks { get; set; } = [];

    public List<JournalResourceReferenceSnapshot> ResourceReferences { get; set; } = [];
}

public sealed class JournalMetricSnapshot
{
    public string? Source { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

public sealed class JournalResourceSnapshot
{
    public string? Source { get; set; }

    public string Url { get; set; } = string.Empty;

    public string? AltText { get; set; }

    public string? LocalFilePath { get; set; }

    public string? CacheStatus { get; set; }

    public string? Role { get; set; }

    public string? NodeKey { get; set; }

    public int Order { get; set; }

    [JsonIgnore]
    public string PreviewUri
    {
        get
        {
            try
            {
                return string.IsNullOrWhiteSpace(LocalFilePath)
                    ? string.Empty
                    : new Uri(Path.GetFullPath(LocalFilePath), UriKind.Absolute).AbsoluteUri;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return string.Empty;
            }
        }
    }
}

public sealed class JournalContentBlockSnapshot
{
    public string Key { get; set; } = string.Empty;

    public string? ParentKey { get; set; }

    public string Kind { get; set; } = "text";

    public int Order { get; set; }

    public string? Label { get; set; }

    public string? Value { get; set; }

    public string? Status { get; set; }

    public string? Unit { get; set; }

    public string? Current { get; set; }

    public string? Total { get; set; }

    public string? ResourceUrl { get; set; }

    public string? Source { get; set; }
}

public sealed class JournalResourceReferenceSnapshot
{
    public string Url { get; set; } = string.Empty;

    public string? Role { get; set; }

    public string? NodeKey { get; set; }

    public int Order { get; set; }

    public string? Source { get; set; }
}

public sealed class JournalSnapshotCache
{
    private const long MaxResourceBytes = 8 * 1024 * 1024;
    private const long MaxTotalCacheBytes = 512 * 1024 * 1024;
    private const int MaxResourceCount = 2048;
    private const int MaxRedirectCount = 5;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly JsonTypeInfo<JournalSnapshot> JournalSnapshotJsonTypeInfo =
        new NikkiwardJsonContext(JsonOptions).JournalSnapshot;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public JournalSnapshotCache(string? localApplicationDataPath = null)
    {
        var applicationRoot = string.IsNullOrWhiteSpace(localApplicationDataPath)
            ? ApplicationDataPaths.Root
            : Path.Combine(
                Path.GetFullPath(localApplicationDataPath),
                "Nikkiward");
        RootPath = Path.Combine(applicationRoot, "JournalCache");
        AssetsPath = Path.Combine(RootPath, "Assets");
        SnapshotPath = Path.Combine(RootPath, "journal-snapshot.json");
    }

    public string RootPath { get; }

    public string AssetsPath { get; }

    public string SnapshotPath { get; }

    public async Task<JournalSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SnapshotPath))
        {
            return null;
        }

        await using var stream = new FileStream(
            SnapshotPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            32 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var snapshot = await JsonSerializer.DeserializeAsync<JournalSnapshot>(
            stream,
            JournalSnapshotJsonTypeInfo,
            cancellationToken);
        if (snapshot?.SchemaVersion != JournalSnapshot.CurrentSchemaVersion)
        {
            return null;
        }

        snapshot.PageTitle = Trim(snapshot.PageTitle, 240) ?? string.Empty;
        snapshot.SourcePagePath = Trim(snapshot.SourcePagePath, 240) ?? string.Empty;
        ApplySourcedField(snapshot, nameof(snapshot.LoginDays), 80);
        ApplySourcedField(snapshot, nameof(snapshot.GameHours), 80);
        ApplySourcedField(snapshot, nameof(snapshot.OutfitCount), 80);
        ApplySourcedField(snapshot, nameof(snapshot.MomoCloakCount), 80);
        ApplySourcedField(snapshot, nameof(snapshot.SketchCount), 80);
        ApplySourcedField(snapshot, nameof(snapshot.SummaryText), 500);
        snapshot.Sections = snapshot.Sections
            .Where(section => !string.IsNullOrWhiteSpace(section.Title))
            .Take(32)
            .Select(section => new JournalSectionSnapshot
            {
                SectionKey = NormalizeSectionKey(section),
                Source = Trim(section.Source, 240),
                Title = Trim(section.Title, 120) ?? string.Empty,
                Text = SanitizeContent(section.Text, 1200) ?? string.Empty,
                Route = NormalizeJournalRoute(section.Route),
                ImageUrl = NormalizePublicUrl(section.ImageUrl),
                Metrics = NormalizeMetrics(section.Metrics),
                Blocks = NormalizeBlocks(section.Blocks),
                ResourceReferences = NormalizeResourceReferences(section.ResourceReferences),
            })
            .Where(section =>
                JournalSectionKey.IsStable(section.SectionKey) &&
                !string.IsNullOrWhiteSpace(section.Source))
            .ToList();
        snapshot.Resources = snapshot.Resources
            .Select(resource => new JournalResourceSnapshot
            {
                Source = Trim(resource.Source, 240),
                Url = NormalizePublicUrl(resource.Url),
                AltText = SanitizeContent(resource.AltText, 240),
                LocalFilePath = IsCachedAssetPath(resource.LocalFilePath)
                    ? Path.GetFullPath(resource.LocalFilePath!)
                    : null,
                CacheStatus = Trim(resource.CacheStatus, 120),
                Role = Trim(resource.Role, 80),
                NodeKey = Trim(resource.NodeKey, 160),
                Order = Math.Max(0, resource.Order),
            })
            .Where(resource => resource.Url.Length > 0)
            .DistinctBy(resource => resource.Url, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResourceCount)
            .ToList();
        snapshot.ContentBlocks = NormalizeBlocks(snapshot.ContentBlocks);
        snapshot.SanitizedVisibleText = snapshot.SanitizedVisibleText
            .Where(line => !ContainsAccountIdentifier(line))
            .Select(line => Trim(line, 300))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(600)
            .Cast<string>()
            .ToList();
        return snapshot;
    }

    public async Task<JournalSnapshot> DownloadAndSaveAsync(
        JournalSnapshot snapshot,
        CancellationToken cancellationToken = default,
        Func<bool>? commitGuard = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureCommitAllowed(commitGuard, cancellationToken);

        Directory.CreateDirectory(AssetsPath);
        var resources = snapshot.Resources
            .Where(resource => !string.IsNullOrWhiteSpace(resource.Url))
            .Select(resource => new JournalResourceSnapshot
            {
                Source = Trim(resource.Source, 240),
                Url = NormalizePublicUrl(resource.Url),
                AltText = SanitizeContent(resource.AltText, 240),
                LocalFilePath = resource.LocalFilePath,
                CacheStatus = resource.CacheStatus,
                Role = Trim(resource.Role, 80),
                NodeKey = Trim(resource.NodeKey, 160),
                Order = Math.Max(0, resource.Order),
            })
            .Where(resource => resource.Url.Length > 0)
            .DistinctBy(resource => resource.Url, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResourceCount)
            .ToList();

        var existingBytes = Directory.EnumerateFiles(AssetsPath, "*", SearchOption.TopDirectoryOnly)
            .Sum(path =>
            {
                try
                {
                    return new FileInfo(path).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return 0L;
                }
            });
        var budget = new ResourceCacheBudget(Math.Max(0, MaxTotalCacheBytes - existingBytes));
        var gate = new SemaphoreSlim(4, 4);
        try
        {
            var tasks = resources.Select(async resource =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var result = await CacheResourceAsync(resource, budget, cancellationToken);
                    resource.LocalFilePath = result.LocalFilePath;
                    resource.CacheStatus = result.Status;
                }
                finally
                {
                    gate.Release();
                }
            }).ToArray();
            await Task.WhenAll(tasks);
        }
        finally
        {
            gate.Dispose();
        }

        var safeSnapshot = new JournalSnapshot
        {
            SchemaVersion = JournalSnapshot.CurrentSchemaVersion,
            CapturedAtUtc = snapshot.CapturedAtUtc,
            PageTitle = Trim(snapshot.PageTitle, 240) ?? string.Empty,
            SourcePagePath = Trim(snapshot.SourcePagePath, 240) ?? string.Empty,
            LoginDays = SourcedValue(snapshot.LoginDays, snapshot.LoginDaysSource, 80),
            LoginDaysSource = SourcedSource(snapshot.LoginDays, snapshot.LoginDaysSource),
            GameHours = SourcedValue(snapshot.GameHours, snapshot.GameHoursSource, 80),
            GameHoursSource = SourcedSource(snapshot.GameHours, snapshot.GameHoursSource),
            OutfitCount = SourcedValue(snapshot.OutfitCount, snapshot.OutfitCountSource, 80),
            OutfitCountSource = SourcedSource(snapshot.OutfitCount, snapshot.OutfitCountSource),
            MomoCloakCount = SourcedValue(snapshot.MomoCloakCount, snapshot.MomoCloakCountSource, 80),
            MomoCloakCountSource = SourcedSource(snapshot.MomoCloakCount, snapshot.MomoCloakCountSource),
            SketchCount = SourcedValue(snapshot.SketchCount, snapshot.SketchCountSource, 80),
            SketchCountSource = SourcedSource(snapshot.SketchCount, snapshot.SketchCountSource),
            SummaryText = SourcedValue(snapshot.SummaryText, snapshot.SummarySource, 500),
            SummarySource = SourcedSource(snapshot.SummaryText, snapshot.SummarySource),
            Sections = snapshot.Sections
                .Where(section => !string.IsNullOrWhiteSpace(section.Title))
                .Take(32)
                .Select(section => new JournalSectionSnapshot
                {
                    SectionKey = NormalizeSectionKey(section),
                    Source = Trim(section.Source, 240),
                    Title = Trim(section.Title, 120) ?? string.Empty,
                    Text = SanitizeContent(section.Text, 1200) ?? string.Empty,
                    Route = NormalizeJournalRoute(section.Route),
                    ImageUrl = NormalizePublicUrl(section.ImageUrl),
                    Metrics = NormalizeMetrics(section.Metrics),
                    Blocks = NormalizeBlocks(section.Blocks),
                    ResourceReferences = NormalizeResourceReferences(section.ResourceReferences),
                })
                .Where(section =>
                    JournalSectionKey.IsStable(section.SectionKey) &&
                    !string.IsNullOrWhiteSpace(section.Source))
                .ToList(),
            Resources = resources,
            ContentBlocks = NormalizeBlocks(snapshot.ContentBlocks),
            SanitizedVisibleText = snapshot.SanitizedVisibleText
                .Where(line => !ContainsAccountIdentifier(line))
                .Select(line => Trim(line, 300))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(600)
                .Cast<string>()
                .ToList(),
        };

        EnsureCommitAllowed(commitGuard, cancellationToken);
        await SaveAsync(safeSnapshot, cancellationToken, commitGuard);
        return safeSnapshot;
    }

    public async Task SaveAsync(
        JournalSnapshot snapshot,
        CancellationToken cancellationToken = default,
        Func<bool>? commitGuard = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureCommitAllowed(commitGuard, cancellationToken);
        Directory.CreateDirectory(RootPath);
        var temporaryPath = $"{SnapshotPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, JournalSnapshotJsonTypeInfo, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            EnsureCommitAllowed(commitGuard, cancellationToken);
            File.Move(temporaryPath, SnapshotPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
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

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    private async Task<CacheResourceResult> CacheResourceAsync(
        JournalResourceSnapshot resource,
        ResourceCacheBudget budget,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(resource.Url, UriKind.Absolute, out var uri) || !IsAllowedPublicUrl(uri))
        {
            return new CacheResourceResult(null, "未缓存：非官方公开图片地址", 0);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resource.Url))).ToLowerInvariant();
        if (FindCachedAsset(hash) is { } existingPath)
        {
            return new CacheResourceResult(existingPath, "已缓存", 0);
        }

        var temporaryPath = Path.Combine(AssetsPath, $"{hash}.{Guid.NewGuid():N}.tmp");
        var reservedBytes = 0L;
        var committed = false;
        try
        {
            using var response = await SendWithRedirectsAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new CacheResourceResult(null, $"未缓存：HTTP {(int)response.StatusCode}", 0);
            }

            if (response.Content.Headers.ContentType?.MediaType is not { } mediaType ||
                !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return new CacheResourceResult(null, "未缓存：响应不是图片", 0);
            }

            if (response.Content.Headers.ContentLength is long contentLength &&
                (contentLength <= 0 || contentLength > MaxResourceBytes))
            {
                return new CacheResourceResult(null, "未缓存：图片大小超限", 0);
            }

            reservedBytes = response.Content.Headers.ContentLength ?? MaxResourceBytes;
            if (!budget.TryReserve(reservedBytes))
            {
                return new CacheResourceResult(null, "未缓存：本地资源上限", 0);
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[64 * 1024];
            var written = 0L;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                written += read;
                if (written > MaxResourceBytes)
                {
                    return new CacheResourceResult(null, "未缓存：图片大小超限", 0);
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await output.FlushAsync(cancellationToken);
            output.Close();
            var signature = new byte[32];
            var signatureLength = 0;
            await using (var validation = new FileStream(
                temporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                signature.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                signatureLength = await validation.ReadAsync(signature, cancellationToken);
            }

            if (!JournalImageMagic.TryDetect(signature.AsSpan(0, signatureLength), out var format))
            {
                return new CacheResourceResult(null, "未缓存：图片字节格式无效", 0);
            }

            var path = Path.Combine(AssetsPath, $"{hash}{JournalImageMagic.GetFileExtension(format)}");
            if (!IsSafeAssetPath(path, requireExisting: false))
            {
                return new CacheResourceResult(null, "未缓存：缓存路径无效", 0);
            }

            File.Move(temporaryPath, path, overwrite: false);
            committed = true;
            budget.Release(Math.Max(0, reservedBytes - written));
            return new CacheResourceResult(path, "已缓存", written);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or UnauthorizedAccessException)
        {
            return new CacheResourceResult(null, $"未缓存：{ex.GetType().Name}", 0);
        }
        finally
        {
            if (!committed && reservedBytes > 0)
            {
                budget.Release(reservedBytes);
            }

            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Nikkiward", "1.0"));
        return client;
    }

    private static async Task<HttpResponseMessage> SendWithRedirectsAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var current = initialUri;
        for (var redirect = 0; redirect <= MaxRedirectCount; redirect++)
        {
            if (!JournalUrlPolicy.IsAllowedOfficialUri(current))
            {
                throw new HttpRequestException("The resource URL is outside the official allowlist.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            if (redirect == MaxRedirectCount ||
                response.Headers.Location is not { } location ||
                !Uri.TryCreate(current, location, out var next) ||
                !JournalUrlPolicy.IsAllowedOfficialUri(next))
            {
                response.Dispose();
                throw new HttpRequestException("The resource redirect chain is invalid.");
            }

            response.Dispose();
            current = next;
        }

        throw new HttpRequestException("The resource redirect chain exceeded the limit.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static string NormalizePublicUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) || !IsAllowedPublicUrl(uri))
        {
            return string.Empty;
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri.AbsoluteUri;
    }

    private static string? NormalizeJournalRoute(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!Uri.TryCreate(new Uri("https://myl.nuanpaper.com"), trimmed, out var uri) ||
            !IsAllowedPublicUrl(uri) ||
            !uri.AbsolutePath.StartsWith("/tools/journal", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.AbsolutePath.Length <= 240 ? uri.AbsolutePath : null;
    }

    private static List<JournalMetricSnapshot> NormalizeMetrics(
        IEnumerable<JournalMetricSnapshot>? metrics)
    {
        return (metrics ?? [])
            .Select(metric => new JournalMetricSnapshot
            {
                Source = Trim(metric.Source, 240),
                Label = SanitizeContent(metric.Label, 80) ?? string.Empty,
                Value = SanitizeContent(metric.Value, 80) ?? string.Empty,
            })
            .Where(metric =>
                metric.Label.Length > 0 &&
                metric.Value.Length > 0 &&
                !string.IsNullOrWhiteSpace(metric.Source))
            .DistinctBy(
                metric => $"{metric.Label}\u001f{metric.Value}",
                StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static List<JournalContentBlockSnapshot> NormalizeBlocks(
        IEnumerable<JournalContentBlockSnapshot>? blocks)
    {
        return (blocks ?? [])
            .Select(block => new JournalContentBlockSnapshot
            {
                Key = Trim(block.Key, 160) ?? string.Empty,
                ParentKey = Trim(block.ParentKey, 160),
                Kind = Trim(block.Kind, 48) ?? "text",
                Order = Math.Max(0, block.Order),
                Label = SanitizeContent(block.Label, 160),
                Value = SanitizeContent(block.Value, 240),
                Status = SanitizeContent(block.Status, 80),
                Unit = SanitizeContent(block.Unit, 40),
                Current = SanitizeContent(block.Current, 80),
                Total = SanitizeContent(block.Total, 80),
                ResourceUrl = NormalizePublicUrl(block.ResourceUrl),
                Source = Trim(block.Source, 240),
            })
            .Where(block =>
                block.Key.Length > 0 &&
                block.Kind.Length > 0 &&
                !string.IsNullOrWhiteSpace(block.Source))
            .DistinctBy(
                block => $"{block.Key}\u001f{block.ParentKey}\u001f{block.Order}",
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(block => block.Order)
            .Take(4096)
            .ToList();
    }

    private static List<JournalResourceReferenceSnapshot> NormalizeResourceReferences(
        IEnumerable<JournalResourceReferenceSnapshot>? references)
    {
        return (references ?? [])
            .Select(reference => new JournalResourceReferenceSnapshot
            {
                Url = NormalizePublicUrl(reference.Url),
                Role = Trim(reference.Role, 80),
                NodeKey = Trim(reference.NodeKey, 160),
                Order = Math.Max(0, reference.Order),
                Source = Trim(reference.Source, 240),
            })
            .Where(reference =>
                reference.Url.Length > 0 &&
                !string.IsNullOrWhiteSpace(reference.Source))
            .DistinctBy(
                reference => $"{reference.Url}\u001f{reference.Order}",
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference.Order)
            .Take(4096)
            .ToList();
    }

    private static string NormalizeSectionKey(JournalSectionSnapshot section)
    {
        if (JournalSectionKey.IsStable(section.SectionKey))
        {
            return section.SectionKey.Trim().ToLowerInvariant();
        }

        var derived = JournalSectionKey.Derive(section.Route, section.SectionKey);
        return derived != JournalSectionKey.Unknown
            ? derived
            : JournalSectionKey.Derive(null, section.Source);
    }

    private static bool IsAllowedPublicUrl(Uri uri) => JournalUrlPolicy.IsAllowedOfficialUri(uri);

    private static bool ContainsAccountIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("搭配师", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("昵称", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("UID", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("退出登录", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("账号", StringComparison.OrdinalIgnoreCase);
    }

    private string? FindCachedAsset(string hash)
    {
        foreach (var extension in Enum.GetValues<JournalImageFormat>()
                     .Select(JournalImageMagic.GetFileExtension)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(AssetsPath, $"{hash}{extension}");
            if (IsSafeAssetPath(candidate, requireExisting: true))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? SourcedValue(string? value, string? source, int maxLength) =>
        JournalSourcedField.FromCapture(
                Trim(value, maxLength),
                Trim(source, 240))
            .Value;

    private static string? SourcedSource(string? value, string? source) =>
        JournalSourcedField.FromCapture(
                Trim(value, 500),
                Trim(source, 240))
            .Source;

    private static void ApplySourcedField(
        JournalSnapshot snapshot,
        string fieldName,
        int maxLength)
    {
        var (value, source) = fieldName switch
        {
            nameof(snapshot.LoginDays) => (snapshot.LoginDays, snapshot.LoginDaysSource),
            nameof(snapshot.GameHours) => (snapshot.GameHours, snapshot.GameHoursSource),
            nameof(snapshot.OutfitCount) => (snapshot.OutfitCount, snapshot.OutfitCountSource),
            nameof(snapshot.MomoCloakCount) => (snapshot.MomoCloakCount, snapshot.MomoCloakCountSource),
            nameof(snapshot.SketchCount) => (snapshot.SketchCount, snapshot.SketchCountSource),
            nameof(snapshot.SummaryText) => (snapshot.SummaryText, snapshot.SummarySource),
            _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, null),
        };
        var normalized = JournalSourcedField.FromCapture(
            Trim(value, maxLength),
            Trim(source, 240));
        switch (fieldName)
        {
            case nameof(snapshot.LoginDays):
                snapshot.LoginDays = normalized.Value;
                snapshot.LoginDaysSource = normalized.Source;
                break;
            case nameof(snapshot.GameHours):
                snapshot.GameHours = normalized.Value;
                snapshot.GameHoursSource = normalized.Source;
                break;
            case nameof(snapshot.OutfitCount):
                snapshot.OutfitCount = normalized.Value;
                snapshot.OutfitCountSource = normalized.Source;
                break;
            case nameof(snapshot.MomoCloakCount):
                snapshot.MomoCloakCount = normalized.Value;
                snapshot.MomoCloakCountSource = normalized.Source;
                break;
            case nameof(snapshot.SketchCount):
                snapshot.SketchCount = normalized.Value;
                snapshot.SketchCountSource = normalized.Source;
                break;
            case nameof(snapshot.SummaryText):
                snapshot.SummaryText = normalized.Value;
                snapshot.SummarySource = normalized.Source;
                break;
        }
    }

    private static string? SanitizeContent(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var safeSegments = value
            .Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !ContainsAccountIdentifier(segment));
        return Trim(string.Join(" · ", safeSegments), maxLength);
    }

    private bool IsCachedAssetPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return IsSafeAssetPath(value, requireExisting: true);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private bool IsSafeAssetPath(string value, bool requireExisting)
    {
        try
        {
            var fullPath = Path.GetFullPath(value);
            var root = Path.GetFullPath(AssetsPath);
            var relative = Path.GetRelativePath(root, fullPath);
            if (relative.Length == 0 ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                Path.IsPathRooted(relative))
            {
                return false;
            }

            if (Directory.Exists(root) &&
                (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            var current = root;
            var segments = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments[..^1])
            {
                current = Path.Combine(current, segment);
                if (Directory.Exists(current) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }
            }

            if (!File.Exists(fullPath))
            {
                return !requireExisting;
            }

            return (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception ex) when (ex is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            IOException or
            UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class ResourceCacheBudget
    {
        private readonly object _sync = new();
        private long _remainingBytes;

        public ResourceCacheBudget(long remainingBytes)
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
                _remainingBytes = Math.Min(MaxTotalCacheBytes, _remainingBytes + bytes);
            }
        }
    }

    private sealed record CacheResourceResult(string? LocalFilePath, string Status, long BytesWritten);
}
