using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Nikkiward.Features.GamepadControl;
using Nikkiward.Models;
using Nikkiward.Services;

namespace Nikkiward;

public partial class App : Application
{
    private const string InstanceKey = "Nikkiward.Main";

    public static MainWindow MainWindow { get; private set; } = null!;

    internal static WindowSettingsSession Settings { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var current = AppInstance.GetCurrent();
        var showWhenReady = false;
        current.Activated += (_, _) => dispatcher.TryEnqueue(() =>
        {
            if (MainWindow is { } window)
            {
                window.ShowWindowCore();
            }
            else
            {
                showWhenReady = true;
            }
        });

        try
        {
            if (!await OwnsWindowAsync(current))
            {
                Exit();
                return;
            }
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, $"无法完成窗口唤回或重启。请稍后重新打开 Nikkiward。\n{ex.Message}",
                "Nikkiward", 0x10);
            Exit();
            return;
        }

        // Only the instance that owns the window may hand a stale Game Bar
        // takeover back; a redirected second launch never touches it.
        GamepadController.RecoverGameBarGuideButton();

        Settings = new WindowSettingsSession(new JsonUserSettingsStore());
        UserSettings? settings = null;
        try
        {
            settings = await Settings.LoadAsync();
        }
        catch (UserSettingsStoreException)
        {
            // The page reports unreadable settings through its existing status UI.
        }

        MainWindow = new MainWindow();

        // The Guide button is taken from the Xbox Game Bar through a registry
        // value, so it has to be handed back before the process goes away.
        MainWindow.Closed += (_, _) =>
        {
            GamepadController.Shutdown();
            current.UnregisterKey();
        };
        if (settings is not null)
        {
            MainWindow.ApplyCloseBehavior(settings.General.CloseWindowBehavior);
        }

        MainWindow.RestorePlacement(settings?.WindowPlacement);
        if (showWhenReady)
        {
            MainWindow.ShowWindowCore();
        }
        else
        {
            MainWindow.Activate();
        }
    }

    private static async Task<bool> OwnsWindowAsync(AppInstance current)
    {
        var predecessor = InstanceActivation.GetPredecessor(Environment.GetCommandLineArgs()[1..]);
        var timer = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(15);
        while (timer.Elapsed < timeout)
        {
            var owner = AppInstance.FindOrRegisterForKey(InstanceKey);
            switch (InstanceActivation.Decide(owner?.IsCurrent == true, owner?.ProcessId, predecessor))
            {
                case InstanceAction.OpenWindow:
                    return true;
                case InstanceAction.Redirect:
                    var remaining = timeout - timer.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    AllowSetForegroundWindow(owner!.ProcessId);
                    try
                    {
                        await owner.RedirectActivationToAsync(current.GetActivatedEventArgs()).AsTask()
                            .WaitAsync(remaining);
                        return false;
                    }
                    catch (COMException) when (timer.Elapsed < timeout)
                    {
                        // The owner can disappear between registration and redirection.
                    }
                    break;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("原实例未能及时响应。");
    }

    internal static void StartSuccessor()
    {
        var start = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath ?? throw new InvalidOperationException("无法定位 Nikkiward 程序。"),
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            ArgumentList = { InstanceActivation.RestartArgument, Environment.ProcessId.ToString(CultureInfo.InvariantCulture) },
        };
        using var successor = Process.Start(start) ?? throw new InvalidOperationException("无法启动新的 Nikkiward 实例。");
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
