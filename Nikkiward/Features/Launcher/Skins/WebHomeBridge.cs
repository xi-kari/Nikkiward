using System.Text.Json;
using System.Text.Json.Serialization;
using Nikkiward.Features.Background;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Launcher.Skins;

internal sealed record HomeBackground(string Type, string Url, string? Poster = null)
{
    internal const string StillHost = "still.nikkiward.invalid", MotionHost = "motion.nikkiward.invalid";
    internal static string StillFolder(string root) => Path.Combine(root, "WallpaperImports", "Still");
    internal static string MotionFolder(string root) => Path.Combine(root, "MotionBackgrounds");

    internal static (string Still, string Motion) CreateMediaFolders(string root) =>
        (Directory.CreateDirectory(StillFolder(root)).FullName, Directory.CreateDirectory(MotionFolder(root)).FullName);

    internal static async Task<HomeBackground> ResolveAsync(string art, string scenePreview, string root,
        Func<string, bool, Task<string>> preview)
    {
        bool scene = WallpaperSourceRules.IsPackageExtension(Path.GetExtension(art));
        var source = scene ? scenePreview : art;
        if (AppearanceBackgroundPresets.IsBuiltInSource(source))
            return new("image", $"https://{WebHomeBridge.Host}/art/{Path.GetFileName(source)}");

        var file = new FileInfo(source);
        string Url(string host, string name) => $"https://{host}/{Uri.EscapeDataString(name)}";
        var ext = file.Extension.ToLowerInvariant();
        bool video = MotionSourceRules.IsSupportedExtension(ext);
        bool direct = ext is ".jpg" or ".jpeg" or ".bmp" || !scene && ext is (".png" or ".gif" or ".webp" or ".avif");
        if (direct && string.Equals(file.DirectoryName, StillFolder(root), StringComparison.OrdinalIgnoreCase))
            return new("image", Url(StillHost, file.Name));

        var poster = Url(StillHost, await preview(source, video));
        return video && string.Equals(file.DirectoryName, MotionFolder(root), StringComparison.OrdinalIgnoreCase)
            ? new("video", Url(MotionHost, file.Name), poster)
            : new("image", poster);
    }
}

internal sealed record HomeProfileOption(string Id, string Name, string Region, string Channel, bool Available, string Reason, bool Current, string? Detail);
internal sealed record HomePhoto(string Id, string FileName, string Time, string? Thumbnail, string? Session = null);
internal sealed record HomeChecklist(int Completed, int Total, string? Next, ChecklistItem[] Items)
{
    internal static HomeChecklist? From(ChecklistItem[] items) => items.Length == 0 ? null :
        new(items.Count(i => i.Done), items.Length, items.FirstOrDefault(i => !i.Done)?.Text, items);
}
internal sealed record HomeRect(double X, double Y, double Width, double Height);
internal sealed record HomeAction(string Action, string? PhotoId = null, HomeRect? Rect = null, string? ProfileId = null, PlannerCommand? Planner = null);
internal sealed record WebHomeState(LaunchButtonState State, string LaunchLabel, string Detail, bool Enabled, bool Busy,
    bool CloseVisible, bool CloseEnabled, string CloseLabel, string Profile, string Channel, string Check, string CheckTime,
    string? OfficialDuration, string? LocalDuration, string? TaskText, double? Progress, string ArtName, string ArtSource,
    string Theme, string Accent, bool Still,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<HomePhoto>? Photos, DateOnly Date)
{
    internal static WebHomeState Create(MainPageViewModel vm, string theme, uint accent, bool still, IReadOnlyList<HomePhoto> photos)
    {
        var profile = vm.HomeProfile;
        var (paperTheme, fill, ink) = vm.HomeArtKey switch
        {
            AppearanceBackgroundPresets.Preset1Source => ("dark", "#A9DBFF", "#0A1322"),
            AppearanceBackgroundPresets.Preset2Source => ("light", "#B43B56", "#FFF8F2"),
            _ => (theme, $"#{accent & 0xffffff:X6}", ContrastInk(accent))
        };
        return new WebHomeState(vm.HomeState, vm.HomeLaunchLabel, vm.HomeLaunchDetail, vm.HomeLaunchEnabled, vm.HomeBusy,
            vm.HomeCloseVisibility == Microsoft.UI.Xaml.Visibility.Visible, vm.HomeCloseEnabled, vm.CloseGameButtonLabel,
            profile.Title, profile.Region, vm.HomeCheckText, vm.HomeCheckTime,
            vm.OfficialJournalDuration == "奇想手账未同步" ? null : vm.OfficialJournalDuration, vm.LocalPlayDuration,
            vm.HasHomeTask ? vm.HomeTaskText : null,
            vm.HasHomeTask && vm.ChannelStoreProgressVisibility == Microsoft.UI.Xaml.Visibility.Visible ? vm.ChannelStoreProgressPercent : null,
            vm.HomeArtName, vm.HomeArtSource, paperTheme, fill, still, photos, DateOnly.FromDateTime(DateTime.Today))
        {
            ChecklistReset = vm.GeneralSettings.ChecklistReset,
            CheckCount = vm.HomeCheckCount,
            DiscoveryTime = vm.HomeCandidates.Count == 0 ? null : vm.HomeCandidates.Max(c => c.ObservedAtUtc).ToLocalTime().ToString("MM-dd HH:mm"),
            Profiles = ProfileRows(vm.HomeCandidates, vm.SelectedProfileId).ToArray(),
            ReadySummary = vm.HomeReadySummary,
            OnAccent = ink,
        };
    }

    internal static IEnumerable<HomeProfileOption> ProfileRows(IReadOnlyList<InstallationProfileCandidate> candidates, string? selected)
    {
        var groups = candidates.ToLookup(c => c.Identity.DistributionChannel);
        DistributionChannel[] channels = [DistributionChannel.Official, DistributionChannel.Bilibili, DistributionChannel.Steam];
        foreach (var channel in channels.Union(groups.Select(g => g.Key)))
        {
            var group = groups[channel];
            var choices = group.Where(c => c.Profile != null &&
                c.State is InstallationCandidateState.Candidate or InstallationCandidateState.ReadyForStaticVerification).ToArray();
            if (choices.Length > 0)
            {
                foreach (var c in choices)
                {
                    var name = MainPageViewModel.FormatHomeProfile(c.Identity.RegionFamily, channel, c.DisplayName);
                    var path = c.GameRootPath ?? c.Profile!.GameRootPath;
                    yield return new(c.ProfileId, name.Title, name.Region, name.Channel, true,
                        choices.Length > 1 ? path : "已发现安装", c.ProfileId == selected, choices.Length > 1 ? path : null);
                }
                continue;
            }

            var first = group.FirstOrDefault();
            var label = MainPageViewModel.FormatHomeProfile(first?.Identity.RegionFamily, channel, first?.DisplayName ?? "");
            var reason = group.Any(c => c.State == InstallationCandidateState.Downloading) ? "正在下载" :
                group.Any(c => c.State == InstallationCandidateState.Incomplete) ? "安装不完整" :
                group.Any(c => c.State == InstallationCandidateState.Unsupported) ? "暂不支持" : "未发现安装";
            var details = string.Join("\n", group.Select(c => c.Detail).Where(d => !string.IsNullOrWhiteSpace(d)).Distinct());
            yield return new("", label.Title, label.Region, label.Channel, false, reason, false, details.Length > 0 ? details : null);
        }
    }

    private static string ContrastInk(uint fill)
    {
        var luminance = ArtPaletteAnalyzer.RelativeLuminance((byte)(fill >> 16), (byte)(fill >> 8), (byte)fill);
        if (ArtPaletteAnalyzer.ContrastRatio(luminance, ArtPaletteAnalyzer.RelativeLuminance(0x2a, 0x23, 0x20)) >= 4.5)
            return "#2A2320";
        return ArtPaletteAnalyzer.ContrastRatio(luminance, 1) >= 4.5 ? "#FFFFFF" : "#000000";
    }

    public HomeBackground? Background { get; init; }
    public HomeProfileOption[] Profiles { get; init; } = [];
    public string? DiscoveryTime { get; init; }
    public string Platform => Channel + " · Windows";
    public bool Running => State == LaunchButtonState.Running;
    public int? SessionMinutes { get; init; }
    public LocalReminder[] Reminders { get; init; } = [];
    public int ReminderCount => Reminders.Length;
    public LocalReminder? NextReminder => Reminders.Where(r => r.Due.HasValue).MinBy(r => r.Due);
    public PinnedLink[] Links { get; init; } = [];
    public DateTimeOffset? SessionStarted { get; init; }
    public int? CurrentSessionPhotos { get; init; }
    public string[] Palette { get; init; } = [];
    public bool ContrastPassed => ArtPaletteAnalyzer.ContrastRatio(Luminance(Accent), Luminance(OnAccent)) >= 4.5;
    private static double Luminance(string hex)
    {
        uint rgb = Convert.ToUInt32(hex[1..], 16);
        return ArtPaletteAnalyzer.RelativeLuminance((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
    public string[] NextActions => Visual switch { "notfound" => ["locate", "rediscover"], "blocked" => ["status", "recheck"], _ => [] };
    public string OnAccent { get; init; } = "#2A2320";
    public string? ReadySummary { get; init; }
    public int? CheckCount { get; init; }
    public DateOnly? JournalSnapshotDate { get; init; }
    public int? PhotoTotal { get; init; }
    public int? LastSessionPhotos { get; init; }
    public string? Skin { get; init; }
    public TimeOnly ChecklistReset { get; init; }
    public HomeChecklist? Checklist { get; init; }
    private static readonly string[] Months = "一 二 三 四 五 六 七 八 九 十 十一 十二".Split();
    public string Issue => string.Concat(Date.Year.ToString("D4").Select(c => "〇一二三四五六七八九"[c - '0']))
        + "年" + Months[Date.Month - 1] + "月号";

    public bool ProfileOpen { get; init; }

    public string Visual => State switch
    {
        LaunchButtonState.Ready => "ready",
        LaunchButtonState.Launching => "launching",
        LaunchButtonState.Running => "running",
        LaunchButtonState.NotInstalled => "notfound",
        LaunchButtonState.Checking => "checking",
        _ => "blocked"
    };
}

internal static class WebHomeBridge
{
    internal const string Host = "home.nikkiward.invalid";
    internal static string Page(string skin) => $"https://{Host}/skins/{skin}/index.html";
    internal static bool AllowsPage(string page, string uri) => uri == page;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<LaunchButtonState>() }
    };

    internal static HomeAction? Parse(string json)
    {
        HomeAction? message;
        try { message = JsonSerializer.Deserialize<HomeAction>(json, Json); }
        catch (JsonException) { return null; }
        return message?.Action switch
        {
            "planner" when message.Planner?.Valid() == true => message,
            "profile" when message.Rect is { X: >= 0, Y: >= 0, Width: > 0, Height: > 0 } r &&
                double.IsFinite(r.X + r.Width) && double.IsFinite(r.Y + r.Height) => message,
            "select-profile" when !string.IsNullOrEmpty(message.ProfileId) => message,
            "locate" or "rediscover" or "recheck" or "tide" or "blossom" => message,
            "photo" when !string.IsNullOrEmpty(message.PhotoId) => message,
            "launch" or "close" or "settings" or "gallery" or "journal" or "background" or "reset" or "status" or
            "download" or "time" or "nav-launcher" or "nav-library" or "nav-gallery" or "nav-gallery-favorites" or
            "nav-resonance" or "nav-settings" => message,
            _ => null
        };
    }
}
