using NuGet.Versioning;

namespace Nikkiward.Features.Updates;

internal static class ReleaseSelector
{
    public static SelectedRelease? Select(IEnumerable<GitHubRelease> releases, UpdateChannel channel)
    {
        SelectedRelease? selected = null;

        foreach (var release in releases)
        {
            if (release.Draft ||
                (channel == UpdateChannel.Stable && release.Prerelease) ||
                !TryParseTag(release.TagName, out var version) ||
                release.Prerelease != version.IsPrerelease)
            {
                continue;
            }

            if (selected is null ||
                VersionComparer.VersionRelease.Compare(version, selected.Version) > 0)
            {
                selected = new SelectedRelease(release, version);
            }
        }

        return selected;
    }

    private static bool TryParseTag(string tag, out NuGetVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(tag) || tag[0] is not ('v' or 'V'))
        {
            return false;
        }

        if (!NuGetVersion.TryParse(tag[1..], out var parsed) || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }
}
