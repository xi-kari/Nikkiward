using Nikkiward.Features.Pages;

namespace Nikkiward;

public sealed partial class MainPage
{
    private PageAdapter AdvancedPageSection() => new("Advanced", ["change", "profile"], () => new {
        developer = ViewModel.DeveloperModeEnabled, profiles = DeveloperSettingsState.ChannelTools(ViewModel),
        contract = DeveloperSettingsState.Contract(ViewModel)
    }, async (op, message) => {
        if (op == "profile" && ViewModel.DeveloperModeEnabled) { await RunChannelToolAsync(message); return; }
        if (op != "change" || SettingsValues.Field(message, "field") != "developer") throw SettingsValues.Invalid();
        await ViewModel.SaveDeveloperModeAsync(SettingsValues.Toggle(SettingsValues.Field(message, "value")),
            _lifetimeCancellation?.Token ?? CancellationToken.None);
    });

    private async Task RunChannelToolAsync(System.Text.Json.JsonElement message)
    {
        var token = _lifetimeCancellation?.Token ?? CancellationToken.None;
        switch (SettingsValues.Field(message, "tool"))
        {
            case "discover" when ViewModel.CanDiscover: await DiscoverProfilesAsync(); break;
            case "game" when ViewModel.CanDiscover: await ChooseGameRootAsync(); break;
            case "launcher" when ViewModel.CanDiscover: await ChooseLauncherRootAsync(); break;
            case "select":
                var id = SettingsValues.Field(message, "id");
                if (!ViewModel.Profiles.Any(p => p.ProfileId == id)) throw SettingsValues.Invalid();
                await SelectProfileAsync(id);
                break;
            case "root" when ViewModel.CanPlanChannelStore: await ChooseChannelStoreRootAsync(); break;
            case "plan" when ViewModel.CanPlanChannelStore:
                var root = _manualChannelStoreRootPath ?? ViewModel.ChannelStoreRootPath;
                if (!Path.IsPathFullyQualified(root)) root = await ChooseChannelStoreRootAsync();
                if (root != null) await ViewModel.PlanChannelStoreAsync(root, token);
                break;
            case "build" when ViewModel.CanBuildChannelStore:
                if (await ConfirmWebSettingsAsync("创建单本体？", "将按当前预演计划在所选位置创建渠道存储。", "创建单本体"))
                    await ViewModel.BuildChannelStoreAsync(token);
                break;
            case "activate" when ViewModel.CanActivateSelectedChannel:
                if (await ConfirmWebSettingsAsync("激活渠道？", "将更新官方启动器的游戏目录，指向所选渠道。", "激活渠道"))
                    await ViewModel.ActivateSelectedChannelAsync(token);
                break;
            case "rollback" when ViewModel.CanRollbackChannelActivation:
                if (await ConfirmWebSettingsAsync("撤销渠道激活？", "将按最近一次激活的回执恢复启动器之前的游戏目录。", "撤销渠道激活"))
                    await ViewModel.RollbackLastChannelActivationAsync(token);
                break;
            default: throw SettingsValues.Invalid();
        }
    }
}
