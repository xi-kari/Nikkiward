using Nikkiward.Features.Updates;

namespace Nikkiward.Features.Pages;

internal sealed class AboutSection
{
    internal static readonly string[] Actions = ["channel", "check", "link", "release"];

    internal static readonly Dictionary<string, string> Links = new()
    {
        ["project"] = "", ["issues"] = "/issues", ["releases"] = "/releases",
        ["privacy"] = "/blob/main/PRIVACY.md", ["license"] = "/blob/main/LICENSE",
        ["notices"] = "/blob/main/THIRD-PARTY-NOTICES.md"
    };

    internal AppVersionInfo? Version { get; }
    internal UpdateChannel Channel { get; private set; }
    internal Uri? Release { get; private set; }
    internal string? Status { get; private set; }
    internal bool Checking { get; private set; }

    internal static Lazy<AboutSection> CreateLazy() => new(static () => new AboutSection());

    internal AboutSection()
    {
        try { Version = AppVersionProvider.GetCurrent(); }
        catch (InvalidOperationException ex) { Status = ex.Message; }
        Channel = Version?.Version.IsPrerelease == true ? UpdateChannel.Preview : UpdateChannel.Stable;
    }

    internal object State() => new {
        version = Version?.DisplayVersion ?? "版本信息无效", runtime = Version?.RuntimeIdentifier,
        distribution = Version?.DistributionKind, commit = Version?.CommitSha,
        channel = Channel.ToString(), status = Status, release = Release != null, checking = Checking, available = Version != null
    };

    internal void SetChannel(string value)
    {
        Channel = SettingsValues.Choice<UpdateChannel>(value);
        Release = null;
        Status = null;
    }

    internal async Task CheckAsync(CancellationToken token)
    {
        if (Version == null) return;
        Release = null;
        Status = null;
        Checking = true;
        try
        {
            var result = await new GitHubReleaseUpdateService().CheckAsync(Channel, Version.Version, token);
            Release = result.ReleaseUri;
            Status = result.Status switch
            {
                UpdateCheckStatus.UpdateAvailable => $"发现新版本：{result.CurrentVersion} → {result.LatestVersion}",
                UpdateCheckStatus.UpToDate => $"已是最新版本：{result.CurrentVersion}",
                _ => $"{(Channel == UpdateChannel.Preview ? "预览版" : "稳定版")}暂无公开发布"
            };
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { Status = "连接超时，请检查网络或代理后重试。"; }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidDataException)
        { Status = $"检查更新失败：{ex.Message}"; }
        finally { Checking = false; }
    }
}
