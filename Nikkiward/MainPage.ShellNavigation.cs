using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Nikkiward.Features.Background;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.GamepadControl;
using Nikkiward.Features.Journal;
using Nikkiward.Features.Launcher;
using Nikkiward.Features.Settings;
using Nikkiward.Features.Shell;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private async void OnNavigationItemInvoked(object? sender, string tag)
    {
#if DEBUG
        if (HomePreview.Active) return;
#endif
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(
            ref _shellNavigationDebounceCancellation,
            cancellation);
        previous?.Cancel();
        previous?.Dispose();
        try
        {
            await Task.Delay(120, cancellation.Token);
            if (!ReferenceEquals(_shellNavigationDebounceCancellation, cancellation))
            {
                return;
            }

            await ExecuteShellNavigationAsync(tag);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _shellNavigationDebounceCancellation,
                        null,
                        cancellation),
                    cancellation))
            {
                cancellation.Dispose();
            }
        }
    }

    private Task ExecuteShellNavigationAsync(string tag)
    {
        switch (tag)
        {
            case "launcher": ShowLauncher(); break;
            case "library": ShowWebPage("journal"); break;
            case "gallery": ShowWebPage("gallery"); break;
            case "gallery-favorites": ShowWebPage("favorites"); break;
            case "resonance": ShowWebPage("wish"); break;
            case "settings": ShowSettingsPage(); break;
            default: return Task.CompletedTask;
        }
        return Task.CompletedTask;
    }

    private void ShowLauncher()
    {
        ContentFrame.Visibility = Visibility.Visible;
        if (ContentFrame.Content is LauncherPage)
        {
            SyncLauncherChrome();
            return;
        }

        ContentFrame.Navigate(
            typeof(LauncherPage),
            new LauncherNavigationContext(ViewModel),
            AppearanceRuntimeValues.CreateNavigationTransitionInfo());
    }
}
