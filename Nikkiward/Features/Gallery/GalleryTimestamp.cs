using System.Globalization;
using System.Text.RegularExpressions;

namespace Nikkiward.Features.Gallery;

/// <summary>
/// Recovers a photo's capture time from its file name, falling back to the file
/// system timestamp. Sorting on <c>LastWriteTime</c> alone reorders the gallery
/// after any copy, move, or cloud sync.
/// </summary>
/// <remarks>
/// The parse-name-then-fall-back pattern is from Starward 0.18.1
/// (MIT, Copyright (c) 2023 Scighost). Only the Infinity Nikki format is kept;
/// the HoYoverse, Star Rail and Xbox formats are dropped.
/// </remarks>
public static partial class GalleryTimestamp
{
    /// <summary>
    /// Infinity Nikki photo mode writes <c>2025_12_06_00_26_33_394282.jpeg</c>.
    /// The trailing group is a disambiguator, not sub-second precision, so it is
    /// matched but not used.
    /// </summary>
    [GeneratedRegex(
        @"^(?<y>\d{4})_(?<mo>\d{2})_(?<d>\d{2})_(?<h>\d{2})_(?<mi>\d{2})_(?<s>\d{2})(?:_\d+)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex NikkiPhotoNamePattern();

    /// <summary>
    /// Returns the capture time as UTC. Uses the file name when it carries a
    /// timestamp, otherwise <paramref name="lastWriteTimeUtc"/>. Cloud photos are
    /// named with a bare id and always take the fallback.
    /// </summary>
    public static DateTime Resolve(string fileName, DateTime lastWriteTimeUtc)
    {
        var fallback = DateTime.SpecifyKind(lastWriteTimeUtc, DateTimeKind.Utc);
        return TryParseFileName(fileName, out var parsed) ? parsed : fallback;
    }

    /// <summary>
    /// True when the name itself carried the timestamp, which callers can use to
    /// explain why an item sorts where it does.
    /// </summary>
    public static bool TryParseFileName(string fileName, out DateTime timestampUtc)
    {
        timestampUtc = default;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var match = NikkiPhotoNamePattern().Match(stem);
        if (!match.Success)
        {
            return false;
        }

        var composed = string.Concat(
            match.Groups["y"].Value,
            match.Groups["mo"].Value,
            match.Groups["d"].Value,
            match.Groups["h"].Value,
            match.Groups["mi"].Value,
            match.Groups["s"].Value);

        // The game writes local wall-clock time, so parse as local and convert
        // rather than treating the digits as UTC.
        if (!DateTime.TryParseExact(
                composed,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var local))
        {
            return false;
        }

        timestampUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        return true;
    }
}
