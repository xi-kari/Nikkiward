using Microsoft.UI.Xaml;
using Nikkiward.Features.GamepadControl;

namespace Nikkiward;

public partial class App : Application
{
    public static MainWindow MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();

        // The Guide button is taken from the Xbox Game Bar through a registry
        // value, so it has to be handed back before the process goes away.
        MainWindow.Closed += (_, _) => GamepadController.Shutdown();
        MainWindow.Activate();
    }
}
