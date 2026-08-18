using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Nikkiward.Features.Background;
using Nikkiward.Features.Diagnostics;
using Nikkiward.Features.Gallery;
using Nikkiward.Features.GamepadControl;
using Nikkiward.Features.Journal;
using Nikkiward.Features.Launcher;
using Nikkiward.Features.Profile;
using Nikkiward.Features.Settings;
using Nikkiward.Features.Shell;
using Nikkiward.Features.Wish;
using Nikkiward.Models;
using Nikkiward.Pages;
using Nikkiward.Services;
using Nikkiward.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace Nikkiward;

public sealed partial class MainPage
{
    private async void OnNavigationItemInvoked(
        NavigationView sender,
        NavigationViewItemInvokedEventArgs args)
    {
        var isSettingsInvoked = args.IsSettingsInvoked;
        var item = args.InvokedItemContainer as NavigationViewItem;
        var tag = item?.Tag as string;
        if (!isSettingsInvoked && (item is null || tag is null))
        {
            return;
        }

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

            await ExecuteShellNavigationAsync(isSettingsInvoked, item, tag);
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

    private async Task ExecuteShellNavigationAsync(
        bool isSettingsInvoked,
        NavigationViewItem? item,
        string? tag)
    {
        if (_launchSettingsOpen)
        {
            CloseLaunchSettings();
        }

        if (isSettingsInvoked)
        {
            if (ContentFrame.Visibility == Visibility.Visible &&
                ContentFrame.Content is SettingsPage)
            {
                CloseDetails();
                SetShellNavigationSelection(LauncherNavigationItem);
            }
            else
            {
                ShowSettingsPage();
                SetShellNavigationSelection(ShellNavigation.SettingsItem);
            }
            return;
        }

        CloseProfileOverlay();
        object selectedItem = item!;
        switch (tag)
        {
            case "launcher":
                ShowLauncher();
                selectedItem = LauncherNavigationItem;
                break;
            case "library":
                if (ContentFrame.Visibility == Visibility.Visible &&
                    ContentFrame.Content is JournalPage)
                {
                    CloseDetails();
                    selectedItem = LauncherNavigationItem;
                }
                else
                {
                    ShowLibrary();
                }
                break;
            case "gallery":
                if (ContentFrame.Visibility == Visibility.Visible &&
                    ContentFrame.Content is GalleryPage allGalleryPage &&
                    allGalleryPage.ViewModel.ViewMode == GalleryViewMode.All)
                {
                    CloseDetails();
                    selectedItem = LauncherNavigationItem;
                }
                else
                {
                    await ShowGalleryAsync(viewMode: GalleryViewMode.All);
                }
                break;
            case "gallery-favorites":
                if (ContentFrame.Visibility == Visibility.Visible &&
                    ContentFrame.Content is GalleryPage favoritesGalleryPage &&
                    favoritesGalleryPage.ViewModel.ViewMode == GalleryViewMode.Favorites)
                {
                    CloseDetails();
                    selectedItem = LauncherNavigationItem;
                }
                else
                {
                    await ShowGalleryAsync(viewMode: GalleryViewMode.Favorites);
                }
                break;
            case "photo-plugin":
                if (ContentFrame.Visibility == Visibility.Visible &&
                    ContentFrame.Content is PhotoPluginPage)
                {
                    CloseDetails();
                    selectedItem = LauncherNavigationItem;
                }
                else
                {
                    ShowPhotoPlugin();
                }
                break;
            case "resonance":
                if (ContentFrame.Visibility == Visibility.Visible &&
                    ContentFrame.Content is WishPage)
                {
                    CloseDetails();
                    selectedItem = LauncherNavigationItem;
                }
                else
                {
                    ShowResonance();
                }
                break;
            default:
                return;
        }

        SetShellNavigationSelection(selectedItem);
    }

    private void SetShellNavigationSelection(object selectedItem)
    {
        ShellNavigation.SelectedItem = selectedItem;
    }

    private void CloseDetails()
    {
        CloseProfileOverlay();
        HideLibrary();
        HideGallery();
        HideResonance();
        HidePhotoPlugin();
        ShowLauncher();
    }

    private void ShowLauncher()
    {
        CloseProfileOverlay();
        HideLibrary();
        HideGallery();
        HideResonance();
        HidePhotoPlugin();
        HideStatusDrawer();
        ContentFrame.Visibility = Visibility.Visible;
        SetShellNavigationSelection(LauncherNavigationItem);
        if (ContentFrame.Content is LauncherPage page)
        {
            page.ApplyJournalDuration(
                _journalDurationText,
                _journalDurationDetailText);
            SyncLauncherChrome();
            return;
        }

        ContentFrame.Navigate(
            typeof(LauncherPage),
            new LauncherNavigationContext(ViewModel),
            AppearanceRuntimeValues.CreateNavigationTransitionInfo());
    }
}
