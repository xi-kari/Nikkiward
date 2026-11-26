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

        string? unavailable;
        try
        {
            unavailable = ApplicationDataPaths.UnavailableRoot;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            MessageBox(IntPtr.Zero, "无法从注册表读取数据目录。请检查当前用户的注册表访问权限后重新打开 Nikkiward。",
                "Nikkiward · 无法读取数据目录", 0x10);
            Exit();
            return;
        }

        if (unavailable is not null)
        {
            var useDefault = MessageBox(IntPtr.Zero,
                $"选定的数据目录不可用：{unavailable}\n\n要在本次启动中使用默认目录吗？\n{ApplicationDataPaths.DefaultRoot}\n" +
                "选择“是”将载入并保存该目录的数据。选择“否”退出，恢复原目录或重新连接磁盘后再打开。",
                "Nikkiward · 数据目录不可用", 0x04 | 0x30 | 0x100);
            if (useDefault != 6) // IDYES
            {
                Exit();
                return;
            }

            ApplicationDataPaths.UseDefaultForSession();
        }

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
        MainWindow.RestorePlacement(settings?.WindowPlacement);
        MainWindow.Open(settings?.General.CloseWindowBehavior ?? CloseWindowBehavior.Exit, showWhenReady);
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
