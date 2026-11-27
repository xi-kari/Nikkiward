using Nikkiward.Models;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Pages;

internal static class DeveloperSettingsState
{
    internal static Dictionary<string, string> Contract(MainPageViewModel model)
    {
        var steam = model.HomeCandidates.FirstOrDefault(c => c.Identity.DistributionChannel == DistributionChannel.Steam);
        var manifest = steam?.SteamManifest;
        return new() {
            ["渠道文件"] = "只有不可变且完整 SHA-256 相同的游戏资源才共用硬链接。启动器、引导程序、产品标记、反作弊、热更新、存档与账号数据按渠道分开。",
            ["启动参数"] = "-skiplauncher 是国服 Windows 的候选参数。-SkipLauncherTokenCheck_SD 仅用于 SteamOS / Proton，不用于国服或 Windows Steam。",
            ["Steam 应用编号"] = manifest == null ? "3164330（公开记录）" : manifest.AppId ?? "未读取",
            ["订阅编号"] = manifest?.SubId ?? "未读取", ["内容仓库编号"] = manifest?.DepotId ?? "未读取",
            ["构建编号"] = manifest?.BuildId ?? "未读取", ["清单编号"] = manifest?.ManifestId ?? "未读取",
            ["安装来源"] = steam?.DiscoverySource switch {
                ProfileDiscoverySource.ManualSelection => "手动选择", ProfileDiscoverySource.SteamLibraryManifest => "Steam 游戏库清单",
                ProfileDiscoverySource.SteamRegistry => "Steam 注册表", ProfileDiscoverySource.LauncherConfig => "启动器配置",
                ProfileDiscoverySource.LauncherRegistry => "启动器注册表", ProfileDiscoverySource.ChannelStoreReceipt => "渠道文件导入记录", _ => "尚未确定"
            },
            ["平台条件"] = "公开记录中的 Windows 入口为 launcher.exe，Linux 入口为 InfinityNikki\\InfinityNikki.exe。内部环境条件尚未核实。",
            ["进程通信"] = "公开资料未确认游戏的本地通信名称、消息格式或握手流程。启动器不会模拟这些连接或链接。"
        };
    }

    internal static object? ChannelTools(MainPageViewModel model) => !model.DeveloperModeEnabled ? null : new {
        facts = new Dictionary<string, string> {
            ["当前档案"] = model.ProfileDisplayName, ["版本"] = model.ProfileVersionText,
            ["渠道"] = model.ChannelText, ["平台"] = model.PlatformText,
            ["安装根"] = model.GameRootPath, ["官方后端"] = model.XStarterPath,
            ["身份来源"] = model.ProfileIdentityText, ["安装发现"] = model.ManualSelectionHint,
            ["存储位置"] = model.ChannelStoreRootPath, ["容量"] = model.ChannelStoreCapacityText,
            ["启动路径"] = model.SelectedChannelLaunchRoute, ["单本体状态"] = model.ChannelStoreStatusText,
            ["设置状态"] = model.SettingsStatusText
        },
        candidates = model.Profiles.Select(p => new { id = p.ProfileId, name = p.DisplayName, selected = p.IsSelected,
            channel = p.Channel, capability = p.CapabilitySummary, root = p.GameRootPath }),
        evidence = model.PlatformEvidence.Select(e => new { e.Title, e.Source, e.State, e.Detail, e.Capability }),
        discover = model.CanDiscover, plan = model.CanPlanChannelStore, build = model.CanBuildChannelStore,
        buildLabel = model.ChannelStoreBuildLabel, activate = model.CanActivateSelectedChannel,
        rollback = model.CanRollbackChannelActivation
    };

    internal static object Diagnostics(MainPageViewModel model) => new {
        components = model.Components.Select(c => new {
            name = c.DisplayName, status = c.StatusText, path = c.FilePath, fileVersion = c.FileVersionText,
            productVersion = c.ProductVersionText, signature = c.SignatureText.Split(" · ").Last(), sha = c.Sha256Text,
            metadata = c.FileMetadataText, issue = c.ErrorText.StartsWith("签名状态为 ", StringComparison.Ordinal) ? c.StatusText : c.ErrorText
        }),
        canExport = model.CanExport, exportStatus = model.ExportStatusText,
        artifact = model.ProviderValidationArtifactPath.Replace("provider/preflight", "启动渠道和启动检查"),
        validation = model.ProviderValidationStatusText.Replace("provider/preflight", "启动渠道和启动检查")
    };
}
