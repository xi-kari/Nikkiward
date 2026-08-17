namespace Nikkiward.Features.Background;

internal static class MotionImportFileCopier
{
    private const int AttemptCount = 4;
    private const int BufferBytes = 128 * 1024;

    public static async Task CopyWithRetryAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < AttemptCount; attempt++)
        {
            try
            {
                await using var source = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    BufferBytes,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var sourceLength = source.Length;
                await using var destination = new FileStream(
                    destinationPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferBytes,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                await source.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
                if (source.Length != sourceLength || destination.Length != sourceLength)
                {
                    throw new IOException("The video changed while it was being imported.");
                }

                return;
            }
            catch (IOException) when (attempt < AttemptCount - 1)
            {
                TryDelete(destinationPath);
                await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), cancellationToken);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
