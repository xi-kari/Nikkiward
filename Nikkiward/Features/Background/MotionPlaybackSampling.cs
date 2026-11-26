namespace Nikkiward.Features.Background;

internal delegate Task<ArtPixelBuffer?> MotionFrameReader(
    int targetWidth, TimeSpan position, CancellationToken cancellationToken);

internal interface IMotionFrameSource
{
    Task<MotionFrameReader?> OpenFrameReaderAsync(
        BackgroundSourceDescriptor descriptor, CancellationToken cancellationToken);
}

internal sealed class MotionPlaybackSampling(CancellationToken lifetime = default)
{
    internal CancellationToken Lifetime { get; } = lifetime;
    private Identity? _identity;

    internal async Task<ArtAnalysis?> AnalyzeAsync(
        BackgroundSourceDescriptor descriptor,
        IBackgroundSampler sampler,
        Func<string?, MotionFrameReader, CancellationToken, Task<ArtAnalysis?>> analyze,
        CancellationToken cancellationToken,
        bool requireIdentity = true)
    {
        using var linked = Lifetime.CanBeCanceled && Lifetime != cancellationToken
            ? CancellationTokenSource.CreateLinkedTokenSource(Lifetime, cancellationToken)
            : null;
        var token = linked?.Token ?? cancellationToken;
        token.ThrowIfCancellationRequested();

        var stamp = FileStamp.TryRead(descriptor.Source);
        if (_identity?.Stamp != stamp || !ReferenceEquals(_identity?.Sampler, sampler))
        {
            _identity = null;
        }
        if (stamp is null)
        {
            return null;
        }

        var hash = _identity?.Hash;
        if (hash is null)
        {
            hash = await sampler.TryIdentifyAsync(descriptor, token);
            token.ThrowIfCancellationRequested();
            if ((requireIdentity && hash is null) || stamp != FileStamp.TryRead(descriptor.Source))
            {
                return null;
            }
        }

        var reader = _identity?.Reader;
        var result = await analyze(hash, ReadFrameAsync, token);
        token.ThrowIfCancellationRequested();
        if (stamp != FileStamp.TryRead(descriptor.Source))
        {
            _identity = null;
            return null;
        }

        if (hash is not null && (_identity is null || _identity.Reader != reader))
        {
            _identity = new(stamp.Value, sampler, hash, reader);
        }
        return result;

        async Task<ArtPixelBuffer?> ReadFrameAsync(int width, TimeSpan position, CancellationToken frameToken)
        {
            if (reader is null && sampler is IMotionFrameSource media)
            {
                reader = await media.OpenFrameReaderAsync(descriptor, frameToken);
                frameToken.ThrowIfCancellationRequested();
                if (reader is null)
                {
                    return null;
                }
            }

            var frame = reader is null
                ? await sampler.SampleAsync(descriptor, width, position, frameToken)
                : await reader(width, position, frameToken);
            frameToken.ThrowIfCancellationRequested();
            return stamp == FileStamp.TryRead(descriptor.Source) ? frame : null;
        }
    }

    private sealed record Identity(FileStamp Stamp, IBackgroundSampler Sampler, string Hash, MotionFrameReader? Reader);

    private readonly record struct FileStamp(string Path, long Length, DateTime LastWriteTimeUtc)
    {
        internal static FileStamp? TryRead(string path)
        {
            try
            {
                var file = new FileInfo(path);
                return file.Exists ? new(file.FullName, file.Length, file.LastWriteTimeUtc) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }
    }
}
