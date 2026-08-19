using System.Text.RegularExpressions;

internal static class MotionRuntimeHardeningContractTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("appearance writes share one serialized transaction", AppearanceWritesShareOneTransaction),
        ("motion sampling contains media failures to the current request", MotionSamplingContainsMediaFailures),
        ("motion freeze and resume own the resample timer", MotionFreezeAndResumeOwnTimer),
        ("backdrop detach uses its captured AppWindow", BackdropDetachUsesCapturedAppWindow),
        ("motion import is uncapped and isolates transient source locks", MotionImportIsUncappedAndLockTolerant),
        ("motion playback ignores audio codec failures", MotionPlaybackIgnoresAudioCodecFailures),
        ("visible focus loss preserves the clear motion frame", VisibleFocusLossPreservesClearMotionFrame),
        ("native focus transitions drive backdrop lifecycle", NativeFocusTransitionsDriveBackdropLifecycle),
        ("wallpaper engine layout updates are coalesced", WallpaperEngineLayoutUpdatesAreCoalesced),
        ("wallpaper engine parking and resume races remain live", WallpaperEngineParkingAndResumeRacesRemainLive),
        ("wallpaper capture setup uses the owner dispatcher", WallpaperCaptureSetupUsesOwnerDispatcher),
        ("wallpaper resume cancellation follows stop", WallpaperResumeCancellationFollowsStop),
        ("wallpaper import defers startup while launcher is covered", WallpaperImportDefersStartupWhileLauncherIsCovered),
    ];

    private static Task AppearanceWritesShareOneTransaction()
    {
        var appearance = ReadProductSource("MainPage.Appearance.cs");
        var runtime = ReadProductSource("MainPage.AppearanceRuntime.cs");
        Assert(
            appearance.Contains(
                "private readonly SemaphoreSlim _appearanceSaveGate = new(1, 1);",
                StringComparison.Ordinal),
            "MainPage must own one appearance persistence gate");

        var guardedWrites = 0;
        foreach (var source in new[] { appearance, runtime })
        {
            foreach (Match write in Regex.Matches(
                         source,
                         @"ViewModel\.SaveAppearanceSettingsAsync\s*\(",
                         RegexOptions.CultureInvariant))
            {
                var method = FindContainingMethod(source, write.Index);
                Assert(
                    method.Contains("await _appearanceSaveGate.WaitAsync", StringComparison.Ordinal) &&
                    method.Contains("_appearanceSaveGate.Release()", StringComparison.Ordinal),
                    "every MainPage appearance write must execute inside the shared gate");
                guardedWrites++;
            }
        }

        Assert(guardedWrites >= 4, "the persistence contract must cover every current MainPage write path");
        AssertReadsPreviousAfterGate(
            FindMethod(appearance, "ImportMotionBackgroundAsync"),
            "var previousAppearance = ViewModel.AppearanceSettings");
        AssertReadsPreviousAfterGate(
            FindMethod(appearance, "SaveAndApplyAppearanceAsync"),
            "var previous = ViewModel.AppearanceSettings");
        return Task.CompletedTask;
    }

    private static Task MotionSamplingContainsMediaFailures()
    {
        var view = ReadProductSource("Features", "Background", "ArtBackdropView.xaml.cs");
        var samplers = ReadProductSource("Features", "Background", "BackgroundSamplers.cs");
        var tick = FindMethod(view, "OnMotionResampleTick");
        var samplingGuard = FindMethod(view, "IsCurrentMotionSamplingRequest");
        var playbackRead = tick.IndexOf(
            "player.PlaybackSession.PlaybackState",
            StringComparison.Ordinal);

        Assert(
            tick.IndexOf("try", StringComparison.Ordinal) >= 0 &&
            playbackRead > tick.IndexOf("try", StringComparison.Ordinal),
            "media playback state reads must stay inside the async-void exception boundary");
        Assert(
            tick.Contains("Volatile.Read(ref _motionRequestVersion)", StringComparison.Ordinal) &&
            Count(tick, "IsCurrentMotionSamplingRequest(requestVersion, descriptor, player)") >= 3,
            "resampling must bind completion, failure, and cleanup to the captured request");
        Assert(
            samplingGuard.Contains("IsCurrentMotionRequest(requestVersion)", StringComparison.Ordinal) &&
            samplingGuard.Contains("ReferenceEquals(player, _mediaPlayer)", StringComparison.Ordinal) &&
            samplingGuard.Contains("descriptor.Source", StringComparison.Ordinal) &&
            samplingGuard.Contains("_motionDescriptor?.Source", StringComparison.Ordinal),
            "the sampling guard must verify request version, player identity, and source");
        AssertKnownMediaFailuresAreCaught(tick, "motion resample tick");
        Assert(
            tick.Contains("_motionResampleTimer?.Stop()", StringComparison.Ordinal),
            "a current request media failure must stop further dynamic sampling");

        var motionSamplerStart = samplers.IndexOf(
            "public sealed class MotionSampler",
            StringComparison.Ordinal);
        var importerStart = samplers.IndexOf(
            "public sealed class MotionBackgroundImporter",
            StringComparison.Ordinal);
        Assert(
            motionSamplerStart >= 0 && importerStart > motionSamplerStart,
            "motion sampler source boundary was not found");
        var motionSampler = samplers[motionSamplerStart..importerStart];
        var sample = FindMethod(motionSampler, "SampleAsync");
        Assert(
            sample.Contains("MediaClip.CreateFromFileAsync", StringComparison.Ordinal) &&
            sample.Contains("GetThumbnailAsync", StringComparison.Ordinal),
            "motion analysis must sample a decoded media thumbnail");
        AssertKnownMediaFailuresAreCaught(sample, "motion thumbnail sampler");
        Assert(
            sample.Contains("return null;", StringComparison.Ordinal),
            "known thumbnail failures must become an unavailable sample");
        var validate = FindMethod(motionSampler, "ValidateAsync");
        AssertKnownMediaFailuresAreCaught(validate, "motion decoder validation");
        return Task.CompletedTask;
    }

    private static Task MotionFreezeAndResumeOwnTimer()
    {
        var view = ReadProductSource("Features", "Background", "ArtBackdropView.xaml.cs");
        var freeze = FindMethod(view, "FreezeMotion");
        var resume = FindMethod(view, "ResumeMotionIfAllowed");
        Assert(
            freeze.Contains("_motionResampleTimer?.Stop()", StringComparison.Ordinal),
            "freezing motion must stop the resample timer");
        Assert(
            resume.Contains("StartMotionResampling()", StringComparison.Ordinal),
            "resuming motion must restart dynamic sampling");
        return Task.CompletedTask;
    }

    private static Task BackdropDetachUsesCapturedAppWindow()
    {
        var view = ReadProductSource("Features", "Background", "ArtBackdropView.xaml.cs");
        var attach = FindMethod(view, "Attach");
        var detach = FindMethod(view, "Detach");
        Assert(
            view.Contains("private AppWindow? _hostAppWindow;", StringComparison.Ordinal) &&
            attach.Contains("_hostAppWindow = _hostWindow.AppWindow;", StringComparison.Ordinal),
            "the live AppWindow must be captured while the host Window is attached");
        Assert(
            detach.Contains("var hostAppWindow = _hostAppWindow;", StringComparison.Ordinal) &&
            detach.Contains("hostAppWindow.Changed -= OnHostAppWindowChanged;", StringComparison.Ordinal) &&
            !detach.Contains("_hostWindow.AppWindow", StringComparison.Ordinal) &&
            !detach.Contains("hostWindow.AppWindow", StringComparison.Ordinal),
            "detach must not query AppWindow after the host Window has started closing");
        return Task.CompletedTask;
    }

    private static Task MotionImportIsUncappedAndLockTolerant()
    {
        var rules = ReadProductSource("Features", "Background", "MotionSourceRules.cs");
        var samplers = ReadProductSource("Features", "Background", "BackgroundSamplers.cs");
        var copier = ReadProductSource("Features", "Background", "MotionImportFileCopier.cs");
        var settingsXaml = ReadProductSource(
            "Features",
            "Settings",
            "GeneralAppearanceSettingsView.xaml");
        var appearance = ReadProductSource("MainPage.Appearance.cs");
        Assert(
            !rules.Contains("MaximumFramesPerSecond", StringComparison.Ordinal) &&
            !rules.Contains("maximumFramesPerSecond", StringComparison.Ordinal) &&
            !settingsXaml.Contains("MotionFpsComboBox", StringComparison.Ordinal) &&
            !settingsXaml.Contains("导入帧率上限", StringComparison.Ordinal) &&
            !appearance.Contains("Background.MotionFpsCap", StringComparison.Ordinal),
            "frame-rate caps must not remain in validation, UI, or the import call");

        var importerStart = samplers.IndexOf(
            "public sealed class MotionBackgroundImporter",
            StringComparison.Ordinal);
        Assert(importerStart >= 0, "motion importer source boundary was not found");
        var importer = samplers[importerStart..];
        var chooseWallpaper = FindMethod(appearance, "ChooseWallpaperAsync");
        Assert(
            !rules.Contains("MaximumFileBytes", StringComparison.Ordinal) &&
            !importer.Contains("MaximumFileBytes", StringComparison.Ordinal) &&
            !rules.Contains("200 MB", StringComparison.Ordinal) &&
            !importer.Contains("200 MB", StringComparison.Ordinal),
            "motion imports must not retain a file-size ceiling");
        Assert(
            rules.Contains("SupportedExtensions", StringComparison.Ordinal) &&
            rules.Contains("IsSupportedExtension", StringComparison.Ordinal) &&
            !FindMethod(rules, "Validate").Contains("IsSupportedExtension", StringComparison.Ordinal) &&
            !importer.Contains("IsSupportedExtension", StringComparison.Ordinal) &&
            !rules.Contains("IsH264", StringComparison.Ordinal) &&
            !rules.Contains("IsAac", StringComparison.Ordinal) &&
            appearance.Contains(
                "WallpaperSourceRules.InferMode(file.Path)",
                StringComparison.Ordinal) &&
            chooseWallpaper.Contains("WallpaperSourceRules.PackageExtensions", StringComparison.Ordinal) &&
            !chooseWallpaper.Contains("picker.FileTypeFilter.Add(\"*\")", StringComparison.Ordinal) &&
            appearance.Contains(
                "ImportMotionBackgroundAsync",
                StringComparison.Ordinal),
            "common containers must reach the dedicated import path while decoder selection remains system-owned");
        var copy = importer.IndexOf(
            "MotionImportFileCopier.CopyWithRetryAsync",
            StringComparison.Ordinal);
        var validate = importer.IndexOf("_sampler.ValidateAsync", StringComparison.Ordinal);
        Assert(
            copy >= 0 && validate > copy,
            "the selected source must be copied before WinRT media inspection can retain it");
        Assert(
            importer.Contains(
                "$\"{Guid.NewGuid():N}{extension}\"",
                StringComparison.Ordinal) &&
            importer.Contains(
                "$\"{hash}{extension}\"",
                StringComparison.Ordinal) &&
            !importer.Contains(".import.mp4", StringComparison.Ordinal),
            "the imported cache must preserve the selected media container extension");
        Assert(
            samplers.Contains(
                "系统没有可用于视频编码 {profile.Video.Subtype} 的解码器",
                StringComparison.Ordinal) &&
            !samplers.Contains(
                "系统没有可用于该文件的 AAC 解码器",
                StringComparison.Ordinal),
            "decoder errors must identify the real video subtype and must not reject muted audio tracks");
        Assert(
            copier.Contains("AttemptCount = 4", StringComparison.Ordinal) &&
            copier.Contains("FileShare.ReadWrite | FileShare.Delete", StringComparison.Ordinal) &&
            copier.Contains("catch (IOException) when", StringComparison.Ordinal),
            "motion import must retry transient picker or media-preview locks");
        return Task.CompletedTask;
    }

    private static Task MotionPlaybackIgnoresAudioCodecFailures()
    {
        var view = ReadProductSource("Features", "Background", "ArtBackdropView.xaml.cs");
        var appearance = ReadProductSource("MainPage.Appearance.cs");
        var showMotion = FindMethod(view, "ShowMotionAsync");
        var disableAudio = FindMethod(view, "DisableAudioTracks");
        var import = FindMethod(appearance, "ImportMotionBackgroundAsync");
        var playback = import.IndexOf(
            "LauncherBackground.ShowMotionAsync",
            StringComparison.Ordinal);
        var analysis = import.IndexOf(
            "AnalyzeMotionOrFallbackAsync",
            StringComparison.Ordinal);

        Assert(
            showMotion.Contains("AudioTracksChanged", StringComparison.Ordinal) &&
            showMotion.Contains("DisableAudioTracks(playbackItem)", StringComparison.Ordinal) &&
            showMotion.Contains("IsMuted = true", StringComparison.Ordinal) &&
            disableAudio.Contains("SelectedIndex = -1", StringComparison.Ordinal),
            "wallpaper playback must deselect audio tracks as well as mute output");
        Assert(
            playback >= 0 && analysis > playback &&
            import.Contains("await AnalyzeMotionOrFallbackAsync", StringComparison.Ordinal),
            "thumbnail analysis must be optional after the video playback pipeline opens");
        return Task.CompletedTask;
    }

    private static Task VisibleFocusLossPreservesClearMotionFrame()
    {
        var view = ReadProductSource("Features", "Background", "ArtBackdropView.xaml.cs");
        var activation = FindMethod(view, "UpdateHostFocus");
        var appWindowChanged = FindMethod(view, "OnHostAppWindowChanged");
        var pause = FindMethod(view, "PauseAfterDeactivationAsync");
        var freeze = FindMethod(view, "FreezeMotion");
        var keepFrame = FindMethod(view, "KeepMotionFrameVisible");
        var canResume = FindMethod(view, "CanResumeMotion");
        Assert(
            activation.Contains("ScheduleMotionPause()", StringComparison.Ordinal) &&
            activation.Contains("if (CanResumeMotion())", StringComparison.Ordinal) &&
            activation.Contains("FreezeMotion();", StringComparison.Ordinal) &&
            !activation.Contains("SetWindowOccluded(true)", StringComparison.Ordinal) &&
            !pause.Contains("SetWindowOccluded(true)", StringComparison.Ordinal),
            "focus transitions must preserve occlusion semantics and restore the current tier on activation");
        Assert(
            pause.Contains(
                "FreezeMotion(showStaticFallback: _hostMinimized)",
                StringComparison.Ordinal),
            "focus loss must retain the motion frame unless the window is minimized");
        Assert(
            appWindowChanged.Contains(
                "FreezeMotion(showStaticFallback: false)",
                StringComparison.Ordinal),
            "a visible unfocused window restored from minimization must recover its clear frame");
        Assert(
            freeze.Contains("KeepMotionFrameVisible()", StringComparison.Ordinal) &&
            keepFrame.Contains("StaticBackdrop.Visibility = Visibility.Collapsed", StringComparison.Ordinal) &&
            keepFrame.Contains("MotionHost.Visibility = Visibility.Visible", StringComparison.Ordinal),
            "the focus-loss freeze path must never reveal the blurred static fallback");
        Assert(
            canResume.Contains("_hostFocused", StringComparison.Ordinal) &&
            canResume.Contains("!_hostMinimized", StringComparison.Ordinal),
            "motion resume must remain blocked while the window is unfocused or minimized");
        return Task.CompletedTask;
    }

    private static Task NativeFocusTransitionsDriveBackdropLifecycle()
    {
        var runtime = ReadProductSource("Services", "NativeWindowRuntime.cs");
        var window = ReadProductSource("MainWindow.xaml.cs");
        var view = ReadProductSource("Features", "Background", "ArtBackdropView.xaml.cs");
        var updateRuntimeState = FindMethod(runtime, "SetApplicationActive");
        var attach = FindMethod(view, "Attach");
        var detach = FindMethod(view, "Detach");
        var nativeActivation = FindMethod(view, "OnNativeHostActivationChanged");

        Assert(
            runtime.Contains("private const uint WmActivateApp = 0x001C;", StringComparison.Ordinal) &&
            runtime.Contains("SetWinEventHook(", StringComparison.Ordinal) &&
            runtime.Contains("EventSystemForeground", StringComparison.Ordinal) &&
            runtime.Contains("PostMessage(", StringComparison.Ordinal) &&
            runtime.Contains("message is WmActivateApp or WmAppForegroundSync", StringComparison.Ordinal) &&
            runtime.Contains("SetApplicationActive(IsCurrentProcessForeground())", StringComparison.Ordinal),
            "the native host must reconcile WM_ACTIVATEAPP with system foreground transitions");
        Assert(
            updateRuntimeState.Contains("ActivationChanged?.Invoke", StringComparison.Ordinal) &&
            runtime.Contains("public bool IsApplicationActive", StringComparison.Ordinal) &&
            runtime.Contains("_applicationActive = IsCurrentProcessForeground();", StringComparison.Ordinal) &&
            runtime.Contains("UnhookWinEvent(_foregroundWinEventHook)", StringComparison.Ordinal),
            "native activation must expose both current state and transitions");
        Assert(
            window.Contains("ForegroundActivationChanged", StringComparison.Ordinal) &&
            window.Contains("IsForegroundActive", StringComparison.Ordinal),
            "MainWindow must expose the native foreground contract");
        Assert(
            attach.Contains("_hostFocused = nativeHostWindow.IsForegroundActive", StringComparison.Ordinal) &&
            attach.Contains("ForegroundActivationChanged += OnNativeHostActivationChanged", StringComparison.Ordinal) &&
            attach.Contains("else", StringComparison.Ordinal) &&
            attach.Contains("_hostWindow.Activated += OnHostActivated", StringComparison.Ordinal) &&
            detach.Contains("ForegroundActivationChanged -= OnNativeHostActivationChanged", StringComparison.Ordinal),
            "the backdrop must prefer native activation while retaining the generic Window fallback");
        Assert(
            nativeActivation.Contains("UpdateHostFocus(args.IsActive)", StringComparison.Ordinal),
            "native foreground transitions must use the shared focus lifecycle");
        Assert(
            FindMethod(view, "OnHostActivated").Contains(
                "if (_nativeHostWindow is not null)",
                StringComparison.Ordinal),
            "WinUI activation must not override the native MainWindow authority");
        return Task.CompletedTask;
    }

    private static Task WallpaperEngineLayoutUpdatesAreCoalesced()
    {
        var view = ReadProductSource("Features", "Background", "ArtBackdropView.xaml.cs");
        var runtime = ReadProductSource("Features", "Background", "WallpaperEngineRuntimeHost.cs");
        var pointer = FindMethod(view, "OnRootPointerMoved");
        var appWindowChanged = FindMethod(view, "OnHostAppWindowChanged");
        var sizeChanged = FindMethod(view, "OnBackdropSizeChanged");
        var hostSizeChanged = FindMethod(view, "OnArtSharpHostSizeChanged");
        var timer = FindMethod(view, "CreateWallpaperEngineLayoutTimer");
        var updateBounds = FindMethod(runtime, "UpdateBounds");
        var startCapture = FindMethod(runtime, "StartCapture");
        var show = FindMethod(runtime, "ShowCoreAsync");

        Assert(
            !pointer.Contains("UpdateWallpaperEngineLayout", StringComparison.Ordinal),
            "pointer samples must not reposition the hidden Wallpaper Engine window");
        Assert(
            appWindowChanged.Contains("args.DidPositionChange", StringComparison.Ordinal) &&
            appWindowChanged.Contains("args.DidSizeChange", StringComparison.Ordinal) &&
            appWindowChanged.Contains("args.DidVisibilityChange", StringComparison.Ordinal) &&
            appWindowChanged.Contains("args.DidPresenterChange", StringComparison.Ordinal) &&
            appWindowChanged.Contains("_wallpaperEngineHost?.UpdatePlacement()", StringComparison.Ordinal),
            "pure AppWindow position changes must keep the runtime behind the owner without resizing capture");
        Assert(
            sizeChanged.Contains("QueueWallpaperEngineLayout", StringComparison.Ordinal) &&
            hostSizeChanged.Contains("QueueWallpaperEngineLayout", StringComparison.Ordinal),
            "backdrop size changes must use the coalesced layout path");
        Assert(
            view.Contains("DispatcherQueueTimer? _wallpaperEngineLayoutTimer", StringComparison.Ordinal) &&
            timer.Contains("Interval = TimeSpan.FromMilliseconds(75)", StringComparison.Ordinal) &&
            timer.Contains("IsRepeating = false", StringComparison.Ordinal),
            "Wallpaper Engine layout updates must be debounced with a one-shot dispatcher timer");

        var placement = updateBounds.IndexOf(
            "PlaceWindowBehindOwner",
            StringComparison.Ordinal);
        var dimensionsGuard = updateBounds.IndexOf(
            "!dimensionsChanged",
            StringComparison.Ordinal);
        var recreate = updateBounds.IndexOf(
            "framePool.Recreate",
            StringComparison.Ordinal);
        Assert(
            placement >= 0 && dimensionsGuard > placement && recreate > dimensionsGuard,
            "runtime bounds updates must refresh owner-relative placement before recreating changed capture dimensions");
        Assert(
            show.Contains("_captureWindow = _wallpaperWindow", StringComparison.Ordinal) &&
            show.IndexOf("PlaceWindowBehindOwner", StringComparison.Ordinal) <
                show.IndexOf("StartCapture", StringComparison.Ordinal) &&
            !show.Contains("WaitForRenderWindowAsync", StringComparison.Ordinal) &&
            startCapture.Contains("TryCreateFromWindowId", StringComparison.Ordinal) &&
            startCapture.Contains("CreateFreeThreaded", StringComparison.Ordinal) &&
            startCapture.Contains("Direct3D11CaptureFramePool.Create(", StringComparison.Ordinal),
            "capture must target the WPE owner and prefer the free-threaded frame pool with a fallback");
        return Task.CompletedTask;
    }

    private static Task WallpaperEngineParkingAndResumeRacesRemainLive()
    {
        var view = ReadProductSource("Features", "Background", "ArtBackdropView.xaml.cs");
        var runtime = ReadProductSource("Features", "Background", "WallpaperEngineRuntimeHost.cs");
        var show = FindMethod(view, "ShowWallpaperEngineAsync");
        var stop = FindMethod(view, "StopWallpaperEngine");
        var suspend = FindMethod(view, "SuspendWallpaperEngine");
        var queueResume = FindMethod(view, "QueueWallpaperEngineResume");
        var resume = FindMethod(view, "ResumeWallpaperEngineAsync");
        var stopMotion = FindMethod(view, "StopMotion");
        var placement = FindMethod(runtime, "PlaceWindowBehindOwner");
        var updatePlacement = FindMethod(runtime, "UpdatePlacement");

        Assert(
            runtime.Contains("private static readonly IntPtr HwndBottom = new(1);", StringComparison.Ordinal) &&
            placement.Contains("GetWindowRect(_ownerWindowHandle", StringComparison.Ordinal) &&
            placement.Contains("SetWindowPos(", StringComparison.Ordinal) &&
            placement.Contains("HwndBottom", StringComparison.Ordinal) &&
            !runtime.Contains("MoveWindowOffscreen", StringComparison.Ordinal) &&
            !runtime.Contains("SmXVirtualScreen", StringComparison.Ordinal),
            "the WPE owner must remain on-screen behind Nikkiward so the scene renderer keeps presenting frames");
        Assert(
            updatePlacement.Contains("configureStyle: false", StringComparison.Ordinal) &&
            placement.Contains("if (configureStyle)", StringComparison.Ordinal) &&
            placement.Contains("ShowWindow(wallpaperWindow, SwHide)", StringComparison.Ordinal) &&
            placement.Contains("SwShowNoActivate", StringComparison.Ordinal),
            "pure owner movement must not rewrite WPE window styles on every position event");
        Assert(
            show.Contains("CancelWallpaperEnginePause()", StringComparison.Ordinal) &&
            show.Contains("_wallpaperEngineHost.ShowAsync", StringComparison.Ordinal) &&
            show.Contains("_wallpaperEngineResumePending = true", StringComparison.Ordinal) &&
            runtime.Contains("CancellationTokenSource? _showCancellation", StringComparison.Ordinal) &&
            runtime.Contains("CloseWallpaperWindowAfterStop", StringComparison.Ordinal) &&
            stop.Contains("_wallpaperEngineResumePending = false", StringComparison.Ordinal) &&
            suspend.Contains("_wallpaperEngineResumePending = false", StringComparison.Ordinal) &&
            !stopMotion.Contains("CancelWallpaperEnginePause()", StringComparison.Ordinal),
            "inactive import must defer WPE startup while preserving independent stop state");

        var inFlight = queueResume.IndexOf(
            "if (_wallpaperEngineStartInFlight)",
            StringComparison.Ordinal);
        var active = queueResume.IndexOf(
            "if (_wallpaperEngineHost.IsActive)",
            StringComparison.Ordinal);
        Assert(
            inFlight >= 0 && active > inFlight &&
            queueResume.Contains("_wallpaperEngineResumePending = true", StringComparison.Ordinal) &&
            resume.Contains("_wallpaperEngineResumePending", StringComparison.Ordinal) &&
            resume.Contains("QueueWallpaperEngineResume()", StringComparison.Ordinal),
            "a focus return during an in-flight WPE start must schedule one deferred resume");
        return Task.CompletedTask;
    }

    private static Task WallpaperCaptureSetupUsesOwnerDispatcher()
    {
        var runtime = ReadProductSource(
            "Features",
            "Background",
            "WallpaperEngineRuntimeHost.cs");
        var retry = FindMethod(runtime, "StartCaptureWithRetryAsync");
        var dispatch = FindMethod(runtime, "StartCaptureOnOwnerDispatcherAsync");

        Assert(
            retry.Contains(
                "StartCaptureOnOwnerDispatcherAsync",
                StringComparison.Ordinal),
            "capture retries must marshal UI-bound setup to the owner dispatcher");
        Assert(
            dispatch.Contains(
                "var dispatcherQueue = _ownerDispatcherQueue",
                StringComparison.Ordinal) &&
            dispatch.Contains(
                "dispatcherQueue.HasThreadAccess",
                StringComparison.Ordinal) &&
            dispatch.Contains("TryEnqueue", StringComparison.Ordinal) &&
            dispatch.Contains(
                "StartCapture(captureWindow, bounds)",
                StringComparison.Ordinal),
            "capture setup must run on the owner dispatcher when continuation resumes off-thread");
        return Task.CompletedTask;
    }

    private static Task WallpaperResumeCancellationFollowsStop()
    {
        var view = ReadProductSource(
            "Features",
            "Background",
            "ArtBackdropView.xaml.cs");
        var stop = FindMethod(view, "StopWallpaperEngine");
        var suspend = FindMethod(view, "SuspendWallpaperEngine");
        var queue = FindMethod(view, "QueueWallpaperEngineResume");
        var resume = FindMethod(view, "ResumeWallpaperEngineAsync");

        Assert(
            view.Contains(
                "CancellationTokenSource? _wallpaperEngineResumeCancellation",
                StringComparison.Ordinal) &&
            stop.Contains("CancelWallpaperEngineResume()", StringComparison.Ordinal) &&
            suspend.Contains("CancelWallpaperEngineResume()", StringComparison.Ordinal),
            "stopping or suspending the backdrop must cancel an in-flight WPE resume");
        Assert(
            queue.Contains(
                "new CancellationTokenSource()",
                StringComparison.Ordinal) &&
            resume.Contains(
                "cancellationSource.Token",
                StringComparison.Ordinal) &&
            resume.Contains(
                "_wallpaperEngineRequested",
                StringComparison.Ordinal),
            "WPE resume must carry its cancellation token and never requeue after stop");
        return Task.CompletedTask;
    }

    private static Task WallpaperImportDefersStartupWhileLauncherIsCovered()
    {
        var view = ReadProductSource(
            "Features",
            "Background",
            "ArtBackdropView.xaml.cs");
        var show = FindMethod(view, "ShowWallpaperEngineAsync");

        var inactive = show.IndexOf(
            "if (!CanShowWallpaperEngine())",
            StringComparison.Ordinal);
        var pending = show.IndexOf(
            "_wallpaperEngineResumePending = true",
            StringComparison.Ordinal);
        var runtimeStart = show.IndexOf(
            "_wallpaperEngineHost.ShowAsync",
            StringComparison.Ordinal);
        Assert(
            inactive >= 0 && pending > inactive && runtimeStart > pending,
            "an import initiated under the settings overlay must defer runtime startup until the launcher surface returns");
        return Task.CompletedTask;
    }

    private static void AssertReadsPreviousAfterGate(string method, string previousRead)
    {
        var gate = method.IndexOf(
            "await _appearanceSaveGate.WaitAsync",
            StringComparison.Ordinal);
        var read = method.IndexOf(previousRead, StringComparison.Ordinal);
        Assert(
            gate >= 0 && read > gate,
            $"'{previousRead}' must be evaluated only after acquiring the appearance gate");
    }

    private static void AssertKnownMediaFailuresAreCaught(string method, string boundary)
    {
        Assert(
            method.Contains("catch (Exception ex) when", StringComparison.Ordinal) &&
            method.Contains("FileNotFoundException", StringComparison.Ordinal) &&
            method.Contains("UnauthorizedAccessException", StringComparison.Ordinal) &&
            method.Contains("IOException", StringComparison.Ordinal) &&
            method.Contains("ArgumentException", StringComparison.Ordinal) &&
            method.Contains("NotSupportedException", StringComparison.Ordinal) &&
            method.Contains("InvalidOperationException", StringComparison.Ordinal) &&
            method.Contains("COMException", StringComparison.Ordinal),
            $"{boundary} must contain known file, media, and COM failures");
    }

    private static string ReadProductSource(params string[] segments)
    {
        var path = segments.Aggregate(Path.Combine(FindRoot(), "Nikkiward"), Path.Combine);
        return File.ReadAllText(path);
    }

    private static string FindMethod(string source, string methodName)
    {
        var declaration = Regex.Match(
            source,
            $@"(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?[A-Za-z0-9_<>,?\[\]\s]+\s+{Regex.Escape(methodName)}\s*\([^;{{}}]*\)\s*(?:=>|\{{)",
            RegexOptions.CultureInvariant);
        if (!declaration.Success)
        {
            throw new InvalidOperationException($"Method '{methodName}' was not found.");
        }

        if (source.AsSpan(declaration.Index, declaration.Length).Contains("=>", StringComparison.Ordinal))
        {
            var end = source.IndexOf(';', declaration.Index + declaration.Length);
            return source[declaration.Index..(end + 1)];
        }

        return FindContainingMethod(source, declaration.Index + declaration.Length);
    }

    private static string FindContainingMethod(string source, int memberIndex)
    {
        var candidates = Regex
            .Matches(
                source[..memberIndex],
                @"(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?[A-Za-z0-9_<>,?\[\]\s]+\s+[A-Za-z0-9_]+\s*\([^;{}]*\)\s*\{",
                RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Reverse();

        foreach (var candidate in candidates)
        {
            var openingBrace = source.IndexOf('{', candidate.Index);
            var depth = 0;
            for (var index = openingBrace; index < source.Length; index++)
            {
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}' && --depth == 0)
                {
                    if (index >= memberIndex)
                    {
                        return source[openingBrace..(index + 1)];
                    }

                    break;
                }
            }
        }

        throw new InvalidOperationException("Containing method was not found.");
    }

    private static string FindRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "Nikkiward", "Nikkiward.csproj")) &&
                File.Exists(Path.Combine(
                    current.FullName,
                    "Nikkiward.ProfileBuilder.Tests",
                    "Nikkiward.ProfileBuilder.Tests.csproj")))
            {
                return current.FullName;
            }
        }

        throw new DirectoryNotFoundException("The Nikkiward source root was not found.");
    }

    private static int Count(string value, string token)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }

        return count;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
