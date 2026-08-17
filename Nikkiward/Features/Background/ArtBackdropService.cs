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
    private const double GlobalScrimBaseLayerOpacity = 0.34;
    private const double LocalScrimReferenceOpacityFallback = 0.34;
    private const double LocalScrimMinimumFactor = 0.60;
    private const double LocalScrimMaximumFactor = 1.40;
    private const double MotionScrimFloorFallback = 0.28;
    private const double DynamicLuminanceDelta = 0.08;
    private readonly IArtAnalysisCache _cache;
    private readonly IArtPaletteAnalyzer _analyzer;
    private readonly IArtBlurBaker _baker;
    private readonly IReadOnlyList<IBackgroundSampler> _samplers;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<FrameworkElement> _onArtInkHosts = [];

    private FrameworkElement? _scrimLayer;
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

    /// <summary>
    /// Registers the on-art surface this service drives. <paramref name="scrimLayer"/>
    /// carries <see cref="ScrimOpacity"/> on its own Opacity and, together with
    /// every element in <paramref name="inkHosts"/>, carries
    /// <see cref="PreferredTheme"/> on its RequestedTheme.
    /// </summary>
    /// <remarks>
    /// Call on the UI thread. Only chrome that composites directly onto the
    /// artwork belongs in <paramref name="inkHosts"/>: RequestedTheme selects the
    /// OnArt ink ramp from the artwork's luminance, so an element sitting on an
    /// opaque page surface would take a ramp chosen for pixels it never shows.
    /// Attaching applies the published values only once an artwork has actually
    /// been analysed, so a host registered late is caught up while a host
    /// registered before the first analysis keeps its XAML defaults rather than
    /// being flipped to a placeholder polarity.
    /// </remarks>
    public void AttachOnArtSurface(FrameworkElement scrimLayer, params FrameworkElement[] inkHosts)
    {
        ArgumentNullException.ThrowIfNull(scrimLayer);

        DetachOnArtSurface();
        _scrimLayer = scrimLayer;
        foreach (var host in inkHosts)
        {
            if (host is not null && !ReferenceEquals(host, scrimLayer))
            {
                _onArtInkHosts.Add(host);
            }
        }

        if (IsReady && _currentAnalysis is not null)
        {
            ApplyOnArtSurface(_currentAnalysis);
        }
    }

    /// <summary>Releases the registered on-art surface without resetting it.</summary>
    public void DetachOnArtSurface()
    {
        _scrimLayer = null;
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

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var analysis = await AnalyzeAsync(
                descriptor,
                TimeSpan.Zero,
                allowCache: true,
                cancellationToken);
            if (analysis is null)
            {
                return null;
            }

            await PublishAsync(analysis);
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

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var analysis = await AnalyzeAsync(
                descriptor,
                position,
                allowCache: false,
                cancellationToken);
            if (analysis is null ||
                _currentAnalysis is null ||
                !HasMaterialLuminanceDrift(
                    _currentAnalysis,
                    analysis,
                    DynamicLuminanceDelta))
            {
                return null;
            }

            analysis.ArtHash = _currentAnalysis.ArtHash;
            analysis.BlurredArtPath = _currentAnalysis.BlurredArtPath;
            await PublishAsync(analysis);
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
        CancellationToken cancellationToken)
    {
        var sampler = _samplers.FirstOrDefault(item => item.CanServe(descriptor));
        if (sampler is null)
        {
            return null;
        }

        var hash = await sampler.TryIdentifyAsync(descriptor, cancellationToken);

        if (allowCache && hash is not null)
        {
            var cached = await _cache.LoadAsync(hash, cancellationToken);
            if (cached?.BlurredArtPath is not null &&
                cached.SourceKind == descriptor.Kind)
            {
                return cached;
            }
        }

        var decoded = await sampler.SampleAsync(
            descriptor,
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
                return (Analysis: result, Blurred: _baker.Bake(DownsampleForBake(decoded)));
            },
            cancellationToken);

        if (descriptor.Kind == BackgroundSourceKind.Motion)
        {
            analysis.Analysis.PreferredTheme = ArtPreferredTheme.Dark;
            analysis.Analysis.ScrimOpacity = Math.Max(
                analysis.Analysis.ScrimOpacity,
                ResolveDoubleResource("MotionScrimFloor", MotionScrimFloorFallback));
        }

        if (allowCache && hash is not null)
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

    private Task PublishAsync(ArtAnalysis analysis)
    {
        void Publish()
        {
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

    /// <summary>
    /// Drives the registered scrim element's Opacity and the on-art ink polarity.
    /// </summary>
    /// <remarks>
    /// Opacity is set on the element, never on OnArtScrimBrush: that brush is
    /// shared with the gallery preview chrome and is theme-scoped, so mutating it
    /// would move every other on-art surface and overwrite the HighContrast
    /// contract. RequestedTheme selects the OnArt ink ramp from the artwork's
    /// luminance rather than the app theme, so it is pinned per host and never on
    /// the page, which also owns the deliberately warm-light journal surfaces.
    /// HighContrast is left alone: the system theme outranks the artwork.
    /// </remarks>
    private void ApplyOnArtSurface(ArtAnalysis analysis)
    {
        var requested = ToElementTheme(analysis.PreferredTheme);

        if (_scrimLayer is not null)
        {
            _scrimLayer.Opacity = Math.Clamp(analysis.ScrimOpacity, 0.0, 1.0);
            ApplyInkPolarity(_scrimLayer, requested);
        }

        foreach (var host in _onArtInkHosts)
        {
            ApplyInkPolarity(host, requested);
            ApplyLocalReadability(host, analysis);
        }
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

    private static void ApplyLocalReadability(DependencyObject root, ArtAnalysis analysis)
    {
        if (root is GlassIsland { LocalScrimBrush: not null } island)
        {
            var regionId = island.ReadabilityRegion switch
            {
                GlassIslandReadabilityRegion.Masthead => "masthead",
                GlassIslandReadabilityRegion.Notice => "notice",
                GlassIslandReadabilityRegion.Cta => "cta",
                GlassIslandReadabilityRegion.Pill => "pill",
                _ => "global",
            };
            var region = analysis.Regions.FirstOrDefault(item =>
                string.Equals(item.RegionId, regionId, StringComparison.Ordinal));
            var regionMean = string.IsNullOrEmpty(region.RegionId)
                ? analysis.MeanLuminance
                : region.MeanLuminance;
            var regionP95 = string.IsNullOrEmpty(region.RegionId)
                ? analysis.MeanLuminance
                : region.P95Luminance;
            var preferredTheme = analysis.SourceKind == BackgroundSourceKind.Motion
                ? ArtPreferredTheme.Dark
                : ArtPaletteAnalyzer.PreferredThemeForLuminance(regionMean);
            var reference = ResolveDoubleResource(
                "LocalScrimReferenceAlpha",
                LocalScrimReferenceOpacityFallback);
            var required = ArtPaletteAnalyzer.SolveScrimOpacity(
                regionP95,
                preferredTheme);
            island.LocalScrimOpacity = Math.Clamp(
                required / Math.Max(0.01, reference),
                LocalScrimMinimumFactor,
                LocalScrimMaximumFactor);

            if (island.ReadabilityRegion != GlassIslandReadabilityRegion.Global)
            {
                ApplyInkPolarity(island, ToElementTheme(preferredTheme));
            }
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            ApplyLocalReadability(VisualTreeHelper.GetChild(root, index), analysis);
        }
    }

    private static double[] ResolveActionBackdropLuminances(ArtAnalysis? analysis)
    {
        if (analysis is null)
        {
            return [0.5];
        }

        var scrimOpacity = Math.Clamp(analysis.ScrimOpacity, 0.0, 1.0);
        var baseLayerOpacity = GlobalScrimBaseLayerOpacity * scrimOpacity;
        var edgeOpacity = 1.0 - ((1.0 - baseLayerOpacity) * (1.0 - scrimOpacity));
        var cornerOpacity = 1.0 -
            ((1.0 - baseLayerOpacity) *
             (1.0 - scrimOpacity) *
             (1.0 - scrimOpacity));

        return
        [
            analysis.CtaLuminance,
            analysis.CtaP95Luminance,
            ArtActionFill.CompositeWithScrim(
                analysis.CtaLuminance,
                analysis.PreferredTheme,
                baseLayerOpacity),
            ArtActionFill.CompositeWithScrim(
                analysis.CtaP95Luminance,
                analysis.PreferredTheme,
                baseLayerOpacity),
            ArtActionFill.CompositeWithScrim(
                analysis.CtaLuminance,
                analysis.PreferredTheme,
                edgeOpacity),
            ArtActionFill.CompositeWithScrim(
                analysis.CtaP95Luminance,
                analysis.PreferredTheme,
                edgeOpacity),
            ArtActionFill.CompositeWithScrim(
                analysis.CtaLuminance,
                analysis.PreferredTheme,
                cornerOpacity),
            ArtActionFill.CompositeWithScrim(
                analysis.CtaP95Luminance,
                analysis.PreferredTheme,
                cornerOpacity),
        ];
    }

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
