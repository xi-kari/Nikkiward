namespace Nikkiward.Features.Gallery;

internal static class GalleryFavoriteChange
{
    internal static async Task<bool> TryUnstarAsync(GalleryFavoriteProtectionService service, string scope, string relative,
        Func<string, string, Task<bool>> confirm, CancellationToken token)
    {
        var approved = new List<GalleryFavoriteProtectionService.UnstarConfirmation>();
        while (true)
        {
            var pending = await service.TryUnstarAsync(scope, relative, token, approved);
            if (pending.Count == 0) return true;
            var messages = pending.Select(copy =>
            {
                var reason = copy.Reason switch
                {
                    GalleryOriginalIssue.Missing => "原图已不存在",
                    GalleryOriginalIssue.Changed => "原图已被修改",
                    _ => "无法读取原图以核对内容"
                };
                var consequence = copy.Paused ? "保护已暂停，此副本将保留至清理时。" : "取消收藏会删除此版本仅存的本地保护副本。";
                return (pending.Count > 1 ? copy.RootPath + "\n" : "") + reason + "。" + consequence;
            });
            if (!await confirm(string.Join("\n\n", messages), pending.Any(copy => !copy.Paused) ? "取消收藏并删除副本" : "取消收藏"))
                return false;
            token.ThrowIfCancellationRequested();
            approved.AddRange(pending);
        }
    }

    internal static async Task<bool> SetAsync(GalleryAnnotationStore annotations, GalleryFavoriteProtectionService protection,
        string scope, string relative, string path, bool starred, Func<string, string, Task<bool>> confirm, CancellationToken token)
    {
        if (!starred && scope != GalleryDefaultFavoriteSeedService.ScopeId)
            return await TryUnstarAsync(protection, scope, relative, confirm, token);
        await annotations.SetStarredAsync(scope, relative, starred, token);
        if (starred && scope != GalleryDefaultFavoriteSeedService.ScopeId)
            await protection.ProtectAsync(scope, relative, path, token);
        return true;
    }
}
