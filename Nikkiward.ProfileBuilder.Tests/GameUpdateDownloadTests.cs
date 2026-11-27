using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Nikkiward.Features.GameUpdate;

internal static class GameUpdateDownloadTests
{
    internal static readonly (string, Func<Task>)[] All =
    [
        ("game update descriptor shapes and CDN fetch", Descriptor),
        ("game update descriptor rejects unsafe input and oversized response", RejectDescription),
        ("game update fresh download", () => Download("fresh")),
        ("game update cancel and resume with If-Range", () => Download("resume")),
        ("game update Last-Modified resumes without ETag", () => Download("date")),
        ("game update ignored Range replaces partial", () => Download("ignored")),
        ("game update changed ETag restarts request", () => Download("changed")),
        ("game update digest mismatch discards partial", () => Download("digest")),
        ("game update rejects short HTTP body", () => Download("short"))
    ];
    private const string Url = "https://x6cn-game-package.papegames.com/base.7z";
    private const string Json = """{"version":"3011","md5":"0123456789abcdef0123456789abcdef","url":"https://x6cn-game-package.papegames.com/base.7z","cleanupDirs":["X6Game/Content/Paks"]}""";

    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            GameUpdateTests.Check(request.Method == HttpMethod.Get && request.Headers.UserAgent.Single().Product!.Name == "Nikkiward" &&
                request.Headers.All(h => h.Key is "User-Agent" or "Range" or "If-Range"));
            return Task.FromResult(respond(request));
        }
    }

    private static async Task Descriptor()
    {
        foreach (var json in new[] { Json, "{\"latest\":" + Json + "}" })
        {
            using var http = new PackageDownload(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(json) }));
            var descriptor = await http.FetchAsync(new Uri(Url));
            GameUpdateTests.Check(descriptor.Version == 3011 && descriptor.Url.AbsoluteUri == Url &&
                descriptor.CleanupDirs.Single() == @"X6Game\Content\Paks");
        }
    }

    private static async Task RejectDescription()
    {
        foreach (var (from, to) in new[] { ("https:", "http:"), (PackageDescription.Host, "example.org"), ("3011", "0"),
            ("abcdef", "zzzzzz"), ("X6Game/Content/Paks", "../escape"), ("X6Game/Content/Paks", "x6Game"),
            ("X6Game/Content/Paks", "downloadcache/data"), ("X6Game/Content/Paks", "X6Game/ScreenShot") })
        {
            try { PackageDescription.Parse(Encoding.UTF8.GetBytes(Json.Replace(from, to))); }
            catch (UpdateException) { continue; }
            throw new Exception($"接受了无效值 {to}");
        }
        using var http = new PackageDownload(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Json.PadRight(65537)) }));
        try { await http.FetchAsync(new Uri(Url)); }
        catch (UpdateException e) when (e.Reason == UpdateFailure.Description) { return; }
        throw new Exception("接受了超长描述");
    }

    private sealed class Callback(Action<long> callback) : IProgress<long>
    {
        public void Report(long value) => callback(value);
    }

    private static async Task Download(string mode)
    {
        var bytes = new byte[160000];
        new Random(19).NextBytes(bytes);
        var package = new PackageDescription(3011, new(Url), mode == "digest" ? new string('0', 32) :
            Convert.ToHexString(MD5.HashData(bytes)), []);
        var dir = Directory.CreateTempSubdirectory().FullName;
        var part = Path.Combine(dir, "base.7z.partial");
        var requests = 0;
        long paused = 0;
        var modified = new DateTimeOffset(2026, 11, 20, 10, 44, 19, TimeSpan.Zero);
        using var handler = new Handler(request =>
        {
            requests++;
            var offset = request.Headers.Range?.Ranges.Single().From ?? 0;
            if (requests == 2)
                GameUpdateTests.Check(offset == paused && offset > 0 && (mode == "date" ?
                    request.Headers.IfRange?.Date == modified : request.Headers.IfRange?.EntityTag?.Tag == "\"a\""));
            else GameUpdateTests.Check(offset == 0);
            var start = mode == "ignored" ? 0 : (int)offset;
            var response = new HttpResponseMessage(start == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
                { Content = new ByteArrayContent(bytes[start..]) };
            if (mode == "date") response.Content.Headers.LastModified = modified;
            else response.Headers.ETag = new EntityTagHeaderValue(mode == "changed" && requests > 1 ? "\"b\"" : "\"a\"");
            if (start > 0) response.Content.Headers.ContentRange = new(start, bytes.Length - 1, bytes.Length);
            if (mode == "short") response.Content.Headers.ContentLength = bytes.Length + 1;
            return response;
        });
        using var http = new PackageDownload(handler);
        try
        {
            if (mode is "ignored" or "resume" or "changed" or "date")
            {
                using var cts = new CancellationTokenSource();
                try { await http.DownloadAsync(package, dir, progress: new Callback(_ => cts.Cancel()), token: cts.Token); }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
                paused = new FileInfo(part).Length;
                GameUpdateTests.Check(paused > 0 && paused < bytes.Length);
            }
            try
            {
                var target = await http.DownloadAsync(package, dir);
                GameUpdateTests.Check(mode is not ("digest" or "short") && File.ReadAllBytes(target).SequenceEqual(bytes));
                GameUpdateTests.Check(requests == (mode == "changed" ? 3 : mode == "fresh" ? 1 : 2));
                GameUpdateTests.Check(Directory.GetFiles(dir).SequenceEqual([target]));
            }
            catch (UpdateException e) when (mode == "digest" && e.Reason == UpdateFailure.Digest)
            { GameUpdateTests.Check(Directory.GetFiles(dir).Length == 0); }
            catch (EndOfStreamException) when (mode == "short")
            { GameUpdateTests.Check(!File.Exists(Path.Combine(dir, "base.7z"))); }
        }
        finally { Directory.Delete(dir, true); }
    }
}
