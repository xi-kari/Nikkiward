using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Nikkiward.Features.Shell;
using Nikkiward.Models;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI.ViewManagement;

namespace Nikkiward.Features.Background;

public sealed class MotionPlaybackFailedEventArgs(
    string source,
    int requestVersion,
    string? errorMessage) : EventArgs
{
    public string Source { get; } = source;

    public int RequestVersion { get; } = requestVersion;

    public string? ErrorMessage { get; } = errorMessage;
}

/// <summary>
/// The L0/L1 backdrop stack. Owns parallax and cross fade; all colour and
/// geometry comes from tokens.
/// </summary>
public sealed partial class ArtBackdropView : UserControl
{
    /// <summary>Damping applied to each pointer sample.</summary>
    private const double ParallaxDamping = 0.16;

    private readonly UISettings _uiSettings = new();
    private readonly FrameIntervalMonitor _frameIntervalMonitor;
    private readonly PlaneProjection _stillArtProjection;

    private ArtBackdropService? _service;
    private Window? _hostWindow;
    private AppWindow? _hostAppWindow;
    private UIElement? _pointerRoot;
    private Vector3 _parallaxTarget;
    private Vector3 _parallaxCurrent;
    private bool _parallaxEnabled;
    private bool _animationsEnabled = true;
    private bool _uiSettingsEventsAttached;
    private bool _hostFocused = true;
    private AppearanceMotionMode _motionMode = AppearanceMotionMode.Full;
    private bool _parallaxPreferenceEnabled = true;
    private bool _holographicCardEnabled = true;
    private bool _launcherSurfaceVisible = true;
    private bool _launcherSurfaceActive = true;
    private double _parallaxAmplitude;
    private double _crossFadeMilliseconds;
    private Storyboard? _crossFadeStoryboard;
    private Storyboard? _motionFadeStoryboard;
    private BitmapImage? _incomingPlate;
    private ImageSource? _stillArtSource;
    private MediaPlayer? _mediaPlayer;
    private MediaPlaybackList? _motionPlaylist;
    private MediaSource? _motionMediaSource;
    private BackgroundSourceDescriptor? _motionDescriptor;
    private DispatcherTimer? _motionResampleTimer;
    private CancellationTokenSource? _motionLifetimeCancellation;
    private CancellationTokenSource? _motionPauseCancellation;
    private TaskCompletionSource<bool>? _motionOpenCompletion;
    private AppearanceSettings _appearanceSettings = new();
    private bool _motionRequested;
    private bool _motionReady;
    private bool _motionResampleInFlight;
    private bool _glassTierSubscribed;
    private bool _isLoaded;
    private bool _hostMinimized;
    private int _motionFadeVersion;
    private int _motionRequestVersion;
    private double _stillSourcePixelWidth;
    private double _stillSourcePixelHeight;
    private bool _pointerOverStillCard;
    private double _stillCardTiltTargetX;
    private double _stillCardTiltTargetY;
    private double _stillCardTiltCurrentX;
    private double _stillCardTiltCurrentY;

    public ArtBackdropView()
    {
        InitializeComponent();
        _stillArtProjection = new PlaneProjection
        {
            CenterOfRotationX = 0.5d,
            CenterOfRotationY = 0.5d,
        };
        ArtSharp.Projection = _stillArtProjection;
        _stillArtSource = ArtSharp.Source;
        _frameIntervalMonitor = new FrameIntervalMonitor(
            GlassCapabilities.Current.ReportLowFrameRate);
        _animationsEnabled = ReadAnimationsEnabled();
        CaptureStillSourceDimensions(ArtSharp.Source);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnBackdropSizeChanged;
    }

    /// <summary>
    /// Attaches the analysis service and, optionally, the host window whose
    /// activation state gates parallax. Idle CPU has to stay near zero when the
    /// window is not focused, so parallax stops rather than merely slowing.
    /// </summary>
    public void Attach(ArtBackdropService service, Window? hostWindow = null)
    {
        ArgumentNullException.ThrowIfNull(service);

        Detach();
        _service = service;
        _service.PropertyChanged += OnServicePropertyChanged;
        _hostWindow = hostWindow;
        if (_hostWindow is not null)
        {
            _hostWindow.Activated += OnHostActivated;
            _hostAppWindow = _hostWindow.AppWindow;
            _hostAppWindow.Changed += OnHostAppWindowChanged;
        }

        ApplyBlurPlate(service.BlurredArtPath);
    }

    public void Detach()
    {
        StopCrossFade(promoteIncoming: true);
        StopMotion(clearSource: true);

        if (_service is not null)
        {
            _service.PropertyChanged -= OnServicePropertyChanged;
            _service = null;
        }

        if (_hostWindow is not null)
        {
            var hostWindow = _hostWindow;
            _hostWindow = null;
            hostWindow.Activated -= OnHostActivated;
        }

        if (_hostAppWindow is not null)
        {
            var hostAppWindow = _hostAppWindow;
            _hostAppWindow = null;
            hostAppWindow.Changed -= OnHostAppWindowChanged;
        }

    }

    public ImageSource? Source
    {
        get => _stillArtSource;
        set
        {
            _stillArtSource = value;
            _stillSourcePixelWidth = 0d;
            _stillSourcePixelHeight = 0d;
            CaptureStillSourceDimensions(value);
            StopMotion(clearSource: true);
            StopCrossFade(promoteIncoming: false);
            ArtSharp.Source = value;
            HolographicOverlay.Visibility = value is null || !_holographicCardEnabled
                ? Visibility.Collapsed
                : Visibility.Visible;
            ArtBlurredSettled.Source = CreateBuiltInBlurPlate();
            ArtBlurredIncoming.Source = null;
            ArtBlurredIncoming.Opacity = 0.0;
            UpdateStillArtworkLayout();
        }
    }

    public void ConfigureAppearance(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _appearanceSettings = settings;
        _motionMode = settings.Motion;
        _parallaxPreferenceEnabled = settings.Background.ParallaxEnabled;
        _holographicCardEnabled = settings.Background.HolographicCardEnabled;
        GlassCapabilities.Current.Configure(settings);
        HolographicOverlay.SetMaterialEnabled(_holographicCardEnabled);
        HolographicOverlay.Visibility =
            _holographicCardEnabled && _stillArtSource is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
        HolographicOverlay.ApplyMotion(_motionMode);
        if (!_holographicCardEnabled)
        {
            ApplyStillCardTilt(0d, 0d, false);
        }
        RefreshFrameIntervalMonitoring();
        ApplyMotionTransform();
        if (!settings.Background.MotionEnabled)
        {
            StopMotion(clearSource: true);
        }
        RefreshMotionProjection();
        if (_crossFadeMilliseconds <= 0)
        {
            StopCrossFade(promoteIncoming: true);
        }
    }

    public void SetLauncherSurfaceActive(bool active) =>
        SetLauncherSurfaceState(active, active);

    public void SetLauncherSurfaceState(bool visible, bool interactionEnabled)
    {
        _launcherSurfaceVisible = visible;
        _launcherSurfaceActive = visible && interactionEnabled;
        HolographicOverlay.SetInteractionEnabled(_launcherSurfaceActive);
        if (!_launcherSurfaceActive)
        {
            ResetParallax();
        }

        ArtSharpHost.Visibility = visible && ArtSharp.Source is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        HolographicOverlay.Visibility =
            visible && _holographicCardEnabled && _stillArtSource is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
        RefreshMotionProjection();
    }

    public bool IsMotionActive => _motionRequested && _motionReady;

    public string? MotionSource => _motionDescriptor?.Source;

    public event EventHandler<MotionPlaybackFailedEventArgs>? MotionPlaybackFailed;

    public async Task<bool> ShowMotionAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            return false;
        }

        if (_motionReady &&
            string.Equals(
                _motionDescriptor?.Source,
                source,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var requestVersion = Interlocked.Increment(ref _motionRequestVersion);
        StopMotion(
            clearSource: true,
            preserveFallbackPlate: true,
            invalidateRequest: false);
        MediaPlayer? pendingPlayer = null;
        MediaPlaybackList? pendingPlaylist = null;
        MediaSource? pendingMediaSource = null;
        CancellationTokenSource? pendingLifetimeCancellation = null;
        var committed = false;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(source));
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentMotionRequest(requestVersion))
            {
                return false;
            }

            pendingMediaSource = MediaSource.CreateFromStorageFile(file);
            pendingPlaylist = new MediaPlaybackList
            {
                AutoRepeatEnabled = true,
            };
            var playbackItem = new MediaPlaybackItem(pendingMediaSource);
            playbackItem.AudioTracksChanged += (_, _) => DisableAudioTracks(playbackItem);
            DisableAudioTracks(playbackItem);
            pendingPlaylist.Items.Add(playbackItem);
            pendingPlayer = new MediaPlayer
            {
                AutoPlay = false,
                IsLoopingEnabled = false,
                IsMuted = true,
            };
            pendingPlayer.CommandManager.IsEnabled = false;
            var openCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            pendingLifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentMotionRequest(requestVersion))
            {
                return false;
            }

            _motionDescriptor = BackgroundSourceDescriptor.Motion(source, Path.GetFileName(source));
            _motionRequested = true;
            _motionLifetimeCancellation = pendingLifetimeCancellation;
            _motionOpenCompletion = openCompletion;
            _mediaPlayer = pendingPlayer;
            _motionPlaylist = pendingPlaylist;
            _motionMediaSource = pendingMediaSource;
            committed = true;

            _mediaPlayer.MediaOpened += OnMotionMediaOpened;
            _mediaPlayer.MediaFailed += OnMotionMediaFailed;
            PrepareMotionStaticFallback();
            MotionHost.SetMediaPlayer(_mediaPlayer);
            MotionHost.Visibility = Visibility.Visible;
            ApplyMotionTransform();
            GlassCapabilities.Current.SetMotionActive(true);
            _mediaPlayer.Source = _motionPlaylist;
            _mediaPlayer.Play();

            using var registration = cancellationToken.Register(() =>
                openCompletion.TrySetCanceled(cancellationToken));
            var completed = await Task.WhenAny(
                openCompletion.Task,
                Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
            if (!ReferenceEquals(completed, openCompletion.Task))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsCurrentMotionRequest(requestVersion))
                {
                    StopMotion(clearSource: true);
                }

                return false;
            }

            return await openCompletion.Task;
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentMotionRequest(requestVersion))
            {
                StopMotion(clearSource: true);
            }

            throw;
        }
        catch (Exception ex) when (ex is
            FileNotFoundException or
            UnauthorizedAccessException or
            IOException or
            ArgumentException or
            NotSupportedException or
            InvalidOperationException or
            COMException)
        {
            if (IsCurrentMotionRequest(requestVersion))
            {
                StopMotion(clearSource: true);
            }

            return false;
        }
        finally
        {
            if (!committed)
            {
                pendingLifetimeCancellation?.Dispose();
                DisposePendingMotion(
                    pendingPlayer,
                    pendingPlaylist,
                    pendingMediaSource);
            }
        }
    }

    public void ShowStill() => StopMotion(clearSource: true);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        if (!_glassTierSubscribed)
        {
            GlassCapabilities.Current.TierChanged += OnGlassTierChanged;
            _glassTierSubscribed = true;
        }

        RefreshMotionProjection();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) &&
            !_uiSettingsEventsAttached)
        {
            try
            {
                _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
                _uiSettingsEventsAttached = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
            }
        }
        if (XamlRoot?.Content is UIElement root)
        {
            _pointerRoot = root;
            root.PointerMoved += OnRootPointerMoved;
            root.PointerExited += OnRootPointerEnded;
            root.PointerCanceled += OnRootPointerEnded;
            root.PointerCaptureLost += OnRootPointerEnded;
        }

        CaptureStillSourceDimensions(ArtSharp.Source);
        UpdateStillArtworkLayout();
        HolographicOverlay.ApplyMotion(_motionMode);
        RefreshFrameIntervalMonitoring();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _frameIntervalMonitor.SetEnabled(false);
        if (_pointerRoot is not null)
        {
            var pointerRoot = _pointerRoot;
            _pointerRoot = null;
            pointerRoot.PointerMoved -= OnRootPointerMoved;
            pointerRoot.PointerExited -= OnRootPointerEnded;
            pointerRoot.PointerCanceled -= OnRootPointerEnded;
            pointerRoot.PointerCaptureLost -= OnRootPointerEnded;
        }

        _stillCardTiltCurrentX = 0d;
        _stillCardTiltCurrentY = 0d;
        ApplyStillCardProjection();

        if (_uiSettingsEventsAttached &&
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
            _uiSettingsEventsAttached = false;
        }

        if (_glassTierSubscribed)
        {
            GlassCapabilities.Current.TierChanged -= OnGlassTierChanged;
            _glassTierSubscribed = false;
        }

        Detach();
    }

    private void OnHostActivated(object sender, WindowActivatedEventArgs args)
    {
        _hostFocused = args.WindowActivationState != WindowActivationState.Deactivated;
        GlassCapabilities.Current.RefreshPlatformState();
        if (_hostFocused)
        {
            _motionPauseCancellation?.Cancel();
            GlassCapabilities.Current.SetWindowOccluded(false);
            if (CanResumeMotion())
            {
                ResumeMotionIfAllowed();
            }
            else
            {
                FreezeMotion();
            }
        }
        else
        {
            ScheduleMotionPause();
        }

        RefreshMotionProjection();
        RefreshFrameIntervalMonitoring();
    }

    private void OnHostAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        var minimized = sender.Presenter is OverlappedPresenter
        {
            State: OverlappedPresenterState.Minimized,
        };
        var unavailable = minimized || !sender.IsVisible;
        _hostMinimized = unavailable;
        GlassCapabilities.Current.RefreshPlatformState();
        GlassCapabilities.Current.SetWindowOccluded(unavailable);
        if (unavailable)
        {
            FreezeMotion();
        }
        else if (_hostFocused)
        {
            ResumeMotionIfAllowed();
        }
        else if (_motionRequested && _motionReady)
        {
            FreezeMotion(showStaticFallback: false);
        }

        RefreshFrameIntervalMonitoring();
    }

    private void OnAnimationsEnabledChanged(UISettings sender, object args)
    {
        GlassCapabilities.Current.RefreshPlatformState();
        RefreshMotionProjection();
        if (!_animationsEnabled)
        {
            StopCrossFade(promoteIncoming: true);
            ResetParallax();
            FreezeMotion(showStaticFallback: _hostFocused || _hostMinimized);
        }
        else
        {
            ResumeMotionIfAllowed();
        }
        RefreshFrameIntervalMonitoring();
    }

    private void OnServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_service is null)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(ArtBackdropService.BlurredArtPath):
                ApplyBlurPlate(_service.BlurredArtPath);
                break;
        }
    }

    private void ApplyBlurPlate(string? path)
    {
        StopCrossFade(promoteIncoming: true);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ArtBlurredSettled.Source = CreateBuiltInBlurPlate();
            ArtBlurredIncoming.Source = null;
            ArtBlurredIncoming.Opacity = 0.0;
            return;
        }

        var image = new BitmapImage(new Uri(path));
        if (_crossFadeMilliseconds <= 0 || ArtBlurredSettled.Source is null)
        {
            // First plate, or reduced motion: no fade to cross.
            ArtBlurredSettled.Source = image;
            ArtBlurredIncoming.Source = null;
            ArtBlurredIncoming.Opacity = 0.0;
            return;
        }

        // Cross fade rather than swap: the plate covers the whole window, so a
        // hard cut reads as a flash. Wait for decode, otherwise the fade starts
        // against an empty layer and the settled plate shows through.
        image.ImageOpened += OnIncomingPlateOpened;
        image.ImageFailed += OnIncomingPlateFailed;
        _incomingPlate = image;
        ArtBlurredIncoming.Opacity = 0.0;
        ArtBlurredIncoming.Source = image;
    }

    private void OnIncomingPlateOpened(object? sender, RoutedEventArgs e)
    {
        DetachPlateHandlers(sender);
        _incomingPlate = null;
        StartCrossFade();
    }

    private void OnIncomingPlateFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        DetachPlateHandlers(sender);
        _incomingPlate = null;
        ArtBlurredIncoming.Source = null;
        ArtBlurredIncoming.Opacity = 0.0;
    }

    private void DetachPlateHandlers(object? sender)
    {
        if (sender is BitmapImage image)
        {
            image.ImageOpened -= OnIncomingPlateOpened;
            image.ImageFailed -= OnIncomingPlateFailed;
        }
    }

    private void StartCrossFade()
    {
        var fade = new DoubleAnimation
        {
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(_crossFadeMilliseconds)),
            EnableDependentAnimation = false,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        Storyboard.SetTarget(fade, ArtBlurredIncoming);
        Storyboard.SetTargetProperty(fade, "Opacity");

        _crossFadeStoryboard = new Storyboard();
        _crossFadeStoryboard.Children.Add(fade);
        _crossFadeStoryboard.Completed += OnCrossFadeCompleted;
        _crossFadeStoryboard.Begin();
    }

    private void OnCrossFadeCompleted(object? sender, object e)
    {
        if (sender is Storyboard storyboard && ReferenceEquals(storyboard, _crossFadeStoryboard))
        {
            storyboard.Completed -= OnCrossFadeCompleted;
            _crossFadeStoryboard = null;
        }

        PromoteIncomingPlate();
    }

    private void StopCrossFade(bool promoteIncoming)
    {
        if (_incomingPlate is not null)
        {
            DetachPlateHandlers(_incomingPlate);
            _incomingPlate = null;
        }

        if (_crossFadeStoryboard is not null)
        {
            _crossFadeStoryboard.Completed -= OnCrossFadeCompleted;
            _crossFadeStoryboard.Stop();
            _crossFadeStoryboard = null;
        }

        if (promoteIncoming)
        {
            PromoteIncomingPlate();
            return;
        }

        ArtBlurredIncoming.Source = null;
        ArtBlurredIncoming.Opacity = 0.0;
    }

    private void PromoteIncomingPlate()
    {
        if (ArtBlurredIncoming.Source is not null)
        {
            ArtBlurredSettled.Source = ArtBlurredIncoming.Source;
        }

        ArtBlurredIncoming.Source = null;
        ArtBlurredIncoming.Opacity = 0.0;
    }

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        UpdateHolographicPointer(e);

        var position = e.GetCurrentPoint(this).Position;
        var normalizedX = ((position.X / ActualWidth) * 2.0) - 1.0;
        var normalizedY = ((position.Y / ActualHeight) * 2.0) - 1.0;

        if (!_parallaxEnabled)
        {
            return;
        }

        _parallaxTarget = new Vector3(
            (float)(-normalizedX * _parallaxAmplitude),
            (float)(-normalizedY * _parallaxAmplitude),
            0f);

        _parallaxCurrent = Vector3.Lerp(
            _parallaxCurrent,
            _parallaxTarget,
            (float)ParallaxDamping);

        // Translation only. Animating Margin or Width would leave the compositor
        // thread and re-run layout on every pointer sample.
        ArtSharpHost.Translation = _parallaxCurrent;
        if (_motionReady && _appearanceSettings.Background.MotionPanEnabled)
        {
            MotionHost.Translation = _parallaxCurrent;
        }
    }

    private void OnRootPointerEnded(object sender, PointerRoutedEventArgs e) =>
        ResetParallax();

    private void ResetParallax()
    {
        _parallaxTarget = Vector3.Zero;
        _parallaxCurrent = Vector3.Zero;
        ArtSharpHost.Translation = Vector3.Zero;
        MotionHost.Translation = Vector3.Zero;
        HolographicOverlay.ResetPointer();
        ApplyStillCardTilt(0d, 0d, false);
    }

    private void UpdateHolographicPointer(PointerRoutedEventArgs args)
    {
        if (!_launcherSurfaceActive ||
            !_holographicCardEnabled ||
            _motionReady ||
            ArtSharpHost.ActualWidth <= 0d ||
            ArtSharpHost.ActualHeight <= 0d)
        {
            ApplyStillCardTilt(0d, 0d, false);
            return;
        }

        var point = args.GetCurrentPoint(ArtSharpHost).Position;
        var isInside =
            point.X >= 0d &&
            point.Y >= 0d &&
            point.X <= ArtSharpHost.ActualWidth &&
            point.Y <= ArtSharpHost.ActualHeight;
        if (!isInside)
        {
            if (_pointerOverStillCard)
            {
                HolographicOverlay.ResetPointer();
            }

            ApplyStillCardTilt(0d, 0d, false);
            return;
        }

        var normalizedX = ((point.X / ArtSharpHost.ActualWidth) * 2d) - 1d;
        var normalizedY = ((point.Y / ArtSharpHost.ActualHeight) * 2d) - 1d;
        HolographicOverlay.SetPointer(normalizedX, normalizedY);
        ApplyStillCardTilt(normalizedX, normalizedY, true);
    }

    private void ApplyStillCardTilt(
        double normalizedX,
        double normalizedY,
        bool pointerOverCard)
    {
        _pointerOverStillCard = pointerOverCard;
        var tiltEnabled =
            _launcherSurfaceActive &&
            _holographicCardEnabled &&
            pointerOverCard &&
            !_motionReady &&
            _motionMode == AppearanceMotionMode.Full &&
            _animationsEnabled &&
            _hostFocused;
        _stillCardTiltTargetX = tiltEnabled
            ? Math.Clamp(normalizedX, -1d, 1d)
            : 0d;
        _stillCardTiltTargetY = tiltEnabled
            ? Math.Clamp(normalizedY, -1d, 1d)
            : 0d;
        _stillCardTiltCurrentX = _stillCardTiltTargetX;
        _stillCardTiltCurrentY = _stillCardTiltTargetY;
        ApplyStillCardProjection();
    }

    private void ApplyStillCardProjection()
    {
        _stillArtProjection.RotationY = _stillCardTiltCurrentX * 2.2d;
        _stillArtProjection.RotationX = -_stillCardTiltCurrentY * 1.7d;
    }

    private bool ReadAnimationsEnabled()
    {
        try
        {
            return _uiSettings.AnimationsEnabled;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return true;
        }
    }

    private void RefreshMotionProjection()
    {
        _animationsEnabled = ReadAnimationsEnabled();
        var projection = AppearanceProjector.ProjectMotion(
            _motionMode,
            _animationsEnabled);
        _parallaxAmplitude = _parallaxPreferenceEnabled
            ? projection.ParallaxAmplitude
            : 0;
        _crossFadeMilliseconds = projection.ArtDurationMilliseconds;
        _parallaxEnabled =
            _launcherSurfaceActive &&
            _hostFocused &&
            _parallaxAmplitude > 0;
        GlassCapabilities.Current.Configure(_appearanceSettings);
        if (!_parallaxEnabled)
        {
            ResetParallax();
        }
    }

    private void OnMotionMediaOpened(MediaPlayer sender, object args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(sender, _mediaPlayer))
            {
                return;
            }

            _motionReady = sender.PlaybackSession.NaturalVideoWidth > 0 &&
                sender.PlaybackSession.NaturalVideoHeight > 0;
            if (!_motionReady)
            {
                _motionOpenCompletion?.TrySetResult(false);
                StopMotion(clearSource: true);
                return;
            }

            ApplyMotionTransform();
            _motionOpenCompletion?.TrySetResult(true);
            if (CanResumeMotion())
            {
                ResumeMotionIfAllowed();
            }
            else
            {
                FreezeMotion(showStaticFallback: _hostFocused || _hostMinimized);
            }
        });
    }

    private void OnMotionMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(sender, _mediaPlayer))
            {
                return;
            }

            var notifyRuntimeFailure = _motionReady;
            var failedSource = _motionDescriptor?.Source;
            var failedRequestVersion = _motionRequestVersion;
            _motionOpenCompletion?.TrySetResult(false);
            StopMotion(clearSource: true);
            if (notifyRuntimeFailure && !string.IsNullOrWhiteSpace(failedSource))
            {
                MotionPlaybackFailed?.Invoke(
                    this,
                    new MotionPlaybackFailedEventArgs(
                        failedSource,
                        failedRequestVersion,
                        args.ErrorMessage));
            }
        });
    }

    private void StartMotionResampling()
    {
        _motionResampleTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _motionResampleTimer.Tick -= OnMotionResampleTick;
        _motionResampleTimer.Tick += OnMotionResampleTick;
        _motionResampleTimer.Start();
    }

    private async void OnMotionResampleTick(object? sender, object args)
    {
        var requestVersion = Volatile.Read(ref _motionRequestVersion);
        var descriptor = _motionDescriptor;
        var player = _mediaPlayer;
        var service = _service;
        if (_motionResampleInFlight ||
            !_motionReady ||
            descriptor is null ||
            player is null ||
            service is null)
        {
            return;
        }

        _motionResampleInFlight = true;
        try
        {
            if (!IsCurrentMotionSamplingRequest(requestVersion, descriptor, player) ||
                player.PlaybackSession.PlaybackState != MediaPlaybackState.Playing)
            {
                return;
            }

            await service.ApplyDynamicAsync(
                descriptor,
                player.PlaybackSession.Position,
                _motionLifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is
            FileNotFoundException or
            UnauthorizedAccessException or
            IOException or
            ArgumentException or
            NotSupportedException or
            InvalidOperationException or
            COMException)
        {
            if (IsCurrentMotionSamplingRequest(requestVersion, descriptor, player))
            {
                _motionResampleTimer?.Stop();
            }
        }
        finally
        {
            if (IsCurrentMotionSamplingRequest(requestVersion, descriptor, player))
            {
                _motionResampleInFlight = false;
            }
        }
    }

    private void OnGlassTierChanged(object? sender, EventArgs args)
    {
        RefreshFrameIntervalMonitoring();
        if (!_motionRequested || !_motionReady)
        {
            return;
        }

        if (CanResumeMotion())
        {
            ResumeMotionIfAllowed();
        }
        else
        {
            FreezeMotion(showStaticFallback: _hostFocused || _hostMinimized);
        }
    }

    private static bool IsMotionTier(GlassTier tier) =>
        tier == GlassTier.MotionScrim;

    private static void DisableAudioTracks(MediaPlaybackItem playbackItem)
    {
        if (playbackItem.AudioTracks.Count > 0)
        {
            playbackItem.AudioTracks.SelectedIndex = -1;
        }
    }

    private bool CanResumeMotion() =>
        _hostFocused &&
        !_hostMinimized &&
        IsMotionTier(GlassCapabilities.Current.Tier);

    private void RefreshFrameIntervalMonitoring()
    {
        _frameIntervalMonitor.SetEnabled(
            _isLoaded &&
            _hostFocused &&
            !_hostMinimized &&
            GlassCapabilities.Current.AllowsLiveBlur &&
            GlassCapabilities.Current.GlassIntensity > 0);
    }

    private void ResumeMotionIfAllowed()
    {
        if (!_motionRequested ||
            !_motionReady ||
            _mediaPlayer is null ||
            !CanResumeMotion())
        {
            return;
        }

        _motionPauseCancellation?.Cancel();
        StartMotionResampling();
        PrepareMotionStaticFallback();
        MotionHost.Visibility = Visibility.Visible;
        _mediaPlayer.Play();
        FadeStaticBackdrop(show: false);
    }

    private void FreezeMotion(bool showStaticFallback = true)
    {
        if (!_motionRequested || _mediaPlayer is null)
        {
            return;
        }

        _motionResampleTimer?.Stop();
        if (showStaticFallback)
        {
            FadeStaticBackdrop(show: true);
        }
        else
        {
            KeepMotionFrameVisible();
        }
        _motionPauseCancellation?.Cancel();
        _motionPauseCancellation?.Dispose();
        _motionPauseCancellation = new CancellationTokenSource();
        _ = PauseAfterFreezeAsync(_motionPauseCancellation.Token);
    }

    private async Task PauseAfterFreezeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var duration = AppearanceProjector.ProjectMotion(
                _motionMode,
                _animationsEnabled).StateDurationMilliseconds;
            if (duration > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(duration), cancellationToken);
            }

            if (!cancellationToken.IsCancellationRequested && !CanResumeMotion())
            {
                _mediaPlayer?.Pause();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ScheduleMotionPause()
    {
        _motionPauseCancellation?.Cancel();
        _motionPauseCancellation?.Dispose();
        _motionPauseCancellation = new CancellationTokenSource();
        _ = PauseAfterDeactivationAsync(_motionPauseCancellation.Token);
    }

    private async Task PauseAfterDeactivationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            if (!_hostFocused && !cancellationToken.IsCancellationRequested)
            {
                FreezeMotion(showStaticFallback: _hostMinimized);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void KeepMotionFrameVisible()
    {
        ++_motionFadeVersion;
        _motionFadeStoryboard?.Stop();
        _motionFadeStoryboard = null;
        StaticBackdrop.Opacity = 0;
        StaticBackdrop.Visibility = Visibility.Collapsed;
        MotionHost.Visibility = Visibility.Visible;
    }

    private void FadeStaticBackdrop(bool show)
    {
        var version = ++_motionFadeVersion;
        if (show)
        {
            StaticBackdrop.Visibility = Visibility.Visible;
        }

        _motionFadeStoryboard?.Stop();
        _motionFadeStoryboard = null;
        var projection = AppearanceProjector.ProjectMotion(_motionMode, _animationsEnabled);
        var milliseconds = show
            ? projection.StateDurationMilliseconds
            : projection.ArtDurationMilliseconds;
        var target = show ? 1d : 0d;
        if (milliseconds <= 0 || Math.Abs(StaticBackdrop.Opacity - target) < 0.001)
        {
            StaticBackdrop.Opacity = target;
            StaticBackdrop.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = StaticBackdrop.Opacity,
            To = target,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = false,
        };
        Storyboard.SetTarget(animation, StaticBackdrop);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            if (version != _motionFadeVersion)
            {
                return;
            }

            _motionFadeStoryboard = null;
            StaticBackdrop.Opacity = target;
            StaticBackdrop.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        };
        _motionFadeStoryboard = storyboard;
        storyboard.Begin();
    }

    private void StopMotion(
        bool clearSource,
        bool preserveFallbackPlate = false,
        bool invalidateRequest = true)
    {
        if (invalidateRequest)
        {
            Interlocked.Increment(ref _motionRequestVersion);
        }

        var hadMotion = _motionRequested || _motionReady || _mediaPlayer is not null;
        ++_motionFadeVersion;
        _motionFadeStoryboard?.Stop();
        _motionFadeStoryboard = null;
        _motionResampleTimer?.Stop();
        _motionPauseCancellation?.Cancel();
        _motionPauseCancellation?.Dispose();
        _motionPauseCancellation = null;
        _motionLifetimeCancellation?.Cancel();
        _motionLifetimeCancellation?.Dispose();
        _motionLifetimeCancellation = null;
        _motionOpenCompletion?.TrySetResult(false);
        _motionOpenCompletion = null;

        if (_mediaPlayer is not null)
        {
            _mediaPlayer.MediaOpened -= OnMotionMediaOpened;
            _mediaPlayer.MediaFailed -= OnMotionMediaFailed;
            _mediaPlayer.Pause();
            _mediaPlayer.Source = null;
            MotionHost.SetMediaPlayer(null);
            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }

        _motionPlaylist?.Items.Clear();
        _motionPlaylist = null;
        _motionMediaSource?.Dispose();
        _motionMediaSource = null;

        MotionHost.Visibility = Visibility.Collapsed;
        MotionHost.Translation = Vector3.Zero;
        MotionHost.Scale = Vector3.One;
        StaticBackdrop.Visibility = Visibility.Visible;
        StaticBackdrop.Opacity = 1;
        _motionRequested = false;
        _motionReady = false;
        _motionResampleInFlight = false;
        if (clearSource)
        {
            _motionDescriptor = null;
        }

        GlassCapabilities.Current.SetMotionActive(false);
        RestoreStillVisualLayer(hadMotion && !preserveFallbackPlate);
    }

    private bool IsCurrentMotionRequest(int requestVersion) =>
        requestVersion == Volatile.Read(ref _motionRequestVersion);

    private bool IsCurrentMotionSamplingRequest(
        int requestVersion,
        BackgroundSourceDescriptor descriptor,
        MediaPlayer player) =>
        IsCurrentMotionRequest(requestVersion) &&
        ReferenceEquals(player, _mediaPlayer) &&
        string.Equals(
            descriptor.Source,
            _motionDescriptor?.Source,
            StringComparison.OrdinalIgnoreCase);

    private void DisposePendingMotion(
        MediaPlayer? player,
        MediaPlaybackList? playlist,
        MediaSource? mediaSource)
    {
        if (player is not null)
        {
            player.MediaOpened -= OnMotionMediaOpened;
            player.MediaFailed -= OnMotionMediaFailed;
            player.Source = null;
            player.Dispose();
        }

        playlist?.Items.Clear();
        mediaSource?.Dispose();
    }

    private void PrepareMotionStaticFallback()
    {
        ApplyStillCardTilt(0d, 0d, false);
        HolographicOverlay.ResetPointer();
        ArtSharpHost.Visibility = Visibility.Collapsed;
        ArtSharp.Source = null;
    }

    private void RestoreStillVisualLayer(bool resetBlurPlate)
    {
        ArtSharp.Source = _stillArtSource;
        ArtSharpHost.Visibility = _launcherSurfaceVisible && _stillArtSource is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        HolographicOverlay.Visibility =
            _launcherSurfaceVisible &&
            _holographicCardEnabled &&
            _stillArtSource is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
        if (!resetBlurPlate || _stillArtSource is null)
        {
            return;
        }

        ApplyBlurPlate(_service?.BlurredArtPath);
    }

    private static BitmapImage CreateBuiltInBlurPlate() =>
        new(new Uri(AppearanceProjector.BuiltInBlurredBackgroundSource));

    private void ApplyMotionTransform()
    {
        var overscan = ReadDoubleResource("MediaOverscanScale", 1.002);
        var zoom = (float)Math.Clamp(
            _appearanceSettings.Background.MotionZoom * overscan,
            overscan,
            2.8 * overscan);
        MotionHost.Scale = new Vector3(zoom, zoom, 1);
        MotionHost.CenterPoint = new Vector3(
            (float)(ActualWidth / 2),
            (float)(ActualHeight / 2),
            0);
        if (!_appearanceSettings.Background.MotionPanEnabled)
        {
            MotionHost.Translation = Vector3.Zero;
        }
    }

    private void OnBackdropSizeChanged(object sender, SizeChangedEventArgs args)
    {
        ApplyMotionTransform();
        UpdateStillArtworkLayout();
    }

    private void OnArtSharpImageOpened(object sender, RoutedEventArgs args)
    {
        CaptureStillSourceDimensions(ArtSharp.Source);
        UpdateStillArtworkLayout();
    }

    private void OnArtSharpImageFailed(object sender, ExceptionRoutedEventArgs args)
    {
        _stillSourcePixelWidth = 0d;
        _stillSourcePixelHeight = 0d;
        UpdateStillArtworkLayout();
    }

    private void CaptureStillSourceDimensions(ImageSource? source)
    {
        if (source is not BitmapSource bitmap ||
            bitmap.PixelWidth <= 0 ||
            bitmap.PixelHeight <= 0)
        {
            return;
        }

        _stillSourcePixelWidth = bitmap.PixelWidth;
        _stillSourcePixelHeight = bitmap.PixelHeight;
    }

    private void UpdateStillArtworkLayout()
    {
        var margin = ArtSharpHost.Margin;
        var viewportWidth = Math.Max(0d, ActualWidth - margin.Left - margin.Right);
        var viewportHeight = Math.Max(0d, ActualHeight - margin.Top - margin.Bottom);
        var layout = HolographicBackdropProjection.ProjectLayout(
            _stillSourcePixelWidth,
            _stillSourcePixelHeight,
            viewportWidth,
            viewportHeight);
        if (layout.UsesBoundedSurface)
        {
            ArtSharpHost.HorizontalAlignment = HorizontalAlignment.Center;
            ArtSharpHost.VerticalAlignment = VerticalAlignment.Center;
            ArtSharpHost.Width = layout.Width;
            ArtSharpHost.Height = layout.Height;
            HolographicOverlay.Width = double.NaN;
            HolographicOverlay.Height = double.NaN;
            return;
        }

        ArtSharpHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        ArtSharpHost.VerticalAlignment = VerticalAlignment.Stretch;
        ArtSharpHost.Width = double.NaN;
        ArtSharpHost.Height = double.NaN;
        ArtSharp.Stretch = Stretch.Uniform;
        HolographicOverlay.Width = layout.IsValid ? layout.Width : double.NaN;
        HolographicOverlay.Height = layout.IsValid ? layout.Height : double.NaN;
    }

    private static double ReadDoubleResource(string key, double fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true &&
        value is double resolved
            ? resolved
            : fallback;
}
