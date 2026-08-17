namespace Nikkiward.Features.Background;

public readonly record struct MotionSourceFacts(
    string Extension,
    ulong FileSize,
    uint Width,
    uint Height,
    double FramesPerSecond,
    string? VideoSubtype,
    string? AudioSubtype);

public static class MotionSourceRules
{
    public const uint MaximumLongEdge = 7680;
    public const uint MaximumShortEdge = 4320;

    public static IReadOnlyList<string> SupportedExtensions { get; } =
        Array.AsReadOnly(
        [
            ".mp4",
            ".m4v",
            ".mp4v",
            ".mov",
            ".qt",
            ".mkv",
            ".webm",
            ".avi",
            ".wmv",
            ".asf",
            ".mpeg",
            ".mpg",
            ".mpe",
            ".mpv",
            ".m1v",
            ".m2v",
            ".vob",
            ".ts",
            ".m2ts",
            ".mts",
            ".m2t",
            ".3gp",
            ".3g2",
            ".3gpp",
            ".3gp2",
            ".ogv",
            ".ogg",
            ".wtv",
            ".dvr-ms",
            ".flv",
            ".f4v",
            ".rm",
            ".rmvb",
        ]);

    public static bool IsSupportedExtension(string? extension) =>
        SupportedExtensions.Contains(extension ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    public static BackgroundSourceValidation Validate(MotionSourceFacts facts)
    {
        if (facts.FileSize == 0)
        {
            return new(false, "视频背景文件不能为空。");
        }

        var longEdge = Math.Max(facts.Width, facts.Height);
        var shortEdge = Math.Min(facts.Width, facts.Height);
        if (facts.Width == 0 || facts.Height == 0 ||
            longEdge > MaximumLongEdge || shortEdge > MaximumShortEdge)
        {
            return new(false, "视频背景最高支持 8K（7680×4320）。");
        }

        if (!double.IsFinite(facts.FramesPerSecond) || facts.FramesPerSecond <= 0)
        {
            return new(false, "视频背景的帧率信息无效。");
        }

        return BackgroundSourceValidation.Accepted;
    }
}
