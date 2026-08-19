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

    private async void OnChooseBackgroundClicked(object sender, RoutedEventArgs e) =>
        await ChooseWallpaperAsync(null);

    private async Task ChooseWallpaperAsync(WallpaperImportMode? requestedMode)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = requestedMode == WallpaperImportMode.MotionBackdrop
                    ? PickerLocationId.VideosLibrary
                    : PickerLocationId.PicturesLibrary,
                ViewMode = PickerViewMode.Thumbnail,
            };
            foreach (var extension in WallpaperSourceRules.StillExtensions
                .Concat(MotionSourceRules.SupportedExtensions)
                .Concat(WallpaperSourceRules.PackageExtensions)
                .Append(".json")
                .Append(".html")
                .Distinct(StringComparer.OrdinalIgnoreCase))
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
                    "无法使用所选壁纸",
                    "所选项目没有本地文件系统路径。请选择本地图片、视频或 Wallpaper Engine 文件。");
                return;
            }

            var mode = requestedMode ?? WallpaperSourceRules.InferMode(file.Path);
            await ImportWallpaperAsync(
                file.Path,
                file.Name,
                mode,
                _lifetimeCancellation?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ViewModel.ReportUiError($"壁纸载入失败：{ex.GetType().Name}: {ex.Message}");
            await TryShowDialogAsync("壁纸载入失败", ViewModel.LastErrorText);
        }
    }

    private async Task ImportWallpaperAsync(
        string sourcePath,
        string displayName,
        WallpaperImportMode mode,
        CancellationToken cancellationToken)
    {
        var resolution = WallpaperSourceRules.Resolve(sourcePath, mode);
        if (!resolution.IsUsable || string.IsNullOrWhiteSpace(resolution.SourcePath))
        {
            await TryShowDialogAsync(
                "无法使用所选壁纸",
                resolution.RejectReason ?? "壁纸资源无法识别。");
            return;
        }

        if (resolution.Kind == WallpaperResolvedKind.WallpaperEnginePackage)
        {
            await ImportWallpaperEnginePackageAsync(
                resolution,
                displayName,
                mode,
                cancellationToken);
            return;
        }

        if (resolution.Kind == WallpaperResolvedKind.Motion &&
            mode == WallpaperImportMode.HolographicCard)
        {
            var frame = await _wallpaperAssetImporter.ImportMotionFrameAsync(
                resolution.SourcePath,
                cancellationToken);
            if (!frame.Validation.IsUsable || frame.ImportedPath is null)
            {
                await TryShowDialogAsync(
                    "无法构建光栅卡片",
                    frame.Validation.RejectReason ?? "无法从动态壁纸生成预览帧。");
                return;
            }

            await CommitStillBackgroundAsync(
                frame.ImportedPath,
                $"{displayName}（预览帧）",
                cancellationToken);
            return;
        }

        if (resolution.Kind == WallpaperResolvedKind.Motion)
        {
            await ImportMotionBackgroundAsync(
                resolution.SourcePath,
                displayName,
                cancellationToken);
            return;
        }

        var imported = await _wallpaperAssetImporter.ImportStillAsync(
            resolution.SourcePath,
            cancellationToken);
        if (!imported.Validation.IsUsable || imported.ImportedPath is null)
        {
            await TryShowDialogAsync(
                "无法构建光栅卡片",
                imported.Validation.RejectReason ?? "图片资源无法导入。");
            return;
        }

        await CommitStillBackgroundAsync(
            imported.ImportedPath,
            displayName,
            cancellationToken);
    }

    private async Task CommitStillBackgroundAsync(
        string importedPath,
        string displayName,
        CancellationToken cancellationToken)
    {
        await _appearanceSaveGate.WaitAsync(cancellationToken);
        try
        {
            var previousAppearance = ViewModel.AppearanceSettings;
            var previousSource = _currentBackgroundSource;
            var previousStatus = _backgroundStatusText;
            SetCurrentBackgroundSource(importedPath);
            _backgroundStatusText = $"当前背景：{displayName}";
            ApplySettingsPageState(_hostedSettingsPage);
            _hostedLaunchSettingsPage?.ApplyBackgroundPreview(LauncherBackground.Source);

            var analyzed = await AnalyzeBackdropAsync(importedPath, cancellationToken);
            if (!analyzed && !cancellationToken.IsCancellationRequested)
            {
                await RestoreBackgroundVisualAsync(
                    previousSource,
                    previousStatus,
                    cancellationToken);
                await TryShowDialogAsync(
                    "无法构建光栅卡片",
                    "无法读取该壁纸的颜色信息，已恢复此前的背景。请选择其他可解码的图片或预览资源。");
                return;
            }

            var sources = previousAppearance.Background.CarouselSources
                .Append(importedPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var updated = previousAppearance with
            {
                Background = previousAppearance.Background with
                {
                    SelectedSource = importedPath,
                    CarouselSources = sources,
                    CarouselEnabled = false,
                    MotionEnabled = false,
                    MotionSource = null,
                    WallpaperEnginePresentation = WallpaperEnginePresentation.None,
                    WallpaperEnginePackageSource = null,
                },
            };
            try
            {
                await ViewModel.SaveAppearanceSettingsAsync(updated, cancellationToken);
            }
            catch
            {
                await RestoreBackgroundVisualAsync(
                    previousSource,
                    previousStatus,
                    cancellationToken);
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

    private async void OnResetBackgroundClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var appearance = ViewModel.AppearanceSettings;
            await SaveAndApplyAppearanceAsync(
                appearance with
                {
                    LauncherCapsuleStyle = LauncherCapsuleStyle.Ocean,
                    ThemeMode = ThemeMode.WarmDark,
                    Background = new BackgroundArtSettings(),
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

            ApplyOnArtScrimTheme();
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

    private async Task ImportWallpaperEnginePackageAsync(
        WallpaperImportResolution resolution,
        string displayName,
        WallpaperImportMode mode,
        CancellationToken cancellationToken)
    {
        await _appearanceSaveGate.WaitAsync(cancellationToken);
        try
        {
            var previousAppearance = ViewModel.AppearanceSettings;
            var previousSource = _currentBackgroundSource;
            var previousStatus = _backgroundStatusText;
            var importedPackage = await _wallpaperAssetImporter.ImportPackageAsync(
                resolution.SourcePath!,
                cancellationToken);
            if (!importedPackage.Validation.IsUsable || importedPackage.ImportedPath is null)
            {
                await TryShowDialogAsync(
                    "无法导入 Wallpaper Engine 场景",
                    importedPackage.Validation.RejectReason ?? "Wallpaper .pkg 文件复制失败。");
                return;
            }

            var previewPath = DefaultBackgroundSource;
            if (!string.IsNullOrWhiteSpace(resolution.PreviewPath))
            {
                var importedPreview = await _wallpaperAssetImporter.ImportStillAsync(
                    resolution.PreviewPath,
                    cancellationToken);
                if (importedPreview.Validation.IsUsable && importedPreview.ImportedPath is not null)
                {
                    previewPath = importedPreview.ImportedPath;
                }
            }

            SetCurrentBackgroundSource(previewPath);
            _backgroundStatusText = mode == WallpaperImportMode.HolographicCard
                ? $"Wallpaper Engine 卡片：{displayName}"
                : $"Wallpaper Engine 动态背景：{displayName}";
            ApplySettingsPageState(_hostedSettingsPage);
            _hostedLaunchSettingsPage?.ApplyBackgroundPreview(LauncherBackground.Source);
            if (!await AnalyzeBackdropAsync(previewPath, cancellationToken) &&
                !cancellationToken.IsCancellationRequested)
            {
                await AnalyzeBackdropAsync(DefaultBackgroundSource, cancellationToken);
            }

            var presentation = mode == WallpaperImportMode.HolographicCard
                ? WallpaperEnginePresentation.HolographicCard
                : WallpaperEnginePresentation.MotionBackdrop;
            var runtime = await LauncherBackground.ShowWallpaperEngineAsync(
                importedPackage.ImportedPath,
                presentation,
                cancellationToken);
            if (!runtime.Succeeded)
            {
                await RestoreConfiguredBackgroundAsync(
                    previousAppearance,
                    previousSource,
                    previousStatus,
                    cancellationToken);
                await TryShowDialogAsync(
                    "无法运行 Wallpaper Engine 场景",
                    runtime.ErrorMessage ?? "Wallpaper Engine 场景初始化失败，已恢复此前的背景。");
                return;
            }

            var sources = mode == WallpaperImportMode.HolographicCard
                ? previousAppearance.Background.CarouselSources
                    .Append(previewPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : previousAppearance.Background.CarouselSources;
            var updated = previousAppearance with
            {
                Background = previousAppearance.Background with
                {
                    SelectedSource = previewPath,
                    CarouselSources = sources,
                    CarouselEnabled = false,
                    MotionEnabled = mode == WallpaperImportMode.MotionBackdrop,
                    MotionSource = mode == WallpaperImportMode.MotionBackdrop
                        ? importedPackage.ImportedPath
                        : null,
                    WallpaperEnginePresentation = presentation,
                    WallpaperEnginePackageSource = importedPackage.ImportedPath,
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

    private async Task ImportMotionBackgroundAsync(
        string sourcePath,
        string displayName,
        CancellationToken cancellationToken)
    {
        await _appearanceSaveGate.WaitAsync(cancellationToken);
        try
        {
            var previousAppearance = ViewModel.AppearanceSettings;
            var previousSource = _currentBackgroundSource;
            var previousStatus = _backgroundStatusText;
            var imported = await _motionBackgroundImporter.ImportAsync(
                sourcePath,
                cancellationToken);
            if (!imported.Validation.IsUsable || imported.ImportedPath is null)
            {
                await TryShowDialogAsync(
                    "无法使用所选动态壁纸",
                    imported.Validation.RejectReason ?? "动态壁纸校验失败。");
                return;
            }

            var descriptor = BackgroundSourceDescriptor.Motion(
                imported.ImportedPath,
                displayName);
            if (!await LauncherBackground.ShowMotionAsync(imported.ImportedPath, cancellationToken))
            {
                await RestoreConfiguredBackgroundAsync(
                    previousAppearance,
                    previousSource,
                    previousStatus,
                    cancellationToken);
                await TryShowDialogAsync(
                    "无法使用所选动态壁纸",
                    "动态壁纸播放管线初始化失败，已恢复此前的背景。");
                return;
            }

            await AnalyzeMotionOrFallbackAsync(descriptor, cancellationToken);

            _backgroundStatusText = $"动态壁纸：{displayName}";
            var updated = previousAppearance with
            {
                Background = previousAppearance.Background with
                {
                    MotionEnabled = true,
                    MotionSource = imported.ImportedPath,
                    CarouselEnabled = false,
                    WallpaperEnginePresentation = WallpaperEnginePresentation.None,
                    WallpaperEnginePackageSource = null,
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
        CancellationToken cancellationToken,
        bool appearanceGateHeld = true)
    {
        ApplyAppearanceSettings(appearance);
        if ((appearance.Background.WallpaperEnginePresentation is
                WallpaperEnginePresentation.HolographicCard or
                WallpaperEnginePresentation.MotionBackdrop) &&
            !string.IsNullOrWhiteSpace(appearance.Background.WallpaperEnginePackageSource) &&
            File.Exists(appearance.Background.WallpaperEnginePackageSource))
        {
            var importedPackage = await _wallpaperAssetImporter.ImportPackageAsync(
                appearance.Background.WallpaperEnginePackageSource,
                cancellationToken);
            if (importedPackage.Validation.IsUsable &&
                !string.IsNullOrWhiteSpace(importedPackage.ImportedPath))
            {
                SetCurrentBackgroundSource(previousSource);
                if (await TryActivateConfiguredWallpaperEngineAsync(
                        appearance,
                        appearance.Background.WallpaperEnginePresentation,
                        cancellationToken,
                        appearanceGateHeld))
                {
                    _backgroundStatusText = previousStatus;
                    ApplySettingsPageState(_hostedSettingsPage);
                    return;
                }
            }
        }

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
        var artworkTheme = _backdrop.IsReady
            ? _backdrop.PreferredTheme
            : DefaultBackgroundPreferredTheme;
        RequestedTheme = themeMode switch
        {
            ThemeMode.WarmLight => ElementTheme.Light,
            ThemeMode.WarmDark => ElementTheme.Dark,
            _ => artworkTheme == ArtPreferredTheme.Dark
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
            await SaveAndApplyAppearanceAsync(
                e.Settings,
                forceStaticBackgroundReload: e.ForceStaticBackgroundReload);
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
        bool restoreDefaultBackground = false,
        bool forceStaticBackgroundReload = false)
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
                await ReconcileMotionRuntimeAsync(
                    settings,
                    previous,
                    cancellationToken,
                    forceStaticBackgroundReload: true);
                throw;
            }

            await ReconcileMotionRuntimeAsync(
                previous,
                ViewModel.AppearanceSettings,
                cancellationToken,
                forceStaticBackgroundReload);
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
        CancellationToken cancellationToken,
        bool appearanceGateHeld = false)
    {
        if (settings.Background.WallpaperEnginePresentation ==
            WallpaperEnginePresentation.MotionBackdrop)
        {
            if (!settings.Background.MotionEnabled)
            {
                return false;
            }

            return await TryActivateConfiguredWallpaperEngineAsync(
                settings,
                WallpaperEnginePresentation.MotionBackdrop,
                cancellationToken,
                appearanceGateHeld);
        }

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

    private async Task<bool> TryActivateConfiguredWallpaperEngineAsync(
        AppearanceSettings settings,
        WallpaperEnginePresentation presentation,
        CancellationToken cancellationToken,
        bool appearanceGateHeld = false)
    {
        if (presentation == WallpaperEnginePresentation.HolographicCard &&
            !settings.Background.HolographicCardEnabled)
        {
            return false;
        }

        if (settings.Background.WallpaperEnginePresentation != presentation ||
            string.IsNullOrWhiteSpace(settings.Background.WallpaperEnginePackageSource) ||
            !File.Exists(settings.Background.WallpaperEnginePackageSource))
        {
            return false;
        }

        var importedPackage = await _wallpaperAssetImporter.ImportPackageAsync(
            settings.Background.WallpaperEnginePackageSource,
            cancellationToken);
        if (!importedPackage.Validation.IsUsable ||
            string.IsNullOrWhiteSpace(importedPackage.ImportedPath))
        {
            return false;
        }

        var runtime = await LauncherBackground.ShowWallpaperEngineAsync(
            importedPackage.ImportedPath,
            presentation,
            cancellationToken);
        if (!runtime.Succeeded)
        {
            return false;
        }

        if (!string.Equals(
                settings.Background.WallpaperEnginePackageSource,
                importedPackage.ImportedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            settings = await PersistCanonicalWallpaperPackageSourceAsync(
                settings,
                importedPackage.ImportedPath,
                cancellationToken,
                appearanceGateHeld);
        }

        _backgroundStatusText = presentation == WallpaperEnginePresentation.HolographicCard
            ? $"Wallpaper Engine 卡片：{Path.GetFileName(importedPackage.ImportedPath)}"
            : $"Wallpaper Engine 动态背景：{Path.GetFileName(importedPackage.ImportedPath)}";
        ApplySettingsPageState(_hostedSettingsPage);
        _hostedLaunchSettingsPage?.ApplyBackgroundPreview(LauncherBackground.Source);
        return true;
    }

    private async Task<AppearanceSettings> PersistCanonicalWallpaperPackageSourceAsync(
        AppearanceSettings settings,
        string canonicalPackageSource,
        CancellationToken cancellationToken,
        bool appearanceGateHeld)
    {
        if (!appearanceGateHeld)
        {
            await _appearanceSaveGate.WaitAsync(cancellationToken);
        }

        var configuredPackageSource = settings.Background.WallpaperEnginePackageSource;
        var normalizedMotionSource = string.Equals(
            settings.Background.MotionSource,
            configuredPackageSource,
            StringComparison.OrdinalIgnoreCase)
            ? canonicalPackageSource
            : settings.Background.MotionSource;
        var normalized = settings with
        {
            Background = settings.Background with
            {
                WallpaperEnginePackageSource = canonicalPackageSource,
                MotionSource = normalizedMotionSource,
            },
        };
        try
        {
            await ViewModel.SaveAppearanceSettingsAsync(normalized, cancellationToken);
            ApplyAppearanceSettings(ViewModel.AppearanceSettings);
            return ViewModel.AppearanceSettings;
        }
        finally
        {
            if (!appearanceGateHeld)
            {
                _appearanceSaveGate.Release();
            }
        }
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
        CancellationToken cancellationToken,
        bool forceStaticBackgroundReload = false)
    {
        var motionSelectionChanged =
            previous.Background.MotionEnabled != current.Background.MotionEnabled ||
            !string.Equals(
                previous.Background.MotionSource,
                current.Background.MotionSource,
                StringComparison.OrdinalIgnoreCase);
        var staticSelectionChanged = !string.Equals(
            previous.Background.SelectedSource,
            current.Background.SelectedSource,
            StringComparison.OrdinalIgnoreCase);
        var wallpaperEngineSelectionChanged =
            previous.Background.WallpaperEnginePresentation !=
                current.Background.WallpaperEnginePresentation ||
            !string.Equals(
                previous.Background.WallpaperEnginePackageSource,
                current.Background.WallpaperEnginePackageSource,
                StringComparison.OrdinalIgnoreCase);
        var holographicCardChanged =
            previous.Background.HolographicCardEnabled !=
            current.Background.HolographicCardEnabled;
        if (!motionSelectionChanged &&
            !staticSelectionChanged &&
            !wallpaperEngineSelectionChanged &&
            !holographicCardChanged &&
            !forceStaticBackgroundReload)
        {
            return;
        }

        if (current.Background.WallpaperEnginePresentation ==
                WallpaperEnginePresentation.HolographicCard &&
            await TryActivateConfiguredWallpaperEngineAsync(
                current,
                WallpaperEnginePresentation.HolographicCard,
                cancellationToken,
                appearanceGateHeld: true))
        {
            return;
        }

        if (current.Background.MotionEnabled &&
            await TryActivateConfiguredMotionAsync(
                current,
                cancellationToken,
                appearanceGateHeld: true))
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
