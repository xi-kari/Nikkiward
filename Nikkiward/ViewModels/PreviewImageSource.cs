using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Nikkiward.ViewModels;

internal static class PreviewImageSource
{
    public static ImageSource? Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return new BitmapImage(uri);
    }
}
