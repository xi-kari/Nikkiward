using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Nikkiward.Services;
using Nikkiward.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;

namespace Nikkiward.Features.Gallery;

internal sealed class PhotoStudioView : Grid
{
    private const string Host = "studio.nikkiward.invalid";
    private const string Page = "https://" + Host + "/studio/index.html";
    private const string Images = "https://photo.nikkiward.invalid/image/";
    internal Border Toolbar { get; } = new() { Height = 44, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly WebView2 _web = new();
    private readonly CancellationTokenSource _life = new();
    private readonly Nuan5GalleryMetadataService _metadata = new();
    private readonly StudioSession<GalleryPhotoItemViewModel> _session;
    private readonly MainPage _shell;
    private readonly GalleryPhotoItemViewModel _selected;
    internal static readonly PhotoStudioStore Store = new(Path.Combine(ApplicationDataPaths.Root, "PhotoStudio"));

    internal PhotoStudioView(MainPage shell, IEnumerable<GalleryPhotoItemViewModel> list, GalleryPhotoItemViewModel selected)
    {
        _session = new(list);
        _shell = shell;
        _selected = selected;
        Children.Add(_web);
        Children.Add(Toolbar);
        Loaded += Start;
        Unloaded += (_, _) => { _life.Cancel(); _web.Close(); };
    }

    private async void Start(object sender, RoutedEventArgs args)
    {
        Loaded -= Start;
        try
        {
            var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(ApplicationDataPaths.Root, "HomeWebView2"), new CoreWebView2EnvironmentOptions());
            if (_life.IsCancellationRequested) return;
            await _web.EnsureCoreWebView2Async(env);
            if (_life.IsCancellationRequested) return;
            var core = _web.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsNonClientRegionSupportEnabled = true;
            core.SetVirtualHostNameToFolderMapping(Host, Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.Deny);
            core.NavigationStarting += (_, e) => e.Cancel = e.Uri != Page;
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
            core.AddWebResourceRequestedFilter("https://photo.nikkiward.invalid/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += Resource;
            core.WebMessageReceived += Message;
            core.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess) { Failed("页面载入失败"); return; }
                Send(new { type = "list", origin = _shell.StudioOrigin, skin = _shell.StudioSkin,
                    tone = _shell.StudioTone, photos = _session.Photos.Select(p => new { id = p.Key, name = p.Value.FileName, time = p.Value.LastWriteTimeUtc.ToLocalTime().ToString("HH:mm:ss") }),
                    id = _session.Photos.First(p => p.Value == _selected).Key });
            };
            core.ProcessFailed += (_, _) => Failed("照片工坊进程已停止");
            core.Navigate(Page);
        }
        catch (Exception ex) when (ex is COMException or IOException or NotImplementedException or UnauthorizedAccessException)
        { Failed(ex.Message); }
    }

    private void Failed(string reason)
    {
        if (_life.IsCancellationRequested) return;
        Children.Clear();
        var back = new Button { Content = "返回 · " + reason, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        back.Click += (_, _) => _shell.CloseStudio(this);
        Children.Add(back);
    }

    private void Send(object message)
    {
        if (!_life.IsCancellationRequested) _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    }

    private async void Resource(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var uri = args.Request.Uri;
            var photo = args.Request.Method == "GET" && uri.StartsWith(Images, StringComparison.Ordinal)
                ? _session.Consume(uri[Images.Length..]) : null;
            if (photo == null || VariantPathPolicy.ContainsReparsePoint(photo.FilePath))
                throw new IOException("照片不可访问");
            var ext = photo.Extension.ToLowerInvariant();
            var stream = photo.UsesScreenshotCodec
                ? new MemoryStream(await StarwardScreenshotDecoder.RenderPngAsync(photo.FilePath, ext, _life.Token))
                : (Stream)File.OpenRead(photo.FilePath);
            var mime = photo.UsesScreenshotCodec ? "png" : ext is ".jpg" or ".jpeg" ? "jpeg" : ext[1..];
            args.Response = core.Environment.CreateWebResourceResponse(stream.AsRandomAccessStream(), 200, "OK",
                "Content-Type: image/" + mime + "\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: https://" + Host);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException or COMException or DllNotFoundException or NotSupportedException)
        {
            if (!_life.IsCancellationRequested)
                args.Response = core.Environment.CreateWebResourceResponse(null, 404, "Unavailable", "");
        }
        finally { deferral.Complete(); }
    }

    private async void Message(CoreWebView2 core, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (args.Source != Page || _life.IsCancellationRequested) return;
        try
        {
            using var doc = JsonDocument.Parse(args.WebMessageAsJson);
            var m = doc.RootElement;
            string Str(string key) => m.TryGetProperty(key, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString()! : "";
            if (m.ValueKind != JsonValueKind.Object) return;
            var action = Str("action");
            if (!StudioSession<GalleryPhotoItemViewModel>.Allows(action)) return;
            if (action == "back") { _shell.CloseStudio(this); return; }
            var id = Str("id");
            if (!_session.Photos.TryGetValue(id, out var photo)) return;
            if (action is "open" or "inspect" or "thumb" && VariantPathPolicy.ContainsReparsePoint(photo.FilePath))
                throw new IOException("照片不可访问");
            var key = photo.AnnotationScopeId + "\0" + photo.StarKey;
            switch (action)
            {
                case "inspect":
                    object? facts = null;
                    string? thumbnail = null;
                    if (Str("field") == "palette")
                        thumbnail = await photo.ReadThumbnailDataUrlAsync(_life.Token);
                    else
                    {
                        var info = await _metadata.ReadAsync(photo.FilePath, _life.Token);
                        facts = new { lens = info.Camera?.FocalLength, aperture = info.Camera?.Aperture,
                            filter = info.Camera?.Filter?.Id, outfit = info.ClothingIds.Select(n => n.ToString()),
                            date = photo.FullModifiedText };
                    }
                    Send(new { type = "inspect", token = Str("token"), facts, thumbnail });
                    break;
                case "open":
                    var meta = await _metadata.ReadAsync(photo.FilePath, _life.Token);
                    var stars = await new GalleryAnnotationStore().LoadStarredAsync(photo.AnnotationScopeId, _life.Token);
                    photo.SetStarred(stars.Contains(photo.StarKey));
                    Send(new { type = "photo", id, edits = Store.Load(key), starred = photo.IsStarred,
                        rejected = Store.Rejected(key),
                        date = photo.FullModifiedText, folder = Path.GetDirectoryName(photo.FilePath), size = photo.FileSizeText,
                        hasParameters = meta.HasParameters, lens = meta.Camera?.FocalLength, aperture = meta.Camera?.Aperture, filter = meta.Camera?.Filter?.Id,
                        outfit = meta.ClothingIds.Select(n => n.ToString()), url = Images + _session.Issue(id) });
                    break;
                case "image":
                    var comparisonStars = await new GalleryAnnotationStore().LoadStarredAsync(photo.AnnotationScopeId, _life.Token);
                    Send(new { type = action, id, url = Images + _session.Issue(id),
                        starred = comparisonStars.Contains(photo.StarKey),
                        rejected = Store.Rejected(key) });
                    break;
                case "thumb": Send(new { type = action, id, url = await photo.ReadThumbnailDataUrlAsync(_life.Token) }); break;
                case "save":
                    CheckStore();
                    if (m.TryGetProperty("edits", out var edits) && edits.ValueKind == JsonValueKind.Object && edits.GetRawText().Length <= 32768)
                        Store.Save(key, edits);
                    break;
                case "reject":
                    CheckStore();
                    if (m.TryGetProperty("value", out var mark) && mark.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    { Store.SetRejected(key, mark.GetBoolean()); Send(new { type = "reject", id, value = mark.GetBoolean() }); }
                    break;
                case "favorite":
                    await _shell.StarStudioPhotoAsync(photo, m.GetProperty("value").GetBoolean(), _life.Token);
                    Send(new { type = action, id, value = photo.IsStarred });
                    break;
                case "locate":
                    var file = await StorageFile.GetFileFromPathAsync(photo.FilePath);
                    var options = new FolderLauncherOptions();
                    options.ItemsToSelect.Add(file);
                    await Windows.System.Launcher.LaunchFolderAsync(await file.GetParentAsync(), options);
                    break;
                case "copy-code":
                    var package = new DataPackage();
                    package.SetText(Str("text"));
                    Clipboard.SetContent(package);
                    break;
                case "export" or "copy" or "background":
                    await OutputAsync(action, photo.FileName, Str("image"), Str("text"));
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OperationCanceledException)
        { Send(new { type = "error", message = ex.Message }); }
    }

    private void CheckStore() => PhotoStudioStore.CheckDestination(Store.Root, _shell.ViewModel.GalleryReadOnlyRoots);

    private async Task OutputAsync(string action, string name, string image, string text)
    {
        const string png = "data:image/png;base64,", jpg = "data:image/jpeg;base64,";
        var prefix = image.StartsWith(png, StringComparison.Ordinal) ? png : jpg;
        if (!image.StartsWith(prefix, StringComparison.Ordinal) || image.Length - prefix.Length > 96 * 1024 * 1024)
            throw new IOException("导出图像编码无效或超过 72 MiB。");
        var data = Convert.FromBase64String(image[prefix.Length..]);
        ReadOnlySpan<byte> signature = prefix == png ? [137, 80, 78, 71, 13, 10, 26, 10] : [255, 216, 255];
        if (!data.AsSpan().StartsWith(signature)) throw new IOException("图像格式与文件头不符。");
        if (action == "copy")
        {
            using var stream = new InMemoryRandomAccessStream();
            using var writer = stream.AsStreamForWrite();
            await writer.WriteAsync(data);
            await writer.FlushAsync();
            stream.Seek(0);
            var package = new DataPackage();
            if (text.Length > 0) package.SetText(text);
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        else
        {
            var root = action == "background" ? Path.Combine(ApplicationDataPaths.Root, "PhotoStudio", "Background") : Store.ExportRoot;
            PhotoStudioStore.CheckDestination(root, _shell.ViewModel.GalleryReadOnlyRoots);
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"{Path.GetFileNameWithoutExtension(name)}-{Guid.NewGuid():N}." + (prefix == png ? "png" : "jpg"));
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var keep = false;
            try
            {
                using (file)
                    await file.WriteAsync(data, _life.Token);
                if (action == "background") await _shell.ImportStudioBackgroundAsync(path, _life.Token);
                else keep = true;
            }
            finally { if (!keep) File.Delete(path); }
        }
        Send(new { type = "status", message = action switch { "copy" => "已复制图片", "background" => "已交给启动页背景导入", _ => "已导出到 " + Store.ExportRoot } });
    }
}
