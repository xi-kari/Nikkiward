using Microsoft.UI.Xaml;
using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private Dictionary<string, PageAdapter>? _pageAdapters;
    private bool _webPagesFailed;
    private WebPagesView? CurrentWebPage => ContentFrame.Content as WebPagesView;

    private bool TryShowWebPage(string page)
    {
        _pageAdapters ??= PageRoutes.Register(("wish", WishPageAdapter()), ("journal", JournalPageAdapter()));
        if (_webPagesFailed || !_pageAdapters.TryGetValue(page, out var adapter)) return false;
        ContentFrame.Visibility = Visibility.Visible;
        if (CurrentWebPage is not { } view)
        {
            ContentFrame.Navigate(typeof(WebPagesView));
            view = CurrentWebPage!;
        }
        view.Show(this, page, adapter);
        SyncLauncherChrome();
        return true;
    }

    internal Task NavigateWebPageAsync(string route) => ExecuteShellNavigationAsync(PageRoutes.NavigationTag(route)!);

    internal async Task WebPageFailedAsync(WebPagesView view, string page, string reason)
    {
        if (view != CurrentWebPage) return;
        _webPagesFailed = true;
        ViewModel.ReportUiError($"功能页面已回退到原生界面：{reason}");
        await NavigateWebPageAsync(page);
    }
}

