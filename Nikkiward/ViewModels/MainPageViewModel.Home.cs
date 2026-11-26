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
