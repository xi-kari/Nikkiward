using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Nikkiward.Features.Pages;

public sealed partial class WebPagesView
{
    // A host of its own: requests to the folder-mapped pages host never reach WebResourceRequested.
    private const string ImagePrefix = "https://page-image.nikkiward.invalid/";
    private Func<string, string, Task<Stream?>>? _image;
    private Action? _releaseImages;

    internal void RegisterImages(Func<string, string, Task<Stream?>> image, Action release)
    {
        ReleaseImages();
        _image = image;
        _releaseImages = release;
    }

    private void ReleaseImages()
    {
        _image = null;
        var release = _releaseImages;
        _releaseImages = null;
        release?.Invoke();
    }

    private async void ImageRequested(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        var provider = _image;
        Stream? stream = null;
        try
        {
            var url = args.Request.Uri;
            if (provider != null && !_closed && _ready && args.Request.Method == "GET" && url.StartsWith(ImagePrefix, StringComparison.Ordinal))
            {
                var parts = url[ImagePrefix.Length..].Split('/');
                if (parts.Length == 2 && parts.All(p => Guid.TryParseExact(p, "N", out _)))
                    stream = await provider(parts[0], parts[1]);
            }
            if (stream != null && provider == _image && !_closed)
            {
                args.Response = core.Environment.CreateWebResourceResponse(stream.AsRandomAccessStream(), 200, "OK",
                    "Content-Type: image/jpeg\r\nCache-Control: no-store");
                stream = null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException
            or InvalidDataException or NotSupportedException or COMException or DllNotFoundException) { }
        finally
        {
            stream?.Dispose();
            if (!_closed && args.Response == null)
                args.Response = core.Environment.CreateWebResourceResponse(null, 404, "Unavailable", "Cache-Control: no-store");
            deferral.Complete();
        }
    }
}
