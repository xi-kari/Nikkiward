using Nikkiward.Models;

namespace Nikkiward.ViewModels;

#if DEBUG
internal static class HomePreview
{
    internal static readonly string? Skin = Environment.GetEnvironmentVariable("NIKKIWARD_HOME_SKIN");
    internal static readonly string? Art = Environment.GetEnvironmentVariable("NIKKIWARD_HOME_ART")?.ToLowerInvariant() switch
    {
        "blossom" or "花信" => AppearanceBackgroundPresets.Preset2Source,
        "tide" or "海月" => AppearanceBackgroundPresets.Preset1Source,
        _ => null
    };
    internal static readonly bool Idle = Environment.GetEnvironmentVariable("NIKKIWARD_HOME_IDLE") == "1";
    internal static readonly LaunchButtonState? State =
        Enum.TryParse<LaunchButtonState>(Environment.GetEnvironmentVariable("NIKKIWARD_HOME_STATE"), true, out var state)
        && Enum.IsDefined(state) ? state : Idle ? LaunchButtonState.Ready : null;
    internal static bool Active => Skin != null || Art != null || State.HasValue;
}
#endif

public sealed partial class MainPageViewModel
{
    private string _artSource = AppearanceBackgroundPresets.Preset1Source;
    private string _officialDuration = "奇想手账未同步";
    private string? _localDuration;

    public string OfficialJournalDuration { get => _officialDuration; internal set => SetField(ref _officialDuration, value); }
    public string? LocalPlayDuration
    {
        get => _localDuration;
        internal set
        {
            if (SetField(ref _localDuration, value)) OnPropertyChanged(nameof(HomeLocalTimeVisibility));
        }
    }
    public Microsoft.UI.Xaml.Visibility HomeLocalTimeVisibility => LocalPlayDuration == null
        ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    private DateTimeOffset? _observedStart;
    internal DateTimeOffset? HomeObservedStart => _isOfficialAssistedRunning && _activeProcessBinding?.ProfileId == SelectedProfileId ? _observedStart : null;
    internal static DateTimeOffset? RunningStart(OfficialAssistedProcessObservation observation, string gamePath) =>
        observation.GameProcesses.Where(p => string.Equals(p.ExecutablePath, gamePath, StringComparison.OrdinalIgnoreCase))
            .Min(p => (DateTimeOffset?)p.StartTimeUtc);
    internal string? PlayChannel => _selectedCandidate is { } c ? FormatDistribution(c.Identity.DistributionChannel) : null;
    internal int? HomeCheckCount => _preflightResult?.Components.Count is > 0 and var count ? count : null;
    public string HomeCheckText => HomeState == LaunchButtonState.Ready ? "检查通过" : HomeState.Label();
    public bool HasHomeTask => IsChannelStoreBusy ||
        _profileBuildResult?.Candidates.Any(c => c.State == InstallationCandidateState.Downloading) == true;
    public string HomeTaskText => IsChannelStoreBusy ? ChannelStoreStatusText : DownloadStatusText;

    public LaunchButtonState HomeState =>
#if DEBUG
        HomePreview.State ??
#endif
        LaunchButtonState;
    public string HomeLaunchLabel => HomeState.Label();
    public string HomeLaunchDetail => DescribeLaunchState(HomeState);
    public string HomeLaunchConclusion => Features.Launcher.Skins.HomeOverlayState.From(this).Conclusion;
    public bool HomeLaunchEnabled =>
#if DEBUG
        HomePreview.State.HasValue ? HomeState == LaunchButtonState.Ready :
#endif
        CanAttemptSelectedChannelLaunch;
    public bool HomeBusy => HomeState is LaunchButtonState.Checking or LaunchButtonState.Launching;
    public bool HomeCloseEnabled =>
#if DEBUG
        HomePreview.State.HasValue ? HomeState is LaunchButtonState.Running or LaunchButtonState.CleanupRequired :
#endif
        CanCloseGame;
    public Microsoft.UI.Xaml.Visibility HomeCloseVisibility => HomeCloseEnabled
        ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private DistributionChannel HomeChannel => _selectedCandidate?.Identity.DistributionChannel ?? _profile?.Channel switch
    {
        "CN" => DistributionChannel.Official,
        "Bilibili" => DistributionChannel.Bilibili,
        "Steam Global" => DistributionChannel.Steam,
        _ => DistributionChannel.Unknown
    };
    internal (string Region, string Title, string Channel) HomeProfile => FormatHomeProfile(_selectedCandidate?.Identity.RegionFamily, HomeChannel, ProfileDisplayName);
    internal static (string Region, string Title, string Channel) FormatHomeProfile(RegionFamily? region, DistributionChannel channel, string fallback)
    {
        var label = region switch
        {
            RegionFamily.MainlandChina => "国服",
            RegionFamily.Overseas => "国际服",
            _ when channel == DistributionChannel.Steam => "国际服",
            _ when channel is DistributionChannel.Official or DistributionChannel.Bilibili => "国服",
            _ => "未配置"
        };
        var provider = channel switch
        {
            DistributionChannel.Official => "官方",
            DistributionChannel.Bilibili => "哔哩哔哩",
            DistributionChannel.Steam => "Steam",
            DistributionChannel.Epic => "Epic",
            _ => null
        };
        return (label, provider == null ? fallback : $"{label} · {provider}", provider ?? "未知渠道");
    }
    internal string? HomeReadySummary => HomeState != LaunchButtonState.Ready ? null : HomeChannel switch
    {
        DistributionChannel.Steam => "Steam · 签名已核对",
        DistributionChannel.Bilibili => "哔哩哔哩 · 签名已核对",
        _ => SelectedLauncherVersionName is { } version ? $"官方启动器 {version} · 签名已核对" : "签名已核对"
    };

    private string _webPageFallback = "";
    public string WebPageFallback { get => _webPageFallback; internal set => SetField(ref _webPageFallback, RedactUiText(value)); }

    internal void ReportWebPageFallback(string page, string reason) =>
        WebPageFallback = $"功能页面加载失败（{page}）：{reason}";

    private string _homeFallback = "";
    public string HomeFallback { get => _homeFallback; internal set => SetField(ref _homeFallback, value); }
    internal IReadOnlyList<InstallationProfileCandidate> HomeCandidates => _profileBuildResult?.Candidates ?? [];
    public string HomeArtKey => _artSource;
    public bool CanNameArtwork => !AppearanceBackgroundPresets.IsBuiltInSource(_artSource);
    public string HomeArtSource => CanNameArtwork ? "自定义背景" : "内置原画";
    public string HomeArtName
    {
        get
        {
            if (_artSource == AppearanceBackgroundPresets.Preset1Source) return "海月";
            if (_artSource == AppearanceBackgroundPresets.Preset2Source) return "花信";
            AppearanceSettings.ArtworkTitles.TryGetValue(_artSource, out var title);
            return !string.IsNullOrWhiteSpace(title?.Name) ? title.Name : title?.OriginalFileName is { Length: > 0 } file
                ? Path.GetFileNameWithoutExtension(file) : "自定义背景";
        }
    }

    internal void ShowArtwork(string source)
    {
#if DEBUG
        source = HomePreview.Art ?? source;
#endif
        _artSource = source;
        OnPropertyChanged(nameof(HomeArtName));
        OnPropertyChanged(nameof(HomeArtSource));
        OnPropertyChanged(nameof(CanNameArtwork));
    }
}
