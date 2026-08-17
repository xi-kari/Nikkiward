using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
    private readonly SemaphoreSlim _appearanceSaveGate = new(1, 1);

    private static async Task<string?> PickFolderAsync(
        string commitButtonText,
        PickerLocationId suggestedStartLocation)
    {
        var picker = new FolderPicker
        {
            CommitButtonText = commitButtonText,
            SuggestedStartLocation = suggestedStartLocation,
        };
        picker.FileTypeFilter.Add("*");

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return string.IsNullOrWhiteSpace(folder?.Path) ? null : folder.Path;
    }

    private async void OnChooseBackgroundClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                ViewMode = PickerViewMode.Thumbnail,
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".bmp");
            foreach (var extension in MotionSourceRules.SupportedExtensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(file.Path))
            {
                await TryShowDialogAsync(
                    "无法使用所选图片",
                    "所选项目没有本地文件系统路径。请选择本地图片或视频文件。");
                return;
            }

            if (MotionSourceRules.IsSupportedExtension(file.FileType))
            {
                await ImportMotionBackgroundAsync(
                    file,
                    _lifetimeCancellation?.Token ?? CancellationToken.None);
                return;
            }

            var backdropToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
            await _appearanceSaveGate.WaitAsync(backdropToken);
            try
            {
                var previousAppearance = ViewModel.AppearanceSettings;
                var previousSource = _currentBackgroundSource;
                var previousStatus = _backgroundStatusText;
                SetCurrentBackgroundSource(file.Path);
                _backgroundStatusText = $"当前背景：{file.Name}";
                ApplySettingsPageState(_hostedSettingsPage);
                _hostedLaunchSettingsPage?.ApplyBackgroundPreview(
                    LauncherBackground.Source);

            // On-art ink polarity comes from the analysis, so an unanalysable
            // wallpaper would keep the previous artwork's polarity over the new
            // pixels: a bright image under off-white ink is unreadable. Revert to
            // the default artwork, whose polarity is already published. A
            // cancelled analysis means the page is going away, so it is not a
            // failure to recover from.
                var analyzed = await AnalyzeBackdropAsync(file.Path, backdropToken);
                if (!analyzed && !backdropToken.IsCancellationRequested)
                {
                    await RestoreBackgroundVisualAsync(
                        previousSource,
                        previousStatus,
                        backdropToken);
                    await TryShowDialogAsync(
                        "无法使用所选图片",
                        "无法读取该图片的颜色信息，已恢复此前的背景。请选择其他本地 PNG、JPG、WebP 或 BMP 图片。");
                    return;
                }

                var sources = previousAppearance.Background.CarouselSources
                    .Append(file.Path)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var updated = previousAppearance with
                {
                    Background = previousAppearance.Background with
                    {
                        SelectedSource = file.Path,
                        CarouselSources = sources,
                        MotionEnabled = false,
                    },
                };
                try
                {
                    await ViewModel.SaveAppearanceSettingsAsync(updated, backdropToken);
                }
                catch
                {
                    await RestoreBackgroundVisualAsync(
                        previousSource,
                        previousStatus,
                        backdropToken);
                    ApplyAppearanceSettings(previousAppearance);
                    throw;
                }

                ApplyAppearanceSettings(ViewModel.AppearanceSettings);
                ApplySettingsPageState(_hostedSettingsPage);
            }
            finally
            {
                _appearanceSaveGate.Release();
            }
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"背景载入失败：{ex.GetType().Name}: {ex.Message}");
            await TryShowDialogAsync("背景载入失败", ViewModel.LastErrorText);
        }
    }

    private async void OnChooseMotionBackgroundClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
                ViewMode = PickerViewMode.Thumbnail,
            };
            picker.FileTypeFilter.Add("*");

            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(file.Path))
            {
                await TryShowDialogAsync(
                    "无法使用所选视频",
                    "所选项目没有本地文件系统路径。请选择本地视频文件。");
                return;
            }

            await ImportMotionBackgroundAsync(
                file,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"视频背景载入失败：{ex.GetType().Name}: {ex.Message}");
            await TryShowDialogAsync("视频背景载入失败", ViewModel.LastErrorText);
        }
    }

    private async void OnResetBackgroundClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var appearance = ViewModel.AppearanceSettings;
            await SaveAndApplyAppearanceAsync(
                appearance with
                {
                    Background = appearance.Background with
                    {
                        SelectedSource = null,
                        MotionEnabled = false,
                    },
                },
                restoreDefaultBackground: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"背景设置保存失败：{ex.GetType().Name}: {ex.Message}");
            ApplySettingsPageState(_hostedSettingsPage);
        }
    }

    /// <summary>
    /// Restores the shipped artwork and re-derives the backdrop from it. Also the
    /// recovery path when a user-chosen wallpaper cannot be analysed.
    /// </summary>
    private void RestoreDefaultBackground()
    {
        SetCurrentBackgroundSource(DefaultBackgroundSource);
        _backgroundStatusText = "使用 Nikkiward 内置背景；可随时选择本地图片覆盖。";
        ApplySettingsPageState(_hostedSettingsPage);
        _hostedLaunchSettingsPage?.ApplyBackgroundPreview(
            LauncherBackground.Source);
        _ = AnalyzeBackdropAsync(
            DefaultBackgroundSource,
            _lifetimeCancellation?.Token ?? CancellationToken.None);
    }

    private async Task RestoreBackgroundVisualAsync(
        string source,
        string status,
        CancellationToken cancellationToken)
    {
        var canRestore = source.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(source);
        if (canRestore)
        {
            SetCurrentBackgroundSource(source);
            if (await AnalyzeBackdropAsync(source, cancellationToken))
            {
                _backgroundStatusText = status;
                ApplySettingsPageState(_hostedSettingsPage);
                _hostedLaunchSettingsPage?.ApplyBackgroundPreview(
                    LauncherBackground.Source);
                return;
            }
        }

        RestoreDefaultBackground();
    }

    /// <summary>
    /// Re-derives accent, scrim strength and on-art ink polarity for
    /// <paramref name="source"/>. The service applies all three atomically on the
    /// UI thread. Returns false when the artwork could not be analysed, in which
    /// case the previous values stay in effect and the caller owns recovery;
    /// cancellation reports false without being treated as an error.
    /// </summary>
    private Task<bool> AnalyzeBackdropAsync(
        string source,
        CancellationToken cancellationToken) =>
        AnalyzeBackdropAsync(
            BackgroundSourceDescriptor.Still(source),
            cancellationToken);

    private async Task<bool> AnalyzeBackdropAsync(
        BackgroundSourceDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        try
        {
            var analysis = await _backdrop.ApplyAsync(descriptor, cancellationToken);
            if (analysis is null)
            {
                return false;
            }

            if (ViewModel.ThemeMode == ThemeMode.FollowArtwork)
            {
                ApplyThemeMode(ThemeMode.FollowArtwork);
            }
            else
            {
                ApplyAccentForActualTheme();
            }

            App.MainWindow.ApplyCaptionButtonPolarity(analysis.PreferredTheme);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"背景取色失败：{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private async Task ImportMotionBackgroundAsync(
        StorageFile file,
        CancellationToken cancellationToken)
    {
        await _appearanceSaveGate.WaitAsync(cancellationToken);
        try
        {
            var previousAppearance = ViewModel.AppearanceSettings;
            var previousSource = _currentBackgroundSource;
            var previousStatus = _backgroundStatusText;
            var imported = await _motionBackgroundImporter.ImportAsync(
                file.Path,
                cancellationToken);
            if (!imported.Validation.IsUsable || imported.ImportedPath is null)
            {
                await TryShowDialogAsync(
                    "无法使用所选视频",
                    imported.Validation.RejectReason ?? "视频背景校验失败。");
                return;
            }

            var descriptor = BackgroundSourceDescriptor.Motion(
                imported.ImportedPath,
                file.Name);
            if (!await LauncherBackground.ShowMotionAsync(imported.ImportedPath, cancellationToken))
            {
                await RestoreConfiguredBackgroundAsync(
                    previousAppearance,
                    previousSource,
                    previousStatus,
                    cancellationToken);
                await TryShowDialogAsync(
                    "无法使用所选视频",
                    "视频播放管线初始化失败，已恢复此前的背景。");
                return;
            }

            await AnalyzeMotionOrFallbackAsync(descriptor, cancellationToken);

            _backgroundStatusText = $"视频背景：{file.Name}";
            var updated = previousAppearance with
            {
                Background = previousAppearance.Background with
                {
                    MotionEnabled = true,
                    MotionSource = imported.ImportedPath,
                    CarouselEnabled = false,
                },
            };
            try
            {
                await ViewModel.SaveAppearanceSettingsAsync(updated, cancellationToken);
                ApplyAppearanceSettings(ViewModel.AppearanceSettings);
                ApplySettingsPageState(_hostedSettingsPage);
                _hostedLaunchSettingsPage?.ApplyBackgroundPreview(LauncherBackground.Source);
            }
            catch
            {
                await RestoreConfiguredBackgroundAsync(
                    previousAppearance,
                    previousSource,
                    previousStatus,
                    cancellationToken);
                throw;
            }
        }
        finally
        {
            _appearanceSaveGate.Release();
        }
    }

    private async Task RestoreConfiguredBackgroundAsync(
        AppearanceSettings appearance,
        string previousSource,
        string previousStatus,
        CancellationToken cancellationToken)
    {
        ApplyAppearanceSettings(appearance);
        var motionSource = appearance.Background.MotionSource;
        if (appearance.Background.MotionEnabled &&
            !string.IsNullOrWhiteSpace(motionSource) &&
            File.Exists(motionSource))
        {
            var descriptor = BackgroundSourceDescriptor.Motion(motionSource);
            if (await LauncherBackground.ShowMotionAsync(motionSource, cancellationToken))
            {
                await AnalyzeMotionOrFallbackAsync(descriptor, cancellationToken);
                _backgroundStatusText = previousStatus;
                ApplySettingsPageState(_hostedSettingsPage);
                return;
            }
        }

        LauncherBackground.ShowStill();
        await RestoreBackgroundVisualAsync(
            previousSource,
            previousStatus,
            cancellationToken);
    }

    private void ApplyThemeMode(ThemeMode themeMode)
    {
        RequestedTheme = themeMode switch
        {
            ThemeMode.WarmLight => ElementTheme.Light,
            ThemeMode.WarmDark => ElementTheme.Dark,
            _ => _backdrop.IsReady && _backdrop.PreferredTheme == ArtPreferredTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light,
        };

        ApplyAccentForActualTheme();
        RefreshOnArtSurfaceRegistration();
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (!_refreshingThemeResources)
        {
            ApplyAccentForActualTheme();
        }
    }

    private void ApplyAccentForActualTheme()
    {
        if (ViewModel.AppearanceSettings.AccentMode == AccentColorMode.Adaptive)
        {
            _backdrop.ApplyThemeAccent(
                ActualTheme == ElementTheme.Dark
                    ? ArtPreferredTheme.Dark
                    : ArtPreferredTheme.Light);
            return;
        }

        ApplyFixedAccent(ViewModel.AppearanceSettings);
    }

    private void InitializeThemeModeUi()
    {
        ApplySettingsPageState(_hostedSettingsPage);
    }

    private async void OnSettingsAppearanceChanged(
        object? sender,
        AppearanceSettingsChangedEventArgs e)
    {
        try
        {
            await SaveAndApplyAppearanceAsync(e.Settings);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"外观设置保存失败：{ex.GetType().Name}: {ex.Message}");
            InitializeThemeModeUi();
        }
    }

    private async Task SaveAndApplyAppearanceAsync(
        AppearanceSettings settings,
        bool restoreDefaultBackground = false)
    {
        var cancellationToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
        await _appearanceSaveGate.WaitAsync(cancellationToken);
        try
        {
            var previous = ViewModel.AppearanceSettings;
            ApplyAppearanceSettings(settings);
            try
            {
                await ViewModel.SaveAppearanceSettingsAsync(
                    settings,
                    cancellationToken);
            }
            catch
            {
                ApplyAppearanceSettings(previous);
                await ReconcileMotionRuntimeAsync(settings, previous, cancellationToken);
                throw;
            }

            await ReconcileMotionRuntimeAsync(
                previous,
                ViewModel.AppearanceSettings,
                cancellationToken);
            if (restoreDefaultBackground)
            {
                RestoreDefaultBackground();
            }

            ApplySettingsPageState(_hostedSettingsPage);
        }
        finally
        {
            _appearanceSaveGate.Release();
        }
    }

    private async Task<bool> TryActivateConfiguredMotionAsync(
        AppearanceSettings settings,
        CancellationToken cancellationToken)
    {
        var source = settings.Background.MotionSource;
        if (!settings.Background.MotionEnabled ||
            string.IsNullOrWhiteSpace(source) ||
            !File.Exists(source))
        {
            return false;
        }

        if (LauncherBackground.IsMotionActive &&
            string.Equals(
                LauncherBackground.MotionSource,
                source,
                StringComparison.OrdinalIgnoreCase))
        {
            _backgroundStatusText = $"视频背景：{Path.GetFileName(source)}";
            return true;
        }

        var descriptor = BackgroundSourceDescriptor.Motion(
            source,
            Path.GetFileName(source));
        if (!await LauncherBackground.ShowMotionAsync(source, cancellationToken))
        {
            return false;
        }

        await AnalyzeMotionOrFallbackAsync(descriptor, cancellationToken);

        _backgroundStatusText = $"视频背景：{Path.GetFileName(source)}";
        ApplySettingsPageState(_hostedSettingsPage);
        _hostedLaunchSettingsPage?.ApplyBackgroundPreview(LauncherBackground.Source);
        return true;
    }

    private async Task AnalyzeMotionOrFallbackAsync(
        BackgroundSourceDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!await AnalyzeBackdropAsync(descriptor, cancellationToken))
        {
            await AnalyzeBackdropAsync(DefaultBackgroundSource, cancellationToken);
        }
    }

    private async Task RestoreStaticBackgroundAsync(
        AppearanceSettings settings,
        CancellationToken cancellationToken,
        string? fallbackStatus = null)
    {
        LauncherBackground.ShowStill();
        var projection = AppearanceProjector.ProjectBackground(
            settings.Background,
            settings.Background.CarouselSources.Where(File.Exists),
            DefaultBackgroundSource);
        var source = projection.Source;
        SetCurrentBackgroundSource(source);
        _backgroundStatusText = fallbackStatus ??
            (projection.UsesFallback
                ? "使用 Nikkiward 内置背景；可随时选择本地图片覆盖。"
                : $"当前背景：{Path.GetFileName(source)}");

        if (!await AnalyzeBackdropAsync(source, cancellationToken) &&
            !cancellationToken.IsCancellationRequested)
        {
            SetCurrentBackgroundSource(DefaultBackgroundSource);
            _backgroundStatusText = "背景文件不可用，已恢复 Nikkiward 内置背景。";
            await AnalyzeBackdropAsync(DefaultBackgroundSource, cancellationToken);
        }

        ApplySettingsPageState(_hostedSettingsPage);
        _hostedLaunchSettingsPage?.ApplyBackgroundPreview(LauncherBackground.Source);
    }

    private async Task ReconcileMotionRuntimeAsync(
        AppearanceSettings previous,
        AppearanceSettings current,
        CancellationToken cancellationToken)
    {
        var motionSelectionChanged =
            previous.Background.MotionEnabled != current.Background.MotionEnabled ||
            !string.Equals(
                previous.Background.MotionSource,
                current.Background.MotionSource,
                StringComparison.OrdinalIgnoreCase);
        if (!motionSelectionChanged)
        {
            return;
        }

        if (current.Background.MotionEnabled &&
            await TryActivateConfiguredMotionAsync(current, cancellationToken))
        {
            return;
        }

        await RestoreStaticBackgroundAsync(
            current,
            cancellationToken,
            current.Background.MotionEnabled
                ? "视频背景不可用，已回退到静态背景。"
                : null);
    }

}
