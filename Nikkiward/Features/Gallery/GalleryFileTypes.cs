namespace Nikkiward.Features.Gallery;

/// <summary>
/// The single source of truth for which files the gallery shows. Upstream keeps a
/// watcher filter list and an <c>IsSupportedExtension</c> check separately, and
/// they disagree — new formats only appeared after a manual refresh.
/// </summary>
public static class GalleryFileTypes
{
    /// <summary>
    /// Infinity Nikki writes <c>.jpeg</c> for photo mode; the rest cover system
    /// screenshots and third-party capture tools.
    /// </summary>
    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(
            [".png", ".jpg", ".jpeg", ".webp", ".bmp"],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Case-insensitive, so <c>.JPG</c> is accepted.
    /// </summary>
    public static bool IsSupported(string? extension) =>
        !string.IsNullOrEmpty(extension) && SupportedExtensions.Contains(extension);
}
