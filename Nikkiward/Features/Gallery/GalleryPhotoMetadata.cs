namespace Nikkiward.Features.Gallery;

public enum GalleryPhotoMetadataAvailability
{
    NoParameters,
    Available,
}

public enum GalleryPhotoMetadataStatus
{
    Available,
    Cancelled,
    FileUnavailable,
    FileTooLarge,
    UserIdUnavailable,
    NativeLibraryUnavailable,
    AbiVersionMismatch,
    NativeKeyUnavailable,
    DecryptionFailed,
    CameraParametersUnavailable,
    InvalidPayload,
}

public sealed record GalleryPhotoAdjustment(string Id, double Strength);

public sealed record GalleryPhotoCameraParameters
{
    public double FocalLength { get; init; }

    public double Aperture { get; init; }

    public bool PortraitMode { get; init; }

    public GalleryPhotoAdjustment? Light { get; init; }

    public double Vignette { get; init; }

    public double Bloom { get; init; }

    public double BloomThreshold { get; init; }

    public double Brightness { get; init; }

    public double Exposure { get; init; }

    public double Contrast { get; init; }

    public double Saturation { get; init; }

    public double Vibrance { get; init; }

    public double Highlights { get; init; }

    public double Shadows { get; init; }

    public GalleryPhotoAdjustment? Filter { get; init; }
}

public sealed record GalleryPhotoLocation(double X, double Y, double Z);

public sealed record GalleryPhotoTask(string Kind, long? Id);

public sealed record GalleryPhotoMetadata
{
    public const string NoParametersDisplayText = "该照片无游戏内参数";

    public GalleryPhotoMetadataAvailability Availability { get; init; }

    public GalleryPhotoMetadataStatus Status { get; init; }

    public string DisplayStatus =>
        Availability == GalleryPhotoMetadataAvailability.Available
            ? "已读取游戏内参数"
            : NoParametersDisplayText;

    public string? UserId { get; init; }

    public GalleryPhotoCameraParameters? Camera { get; init; }

    public long? PoseId { get; init; }

    public long? FramedMoment { get; init; }

    public IReadOnlyList<long> ClothingIds { get; init; } = [];

    public GalleryPhotoLocation? Location { get; init; }

    public IReadOnlyList<GalleryPhotoTask> Tasks { get; init; } = [];

    public bool HasParameters => Availability == GalleryPhotoMetadataAvailability.Available;

    internal static GalleryPhotoMetadata NoParameters(
        GalleryPhotoMetadataStatus status,
        string? userId = null) =>
        new()
        {
            Availability = GalleryPhotoMetadataAvailability.NoParameters,
            Status = status,
            UserId = userId,
        };
}
