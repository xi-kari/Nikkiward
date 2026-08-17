namespace Nikkiward.Features.Background;

public enum BackgroundSourceKind
{
    None,
    StillImage,
    Motion,
}

public sealed record BackgroundSourceDescriptor(
    BackgroundSourceKind Kind,
    string Source,
    string? DisplayName = null)
{
    public static BackgroundSourceDescriptor Still(string source, string? displayName = null) =>
        new(BackgroundSourceKind.StillImage, source, displayName);

    public static BackgroundSourceDescriptor Motion(string source, string? displayName = null) =>
        new(BackgroundSourceKind.Motion, source, displayName);

    public static BackgroundSourceDescriptor Default() =>
        new(BackgroundSourceKind.None, string.Empty, "Nikkiward 内置背景");
}

public readonly record struct ArtRegionLuminance(
    string RegionId,
    double MeanLuminance,
    double P95Luminance);

public sealed record BackgroundSourceValidation(
    bool IsUsable,
    string? RejectReason = null,
    string? MissingCodecStoreProductId = null)
{
    public static BackgroundSourceValidation Accepted { get; } = new(true);
}

public interface IBackgroundSampler
{
    BackgroundSourceKind Kind { get; }

    bool CanServe(BackgroundSourceDescriptor descriptor);

    Task<string?> TryIdentifyAsync(
        BackgroundSourceDescriptor descriptor,
        CancellationToken cancellationToken);

    Task<ArtPixelBuffer?> SampleAsync(
        BackgroundSourceDescriptor descriptor,
        int targetWidth,
        TimeSpan position,
        CancellationToken cancellationToken);

    Task<BackgroundSourceValidation> ValidateAsync(
        BackgroundSourceDescriptor descriptor,
        CancellationToken cancellationToken);
}
