using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Nikkiward.Features.Gallery;

internal static class GalleryFavoriteCleanupDialog
{
    public static async Task CleanAsync(GalleryFavoriteProtectionService protection, string scopeId,
        XamlRoot xamlRoot, CancellationToken cancellationToken)
    {
        var approved = new HashSet<GalleryCleanupCopy>();
        while (true)
        {
            var result = await protection.CleanUnstarredAsync(scopeId, cancellationToken, approved);
            if (result.NeedsConfirmation.Count == 0)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var names = result.NeedsConfirmation.GroupBy(copy => copy.DisplayName, StringComparer.OrdinalIgnoreCase)
                .SelectMany(group => group.Count() == 1 ? [group.Key] :
                    group.Select(copy => $"{copy.DisplayName}（{copy.ObjectPath}）"));
            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = "删除这些保护副本？",
                Content = new ScrollViewer
                {
                    MaxHeight = 320,
                    Content = new TextBlock
                    {
                        Text = "无法确认以下版本还有内容相同的原图。清理会永久删除这些本地保护副本。\n\n" +
                            string.Join("\n", names),
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
                PrimaryButtonText = "删除副本",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
            approved.UnionWith(result.NeedsConfirmation);
        }
    }
}
