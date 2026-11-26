using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter JournalStoragePageSection() => StorageSettingsAdapter("Journal");

    private PageAdapter StorageSettingsAdapter(string id) => new(id, ["open", "clear"], () => new Dictionary<string, string> {
        ["手账登录会话"] = JournalWebViewDataPath, ["统计快照"] = JournalSnapshotPath, ["公开图片缓存"] = JournalAssetsPath
    }, async (op, _) => {
        if (op == "open") await OpenJournalAsync();
        else if (await ConfirmSettingsActionAsync("清除手账缓存", "将清除本机手账与共鸣统计快照及公开图片。下次同步后可重新获取。", "清除"))
            await ClearJournalCacheAsync();
    });
}
