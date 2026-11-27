namespace Nikkiward.Features.Shell;

internal static class WebViewDefaults
{
    /// <summary>
    /// What a WebView2 shows before its page has painted. The default is white, which flashed on
    /// every page load; this dark base sits close to the skins' dark paper.
    /// </summary>
    internal static readonly Windows.UI.Color Background = Windows.UI.Color.FromArgb(255, 14, 18, 32);
}
