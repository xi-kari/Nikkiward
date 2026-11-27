using Microsoft.UI.Xaml;
using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private Dictionary<string, PageAdapter>? _pageAdapters;

    private WebPagesView? CurrentWebPage => ContentFrame.Content as WebPagesView;

    private void ShowWebPage(string page)
    {
        ContentFrame.Visibility = Visibility.Visible;
        try
        {
            _pageAdapters ??= PageRoutes.Register(("wish", WishPageAdapter()), ("journal", JournalPageAdapter()),
                ("settings", SettingsPageAdapter()), ("gallery", GalleryPageAdapter(false)), ("favorites", GalleryPageAdapter(true)));
            var adapter = _pageAdapters[page];
            if (CurrentWebPage == null && !ContentFrame.Navigate(typeof(WebPagesView)))
                throw new InvalidOperationException("无法打开功能页面宿主。");
            CurrentWebPage!.Show(this, page, adapter);
            SyncLauncherChrome();
        }
        catch (Exception ex)
        {
            ViewModel.ReportWebPageFallback(page, $"{ex.GetType().Name}: {ex.Message}");
            ShowWebPageError(page, ViewModel.WebPageFallback);
        }
    }

    internal Task NavigateWebPageAsync(string route) => ExecuteShellNavigationAsync(PageRoutes.NavigationTag(route)!);

    internal void WebPageFailed(WebPagesView view, string page, string reason)
    {
        if (view != CurrentWebPage) return;
        ViewModel.ReportWebPageFallback(page, reason);
        var diagnostic = ViewModel.WebPageFallback;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (view == CurrentWebPage && view.CurrentPage == page)
                ShowWebPageError(page, diagnostic);
        });
    }

    private void ShowWebPageError(string page, string diagnostic)
    {
        ContentFrame.Navigate(typeof(WebPageErrorView), new WebPageErrorView.Failure(
            diagnostic, () => ShowWebPage(page), ShowLauncher));
    }
}
