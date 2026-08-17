using System.Diagnostics;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Nikkiward.Models;
using SharpGameInput.V0;
using WindowsInput;

namespace Nikkiward.Features.GamepadControl;

/// <summary>
/// Maps the Xbox Guide and Share buttons to Nikkiward actions or to keyboard
/// keystrokes, using the GameInput system-button callback so the presses are
/// seen even while the app is in the background.
/// </summary>
/// <remarks>
/// Ported from Starward 0.18.1 (MIT, Copyright (c) 2023 Scighost). Two parts of
/// the original are deliberately left out: the screenshot button action, which
/// only resolves inside Starward's own capture service, and the simulated
/// keyboard/mouse input loop with its click-through hint overlay, which needs
/// Starward's WindowEx base class and a Win2D composition clip.
///
/// This type does not read or write settings. The settings surface owns
/// persistence and pushes state in through <see cref="Apply"/>.
/// </remarks>
internal static class GamepadController
{
    /// <summary>
    /// Long-press threshold for telling "open the main window" from a short
    /// press that fires the mapped keys.
    /// </summary>
    private const int GuideLongPressMilliseconds = 600;

    private static IGameInput? _gameInput;
    private static InputSimulator? _inputSimulator;
    private static DispatcherQueue? _dispatcherQueue;
    private static Action? _showMainWindow;

    private static bool _enabled;
    private static GamepadButtonAction _guideAction;
    private static (VirtualKeyCode[] Modifiers, VirtualKeyCode[] Keys) _guideKeys;
    private static (VirtualKeyCode[] Modifiers, VirtualKeyCode[] Keys) _shareKeys;

    /// <summary>
    /// Raised when connection state changes, so a settings page can refresh its
    /// status line. Fires on the dispatcher thread passed to <see cref="Initialize"/>.
    /// </summary>
    public static event EventHandler? StateChanged;

    public static bool Initialized { get; private set; }

    public static bool GamepadConnected { get; private set; }

    /// <summary>Windows 10 without the redistributable: GameInput is unavailable.</summary>
    public static bool NeedInstallGameInputRedist { get; private set; }

    public static bool GameInputRedistOutdated { get; private set; }

    /// <summary>
    /// True when the failure was a missing or too-old GameInput runtime, which
    /// the user can fix by installing it, rather than a generic error.
    /// </summary>
    public static bool RuntimeMissing => NeedInstallGameInputRedist || GameInputRedistOutdated;

    public static string RuntimeDownloadUrl =>
        "https://learn.microsoft.com/gaming/gdk/_content/gc/input/overviews/input-overview";

    public static string? InitializationError { get; private set; }

    public static bool GuideLongPressOpensMainWindow { get; set; } = true;

    public static GamepadButtonAction ShareAction { get; set; }

    public static GamepadButtonAction GuideAction
    {
        get => _guideAction;
        set
        {
            _guideAction = value;
            SyncXboxGameBarGuideButton();
        }
    }

    /// <summary>
    /// Creates the GameInput instance and registers the device and system
    /// button callbacks. Safe to call more than once; later calls are ignored
    /// once initialization has succeeded.
    /// </summary>
    /// <param name="dispatcherQueue">
    /// UI dispatcher. Required, because the GameInput callbacks arrive on a
    /// background thread and window activation must be marshalled back.
    /// </param>
    /// <param name="showMainWindow">Invoked when the Guide button is long-pressed.</param>
    public static bool Initialize(DispatcherQueue dispatcherQueue, Action showMainWindow)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        ArgumentNullException.ThrowIfNull(showMainWindow);

        _dispatcherQueue = dispatcherQueue;
        _showMainWindow = showMainWindow;

        if (Initialized)
        {
            return true;
        }

        try
        {
            if (!CheckGameInputAvailability())
            {
                return false;
            }

            if (!SharpGameInput.GameInput.CreateV0(out _gameInput))
            {
                InitializationError = "GameInput 组件初始化失败，手柄增强不可用。";
                return false;
            }

            _inputSimulator = new InputSimulator();

            _gameInput.RegisterDeviceCallback(
                null,
                GameInputKind.Gamepad,
                GameInputDeviceStatus.NoStatus | GameInputDeviceStatus.Connected,
                GameInputEnumerationKind.BlockingEnumeration,
                null,
                OnDeviceStatusChanged,
                out _,
                out _);

            _gameInput.RegisterSystemButtonCallback(
                null,
                GameInputSystemButtons.Guide | GameInputSystemButtons.Share,
                null,
                OnSystemButtonChanged,
                out _,
                out _);

            // Without a background focus policy the Guide and Share presses only
            // arrive while Nikkiward itself is foreground, which defeats the point.
            if (SharpGameInput.GameInput.TryCast(_gameInput, out SharpGameInput.V2.IGameInput? gameInputV2))
            {
                using (gameInputV2)
                {
                    gameInputV2.SetFocusPolicy(
                        SharpGameInput.V2.GameInputFocusPolicy.EnableBackgroundInput
                        | SharpGameInput.V2.GameInputFocusPolicy.EnableBackgroundGuideButton
                        | SharpGameInput.V2.GameInputFocusPolicy.EnableBackgroundShareButton);
                }
            }

            InitializationError = null;
            Initialized = true;
            return true;
        }
        catch (Exception ex)
        {
            InitializationError = $"手柄增强初始化失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// GameInput ships in Windows 11 22000+; on Windows 10 it arrives with the
    /// GameInput redistributable, which must be at least version 1.
    /// </summary>
    private static bool CheckGameInputAvailability()
    {
        NeedInstallGameInputRedist = false;
        GameInputRedistOutdated = false;

        var redistPath = Path.Combine(Environment.SystemDirectory, "GameInputRedist.dll");
        var redistInstalled = File.Exists(redistPath);
        if (redistInstalled &&
            Version.TryParse(FileVersionInfo.GetVersionInfo(redistPath).FileVersion, out var version) &&
            version.Major < 1)
        {
            GameInputRedistOutdated = true;
            InitializationError = "GameInput 运行库版本过低，请更新后重试。";
            return false;
        }

        if (Environment.OSVersion.Version.Build < 22000 && !redistInstalled)
        {
            NeedInstallGameInputRedist = true;
            InitializationError = "Windows 10 需要先安装 GameInput 运行库才能使用手柄增强。";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Pushes persisted settings into the controller. Unrecognised mapping text
    /// is dropped rather than rejected, so a hand-edited settings file cannot
    /// stop the feature from starting.
    /// </summary>
    public static void Apply(GamepadSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _enabled = settings.Enabled;
        GuideLongPressOpensMainWindow = settings.GuideLongPressOpensMainWindow;
        ShareAction = settings.ShareAction;
        _guideKeys = ParseOrEmpty(settings.GuideMapKeys);
        _shareKeys = ParseOrEmpty(settings.ShareMapKeys);

        // Assigned last: its setter reconciles the Xbox Game Bar registry value,
        // which depends on _enabled above.
        GuideAction = settings.GuideAction;
    }

    private static (VirtualKeyCode[] Modifiers, VirtualKeyCode[] Keys) ParseOrEmpty(string? value) =>
        GamepadKeyNames.TryParse(value, out _, out var modifiers, out var keys)
            ? (modifiers, keys)
            : ([], []);

    public static bool TrySetGuideMapKeys(string? value, out string? normalizedTextOrBadKey)
    {
        if (!GamepadKeyNames.TryParse(value, out normalizedTextOrBadKey, out var modifiers, out var keys))
        {
            return false;
        }

        _guideKeys = (modifiers, keys);
        return true;
    }

    public static bool TrySetShareMapKeys(string? value, out string? normalizedTextOrBadKey)
    {
        if (!GamepadKeyNames.TryParse(value, out normalizedTextOrBadKey, out var modifiers, out var keys))
        {
            return false;
        }

        _shareKeys = (modifiers, keys);
        return true;
    }

    /// <summary>
    /// Releases GameInput and hands the Guide button back to the Xbox Game Bar.
    /// Call on app exit; the original leaked both.
    /// </summary>
    public static void Shutdown()
    {
        RestoreXboxGameBarGuideButton();

        try
        {
            _gameInput?.Dispose();
        }
        catch
        {
            // Shutdown path: a failure to release the COM instance is not worth
            // surfacing, and the process is going away regardless.
        }

        _gameInput = null;
        _inputSimulator = null;
        Initialized = false;
        GamepadConnected = false;
    }

    private static void OnDeviceStatusChanged(
        LightGameInputCallbackToken callbackToken,
        object? context,
        LightIGameInputDevice device,
        ulong timestamp,
        GameInputDeviceStatus currentStatus,
        GameInputDeviceStatus previousStatus)
    {
        if (currentStatus.HasFlag(GameInputDeviceStatus.Connected))
        {
            if (previousStatus is GameInputDeviceStatus.NoStatus)
            {
                Interlocked.Increment(ref _gamepadCount);
            }

            SetGamepadConnected(true);
        }
        else if (currentStatus is GameInputDeviceStatus.NoStatus)
        {
            if (Interlocked.Decrement(ref _gamepadCount) <= 0)
            {
                SetGamepadConnected(false);
            }
        }
    }

    private static int _gamepadCount;

    private static void SetGamepadConnected(bool connected)
    {
        if (GamepadConnected == connected)
        {
            return;
        }

        GamepadConnected = connected;
        _dispatcherQueue?.TryEnqueue(() => StateChanged?.Invoke(null, EventArgs.Empty));
    }

    private static void OnSystemButtonChanged(
        LightGameInputCallbackToken callbackToken,
        object? context,
        LightIGameInputDevice device,
        ulong timestamp,
        GameInputSystemButtons currentState,
        GameInputSystemButtons previousState)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            var guideChanged = currentState.HasFlag(GameInputSystemButtons.Guide)
                ^ previousState.HasFlag(GameInputSystemButtons.Guide);
            if (guideChanged)
            {
                if (currentState.HasFlag(GameInputSystemButtons.Guide))
                {
                    OnGuideDown();
                }
                else
                {
                    OnGuideUp();
                }
            }

            var shareChanged = currentState.HasFlag(GameInputSystemButtons.Share)
                ^ previousState.HasFlag(GameInputSystemButtons.Share);
            if (shareChanged && currentState.HasFlag(GameInputSystemButtons.Share))
            {
                SendKeys(ShareAction, _shareKeys);
            }
        }
        catch
        {
            // A single dropped button press must not tear down the callback.
        }
    }

    private static CancellationTokenSource? _guideCancellation;
    private static bool _guideLongPressTriggered;

    private static async void OnGuideDown()
    {
        if (!GuideLongPressOpensMainWindow)
        {
            SendKeys(GuideAction, _guideKeys);
            return;
        }

        _guideCancellation?.Cancel();
        _guideCancellation = new CancellationTokenSource();
        var token = _guideCancellation.Token;
        _guideLongPressTriggered = false;

        try
        {
            await Task.Delay(GuideLongPressMilliseconds, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _guideLongPressTriggered = true;
        _dispatcherQueue?.TryEnqueue(() => _showMainWindow?.Invoke());
    }

    private static void OnGuideUp()
    {
        if (!GuideLongPressOpensMainWindow)
        {
            return;
        }

        _guideCancellation?.Cancel();
        if (!_guideLongPressTriggered)
        {
            SendKeys(GuideAction, _guideKeys);
        }
    }

    private static void SendKeys(
        GamepadButtonAction action,
        (VirtualKeyCode[] Modifiers, VirtualKeyCode[] Keys) mapping)
    {
        if (action is not GamepadButtonAction.MapKeys || _inputSimulator is null)
        {
            return;
        }

        if (mapping.Modifiers.Length is 0 && mapping.Keys.Length is 0)
        {
            return;
        }

        _inputSimulator.Keyboard.ModifiedKeyStroke(mapping.Modifiers, mapping.Keys);
    }

    /// <summary>
    /// Mapping the Guide button only works if the Xbox Game Bar stops answering
    /// it first. Both values live under HKCU, so no elevation is involved, and
    /// the Game Bar value is restored as soon as the mapping is cleared.
    /// </summary>
    private static void SyncXboxGameBarGuideButton()
    {
        if (_enabled && GuideAction is not GamepadButtonAction.None)
        {
            SetGameBarGuideButtonEnabled(false);
        }
        else
        {
            SetGameBarGuideButtonEnabled(true);
        }
    }

    private static void RestoreXboxGameBarGuideButton() => SetGameBarGuideButtonEnabled(true);

    private static void SetGameBarGuideButtonEnabled(bool enabled)
    {
        try
        {
            Registry.SetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\GameBar",
                "UseNexusForGameBarEnabled",
                enabled ? 1 : 0,
                RegistryValueKind.DWord);
        }
        catch
        {
            // Policy or a locked hive can block this; the mapping still works,
            // the Game Bar just answers the Guide button as well.
        }
    }
}
