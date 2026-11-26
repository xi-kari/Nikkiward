using System.Diagnostics;
using System.Text.Json;
using Nikkiward.Features.Pages;
using Nikkiward.Features.Gallery;
using Nikkiward.Services;

internal static class GalleryIndexTests
{
    internal static IEnumerable<(string, Func<Task>)> All =>
    [
        (nameof(NativeAndWebAlbumMembership), NativeAndWebAlbumMembership),
        (nameof(WebQueryAndIssuedPhotos), WebQueryAndIssuedPhotos),
        (nameof(WebFavoriteProtectionAndConsent), WebFavoriteProtectionAndConsent),
        (nameof(IndexFingerprintAndReadOnlySources), IndexFingerprintAndReadOnlySources),
        (nameof(QueryConditionsCountsAndDates), QueryConditionsCountsAndDates),
        (nameof(StablePagesAndInvalidCursors), StablePagesAndInvalidCursors),
        (nameof(BurstAndSessionBoundaries), BurstAndSessionBoundaries),
        (nameof(PartialIndexAndCancellation), PartialIndexAndCancellation),
        (nameof(ProtectedCopyKeepsOriginalIdentity), ProtectedCopyKeepsOriginalIdentity),
        (nameof(PaletteAndOverlappingCache), PaletteAndOverlappingCache)
    ];

    private static void Check(bool ok) { if (!ok) throw new InvalidOperationException("Gallery index assertion failed."); }
    private static readonly DateTime Epoch = new DateTime(2025, 6, 10, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();
    private static readonly GalleryIndexFacts Facts = new(40, 30, true, "F12", [345, 345], 35, 2.8, [0xee2222, 0xef2222]);
    private static readonly GalleryFavoriteState Empty = new("scope", new HashSet<string>(), [], false);

    private sealed class Library : IDisposable
    {
        internal readonly string Folder = Directory.CreateTempSubdirectory().FullName;
        internal string Root => Path.Combine(Folder, "gallery");
        internal string Exports => Path.Combine(Folder, "exports");
        internal string Cache => Path.Combine(Folder, "cache");
        internal int Reads;
        internal GalleryIndex Open(Func<string, CancellationToken, Task<GalleryIndexFacts>>? read = null, string? cache = null) =>
            new(Root, Exports, cache ?? Cache, (path, _, ct) =>
            {
                if (read != null) return read(path, ct);
                Interlocked.Increment(ref Reads);
                return Task.FromResult(Facts);
            }, GalleryFiles.ResolveCategory);

        internal string Add(string relative, int seconds = 0, bool export = false)
        {
            var path = Path.Combine(export ? Exports : Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1, 2, 3]);
            File.SetLastWriteTimeUtc(path, Epoch.AddSeconds(seconds));
            return path;
        }

        internal string[] Inventory() => new[] { Root, Exports }.Where(Directory.Exists)
            .SelectMany(path => Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            .Order().Select(p => $"{p}|{new FileInfo(p).Length}|{File.GetLastWriteTimeUtc(p).Ticks}").ToArray();
        public void Dispose() => Directory.Delete(Folder, true);
    }

    private static async Task NativeAndWebAlbumMembership()
    {
        using var l = new Library();
        foreach (var category in new[] { "NikkiPhotos_HighQuality", "NikkiPhotos_LowQuality", "MagazinePhotos",
            "ClockInPhoto", "ScreenShot", "Collage", "CollagePhoto", "CloudPhotos", "CloudPhotos_LowQuality", "Other" })
        {
            l.Add(Path.Combine(category, "one.jpg"));
            l.Add(Path.Combine(category, "two.jpg"), 2);
        }
        l.Add("one.jpg", export: true);
        l.Add("two.jpg", 1, export: true);
        var defaults = Enumerable.Range(1, 5).Select(i =>
        {
            var relative = Path.Combine("DefaultFavorites", $"{i}.jpg");
            var path = Path.Combine(l.Folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1, 2, 3]);
            return new GalleryDefaultFavorite(GalleryDefaultFavoriteSeedService.ScopeId, relative, path, 3, Epoch);
        }).ToArray();
        var native = GalleryFiles.Enumerate(l.Root, default).Select(f =>
            (Scope: "album", Path: Path.GetRelativePath(l.Root, f.FullName))).ToList();
        GalleryFiles.ReplaceDefaultFavorites(native, p => p.Scope == GalleryDefaultFavoriteSeedService.ScopeId,
            defaults, d => (Scope: d.ScopeId, Path: d.RelativePath));
        var index = l.Open();
        await index.RefreshAsync(Empty, new("", Epoch, Epoch.AddSeconds(3), TimeSpan.FromSeconds(3)), null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "one.jpg" });
        var seeds = defaults.Select(d => new GalleryIndexPhoto(Guid.NewGuid().ToString("N"), Path.GetFileName(d.FilePath),
            d.RelativePath, "DefaultFavorites", "默认收藏", d.LastWriteTimeUtc.ToLocalTime(), 3, Facts, true, false, null)).ToArray();
        var all = index.Snapshot(new(), seeds, false);
        var counts = all.Page(0).Counts;
        Check(native.Select(p => p.Path).Order().SequenceEqual(all.Ordered.Select(p => p.RelativePath).Order()));
        Check(counts.All == 25 && counts.Categories.Values.Sum() == 25 && counts.Favorites == 5 && counts.Exports == 2);
        Check(counts.Bursts == 14 && all.Ordered.Where(p => p.Burst != null).GroupBy(p => p.Burst)
            .All(group => group.Count() == 2 && group.Select(p => p.CategoryId).Distinct().Count() == 1));
        Check(all.Ordered.Where(p => p.CategoryId is "Other" or "CloudPhotos" or "CloudPhotos_LowQuality" or "DefaultFavorites")
            .All(p => p.Burst == null));
        Check(index.Snapshot(new(Exports: true), seeds, false).Ordered.All(p => p.Exported && p.Session == null && p.Burst == null));
        Check(index.Snapshot(new(), seeds, true).Ordered.Count == 5);
        Check(index.Snapshot(new(), seeds.Skip(1), false).Ordered.Count == 24);
    }

    private static async Task WebQueryAndIssuedPhotos()
    {
        using var library = new Library();
        for (int i = 0; i < 94; i++) library.Add(Path.Combine("NikkiPhotos_HighQuality", $"{i:D3}.jpg"), i);
        library.Add("else.jpg", 100, true);
        var before = library.Inventory();
        var index = library.Open();
        await index.RefreshAsync(Empty, null, null);
        var page = new GalleryPageSession(index, false);
        page.Reset([]);
        var old = page.Version;
        page.BeginVisit();
        var oldVisit = page.Visit;
        var command = JsonSerializer.SerializeToElement(new { visit = oldVisit, sequence = 2, version = old });
        Check(page.Accept("query", command) && !page.Accept("query", command));
        page.BeginVisit();
        Check(!page.Accept("clear", command) && !page.Accept("next", command));
        page.Change(JsonSerializer.SerializeToElement(new { tab = "category:NikkiPhotos_HighQuality", text = ".jpg", filter = "F12",
            clothing = "345", color = "15606306", sort = "oldest" }), []);
        var first = page.Page.Photos.First();
        var list = page.StudioList(first, false).ToArray();
        Check(page.Page.Matches == 94 && list.Select(p => p.FileName).SequenceEqual(Enumerable.Range(0,94).Select(i => $"{i:D3}.jpg")));
        Check(page.Find(old, first.Id) == null && page.Find(page.Version, first.Id) == first);
        Check(page.Find(page.Version, list[80].Id) == null && page.Find(page.Version, library.Root) == null);
        Check(!page.Next(old, 80) && page.Next(page.Version, 80) && !page.Next(page.Version, 80));
        Check(page.Find(page.Version, list[80].Id) == list[80]);
        var state = JsonSerializer.SerializeToElement(page.State(false,index.Progress,false,false,true,false,null));
        Check(state.GetProperty("days")[0].GetProperty("count").GetInt32() == 94 &&
            state.GetProperty("offset").GetInt32() == 80 && !state.ToString().Contains(library.Folder));
        Check(page.StudioList(first,true).Count() == 94);
        page.Change(JsonSerializer.SerializeToElement(new { text = "000.jpg" }), []);
        Check(page.Page.Matches == 1 && page.StudioList(page.Page.Photos.Single(),true).Count() == 94);
        var filtered = page.Version;
        page.Change(JsonSerializer.SerializeToElement(new { }), []);
        Check(page.Page.Matches == 94 && page.Find(filtered, first.Id) == null && page.Page.Next != null);
        Check(before.SequenceEqual(library.Inventory()));
    }

    private static async Task WebFavoriteProtectionAndConsent()
    {
        using var library = new Library();
        var source = library.Add("original.jpg");
        var annotations = new GalleryAnnotationStore(library.Folder);
        var service = new GalleryFavoriteProtectionService(library.Folder, Path.Combine(library.Folder,"pictures"), () => [library.Root]);
        var scope = GalleryAnnotationStore.CreateScopeId("profile",library.Root);
        var inventory = library.Inventory();
        Task<bool> Unexpected(string _, string __) => throw new InvalidOperationException("Unexpected confirmation");
        async Task Set(bool value) => Check(await GalleryFavoriteChange.SetAsync(annotations,service,scope,"original.jpg",source,value,Unexpected,default));
        await Set(true);
        var live = await service.LoadGalleryAsync("profile",library.Root);
        Check(live.ProtectedFavorites.Single().IsUsingProtectedCopy == false);
        await Set(false);
        await Set(true);
        Check(inventory.SequenceEqual(library.Inventory()));
        File.Delete(source);
        var missing = await service.LoadGalleryAsync("profile",library.Root);
        var fallback = missing.ProtectedFavorites.Single();
        Check(fallback.IsUsingProtectedCopy);
        var index = library.Open();
        await index.RefreshAsync(missing,null,null);
        var page = new GalleryPageSession(index,true);
        page.Reset([]);
        Check(page.Page.Photos.Single().ProtectedCopy && index.ResolvePath(page.Page.Photos.Single().Id) == fallback.ProtectedPath);
        int prompts = 0;
        Task<bool> Confirm(string message,string button)
        {
            Check(message.Contains("仅存") && button.Contains("删除"));
            return Task.FromResult(++prompts == 2);
        }
        async Task<bool> Unstar() => await GalleryFavoriteChange.SetAsync(annotations,service,scope,"original.jpg",
            fallback.ProtectedPath,false,Confirm,default);
        Check(!await Unstar() && File.Exists(fallback.ProtectedPath));
        Check(await Unstar() && !File.Exists(fallback.ProtectedPath) && prompts == 2 && library.Inventory().Length == 0);
    }

    private static async Task IndexFingerprintAndReadOnlySources()
    {
        using var l = new Library();
        var a = l.Add("a.jpg");
        var b = l.Add("b.jpg", export: true);
        var locked = l.Add("locked.jpg");
        using var held = File.Open(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var inventory = l.Inventory();
        var index = l.Open();
        await index.RefreshAsync(Empty, null, null);
        Check(l.Reads == 2 && index.Progress == new GalleryIndexProgress(2, 3, 1, false));
        Check(index.Issues.Single().Contains("locked.jpg") && inventory.SequenceEqual(l.Inventory()));
        index = l.Open();
        await index.RefreshAsync(Empty, null, null);
        Check(l.Reads == 2 && inventory.SequenceEqual(l.Inventory()));
        var original = index.Query(new()).Photos.Single(p => p.FileName == "a.jpg");
        File.AppendAllText(a, "1");
        File.SetLastWriteTimeUtc(a, Epoch);
        await index.RefreshAsync(Empty, null, null);
        Check(l.Reads == 3 && index.ResolvePath(original.Id) == a);
        File.SetLastWriteTimeUtc(a, Epoch.AddSeconds(10));
        await index.RefreshAsync(Empty, null, null);
        Check(l.Reads == 4);
        File.Move(b, Path.Combine(l.Exports, "c.jpg"));
        File.Delete(a);
        await index.RefreshAsync(Empty, null, null);
        Check(l.Reads == 5 && index.Query(new(Exports: true)).Photos.Single().FileName == "c.jpg" && index.ResolvePath(original.Id) == null);
    }

    private static async Task QueryConditionsCountsAndDates()
    {
        using var l = new Library();
        l.Add(@"NikkiPhotos_HighQuality\z.jpg");
        l.Add(@"NikkiPhotos_HighQuality\y.jpg", 2);
        l.Add("b.jpg", 86400);
        l.Add("export.jpg", 86400, true);
        var fav = Empty with { StarredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"NikkiPhotos_HighQuality\Z.JPG" } };
        var index = l.Open((p, _) => Task.FromResult(Path.GetFileName(p) is "b.jpg" or "export.jpg" ? Facts with
            { FilterId = null, ClothingIds = [], Colors = [0x2222ee] } : Facts));
        await index.RefreshAsync(fav, new("", Epoch, Epoch, TimeSpan.Zero), null);
        var page = index.Query(new());
        Check(page.Photos.Select(p => p.FileName).SequenceEqual(["b.jpg", "y.jpg", "z.jpg"]));
        Check(page.Days.Select(d => d.Photos.Length).SequenceEqual([1, 2]));
        foreach (var q in new[] { new GalleryQuery(Text: "高清"), new(Text: @"nikkiphotos_highquality\"), new(Text: "f12"),
            new(Text: "345"), new(Category: "nikkiphotos_highquality"), new(Bursts: true), new(Filter: "f12"), new(Clothing: 345), new(Color: 0xff2222) })
            Check(index.Query(q).Matches == 2);
        foreach (var q in new[] { new GalleryQuery(Text: "Z.JPG"), new(Favorites: true), new(Current: true) })
            Check(index.Query(q).Photos.Single().FileName == "z.jpg");
        Check(index.Query(new(Exports: true)).Photos.Single().FileName == "export.jpg");
        Check(index.Query(new(Exports: true, Favorites: true)).Matches == 0);
        Check(index.Query(new(Sort: GalleryQuerySort.Oldest)).Photos[0].FileName == "z.jpg");
        Check(index.Query(new(Sort: GalleryQuerySort.Name)).Photos.Select(p => p.FileName).SequenceEqual(["b.jpg","y.jpg","z.jpg"]));
        Check(index.Query(new(Sort: GalleryQuerySort.Category)).Photos.Select(p => p.CategoryName)
            .SequenceEqual(page.Photos.Select(p => p.CategoryName).Order(StringComparer.OrdinalIgnoreCase)));
        var c = page.Counts;
        Check((c.All, c.Favorites, c.Current, c.Bursts, c.Exports) == (3, 1, 1, 2, 1));
        Check(c.Categories["NikkiPhotos_HighQuality"] == 2 && c.Filters["F12"] == 2 && c.Clothing[345] == 2 &&
            c.Colors.Values.Order().SequenceEqual([1, 2]));
    }

    private static async Task StablePagesAndInvalidCursors()
    {
        using var l = new Library();
        for (int i = 0; i < 1000; i++) l.Add($"{i:D4}.png");
        var index = l.Open();
        var timer = Stopwatch.StartNew();
        await index.RefreshAsync(Empty, null, null);
        Console.WriteLine($"Gallery index 1000 synthetic rows: {timer.ElapsedMilliseconds} ms, {new FileInfo(Directory.GetFiles(l.Cache).Single()).Length} cache bytes");
        var page = index.Query(new());
        var first = page;
        l.Add("new.png", 10);
        await index.RefreshAsync(Empty, null, null);
        var names = new List<string>();
        while (true)
        {
            names.AddRange(page.Photos.Select(p => p.FileName));
            if (page.Next == null) break;
            Check(page.Photos.Length == 80);
            page = index.Query(new(), page.Next);
        }
        Check(names.SequenceEqual(Enumerable.Range(0, 1000).Select(i => $"{i:D4}.png")));
        static void Invalid(Action read) { try { read(); } catch (ArgumentException) { return; } Check(false); }
        Invalid(() => index.Query(new(Favorites: true), first.Next));
        index.Query(new(Favorites: true));
        Invalid(() => index.Query(new(), first.Next));
        using var other = new Library();
        Invalid(() => other.Open().Query(new(), first.Next));
    }

    private static async Task BurstAndSessionBoundaries()
    {
        using var l = new Library();
        l.Add(@"NikkiPhotos_HighQuality\a.jpg"); l.Add(@"NikkiPhotos_HighQuality\b.jpg", 2); l.Add(@"NikkiPhotos_HighQuality\c.jpg", 4);
        l.Add(@"NikkiPhotos_HighQuality\d.jpg", 7); l.Add(@"B\e.jpg", 2);
        var index = l.Open();
        await index.RefreshAsync(Empty, new("", Epoch.AddSeconds(2), Epoch.AddSeconds(4), TimeSpan.Zero),
            new("", Epoch, Epoch.AddSeconds(2), TimeSpan.Zero));
        var rows = index.Query(new(Sort: GalleryQuerySort.Name)).Photos;
        Check(rows.Take(3).Select(p => p.Burst).Distinct().Single() != null && rows.Skip(3).All(p => p.Burst == null));
        Check(rows.Select(p => p.Session).SequenceEqual(["previous","current","current",null,"current"]));
    }

    private static async Task PartialIndexAndCancellation()
    {
        using var l = new Library();
        var first = l.Add("0.jpg");
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var index = l.Open(async (_, ct) =>
        {
            int number = Interlocked.Increment(ref calls);
            if (number > 1)
            {
                if (number == 3) blocked.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            return Facts;
        });
        await index.RefreshAsync(Empty, null, null);
        var id = index.Query(new()).Photos.Single().Id;
        for (int i = 1; i < 6; i++) l.Add($"{i}.jpg");
        using var cancel = new CancellationTokenSource();
        var refresh = index.RefreshAsync(Empty, null, null, token: cancel.Token);
        try
        {
            await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var page = index.Query(new());
            Check(page.Counts.All == 1 && page.Progress == new GalleryIndexProgress(1, 6, 0, true));
        }
        finally { cancel.Cancel(); }
        try { await refresh; Check(false); } catch (OperationCanceledException) { }
        Check(!index.Progress.IsIndexing && index.ResolvePath(id) == first);
    }

    private static async Task PaletteAndOverlappingCache()
    {
        var colors = GalleryIndex.Palette([0,0,255,255, 0,0,255,255, 0,255,0,255, 255,0,0,0]);
        Check(colors.SequenceEqual([0xff0000, 0x00ff00]));
        using var l = new Library();
        l.Add("one.png", export: true);
        var index = l.Open(cache: Path.Combine(l.Exports, "cache"));
        await index.RefreshAsync(Empty, null, null);
        Check(index.Progress.Indexed == 1 && Directory.GetDirectories(l.Exports).Length == 0);
    }

    private static async Task ProtectedCopyKeepsOriginalIdentity()
    {
        using var l = new Library();
        string relative = Path.Combine("123456", "a.jpg");
        var original = Path.Combine(l.Root, relative);
        var copy = Path.Combine(l.Folder, "object.bin");
        using (var file = File.Create(copy)) file.SetLength(Nuan5GalleryMetadataService.MaximumFileSize + 1);
        var metadata = new Nuan5GalleryMetadataService();
        var result = await metadata.ReadIndexMetadataAsync(copy, original, default);
        Check(result.Status == GalleryPhotoMetadataStatus.FileTooLarge);
        var entry = new GalleryFavoriteProtectionEntry
        {
            OriginalPath = original, RelativePath = relative, ScopeId = "scope", OriginalLength = new FileInfo(copy).Length,
            OriginalLastWriteTimeUtc = Epoch, Sha256 = "", ObjectPath = copy,
            Status = GalleryFavoriteProtectionStatus.Protected, ProtectedAtUtc = Epoch
        };
        var index = new GalleryIndex(l.Root, l.Exports, l.Cache, (path, identity, _) =>
        {
            Check(path == copy && identity == original);
            return Task.FromResult(Facts);
        }, _ => ("Other", "其他"));
        await index.RefreshAsync(Empty with
        {
            ProtectedFavorites = [new(entry, copy, true)],
            StarredPaths = new HashSet<string> { GalleryAnnotationStore.NormalizeRelativePath(relative) }
        }, null, null);
        var photo = index.Query(new(Favorites: true, Filter: "F12")).Photos.Single();
        Check(photo.FileName == "a.jpg" && photo.LocalTime.ToUniversalTime() == Epoch && index.ResolvePath(photo.Id) == copy);
    }
}
