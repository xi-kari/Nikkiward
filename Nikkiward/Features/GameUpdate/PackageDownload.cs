using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Nikkiward.Features.GameUpdate;

internal sealed class PackageDownload(HttpMessageHandler? handler = null) : IDisposable
{
    private sealed record Resume(string Url, string Md5, string Validator, long? Total);
    private readonly HttpClient client = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    public void Dispose() => client.Dispose();

    private static HttpRequestMessage Request(Uri url)
    {
        PackageDescription.CheckUrl(url);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd($"Nikkiward/{typeof(PackageDownload).Assembly.GetName().Version}");
        return request;
    }

    internal async Task<PackageDescription> FetchAsync(Uri url, CancellationToken token = default)
    {
        using var request = Request(url);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        using var buffer = new MemoryStream();
        using var stream = await response.Content.ReadAsStreamAsync(token);
        var block = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(block, token)) != 0)
        {
            if (buffer.Length + read > 65536)
                throw new UpdateException(UpdateFailure.Description, "包描述超过 64 KB。");
            buffer.Write(block, 0, read);
        }
        return PackageDescription.Parse(buffer.ToArray());
    }

    internal static void Verify(Stream stream, string expected)
    {
        if (!Convert.ToHexString(MD5.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException(UpdateFailure.Digest, "安装包 MD5 不匹配。");
    }

    internal async Task<string> DownloadAsync(PackageDescription package, string cache, long? bytesPerSecond = null,
        IProgress<long>? progress = null, CancellationToken token = default)
    {
        if (bytesPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(bytesPerSecond));
        var name = Uri.UnescapeDataString(package.Url.Segments[^1]);
        if (UpdatePaths.Relative(name).Contains('\\'))
            throw new UpdateException(UpdateFailure.UnsafePath, "包文件名无效。");
        cache = UpdatePaths.Absolute(cache, workingDirectory: true);
        var target = UpdatePaths.Absolute(Path.Combine(cache, name));
        var part = UpdatePaths.Absolute(target + ".partial");
        var sidecar = UpdatePaths.Absolute(part + ".json");
        Directory.CreateDirectory(cache);
        using var lease = new FileStream(UpdatePaths.Absolute(part + ".lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        if (File.Exists(target))
        {
            using var existing = File.OpenRead(target);
            Verify(existing, package.Md5);
            return target;
        }

        Resume? resume = null;
        if (File.Exists(part) && File.Exists(sidecar))
        {
            try { resume = JsonSerializer.Deserialize<Resume>(await File.ReadAllBytesAsync(sidecar, token)); }
            catch (JsonException) { }
        }
        long offset = File.Exists(part) ? new FileInfo(part).Length : 0;
        RangeConditionHeaderValue? condition = null;
        if (resume?.Url != package.Url.AbsoluteUri || resume.Md5 != package.Md5 ||
            !RangeConditionHeaderValue.TryParse(resume.Validator, out condition)) offset = 0;

        while (true)
        {
            using var request = Request(package.Url);
            if (offset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
                request.Headers.IfRange = condition;
            }
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var tag = response.Headers.ETag;
            var validator = tag is { IsWeak: false } ? tag.ToString() : response.Content.Headers.LastModified?.ToString("R");
            var range = response.Content.Headers.ContentRange;
            var partial = response.StatusCode == HttpStatusCode.PartialContent;
            if (offset > 0 && (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable ||
                partial && (validator != resume!.Validator || range?.From != offset ||
                resume.Total.HasValue && range.Length != resume.Total)))
            {
                offset = 0;
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (partial && (offset == 0 || range?.Unit != "bytes" || range.From != offset ||
                range.Length == null || range.To != range.Length - 1))
                throw new InvalidDataException("服务器返回的续传范围无效。");
            if (!partial && response.StatusCode != HttpStatusCode.OK)
                throw new InvalidDataException("服务器未返回完整的包响应。");
            var total = (partial ? range!.Length : response.Content.Headers.ContentLength)
                ?? throw new InvalidDataException("服务器未提供可核对的安装包长度。");
            if (partial && response.Content.Headers.ContentLength is long segment && segment != total - offset)
                throw new InvalidDataException("服务器声明的分段长度与续传范围不一致。");
            if (!partial)
            {
                offset = 0;
                File.Delete(sidecar);
                File.Delete(part);
            }
            using (var output = new FileStream(part, partial ? FileMode.Open : FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Position = offset;
                File.Delete(sidecar);
                if (validator != null)
                {
                    using var metadata = new FileStream(sidecar, FileMode.CreateNew);
                    JsonSerializer.Serialize(metadata, new Resume(package.Url.AbsoluteUri, package.Md5, validator, total));
                }
                using var input = await response.Content.ReadAsStreamAsync(token);
                var block = new byte[65536];
                var watch = Stopwatch.StartNew();
                int count;
                while ((count = await input.ReadAsync(block, token)) != 0)
                {
                    if (count > total - output.Position)
                        throw new InvalidDataException("下载内容超过服务器声明的长度。");
                    await output.WriteAsync(block.AsMemory(0, count), token);
                    progress?.Report(output.Position);
                    if (bytesPerSecond is long rate)
                    {
                        var delay = TimeSpan.FromSeconds((double)(output.Position - offset) / rate) - watch.Elapsed;
                        if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
                    }
                }
                if (output.Length != total)
                    throw new EndOfStreamException("下载长度与服务器声明不一致。");
            }
            token.ThrowIfCancellationRequested();
            try
            {
                using var file = File.OpenRead(part);
                var digest = Convert.ToHexString(await MD5.HashDataAsync(file, token));
                if (!digest.Equals(package.Md5, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException(UpdateFailure.Digest, "安装包 MD5 不匹配。");
            }
            catch (UpdateException)
            {
                File.Delete(part);
                File.Delete(sidecar);
                throw;
            }
            File.Move(part, target);
            File.Delete(sidecar);
            return target;
        }
    }
}
