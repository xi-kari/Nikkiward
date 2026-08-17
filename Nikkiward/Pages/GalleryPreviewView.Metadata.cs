using Nikkiward.Features.Gallery;

namespace Nikkiward.Pages;

public sealed partial class GalleryPreviewView
{
    private void StartMetadataLoad(string filePath)
    {
        CancelMetadataLoad();
        var loader = MetadataLoader;
        if (loader is null)
        {
            ApplyMetadata(GalleryPhotoMetadata.NoParameters(
                GalleryPhotoMetadataStatus.NativeLibraryUnavailable));
            return;
        }

        var cancellation = new CancellationTokenSource();
        _metadataCancellation = cancellation;
        _ = LoadMetadataAsync(filePath, loader, cancellation);
    }

    private async Task LoadMetadataAsync(
        string filePath,
        Func<string, CancellationToken, Task<GalleryPhotoMetadata>> loader,
        CancellationTokenSource cancellation)
    {
        try
        {
            var metadata = await loader(filePath, cancellation.Token);
            if (!cancellation.IsCancellationRequested &&
                ReferenceEquals(_metadataCancellation, cancellation))
            {
                ApplyMetadata(metadata);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (ReferenceEquals(_metadataCancellation, cancellation))
            {
                ApplyMetadata(GalleryPhotoMetadata.NoParameters(
                    GalleryPhotoMetadataStatus.InvalidPayload));
            }
        }
        finally
        {
            if (ReferenceEquals(_metadataCancellation, cancellation))
            {
                _metadataCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelMetadataLoad()
    {
        var cancellation = Interlocked.Exchange(ref _metadataCancellation, null);
        cancellation?.Cancel();
    }

    private void ApplyMetadata(GalleryPhotoMetadata metadata)
    {
        if (!metadata.HasParameters || metadata.Camera is null)
        {
            GalleryInfoCameraText.Text = GalleryPhotoMetadata.NoParametersDisplayText;
            GalleryInfoFilterText.Text = string.Empty;
            GalleryInfoOutfitText.Text = string.Empty;
            GalleryInfoLocationText.Text = string.Empty;
            GalleryInfoTasksText.Text = string.Empty;
            return;
        }

        var camera = metadata.Camera;
        GalleryInfoCameraText.Text =
            $"焦距 {camera.FocalLength:0.##} · 光圈 {camera.Aperture:0.##}" +
            $" · 人像模式 {(camera.PortraitMode ? "开启" : "关闭")}" +
            $" · 姿势 {(metadata.PoseId?.ToString() ?? "未记录")}" +
            $" · 定格 {(metadata.FramedMoment?.ToString() ?? "未记录")}";
        GalleryInfoFilterText.Text =
            $"{FormatAdjustment("灯光", camera.Light)} · {FormatAdjustment("滤镜", camera.Filter)}" +
            Environment.NewLine +
            $"暗角 {camera.Vignette:0.##} · 泛光 {camera.Bloom:0.##}" +
            $" · 泛光阈值 {camera.BloomThreshold:0.##} · 亮度 {camera.Brightness:0.##}" +
            $" · 曝光 {camera.Exposure:0.##} · 对比度 {camera.Contrast:0.##}" +
            $" · 饱和度 {camera.Saturation:0.##} · 鲜艳度 {camera.Vibrance:0.##}" +
            $" · 高光 {camera.Highlights:0.##} · 阴影 {camera.Shadows:0.##}";
        GalleryInfoOutfitText.Text = metadata.ClothingIds.Count == 0
            ? "服装：未记录"
            : $"服装部件 ID：{string.Join(" · ", metadata.ClothingIds)}";
        GalleryInfoLocationText.Text = metadata.Location is null
            ? "地点/任务：未记录"
            : $"坐标 {metadata.Location.X:0.##}, {metadata.Location.Y:0.##}, {metadata.Location.Z:0.##}";
        GalleryInfoTasksText.Text = metadata.Tasks.Count == 0
            ? "任务：未记录"
            : $"任务：{string.Join(" · ", metadata.Tasks.Select(FormatTask))}";
    }

    private static string FormatTask(GalleryPhotoTask task) =>
        task.Id is null ? task.Kind : $"{task.Kind} {task.Id.Value:N0}";

    private static string FormatAdjustment(string label, GalleryPhotoAdjustment? adjustment) =>
        adjustment is null
            ? $"{label} 未记录"
            : $"{label} {adjustment.Id} · 强度 {adjustment.Strength:0.##}";
}
