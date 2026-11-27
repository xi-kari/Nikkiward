using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Nikkiward.Controls;
using Windows.UI;

namespace Nikkiward.Features.Background;

/// <summary>
/// Orchestrates the adaptive backdrop: hash, cache lookup, one scaled decode
/// shared by palette analysis and the depth-plate bake, then publication.
/// </summary>
/// <remarks>
/// Everything expensive runs off the UI thread. The first frame must never wait
/// on this service; callers fire it after the shell is up.
/// </remarks>
public sealed class ArtBackdropService : INotifyPropertyChanged
{
    /// <summary>Resource key of the shared accent brush mutated in place.</summary>
    public const string DerivedAccentBrushKey = "DerivedAccentBrush";
    public const string ActionAccentBrushKey = "PrimaryActionSolidBrush";

    private const int PaletteSampleSize = 16;
    private const double MotionScrimFloorFallback = 0.28;
    private const double DynamicLuminanceDelta = 0.08;
    private readonly IArtAnalysisCache _cache;
    private readonly IArtPaletteAnalyzer _analyzer;
    private readonly IArtBlurBaker _baker;
    private readonly IReadOnlyList<IBackgroundSampler> _samplers;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<FrameworkElement> _onArtInkHosts = [];

    private MotionPlaybackSampling? _motionSampling = new();
    private ArtAnalysis? _currentAnalysis;

    private Color _accentColor = Color.FromArgb(0xFF, 0xE8, 0xA0, 0xB4);
    private uint _derivedAccentLight = ArtPaletteAnalyzer.FallbackAccentArgb;
    private uint _derivedAccentDark = ArtPaletteAnalyzer.FallbackAccentArgb;
    private double _dominantHueWeight;
    private double _scrimOpacity = 0.18;
    private ArtPreferredTheme _preferredTheme = ArtPreferredTheme.Light;
    private ArtPreferredTheme _activeTheme = ArtPreferredTheme.Light;
    private string? _blurredArtPath;
    private string? _artHash;
    private bool _isReady;
    private bool _accentFromFallback;

    /// <summary>
    /// Construct on the UI thread unless <paramref name="dispatcherQueue"/> is
    /// supplied: the captured queue is the only marshalling path publication has,
    /// and a null queue makes it mutate brushes and elements inline on the
    /// calling thread.
    /// </summary>
    public ArtBackdropService(
        IArtAnalysisCache? cache = null,
        IArtPaletteAnalyzer? analyzer = null,
        IArtBlurBaker? baker = null,
        IReadOnlyList<IBackgroundSampler>? samplers = null,
        DispatcherQueue? dispatcherQueue = null)
    {
        _cache = cache ?? new ArtAnalysisCache();
        _analyzer = analyzer ?? new ArtPaletteAnalyzer();
        _baker = baker ?? new ArtBlurBaker();
        _samplers = samplers ??
        [
            new MotionSampler(),
            new StillImageSampler(),
            new NoneSampler(),
        ];
        _dispatcherQueue = dispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void BeginMotionSampling(CancellationToken lifetime) => _motionSampling = new(lifetime);

    internal void EndMotionSampling() => _motionSampling = null;

    /// <summary>Accent derived from the current artwork, or the brand blush.</summary>
    public Color AccentColor
    {
        get => _accentColor;
        private set => SetField(ref _accentColor, value);
    }

    public uint DerivedAccentLight
    {
        get => _derivedAccentLight;
        private set => SetField(ref _derivedAccentLight, value);
    }

    public uint DerivedAccentDark
    {
        get => _derivedAccentDark;
        private set => SetField(ref _derivedAccentDark, value);
    }

    public double DominantHueWeight
    {
        get => _dominantHueWeight;
        private set => SetField(ref _dominantHueWeight, value);
    }

    /// <summary>
    /// Adaptive text-protection strength, 0.12..0.52. Bind this with x:Bind to a
    /// scrim layer's Opacity; the theme-scoped ScrimBrush cannot be rewritten at
    /// runtime through the resource indexer.
    /// </summary>
    public double ScrimOpacity
    {
        get => _scrimOpacity;
        private set => SetField(ref _scrimOpacity, value);
    }

    /// <summary>Theme the artwork suggests. Honoured only in "follow artwork" mode.</summary>
    public ArtPreferredTheme PreferredTheme
    {
        get => _preferredTheme;
        private set => SetField(ref _preferredTheme, value);
    }

    /// <summary>Absolute path of the baked L1 depth plate, when available.</summary>
    public string? BlurredArtPath
    {
        get => _blurredArtPath;
        private set => SetField(ref _blurredArtPath, value);
    }

    public string? ArtHash
    {
        get => _artHash;
        private set => SetField(ref _artHash, value);
    }

    /// <summary>True once at least one artwork has been analysed successfully.</summary>
    public bool IsReady
    {
        get => _isReady;
        private set => SetField(ref _isReady, value);
    }

    /// <summary>True when the derived accent failed contrast and the blush was used.</summary>
    public bool AccentFromFallback
    {
        get => _accentFromFallback;
        private set => SetField(ref _accentFromFallback, value);
    }

    public ArtBackdropDiagnosticState DiagnosticState => new()
    {
        IsReady = IsReady,
        AccentFromFallback = AccentFromFallback,
        DominantHueWeight = DominantHueWeight,
        PreferredTheme = PreferredTheme,
    };

    public void ApplyThemeAccent(ArtPreferredTheme theme)
    {
        _activeTheme = theme;
        if (!IsReady)
        {
            return;
        }

        var accent = ToColor(ArtThemeAccentSelector.Select(
            DerivedAccentLight,
            DerivedAccentDark,
            theme));
        AccentColor = accent;
        ApplyAccentToResources(accent, ResolveActionBackdropLuminances(_currentAnalysis));
    }

    public void ApplyAccentOverride(uint accentArgb)
    {
        var accent = ToColor(accentArgb);
        AccentColor = accent;
        ApplyAccentToResources(accent, ResolveActionBackdropLuminances(_currentAnalysis));
    }

    public void AttachOnArtSurface(params FrameworkElement[] inkHosts)
    {
        _onArtInkHosts.Clear();
        _onArtInkHosts.AddRange(inkHosts);
        if (IsReady && _currentAnalysis is not null)
        {
            ApplyOnArtSurface(_currentAnalysis);
        }
    }

    /// <summary>Releases the registered on-art surface without resetting it.</summary>
    public void DetachOnArtSurface()
    {
        _onArtInkHosts.Clear();
    }

    /// <summary>
    /// Analyses <paramref name="source"/> (a local path or an <c>ms-appx:///</c>
    /// URI) and publishes the result. Returns null when the artwork cannot be
    /// read; the previously published values then stay in effect.
    /// </summary>
    public async Task<ArtAnalysis?> ApplyAsync(
        string source,
        CancellationToken cancellationToken = default) =>
        await ApplyAsync(
            BackgroundSourceDescriptor.Still(source),
            cancellationToken);

    public async Task<ArtAnalysis?> ApplyAsync(
        BackgroundSourceDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Kind != BackgroundSourceKind.None &&
            string.IsNullOrWhiteSpace(descriptor.Source))
        {
            return null;
        }

        var playback = descriptor.Kind == BackgroundSourceKind.Motion ? _motionSampling : null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var analysis = await AnalyzeAsync(
                descriptor,
                TimeSpan.Zero,
                allowCache: true,
                playback,
                cancellationToken);
            if (analysis is null)
            {
                return null;
            }

            await PublishAsync(analysis, playback, cancellationToken);
            return analysis;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ArtAnalysis?> ApplyDynamicAsync(
        BackgroundSourceDescriptor descriptor,
        TimeSpan position,
        CancellationToken cancellationToken = default)
    {
        if (descriptor.Kind != BackgroundSourceKind.Motion)
        {
            return null;
        }

        var playback = _motionSampling;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var analysis = await AnalyzeAsync(
                descriptor,
                position,
                allowCache: false,
                playback,
                cancellationToken);
            if (analysis is null || _currentAnalysis is null)
            {
                return null;
            }

            if (analysis.ArtHash == _currentAnalysis.ArtHash)
            {
                if (!HasMaterialLuminanceDrift(_currentAnalysis, analysis, DynamicLuminanceDelta))
                {
                    return null;
                }
                analysis.BlurredArtPath = _currentAnalysis.BlurredArtPath;
            }
            await PublishAsync(analysis, playback, cancellationToken);
            return analysis;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static bool HasMaterialLuminanceDrift(
        ArtAnalysis current,
        ArtAnalysis candidate,
        double threshold)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidate);
        var currentRegions = current.Regions?.ToDictionary(
            region => region.RegionId,
            StringComparer.Ordinal) ?? new Dictionary<string, ArtRegionLuminance>(StringComparer.Ordinal);
        var candidateRegions = candidate.Regions ?? Array.Empty<ArtRegionLuminance>();
        var compared = false;
        foreach (var region in candidateRegions)
        {
            if (!currentRegions.TryGetValue(region.RegionId, out var previous))
            {
                continue;
            }

            compared = true;
            if (Math.Abs(region.P95Luminance - previous.P95Luminance) > threshold)
            {
                return true;
            }
        }

        return !compared &&
            Math.Abs(candidate.MeanLuminance - current.MeanLuminance) > threshold;
    }

    private async Task<ArtAnalysis?> AnalyzeAsync(
        BackgroundSourceDescriptor descriptor,
        TimeSpan position,
        bool allowCache,
        MotionPlaybackSampling? playback,
        CancellationToken cancellationToken)
    {
        var sampler = _samplers.FirstOrDefault(item => item.CanServe(descriptor));
        if (sampler is null)
        {
            return null;
        }

        if (descriptor.Kind == BackgroundSourceKind.Motion)
        {
            return playback is null ? null : await playback.AnalyzeAsync(
                descriptor, sampler, Analyze, cancellationToken, requireIdentity: !allowCache);
        }

        var hash = await sampler.TryIdentifyAsync(descriptor, cancellationToken);
        return await Analyze(hash, (width, at, token) =>
            sampler.SampleAsync(descriptor, width, at, token), cancellationToken);

        Task<ArtAnalysis?> Analyze(string? artHash, MotionFrameReader readFrame, CancellationToken token) =>
            AnalyzeFrameAsync(descriptor, position, allowCache, artHash, readFrame, token);
    }

    private async Task<ArtAnalysis?> AnalyzeFrameAsync(
        BackgroundSourceDescriptor descriptor,
        TimeSpan position,
        bool allowCache,
        string? hash,
        MotionFrameReader readFrame,
        CancellationToken cancellationToken)
    {
        if (allowCache && hash is not null)
        {
            var cached = await _cache.LoadAsync(hash, cancellationToken);
            if (cached?.BlurredArtPath is not null &&
                cached.SourceKind == descriptor.Kind)
            {
                return cached;
            }
        }

        var decoded = await readFrame(
            ArtBlurBaker.BakeWidth,
            position,
            cancellationToken);
        if (decoded is null)
        {
            return null;
        }

        var effectiveHash = hash ?? Guid.NewGuid().ToString("n") + Guid.NewGuid().ToString("n");

        var analysis = await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sample = decoded.Downsample(PaletteSampleSize, PaletteSampleSize);
                var result = _analyzer.Analyze(sample, effectiveHash);
                result.SourceKind = descriptor.Kind;
                cancellationToken.ThrowIfCancellationRequested();
                result.BlurredArtPath = null;
                var blurred = allowCache ? _baker.Bake(DownsampleForBake(decoded)) : null;
                return (Analysis: result, Blurred: blurred);
            },
            cancellationToken);

        if (descriptor.Kind == BackgroundSourceKind.Motion)
        {
            analysis.Analysis.PreferredTheme = ArtPreferredTheme.Dark;
            analysis.Analysis.ScrimOpacity = Math.Max(
                analysis.Analysis.ScrimOpacity,
                ResolveDoubleResource("MotionScrimFloor", MotionScrimFloorFallback));
        }

        if (analysis.Blurred is not null && hash is not null)
        {
            analysis.Analysis.BlurredArtPath = await TryWriteBlurAsync(
                hash,
                analysis.Blurred,
                cancellationToken);
            try
            {
                await _cache.SaveAsync(analysis.Analysis, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A cache write failure must not cost the user their backdrop.
            }
        }

        return analysis.Analysis;
    }

    private async Task<string?> TryWriteBlurAsync(
        string hash,
        ArtPixelBuffer blurred,
        CancellationToken cancellationToken)
    {
        var target = _cache.GetBlurFilePath(hash);
        var temporary = $"{target}.{Guid.NewGuid():n}.tmp";
        try
        {
            Directory.CreateDirectory(_cache.BlurCachePath);
            var bytes = await ArtDecoder.EncodeJpegAsync(blurred);
            if (bytes.Length == 0)
            {
                return null;
            }

            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, target, overwrite: true);
            return target;
        }
        catch (OperationCanceledException)
        {
            TryDeleteTemporary(temporary);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteTemporary(temporary);
            return null;
        }
    }

    private static ArtPixelBuffer DownsampleForBake(ArtPixelBuffer decoded)
    {
        if (decoded.Width <= ArtBlurBaker.BakeWidth)
        {
            return decoded;
        }

        var height = Math.Max(
            1,
            (int)Math.Round(
                decoded.Height * (double)ArtBlurBaker.BakeWidth / decoded.Width));
        return decoded.Downsample(ArtBlurBaker.BakeWidth, height);
    }

    private static void TryDeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private Task PublishAsync(ArtAnalysis analysis, MotionPlaybackSampling? playback, CancellationToken cancellationToken)
    {
        void Publish()
        {
            cancellationToken.ThrowIfCancellationRequested();
            playback?.Lifetime.ThrowIfCancellationRequested();
            DerivedAccentLight = analysis.DerivedAccentLight;
            DerivedAccentDark = analysis.DerivedAccentDark;
            DominantHueWeight = analysis.DominantHueWeight;
            _currentAnalysis = analysis;
            var accent = ToColor(ArtThemeAccentSelector.Select(
                DerivedAccentLight,
                DerivedAccentDark,
                _activeTheme));
            AccentColor = accent;
            if (analysis.SourceKind == BackgroundSourceKind.Motion)
            {
                analysis.ScrimOpacity = Math.Max(
                    analysis.ScrimOpacity,
                    ResolveDoubleResource("MotionScrimFloor", MotionScrimFloorFallback));
                analysis.PreferredTheme = ArtPreferredTheme.Dark;
            }

            ScrimOpacity = analysis.ScrimOpacity;
            PreferredTheme = analysis.PreferredTheme;
            BlurredArtPath = analysis.BlurredArtPath;
            ArtHash = analysis.ArtHash;
            AccentFromFallback = analysis.AccentFromFallback;

            // Accent, scrim and ink polarity are one visual state. Applying them
            // in separate turns would show a frame of new accent over the old
            // scrim, which reads as a flash.
            ApplyBackdropToVisualTree(accent, analysis);
            IsReady = true;
        }

        return ArtPublicationDispatcher.EnqueueAsync(
            callback =>
            {
                if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
                {
                    callback();
                    return true;
                }

                return _dispatcherQueue.TryEnqueue(
                    DispatcherQueuePriority.Normal,
                    () => callback());
            },
            Publish);
    }

    /// <summary>
    /// Applies the whole published backdrop state to the live visual tree as one
    /// unit. UI thread only; the caller marshals.
    /// </summary>
    private void ApplyBackdropToVisualTree(
        Color accent,
        ArtAnalysis analysis)
    {
        ApplyAccentToResources(accent, ResolveActionBackdropLuminances(analysis));
        ApplyOnArtSurface(analysis);
    }

    /// <summary>
    /// Mutates the shared accent brush in place. Replacing the dictionary entry
    /// would not refresh references that XAML already resolved, so the brush
    /// instance itself has to change colour.
    /// </summary>
    private static void ApplyAccentToResources(Color accent, double[] actionBackdropLuminances)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        if (resources.TryGetValue(DerivedAccentBrushKey, out var existing) &&
            existing is SolidColorBrush brush)
        {
            brush.Color = accent;
        }
        else
        {
            resources[DerivedAccentBrushKey] = new SolidColorBrush(accent);
        }

        var actionFill = ToColor(ArtActionFill.ForBackdrops(
            ToArgb(accent),
            actionBackdropLuminances));
        if (resources.TryGetValue(ActionAccentBrushKey, out var actionResource) &&
            actionResource is SolidColorBrush actionBrush)
        {
            actionBrush.Color = actionFill;
        }
        else
        {
            resources[ActionAccentBrushKey] = new SolidColorBrush(actionFill);
        }
    }

    private void ApplyOnArtSurface(ArtAnalysis analysis)
    {
        var requested = ToElementTheme(analysis.PreferredTheme);
        foreach (var host in _onArtInkHosts) ApplyInkPolarity(host, requested);
    }

    private static ElementTheme ToElementTheme(ArtPreferredTheme preferredTheme) =>
        preferredTheme == ArtPreferredTheme.Dark
            ? ElementTheme.Dark
            : ElementTheme.Light;

    private static void ApplyInkPolarity(FrameworkElement host, ElementTheme requested)
    {
        // ElementTheme has no HighContrast member, and it needs none: under a
        // system high contrast theme XAML resolves the HighContrast dictionary
        // whatever RequestedTheme says, so this assignment is inert there.
        if (host.RequestedTheme != requested)
        {
            host.RequestedTheme = requested;
        }
    }

    private static double[] ResolveActionBackdropLuminances(ArtAnalysis? analysis) =>
        analysis is null ? [0.5] : [analysis.CtaLuminance, analysis.CtaP95Luminance];

    private static double ResolveDoubleResource(string resourceKey, double fallback) =>
        Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true &&
        value is double resolved
            ? resolved
            : fallback;

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));

    private static uint ToArgb(Color color) =>
        ((uint)color.A << 24) |
        ((uint)color.R << 16) |
        ((uint)color.G << 8) |
        color.B;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
