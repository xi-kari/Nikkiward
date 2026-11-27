namespace Nikkiward.Features.Gallery;

public sealed partial class Nuan5GalleryMetadataService
{
    internal async Task<GalleryPhotoMetadata> ReadIndexMetadataAsync(string path, string original, CancellationToken token)
    {
        if (path.Equals(original, StringComparison.OrdinalIgnoreCase)) return await ReadAsync(path, token);
        if (GalleryFileTypes.UsesScreenshotCodec(Path.GetExtension(original)))
            return GalleryPhotoMetadata.NoParameters(GalleryPhotoMetadataStatus.CameraParametersUnavailable);
        if (ExtractUserId(original) is not { } userId)
            return GalleryPhotoMetadata.NoParameters(GalleryPhotoMetadataStatus.UserIdUnavailable);

        using var input = File.OpenRead(path);
        if (input.Length > MaximumFileSize)
            return GalleryPhotoMetadata.NoParameters(GalleryPhotoMetadataStatus.FileTooLarge);
        var bytes = new byte[(int)input.Length];
        await input.ReadExactlyAsync(bytes, token);
        await NativeCallGate.WaitAsync(token);
        try { return ReadWithNativeAdapter(bytes, userId); }
        catch (Exception ex) when (IsNativeLibraryUnavailable(ex))
        {
            return GalleryPhotoMetadata.NoParameters(GalleryPhotoMetadataStatus.NativeLibraryUnavailable);
        }
        catch (Exception) { return GalleryPhotoMetadata.NoParameters(GalleryPhotoMetadataStatus.DecryptionFailed); }
        finally { NativeCallGate.Release(); }
    }
}
