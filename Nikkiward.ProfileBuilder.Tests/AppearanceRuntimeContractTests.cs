using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Text.Json;
using Nikkiward.Features.Background;
using Nikkiward.Features.Launcher.Skins;
using Nikkiward.Models;
using Nikkiward.Services;
using Nikkiward.ViewModels;

internal static class AppearanceRuntimeContractTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("light and dark paper ink pairs meet the planned contrast", PaperInkPairsMeetContrast),
        ("the adaptive backdrop has one live visual-tree owner", AdaptiveBackdropHasOneOwner),
        ("the adaptive backdrop consumes the appearance motion projection", AdaptiveBackdropConsumesAppearanceMotion),
        ("the active backdrop pipeline bakes and reuses its depth plate", ActiveBackdropBakesDepthPlate),
        ("the built-in blurred plate covers every cold-start fallback", BuiltInBlurredPlateCoversColdStart),
        ("the built-in artwork seeds the dark cold-start theme", BuiltInArtworkSeedsDarkColdStart),
        ("the backdrop crossfade can be cancelled and promoted", BackdropCrossFadeCanBeCancelled),
        ("every density spacing token has a live visual consumer", DensitySpacingTokensHaveConsumers),
        ("static visual tokens have one live runtime authority", StaticVisualTokensHaveOneAuthority),
        ("overlay composition and XAML transitions stay sequenced", OverlayTransitionsStaySequenced),
        ("CJK page titles retain their full line bounds", CjkPageTitlesRetainFullLineBounds),
        ("custom background exposes the motion wallpaper importer", CustomBackgroundExposesMotionImporter),
        ("Wallpaper package recovery persists canonical paths and authoritative modes", WallpaperPackageRecoveryPersistsCanonicalState),
        ("custom background exposes the artwork title editor", CustomBackgroundExposesArtworkTitleEditor),
        ("high contrast focus uses the system focus color", HighContrastFocusUsesSystemColor),
        ("every custom transition consumes the live motion authority", CustomTransitionsConsumeMotionAuthority),
        ("interactive scale and connected animation obey motion off", InteractiveMotionObeysMotionOff),
        ("journal cached images use typed XAML sources", JournalCachedImagesUseTypedSources),
        (nameof(AtelierBackgroundSources), AtelierBackgroundSources),
        (nameof(AtelierBackgroundHandoff), AtelierBackgroundHandoff),
        (nameof(WebHomeCreatesMediaFolders), WebHomeCreatesMediaFolders),
        (nameof(WebHomeChannelRows), WebHomeChannelRows),
        (nameof(WebSkinProfileActions), WebSkinProfileActions),
        (nameof(AtelierComparisonAssets), AtelierComparisonAssets),
        (nameof(AtelierArtworkAndIssue), AtelierArtworkAndIssue),
        (nameof(AtelierProfileNames), AtelierProfileNames),
        (nameof(PlannerDayBoundary), PlannerDayBoundary),
        (nameof(ReminderCalendarAndMissedBatch), ReminderCalendarAndMissedBatch),
        (nameof(PlannerCommandsAndPersistence), PlannerCommandsAndPersistence),
        (nameof(SessionPhotosAndHomeFields), SessionPhotosAndHomeFields),
        (nameof(WebHomeStateMatchesLauncher), WebHomeStateMatchesLauncher),
        (nameof(WebHomeActionsAreBounded), WebHomeActionsAreBounded),
        (nameof(AtelierPageUsesBridge), AtelierPageUsesBridge),
        (nameof(WebHomeNavigationIsLocal), WebHomeNavigationIsLocal),
        (nameof(HomePreviewGuardsAllCommands), HomePreviewGuardsAllCommands),
        ("home skin catalog reserves five extension points", HomeSkinCatalogReservesAllDesigns),
        ("developer surfaces stay hidden until explicit opt in", DeveloperSurfacesRequireOptIn),
        ("journal resource gallery stays hidden from the reader", JournalResourceGalleryStaysHidden),
        ("launcher resources use the shared theme authority outside the shell rail", LauncherResourcesUseSharedThemeAuthority),
        ("the primary action resource chain stays live and high contrast aware", PrimaryActionResourceChainStaysIntact),
        ("focused feedback surfaces stay compact and reachable", FocusedFeedbackSurfacesStayCompact),
        ("gallery protection management stays scoped to settings", GalleryProtectionManagementStaysScopedToSettings),
        ("gallery and settings surfaces reflow at narrow widths", GalleryAndSettingsSurfacesReflowAtNarrowWidths),
        ("launcher close action stays separate from the launch action", LauncherCloseActionStaysSeparate),
        ("launcher runtime polling returns to the launch state", LauncherRuntimePollingReturnsToLaunchState),
        ("busy completion republishes the launcher state projection", BusyCompletionRepublishesLauncherStateProjection),
        ("external channels publish a bound runtime lifecycle", ExternalChannelsPublishBoundRuntimeLifecycle),
        ("profile picker uses one anchored server surface", ProfilePickerUsesSingleAnchoredServerSurface),
        ("profile selection returns to the launcher surface", ProfileSelectionReturnsToLauncher),
    ];

    private static Task PaperInkPairsMeetContrast()
    {
        var palette = XDocument.Parse(ReadSource("Nikkiward", "Themes", "Palette.xaml"));
        foreach (var themeName in new[] { "Light", "Dark" })
        {
            var theme = FindThemeDictionary(palette, themeName);
            var paper = ReadBrushColor(theme, "PaperBaseBrush");
            var primary = ReadBrushColor(theme, "InkPrimaryBrush");
            var secondary = ReadBrushColor(theme, "InkSecondaryBrush");
            var primaryRatio = ContrastRatio(primary, paper);
            var secondaryRatio = ContrastRatio(secondary, paper);

            Assert(
                primaryRatio >= 7.0,
                $"{themeName} InkPrimaryBrush / PaperBaseBrush must be at least 7:1, actual {primaryRatio:F2}:1");
            Assert(
                secondaryRatio >= 4.5,
                $"{themeName} InkSecondaryBrush / PaperBaseBrush must be at least 4.5:1, actual {secondaryRatio:F2}:1");
            Console.WriteLine(
                $"      palette contrast: {themeName.ToLowerInvariant()} primary {primaryRatio:F2}:1, secondary {secondaryRatio:F2}:1");
        }

        return Task.CompletedTask;
    }

    private static Task AdaptiveBackdropHasOneOwner()
    {
        var main = ReadSource("Nikkiward", "MainPage.xaml");
        var view = ReadSource(
            "Nikkiward",
            "Features",
            "Background",
            "ArtBackdropView.xaml.cs");

        Assert(
            Count(main, "<background:ArtBackdropView") == 1,
            "MainPage must place exactly one ArtBackdropView");
        Assert(
            !main.Contains("<Image\r\n            x:Name=\"LauncherBackground\"", StringComparison.Ordinal) &&
            !main.Contains("<Image\n            x:Name=\"LauncherBackground\"", StringComparison.Ordinal),
            "the old standalone LauncherBackground image must not remain");
        Assert(
            !view.Contains("service.AttachOnArtSurface", StringComparison.Ordinal) &&
            !view.Contains("_service.DetachOnArtSurface", StringComparison.Ordinal),
            "the view must not steal the service's page-level on-art registration");
        return Task.CompletedTask;
    }

    private static Task AdaptiveBackdropConsumesAppearanceMotion()
    {
        var runtime = ReadSource("Nikkiward", "MainPage.AppearanceRuntime.cs");
        var view = ReadSource(
            "Nikkiward",
            "Features",
            "Background",
            "ArtBackdropView.xaml.cs");

        Assert(
            runtime.Contains("LauncherBackground.ConfigureAppearance", StringComparison.Ordinal),
            "MainPage must project the saved motion and parallax choices into the backdrop");
        Assert(
            !runtime.Contains("RootGrid.PointerMoved +=", StringComparison.Ordinal),
            "MainPage must not run a second pointer-parallax path");
        Assert(
            view.Contains("AppearanceProjector.ProjectMotion", StringComparison.Ordinal) &&
            view.Contains("WindowActivationState.Deactivated", StringComparison.Ordinal),
            "the backdrop must combine appearance motion, system motion, and host focus");
        return Task.CompletedTask;
    }

    private static Task ActiveBackdropBakesDepthPlate()
    {
        var service = ReadSource(
            "Nikkiward",
            "Features",
            "Background",
            "ArtBackdropService.cs");

        Assert(
            service.Contains("IArtBlurBaker", StringComparison.Ordinal) &&
            service.Contains("TryWriteBlurAsync", StringComparison.Ordinal) &&
            service.Contains("cached?.BlurredArtPath is not null", StringComparison.Ordinal),
            "the live service must bake an L1 plate and reuse only a validated cached plate");
        Assert(
            !service.Contains("cached.BlurredArtPath = null", StringComparison.Ordinal) &&
            !service.Contains("result.BlurredArtPath = null;\r\n                return result;", StringComparison.Ordinal),
            "the live service must not erase its depth plate unconditionally");
        return Task.CompletedTask;
    }

    private static Task BuiltInBlurredPlateCoversColdStart()
    {
        var viewXaml = ReadSource(
            "Nikkiward",
            "Features",
            "Background",
            "ArtBackdropView.xaml");
        var viewCode = ReadSource(
            "Nikkiward",
            "Features",
            "Background",
            "ArtBackdropView.xaml.cs");
        var settings = ReadSource("Nikkiward", "Models", "AppearanceSettings.cs");
        var blurPath = Path.Combine(
            FindRoot(),
            "Nikkiward",
            "Assets",
            "NikkiDefaultBackgroundBlur.jpg");
        var blurHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(blurPath)));

        Assert(
            viewXaml.Contains(
                "Source=\"ms-appx:///Assets/NikkiDefaultBackgroundBlur.jpg\"",
                StringComparison.Ordinal),
            "the initial settled plate must use the shipped blurred artwork");
        Assert(
            settings.Contains(
                "BuiltInBlurredBackgroundSource",
                StringComparison.Ordinal) &&
            settings.Contains(
                "ms-appx:///Assets/NikkiDefaultBackgroundBlur.jpg",
                StringComparison.Ordinal),
            "the blurred fallback must have one app-resource authority");
        Assert(
            Count(viewCode, "ArtBlurredSettled.Source = CreateBuiltInBlurPlate();") == 2 &&
            viewCode.Contains(
                "new(new Uri(AppearanceProjector.BuiltInBlurredBackgroundSource))",
                StringComparison.Ordinal) &&
            viewCode.Contains(
                "ApplyBlurPlate(_service?.BlurredArtPath);",
                StringComparison.Ordinal),
            "source changes and missing cache plates must use the shipped blur while still restoration reuses a valid cache plate");
        Assert(
            string.Equals(
                blurHash,
                "E4279123900181ED11C0C4249EFA1A881E20D4AF6FEF29D283D314281DBD9108",
                StringComparison.Ordinal),
            "the built-in blur must retain its verified content hash");
        return Task.CompletedTask;
    }

    private static Task BuiltInArtworkSeedsDarkColdStart()
    {
        var mainDocument = XDocument.Parse(ReadSource("Nikkiward", "MainPage.xaml"));
        var mainCode = ReadSource("Nikkiward", "MainPage.xaml.cs");
        var appearanceCode = ReadSource("Nikkiward", "MainPage.Appearance.cs");
        var backgroundPath = Path.Combine(
            FindRoot(),
            "Nikkiward",
            "Assets",
            "NikkiDefaultBackground.jpg");
        var backgroundHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(backgroundPath)));

        Assert(
            mainDocument.Root?.Attribute("RequestedTheme")?.Value == "Dark",
            "the first XAML frame must match the dark built-in artwork");
        Assert(
            mainCode.Contains(
                "DefaultBackgroundPreferredTheme =\r\n        ArtPreferredTheme.Dark",
                StringComparison.Ordinal) ||
            mainCode.Contains(
                "DefaultBackgroundPreferredTheme =\n        ArtPreferredTheme.Dark",
                StringComparison.Ordinal),
            "the built-in artwork must own the pre-analysis dark theme seed");
        Assert(
            Regex.IsMatch(
                appearanceCode,
                @"artworkTheme\s*=\s*_backdrop\.IsReady\s*\?\s*_backdrop\.PreferredTheme\s*:\s*DefaultBackgroundPreferredTheme") &&
            appearanceCode.Contains("ThemeMode.WarmLight => ElementTheme.Light", StringComparison.Ordinal) &&
            appearanceCode.Contains("ThemeMode.WarmDark => ElementTheme.Dark", StringComparison.Ordinal) &&
            appearanceCode.Contains("_ => artworkTheme == ArtPreferredTheme.Dark", StringComparison.Ordinal),
            "follow-artwork must use the built-in seed only until analysis while explicit themes stay authoritative");
        Assert(
            string.Equals(
                backgroundHash,
                "79E98642EC260C9CA8F4A89A12D8294B0474B78658DAB6DE330BFCB192514880",
                StringComparison.Ordinal),
            "the cold-start theme seed must stay bound to the verified built-in artwork");
        return Task.CompletedTask;
    }

    private static Task BackdropCrossFadeCanBeCancelled()
    {
        var view = ReadSource(
            "Nikkiward",
            "Features",
            "Background",
            "ArtBackdropView.xaml.cs");
        var fieldMatch = Regex.Match(
            view,
            @"private\s+Storyboard\?\s+(?<field>_[A-Za-z0-9_]*cross[A-Za-z0-9_]*fade[A-Za-z0-9_]*)\s*;",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        Assert(fieldMatch.Success, "the active crossfade Storyboard must be held in a nullable field");
        var fieldName = fieldMatch.Groups["field"].Value;
        Assert(
            Regex.IsMatch(
                view,
                $@"{Regex.Escape(fieldName)}\s*=\s*new\s+Storyboard\s*\(",
                RegexOptions.CultureInvariant),
            "the held Storyboard field must own the active crossfade");

        var stopMatch = Regex.Match(
            view,
            $@"{Regex.Escape(fieldName)}(?:\?)?\.Stop\s*\(",
            RegexOptions.CultureInvariant);
        Assert(stopMatch.Success, "the held crossfade Storyboard must have an explicit stop path");

        var stopMethod = FindContainingMethod(view, stopMatch.Index);
        var promotionCall = Regex.Match(
            stopMethod,
            @"(?<method>[A-Za-z0-9_]*[Pp]romot[A-Za-z0-9_]*)\s*\(",
            RegexOptions.CultureInvariant);
        Assert(
            promotionCall.Success,
            "cancelling an active crossfade must be able to promote its incoming plate");

        var promotionMethod = FindMethod(view, promotionCall.Groups["method"].Value);
        Assert(
            promotionMethod.Contains(
                "ArtBlurredSettled.Source = ArtBlurredIncoming.Source",
                StringComparison.Ordinal),
            "promotion must move the incoming plate into the settled layer");
        Assert(
            promotionMethod.Contains("ArtBlurredIncoming.Source = null", StringComparison.Ordinal) &&
            promotionMethod.Contains("ArtBlurredIncoming.Opacity = 0", StringComparison.Ordinal),
            "promotion must clear the incoming plate and its opacity");
        return Task.CompletedTask;
    }

    private static Task DensitySpacingTokensHaveConsumers()
    {
        foreach (var resourceKey in new[] { "ContentPaddingThickness", "CardPaddingThickness" })
        {
            var consumers = FindThemeResourceConsumers(resourceKey);
            Assert(
                consumers.Length > 0,
                $"{resourceKey} must be consumed by a live XAML attribute");
        }

        return Task.CompletedTask;
    }

    private static Task StaticVisualTokensHaveOneAuthority()
    {
        var controls = ReadSource("Nikkiward", "Themes", "Controls.xaml");
        Assert(
            controls.Contains(
                "x:Key=\"OnArtGlassButtonStyle\"",
                StringComparison.Ordinal) &&
            controls.Contains(
                "<Setter Property=\"Height\" Value=\"{ThemeResource ControlHeight}\" />",
                StringComparison.Ordinal),
            "on-art text buttons must follow the live density control height");

        var elevation = ReadSource("Nikkiward", "Themes", "Elevation.xaml");
        var island = ReadSource("Nikkiward", "Controls", "GlassIsland.cs");
        foreach (var resourceKey in new[]
        {
            "ElevationCard",
            "ElevationIsland",
            "ElevationRail",
            "ElevationAction",
            "ElevationDialog",
        })
        {
            Assert(
                elevation.Contains($"x:Key=\"{resourceKey}\"", StringComparison.Ordinal) &&
                island.Contains($"\"{resourceKey}\"", StringComparison.Ordinal),
                $"{resourceKey} must drive the GlassIsland depth ladder");
        }

        var motion = ReadSource("Nikkiward", "Themes", "Motion.xaml");
        var projection = ReadSource("Nikkiward", "Models", "AppearanceSettings.cs");
        var runtime = ReadSource("Nikkiward", "MainPage.AppearanceRuntime.cs");
        foreach (var resourceKey in new[]
        {
            "MotionStateDuration",
            "MotionPanelOpen",
            "MotionPanelClose",
            "EaseGlassX1",
            "EaseGlassY1",
            "EaseGlassX2",
            "EaseGlassY2",
        })
        {
            Assert(
                motion.Contains($"x:Key=\"{resourceKey}\"", StringComparison.Ordinal),
                $"Motion.xaml must define {resourceKey}");
        }
        Assert(
            projection.Contains("PanelOpenDurationMilliseconds", StringComparison.Ordinal) &&
            projection.Contains("PanelCloseDurationMilliseconds", StringComparison.Ordinal) &&
            runtime.Contains("resources[\"MotionPanelOpen\"]", StringComparison.Ordinal) &&
            runtime.Contains("resources[\"MotionPanelClose\"]", StringComparison.Ordinal),
            "motion projection must publish panel durations for Full, Reduced, and Off modes");

        var overlay = ReadSource("Nikkiward", "MainPage.Overlays.cs");
        var shellRuntime = ReadSource(
            "Nikkiward",
            "Features",
            "Shell",
            "AppearanceRuntimeValues.cs");
        Assert(
            overlay.Contains("_statusDrawerAnimationVersion", StringComparison.Ordinal) &&
            overlay.Contains("_launchSettingsAnimationVersion", StringComparison.Ordinal) &&
            overlay.Contains("MotionPanelOpen", StringComparison.Ordinal) &&
            overlay.Contains("MotionPanelClose", StringComparison.Ordinal) &&
            overlay.Contains("ApplyOpacityTransition", StringComparison.Ordinal) &&
            shellRuntime.Contains("StartOpacityAnimation", StringComparison.Ordinal) &&
            shellRuntime.Contains("CreateCubicBezierEasingFunction", StringComparison.Ordinal),
            "overlay motion must consume the projected durations and cancel stale close completions");
        return Task.CompletedTask;
    }

    private static Task HighContrastFocusUsesSystemColor()
    {
        var controls = XDocument.Parse(ReadSource("Nikkiward", "Themes", "Controls.xaml"));
        var highContrast = FindThemeDictionary(controls, "HighContrast");
        var primaryFocus = FindResource(highContrast, "FocusVisualPrimaryBrush");
        Assert(primaryFocus is not null, "HighContrast must override FocusVisualPrimaryBrush");

        var resourceValues = string.Join(
            " ",
            primaryFocus!.Attributes().Select(attribute => attribute.Value));
        Assert(
            resourceValues.Contains("SystemColor", StringComparison.Ordinal) &&
            !resourceValues.Contains("DerivedAccentBrush", StringComparison.Ordinal),
            "HighContrast FocusVisualPrimaryBrush must use a system color");
        foreach (var resource in highContrast.Elements())
        {
            var values = string.Join(
                " ",
                resource.Attributes().Select(attribute => attribute.Value));
            Assert(
                !values.Contains("#", StringComparison.Ordinal) &&
                values.Contains("SystemColor", StringComparison.Ordinal),
                $"HighContrast {resource.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value} must use a system color");
        }

        var materials = XDocument.Parse(ReadSource("Nikkiward", "Themes", "Materials.xaml"));
        var highContrastMaterials = FindThemeDictionary(materials, "HighContrast");
        foreach (var key in new[]
        {
            "GlassPillBrush",
            "GlassIslandBrush",
            "GlassOverlayBrush",
            "PaperGlassBrush",
            "PaperGlassStrongBrush",
            "IslandEdgeHighlightBrush",
            "DrawerMaskBrush",
            "LaunchSettingsMaskBrush",
        })
        {
            var resource = FindResource(highContrastMaterials, key);
            Assert(resource is not null, $"HighContrast must override {key}");
            var values = string.Join(
                " ",
                resource!.Attributes().Select(attribute => attribute.Value));
            Assert(
                values.Contains("SystemColor", StringComparison.Ordinal),
                $"HighContrast {key} must use a system color");
        }
        return Task.CompletedTask;
    }

    private static Task OverlayTransitionsStaySequenced()
    {
        var overlay = ReadSource("Nikkiward", "MainPage.Overlays.cs");
        Assert(
            !overlay.Contains("StartOpacityAnimation", StringComparison.Ordinal) &&
            !overlay.Contains("ResetOpacityAnimation", StringComparison.Ordinal),
            "overlay opacity must not use ElementCompositionPreview beside XAML transitions");
        foreach (var methodName in new[]
        {
            "OpenLaunchSettingsSurface",
            "HideLaunchSettingsSurface",
            "OpenStatusDrawerSurface",
            "HideStatusDrawer",
        })
        {
            var method = FindMethod(overlay, methodName);
            Assert(
                method.Contains("ApplyOpacityTransition", StringComparison.Ordinal) &&
                method.Contains("ApplyTranslationTransition", StringComparison.Ordinal),
                $"{methodName} must keep opacity and translation under the XAML transition authority");
        }
        return Task.CompletedTask;
    }

    private static Task CustomBackgroundExposesMotionImporter()
    {
        var settingsXaml = ReadSource(
            "Nikkiward",
            "Features",
            "Launcher",
            "LaunchSettingsPage.xaml");
        var settingsCode = ReadSource(
            "Nikkiward",
            "Features",
            "Launcher",
            "LaunchSettingsPage.xaml.cs");
        var overlay = ReadSource("Nikkiward", "MainPage.Overlays.cs");
        var appearance = ReadSource("Nikkiward", "MainPage.Appearance.cs");
        var wallpaperSupport = ReadSource(
            "Nikkiward",
            "Features",
            "Background",
            "WallpaperImportSupport.cs");
        var wallpaperRuntime = ReadSource(
            "Nikkiward",
            "Features",
            "Background",
            "WallpaperEngineRuntimeHost.cs");

        Assert(
            settingsXaml.Contains("BackgroundSection", StringComparison.Ordinal) &&
            settingsXaml.Contains("HolographicImportModeButton", StringComparison.Ordinal) &&
            settingsXaml.Contains("MotionImportModeButton", StringComparison.Ordinal) &&
            settingsXaml.Contains("导入壁纸", StringComparison.Ordinal) &&
            settingsXaml.Contains("OnChooseWallpaperClicked", StringComparison.Ordinal),
            "custom background must expose explicit holographic-card and motion import modes");
        Assert(
            settingsCode.Contains("WallpaperImportRequestedEventArgs", StringComparison.Ordinal) &&
            settingsCode.Contains("WallpaperImportMode", StringComparison.Ordinal) &&
            overlay.Contains("OnLaunchSettingsWallpaperImportRequested", StringComparison.Ordinal) &&
            overlay.Contains("ChooseWallpaperAsync(e.Mode)", StringComparison.Ordinal) &&
            appearance.Contains(
                "WallpaperSourceRules.Resolve(sourcePath, mode)",
                StringComparison.Ordinal) &&
            appearance.Contains(
                "WallpaperSourceRules.PackageExtensions",
                StringComparison.Ordinal) &&
            appearance.Contains(
                "CommitStillBackgroundAsync",
                StringComparison.Ordinal) &&
            appearance.Contains("ImportMotionBackgroundAsync", StringComparison.Ordinal) &&
            appearance.Contains("ImportWallpaperEnginePackageAsync", StringComparison.Ordinal),
            "the visible wallpaper command must route each selected import mode into the existing card or motion pipeline");
        Assert(
            wallpaperSupport.Contains("PKGV", StringComparison.Ordinal) &&
            wallpaperSupport.Contains("project.json", StringComparison.Ordinal) &&
            wallpaperSupport.Contains("TryResolveSafeSibling", StringComparison.Ordinal),
            "Wallpaper Engine packages must be identified through a bounded PKGV index and local project metadata");
        Assert(
            wallpaperRuntime.Contains("GraphicsCaptureItem.TryCreateFromWindowId", StringComparison.Ordinal) &&
            wallpaperRuntime.Contains("openWallpaper", StringComparison.Ordinal) &&
            wallpaperRuntime.Contains("closeWallpaper", StringComparison.Ordinal),
            "Wallpaper Engine packages must use the runtime window and capture its live frames into XAML");
        return Task.CompletedTask;
    }

    private static Task WallpaperPackageRecoveryPersistsCanonicalState()
    {
        var appearance = ReadSource("Nikkiward", "MainPage.Appearance.cs");
        var launchSettings = ReadSource(
            "Nikkiward",
            "Features",
            "Launcher",
            "LaunchSettingsPage.xaml.cs");
        var activation = FindMethod(
            appearance,
            "TryActivateConfiguredWallpaperEngineAsync");
        var persistence = FindMethod(
            appearance,
            "PersistCanonicalWallpaperPackageSourceAsync");

        Assert(
            activation.Contains(
                "PersistCanonicalWallpaperPackageSourceAsync",
                StringComparison.Ordinal) &&
            activation.Contains(
                "importedPackage.ImportedPath",
                StringComparison.Ordinal),
            "a recovered Wallpaper package must persist the canonical cache path after activation");
        Assert(
            persistence.Contains(
                "WallpaperEnginePackageSource = canonicalPackageSource",
                StringComparison.Ordinal) &&
            persistence.Contains(
                "MotionSource = normalizedMotionSource",
                StringComparison.Ordinal) &&
            persistence.Contains(
                "ViewModel.SaveAppearanceSettingsAsync",
                StringComparison.Ordinal),
            "canonical Wallpaper package recovery must update and save every matching package path");
        Assert(
            launchSettings.Contains(
                "settings.Background.WallpaperEnginePresentation switch",
                StringComparison.Ordinal) &&
            launchSettings.Contains(
                "WallpaperEnginePresentation.MotionBackdrop => true",
                StringComparison.Ordinal) &&
            launchSettings.Contains(
                "WallpaperEnginePresentation.HolographicCard => false",
                StringComparison.Ordinal) &&
            launchSettings.Contains(
                "_ => settings.Background.MotionEnabled",
                StringComparison.Ordinal),
            "Wallpaper package presentation must be authoritative while ordinary video retains the motion fallback");
        return Task.CompletedTask;
    }

    private static Task CustomBackgroundExposesArtworkTitleEditor()
    {
        var settingsXaml = ReadSource(
            "Nikkiward",
            "Features",
            "Launcher",
            "LaunchSettingsPage.xaml");
        var settingsDocument = XDocument.Parse(settingsXaml);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var settingsCode = ReadSource(
            "Nikkiward",
            "Features",
            "Launcher",
            "LaunchSettingsPage.xaml.cs");
        var overlay = ReadSource("Nikkiward", "MainPage.Overlays.cs");
        var runtime = ReadSource("Nikkiward", "MainPage.AppearanceRuntime.cs");

        Assert(
            settingsXaml.Contains("ArtworkTitleBox", StringComparison.Ordinal) &&
            settingsXaml.Contains("自定义背景画名", StringComparison.Ordinal) &&
            settingsXaml.Contains("OnSaveArtworkTitleClicked", StringComparison.Ordinal) &&
            settingsXaml.Contains("OnResetArtworkTitleClicked", StringComparison.Ordinal),
            "custom background must expose save and reset controls for the artwork title");
        var subtitleBox = settingsDocument
            .Descendants()
            .SingleOrDefault(element =>
                element.Name.LocalName == "TextBox" &&
                element.Attribute(xaml + "Name")?.Value == "ArtworkTitleBox");
        Assert(
            subtitleBox?.Attribute("Height")?.Value == "Auto",
            "the headered artwork title editor must not inherit the fixed 44px TextBox height");
        Assert(
            settingsCode.Contains("ArtworkTitleSaveRequested", StringComparison.Ordinal) &&
            overlay.Contains("OnLaunchSettingsArtworkTitleSaveRequested", StringComparison.Ordinal) &&
            overlay.Contains("WithArtworkTitle(e.Source, e.Title)", StringComparison.Ordinal) &&
            runtime.Contains("_hostedLaunchSettingsPage?.ApplyAppearanceSettings(settings)", StringComparison.Ordinal),
            "the artwork title editor must save through appearance settings and stay synchronized");
        return Task.CompletedTask;
    }

    private static Task CjkPageTitlesRetainFullLineBounds()
    {
        var gallery = XDocument.Parse(ReadSource("Nikkiward", "Pages", "GalleryPage.xaml"));
        var journal = XDocument.Parse(ReadSource(
            "Nikkiward",
            "Features",
            "Journal",
            "JournalPage.xaml"));

        AssertFullLineTitle(gallery, "相册");
        AssertFullLineTitle(journal, "奇想手账");
        AssertFullLineTitle(
            XDocument.Parse(ReadSource(
                "Nikkiward",
                "Features",
                "Journal",
                "JournalSummaryPanel.xaml")),
            "手账概览");

        var galleryRows = gallery
            .Descendants()
            .Where(element => element.Name.LocalName == "RowDefinition")
            .Take(4)
            .Select(element => element.Attribute("Height")?.Value)
            .ToArray();
        Assert(
            galleryRows.SequenceEqual(["48", "48", "*", "32"]),
            "gallery commands must use a compact 48px row directly below the title bar");
        return Task.CompletedTask;
    }

    private static void AssertFullLineTitle(XDocument document, string title)
    {
        var element = document
            .Descendants()
            .SingleOrDefault(candidate =>
                candidate.Name.LocalName == "TextBlock" &&
                candidate.Attribute("Text")?.Value == title);
        Assert(element is not null, $"title '{title}' must exist");
        Assert(
            element!.Attribute("TextLineBounds")?.Value == "Full" &&
            element.Attribute("OpticalMarginAlignment")?.Value == "None",
            $"title '{title}' must preserve the complete CJK glyph bounds");
    }

    private static Task CustomTransitionsConsumeMotionAuthority()
    {
        var customFiles = Directory
            .EnumerateFiles(Path.Combine(FindRoot(), "Nikkiward"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .ToArray();

        var hardCodedDurations = customFiles
            .Where(file => file.Text.Contains("Transition Duration=\"0:", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(FindRoot(), file.Path))
            .ToArray();
        Assert(
            hardCodedDurations.Length == 0,
            $"custom transition durations must use motion resources: {string.Join(", ", hardCodedDurations)}");

        var stat = ReadSource("Nikkiward", "Controls", "StatTile.cs");
        Assert(
            stat.Contains("AppearanceRuntimeValues.ReadDuration", StringComparison.Ordinal) &&
            stat.Contains("CreateCubicBezierEasingFunction", StringComparison.Ordinal) &&
            stat.Contains("StopAnimation", StringComparison.Ordinal),
            "StatTile must consume the live duration and easing, then stop old animations when motion is off");
        return Task.CompletedTask;
    }

    private static Task InteractiveMotionObeysMotionOff()
    {
        var shellValues = ReadSource(
            "Nikkiward",
            "Features",
            "Shell",
            "AppearanceRuntimeValues.cs");
        var journal = ReadSource(
            "Nikkiward",
            "Features",
            "Journal",
            "JournalSnapshotPanel.xaml.cs");
        var profile = ReadSource(
            "Nikkiward",
            "Features",
            "Profile",
            "ProfilePickerView.xaml.cs");
        var gallery = ReadSource(
            "Nikkiward",
            "Pages",
            "GalleryPage.Interactions.cs");
        var mainRuntime = ReadSource("Nikkiward", "MainPage.AppearanceRuntime.cs");

        Assert(
            shellValues.Contains("SuppressNavigationTransitionInfo", StringComparison.Ordinal) &&
            shellValues.Contains("ReadScale", StringComparison.Ordinal),
            "navigation and pointer targets must share the live appearance authority");
        foreach (var source in new[] { journal, gallery })
        {
            Assert(
                source.Contains("AppearanceRuntimeValues.ReadScale", StringComparison.Ordinal),
                "every custom pointer scale must consume the live scale resources");
        }
        Assert(
            !profile.Contains("PointerEntered", StringComparison.Ordinal) &&
            !profile.Contains("AppearanceRuntimeValues.ReadScale", StringComparison.Ordinal),
            "the direct profile selector must not retain the removed game-card hover scale");

        Assert(
            mainRuntime.Contains("ResetInteractiveScales", StringComparison.Ordinal) &&
            mainRuntime.Contains("projection.IsZero", StringComparison.Ordinal),
            "switching motion off must reset already loaded scale transitions");
        return Task.CompletedTask;
    }

    private static Task JournalCachedImagesUseTypedSources()
    {
        var journalXaml = ReadSource(
            "Nikkiward",
            "Features",
            "Journal",
            "JournalSnapshotPanel.xaml");
        var wishXaml = ReadSource(
            "Nikkiward",
            "Features",
            "Wish",
            "WishPage.xaml");
        var journalModels = ReadSource(
            "Nikkiward",
            "ViewModels",
            "JournalRichContentViewModels.cs");
        var resonanceModels = ReadSource(
            "Nikkiward",
            "ViewModels",
            "ResonanceWardrobeViewModels.cs");

        Assert(
            !journalXaml.Contains("Source=\"{x:Bind PreviewUri}\"", StringComparison.Ordinal) &&
            !wishXaml.Contains("Source=\"{x:Bind PreviewUri}\"", StringComparison.Ordinal),
            "cached file URI strings must not rely on implicit ImageSource conversion");
        Assert(
            journalXaml.Contains("Source=\"{x:Bind PreviewSource}\"", StringComparison.Ordinal) &&
            wishXaml.Contains("Source=\"{x:Bind PreviewSource}\"", StringComparison.Ordinal) &&
            journalModels.Contains("ImageSource? PreviewSource", StringComparison.Ordinal) &&
            resonanceModels.Contains("ImageSource? PreviewSource", StringComparison.Ordinal),
            "journal and resonance images must bind explicit typed image sources");
        return Task.CompletedTask;
    }

    private static async Task AtelierBackgroundSources()
    {
        var root = Path.Combine(Path.GetTempPath(), "Nikkiward");
        var still = HomeBackground.StillFolder(root);
        var motion = HomeBackground.MotionFolder(root);
        Assert(still == Path.Combine(root, "WallpaperImports", "Still") &&
            motion == Path.Combine(root, "MotionBackgrounds"), "only managed background folders");
        var calls = new List<(string, bool)>();
        Task<HomeBackground> Resolve(string art, string preview = "") => HomeBackground.ResolveAsync(art, preview, root, (file, video) =>
        {
            calls.Add((file, video));
            return Task.FromResult("frame.jpg");
        });
        foreach (var preset in AppearanceBackgroundPresets.All)
            Assert((await Resolve(preset.Source)) == new HomeBackground("image",
                $"https://{WebHomeBridge.Host}/art/{Path.GetFileName(preset.Source)}"), "built-in original");
        var image = Path.Combine(still, "cover #1.jpg");
        var direct = new HomeBackground("image", $"https://{HomeBackground.StillHost}/cover%20%231.jpg");
        Assert(await Resolve(image) == direct && await Resolve("scene.pkg", image) == direct, "import and scene preview");
        Assert(calls.Count == 0, "direct images avoid transcoding");
        var frame = $"https://{HomeBackground.StillHost}/frame.jpg";
        var jxl = Path.Combine(still, "photo.jxl");
        var movie = Path.Combine(motion, "clip.mp4");
        var external = Path.Combine(root, "Photos", "private.jpg");
        Assert(await Resolve(jxl) == new HomeBackground("image", frame) &&
            await Resolve(movie) == new HomeBackground("video", $"https://{HomeBackground.MotionHost}/clip.mp4", frame) &&
            await Resolve(external) == new HomeBackground("image", frame), "decode, video poster and external image");
        Assert(calls.SequenceEqual([(jxl, false), (movie, true), (external, false)]), "preview source identity");
    }

    private static Task AtelierBackgroundHandoff()
    {
        var js = ReadSource("Nikkiward", "Web", "shared", "home.js");
        var fixture = ReadSource("Nikkiward", "Web.dev", "atelier", "cases.js");
        Assert(js.Contains("const ready = updateCover(state)") && js.Contains("key !== backgroundKey") &&
            js.Contains("cover.src = bg.poster || bg.url") && js.Contains("video.src = bg.url"),
            "update media when its descriptor changes");
        var decoded = js.IndexOf("await Promise.all([document.fonts.ready, ready])", StringComparison.Ordinal);
        Assert(decoded >= 0 && decoded <
            js.IndexOf("postMessage('ready')", StringComparison.Ordinal) && fixture.Contains("background:{type:"),
            "fixture and host use the same decoded cover before handoff");
        return Task.CompletedTask;
    }

    private static Task WebHomeCreatesMediaFolders()
    {
        var root = Directory.CreateTempSubdirectory("Nikkiward-home-").FullName;
        try
        {
            var folders = HomeBackground.CreateMediaFolders(root);
            Assert(Directory.Exists(folders.Still) && Directory.Exists(folders.Motion), "fresh data root");
            Assert(HomeBackground.CreateMediaFolders(root) == folders, "existing data root");
        }
        finally { Directory.Delete(root, recursive: true); }
        return Task.CompletedTask;
    }

    private static Task WebHomeChannelRows()
    {
        var missing = new InstallationProfileCandidate
        {
            Identity = new() { DistributionChannel = DistributionChannel.Steam },
            Detail = "Steam manifest 不存在"
        };
        var other = missing with { Detail = "另一个库也没有安装" };
        var ready = missing with { Profile = new(), State = InstallationCandidateState.Candidate, ProfileId = "a", GameRootPath = @"D:\Game" };
        var second = ready with { ProfileId = "b", GameRootPath = @"E:\Game" };
        var rows = WebHomeState.ProfileRows([missing, missing, other], null).ToArray();
        Assert(rows.Select(p => p.Name).SequenceEqual(["国服 · 官方", "国服 · 哔哩哔哩", "国际服 · Steam"]), "stable channel order");
        Assert(rows.All(p => !p.Available && !p.Current && p.Reason == "未发现安装") &&
            rows[2].Detail == missing.Detail + "\n" + other.Detail, "one unavailable row with distinct details");
        rows = WebHomeState.ProfileRows([missing, ready], "a").ToArray();
        Assert(rows.Length == 3 && rows[2].Available && rows[2].Current && rows[2].Id == "a", "usable installation wins");
        rows = WebHomeState.ProfileRows([ready, missing, second], "b").ToArray();
        Assert(rows.Length == 4 && rows[2].Reason != rows[3].Reason && !rows[2].Current && rows[3].Current &&
            rows[3].Id == "b", "multiple selectable installations retain identity and location");
        return Task.CompletedTask;
    }

    private static Task WebSkinProfileActions()
    {
        Assert(WebHomeBridge.Parse("""{"action":"select-profile","profileId":"cn"}""")?.ProfileId == "cn", "select profile");
        Assert(WebHomeBridge.Parse("""{"action":"select-profile"}""") == null, "missing profile");
        foreach (var action in new[] { "locate", "rediscover", "recheck", "tide", "blossom" })
            Assert(WebHomeBridge.Parse(JsonSerializer.Serialize(new { action }))?.Action == action, action);
        return Task.CompletedTask;
    }

    private static Task AtelierComparisonAssets()
    {
        var web = Path.Combine(FindRoot(), "Nikkiward", "Web");
        var pageDirectory = Path.Combine(web, "skins", "atelier");
        var fixture = File.ReadAllText(Path.Combine(web, "..", "Web.dev", "atelier", "cases.js"));
        foreach (var name in new[] { "home-tide", "home-blossom", "home-photo05", "home-tide-running",
            "home-tide-launching", "home-tide-notfound", "home-blossom-blocked", "switcher" })
            Assert(fixture.Contains($"'{name}':"), name);
        foreach (var family in new[] { "noto-serif-sc", "bodoni-moda" })
        {
            var css = File.ReadAllText(Path.Combine(web, "fonts", family + ".css"));
            foreach (Match match in Regex.Matches(css, @"url\(([^)]+)\)"))
                Assert(File.Exists(Path.Combine(web, "fonts", match.Groups[1].Value)), match.Value);
        }
        return Task.CompletedTask;
    }

    private static Task AtelierArtworkAndIssue()
    {
        var vm = new MainPageViewModel(new JsonUserSettingsStore(Path.GetTempPath()),
            new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
        WebHomeState State() => WebHomeState.Create(vm, "light", 0xffd0bcba, false, []);
        var tide = State();
        Assert((tide.Theme, tide.Accent, tide.OnAccent) == ("dark", "#A9DBFF", "#0A1322"), "tide");
        vm.ShowArtwork(AppearanceBackgroundPresets.Preset2Source);
        Assert((State().Theme, State().Accent, State().OnAccent) == ("light", "#B43B56", "#FFF8F2"), "blossom");
        vm.ShowArtwork("custom.jpg");
        var custom = State();
        Assert(custom.Theme == "light" && custom.Accent == "#D0BCBA" && custom.OnAccent == "#2A2320", "custom");
        foreach (var rgb in new uint[] { 0x010101, 0x737373, 0x888888, 0xFFFFFF })
        {
            var state = WebHomeState.Create(vm, "dark", rgb, false, []);
            var ink = Convert.ToUInt32(state.OnAccent[1..], 16);
            double Luminance(uint color) => ArtPaletteAnalyzer.RelativeLuminance((byte)(color >> 16), (byte)(color >> 8), (byte)color);
            Assert(state.Accent == $"#{rgb:X6}" && ArtPaletteAnalyzer.ContrastRatio(Luminance(ink), Luminance(rgb)) >= 4.5, state.Accent);
        }
        foreach (var (month, label) in new[] { (9,"九"), (10,"十"), (11,"十一"), (12,"十二") })
            Assert((tide with { Date = new DateOnly(2026, month, 1) }).Issue == $"二〇二六年{label}月号", label);
        Assert(tide is { Checklist: null, CheckCount: null, JournalSnapshotDate: null, LastSessionPhotos: null, PhotoTotal: null }, "no example data");
        return Task.CompletedTask;
    }

    private static Task AtelierProfileNames()
    {
        foreach (var (region, channel, title) in new[] {
            (RegionFamily.MainlandChina, DistributionChannel.Official, "国服 · 官方"),
            (RegionFamily.MainlandChina, DistributionChannel.Bilibili, "国服 · 哔哩哔哩"),
            (RegionFamily.Overseas, DistributionChannel.Steam, "国际服 · Steam"),
            (RegionFamily.Overseas, DistributionChannel.Official, "国际服 · 官方") })
            Assert(MainPageViewModel.FormatHomeProfile(region, channel, "").Title == title, title);
        return Task.CompletedTask;
    }

    private static Task PlannerDayBoundary()
    {
        var reset = new GeneralSettings().ChecklistReset;
        DateTimeOffset before = new DateTime(2026, 11, 26, 3, 59, 0, DateTimeKind.Local);
        var planner = PlannerData.Empty.Reset(before, reset) with { Items = [new("a", "拍照", true)] };
        Assert(planner.Day.Day == 25 && planner.Reset(before, reset).Items[0].Done, "before 04:00");
        var next = planner.Reset(before.AddMinutes(1), reset);
        Assert(next.Day.Day == 26 && next.Items is [{ Done: false, Id: "a" }], "reset only completion");
        Assert(planner.Reset(before.AddMinutes(1), new(5, 0)).Items[0].Done, "custom boundary");
        return Task.CompletedTask;
    }

    private static Task ReminderCalendarAndMissedBatch()
    {
        DateTimeOffset now = new DateTime(2026, 11, 26, 19, 30, 0, DateTimeKind.Local);
        var daily = new LocalReminder("a","拍照","daily",new(19,30),null,null,now);
        var weekly = daily with { Id = "b", Repeat = "weekly", Weekday = DayOfWeek.Thursday };
        var once = daily with { Id = "c", Repeat = "once", Date = new(2026,11,26) };
        Assert(daily.Next(now) == now.AddDays(1) && weekly.Next(now) == now.AddDays(7) &&
            once.Next(now.AddMinutes(-1)) == now && once.Next(now) == null, "next occurrence");
        var data = PlannerData.Empty with { Reminders = [daily,weekly,once] };
        var later = now.AddDays(10);
        var advanced = data.Advance(later, out var batch);
        Assert(batch.Length == 3 && advanced.Reminders[2].Due == null && advanced.Reminders[0].Due > later, "one batch after downtime");
        advanced.Advance(later, out var again);
        Assert(again.Length == 0, "no duplicate delivery");
        return Task.CompletedTask;
    }

    private static Task PlannerCommandsAndPersistence()
    {
        var now = DateTimeOffset.Now;
        var data = PlannerData.Empty.Reset(now,new(4,0));
        HomeAction? Parse(PlannerCommand command) => WebHomeBridge.Parse(JsonSerializer.Serialize(new { action = "planner", planner = command }, WebHomeBridge.Json));
        foreach (var group in new[] { "check", "link", "reminder" })
        {
            var add = new PlannerCommand(group+"-add", Text:"第一项", Url:"https://example.com/", Repeat:"daily", Time:new(8,0));
            Assert(Parse(add) != null, group+" add");
            data = data.Change(add,now);
            var id = group switch { "check" => data.Items[0].Id, "link" => data.Links[0].Id, _ => data.Reminders[0].Id };
            foreach (var action in group switch { "check" => new[] {"edit","toggle","move","delete"}, "link" => ["edit","open","move","delete"], _ => ["edit","move","delete"] })
                Assert(Parse(add with { Action = group+"-"+action, Id = id, Step = 1 }) != null, action);
        }
        Assert(Parse(new("link-add", Text:"坏地址", Url:"file:///private")) == null &&
            Parse(new("link-open", Url:"https://example.com")) == null && Parse(new("check-move",Id:"a",Step:2)) == null &&
            Parse(new("reminder-add",Text:"提醒",Repeat:"weekly",Time:new(4,0))) == null &&
            Parse(new("check-unknown")) == null, "reject malformed commands");
        data = data.Change(new("check-add",Text:"第二项"),now);
        data = data.Change(new("reminder-add",Text:"第二条",Repeat:"daily",Time:new(9,0)),now);
        var reminder = data.Reminders[1];
        data = data.Change(new("reminder-move",Id:reminder.Id,Step:-1),now);
        Assert(data.Reminders[0] == reminder, "reorder without changing the reminder schedule");
        var second = data.Items[1].Id;
        data = data.Change(new("check-move",Id:second,Step:-1),now).Change(new("check-toggle",Id:second),now);
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var store = new PlannerStore(folder.FullName);
            store.Save(data);
            var saved = store.Load();
            Assert(saved.Items[0] == new ChecklistItem(second,"第二项",true) && saved.Links.SequenceEqual(data.Links) &&
                saved.Reminders.SequenceEqual(data.Reminders), "ordered offline round trip");
            File.WriteAllText(Path.Combine(folder.FullName, PlannerStore.RelativePath), "{}");
            try { store.Load(); throw new InvalidOperationException("invalid document accepted"); }
            catch (JsonException) { }
        }
        finally { folder.Delete(true); }
        return Task.CompletedTask;
    }

    private static Task SessionPhotosAndHomeFields()
    {
        var now = DateTimeOffset.UtcNow;
        var observed = new OfficialAssistedProcessObservation { GameProcesses = [
            new() { ExecutablePath="game", StartTimeUtc=now }, new() { ExecutablePath="helper", StartTimeUtc=now.AddHours(-1) }] };
        Assert(MainPageViewModel.RunningStart(observed,"GAME") == now && MainPageViewModel.RunningStart(observed,"other") == null,
            "confirmed game creation time without a play ledger");
        var prev = new PlaySession("官方",now.AddHours(-2),now.AddHours(-1),TimeSpan.FromHours(1));
        var ledger = new PlayTimeLedger([prev]);
        ledger.Observe(now,[new("game",1,now.AddSeconds(-10))],new Dictionary<string,string> { ["game"] = "官方" });
        var (current, previous, started) = ledger.HomeSessions("官方");
        Assert(previous == prev && started == now.AddSeconds(-10) && current != null && ledger.HomeSessions("Steam") == default, "channel-scoped history");
        DateTime[] times = [prev.StartUtc.UtcDateTime, prev.EndUtc.UtcDateTime, prev.EndUtc.UtcDateTime, now.UtcDateTime];
        Assert(PlayTimeLedger.CountPhotos(times,prev) == 3 && PlayTimeLedger.CountPhotos(times,current) == 1 &&
            PlayTimeLedger.CountPhotos(times,null) == null && PlayTimeLedger.CountPhotos(null,prev) == null &&
            PlayTimeLedger.ContainsPhoto(prev,prev.EndUtc) && !PlayTimeLedger.ContainsPhoto(prev,now), "inclusive full album counts");
        Assert(HomeChecklist.From([]) == null && HomeChecklist.From([new("1","一",true),new("2","二",false)])
            is { Completed:1, Total:2, Next:"二" }, "checklist projection");
        var vm = new MainPageViewModel(new JsonUserSettingsStore(Path.GetTempPath()),new WindowsInstallationInspector(),new RedactedDiagnosticReportExporter());
        var empty = WebHomeState.Create(vm,"dark",0,false,[]);
        Assert(empty is { NextReminder:null, ReminderCount:0, SessionStarted:null, CurrentSessionPhotos:null, PhotoTotal:null, JournalSnapshotDate:null, CheckCount:null }, "missing evidence");
        var next = new LocalReminder("1","提醒","daily",new(8,0),null,null,now);
        var state = empty with { CheckCount=3, PhotoTotal=times.Length, JournalSnapshotDate=new(2026,11,26), SessionStarted=started,
            CurrentSessionPhotos=1, LastSessionPhotos=3, Reminders=[next with { Due=null }, next] };
        var json = JsonSerializer.SerializeToElement(state,WebHomeBridge.Json);
        Assert(state.NextReminder == next && state.ReminderCount == 2 && json.GetProperty("checkCount").GetInt32()==3 &&
            json.GetProperty("photoTotal").GetInt32()==4 && json.GetProperty("journalSnapshotDate").GetString()=="2026-11-26" &&
            json.GetProperty("sessionStarted").GetDateTimeOffset()==started, "populated web fields");
        return Task.CompletedTask;
    }

    private static Task WebHomeStateMatchesLauncher()
    {
        var vm = new MainPageViewModel(new JsonUserSettingsStore(Path.GetTempPath()),
            new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
        var state = WebHomeState.Create(vm, "dark", 0xffabcdef, false, []);
        Assert(state.LaunchLabel == vm.HomeLaunchLabel && state.Detail == vm.HomeLaunchDetail &&
            state.Enabled == vm.HomeLaunchEnabled && state.Busy == vm.HomeBusy, "native launch state");
        Assert(state.OfficialDuration == null && state.LocalDuration == null && state.TaskText == null &&
            state.Progress == null && state.Photos is { Count: 0 }, "absent data");
        foreach (var kind in Enum.GetValues<LaunchButtonState>())
        {
            var serialized = JsonSerializer.SerializeToElement(state with { State = kind }, WebHomeBridge.Json);
            var visual = kind switch
            {
                LaunchButtonState.NotInstalled => "notfound",
                LaunchButtonState.Ready or LaunchButtonState.Launching or LaunchButtonState.Running or
                    LaunchButtonState.Checking => kind.ToString().ToLowerInvariant(),
                _ => "blocked"
            };
            Assert(serialized.GetProperty("state").GetString() == kind.ToString() &&
                serialized.GetProperty("visual").GetString() == visual, kind.ToString());
        }
        return Task.CompletedTask;
    }

    private static Task WebHomeActionsAreBounded()
    {
        Assert(WebHomeBridge.Parse("""{"action":"photo","photoId":"one"}""")?.PhotoId == "one", "photo");
        Assert(WebHomeBridge.Parse("""{"action":"profile","rect":{"x":1,"y":2,"width":3,"height":4}}""")?.Rect
            == new HomeRect(1, 2, 3, 4), "anchor");
        foreach (var action in new[] { "launch", "close", "settings", "background", "reset", "status", "gallery",
            "journal", "download", "time", "nav-settings", "nav-gallery-favorites", "nav-launcher", "nav-resonance", "nav-gallery", "nav-library" })
            Assert(WebHomeBridge.Parse(JsonSerializer.Serialize(new { action }))?.Action == action, action);
        foreach (var json in new[] { "null", "1", "{", "{}", """{"action":"other"}""", """{"action":"photo"}""",
            """{"action":"profile","rect":{"width":-1,"height":2}}""" })
            Assert(WebHomeBridge.Parse(json) == null, json);
        return Task.CompletedTask;
    }

    private static Task AtelierPageUsesBridge()
    {
        var html = ReadSource("Nikkiward", "Web", "skins", "atelier", "index.html");
        var page = Regex.Match(html, """<div [^>]*id="win"[^>]*>""").Value;
        var classes = Regex.Matches(page, "class=\"([^\"]+)\"");
        Assert(classes.Count == 1 && classes[0].Groups[1].Value.Split(' ').Contains("pending"), "first paint waits for state");
        foreach (Match match in Regex.Matches(html, "data-action=\"([^\"]+)\""))
        {
            var action = match.Groups[1].Value;
            var json = JsonSerializer.Serialize(new { action, rect = new HomeRect(0, 0, 1, 1) });
            Assert(WebHomeBridge.Parse(json)?.Action == action, action);
        }
        return Task.CompletedTask;
    }

    private static Task WebHomeNavigationIsLocal()
    {
        var page = WebHomeBridge.Page("atelier");
        Assert(WebHomeBridge.AllowsPage(page, "https://home.nikkiward.invalid/skins/atelier/index.html"), "packaged entry");
        foreach (var uri in new[] { "about:blank", "file:///index.html", "https://example.org", page + "?preview",
            page + "#fragment", "https://home.nikkiward.invalid.example/index.html",
            "https://home.nikkiward.invalid/home.js", "http://home.nikkiward.invalid/index.html" })
            Assert(!WebHomeBridge.AllowsPage(page, uri), uri);
        return Task.CompletedTask;
    }

    private static Task HomePreviewGuardsAllCommands()
    {
        var host = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml.cs");
        var commands = ReadSource("Nikkiward", "MainPage.LaunchCommands.cs");
        foreach (var method in new[] { FindMethod(host, "InvokeAction"), FindMethod(commands, "OnOfficialFlowClicked"),
            FindMethod(commands, "OnCloseGameClicked") })
            Assert(method.ReplaceLineEndings("\n").Contains("#if DEBUG\n        if (HomePreview.Active) return;"), "preview must guard the action entry");
        return Task.CompletedTask;
    }

    private static Task HomeSkinCatalogReservesAllDesigns()
    {
        var registry = ReadSource("Nikkiward", "Features", "Launcher", "Skins", "HomeSkins.cs");
        foreach (var id in new[] { "Cinema", "Stitch", "Atelier", "Journal", "WishStar" })
            Assert(registry.Contains($"Register(\"{id}\""), $"{id} needs a reserved entry");
        return Task.CompletedTask;
    }

    private static Task DeveloperSurfacesRequireOptIn()
    {
        var settingsXaml = ReadSource("Nikkiward", "Features", "Settings", "SettingsPage.xaml");
        var settingsCode = ReadSource("Nikkiward", "Features", "Settings", "SettingsPage.xaml.cs");
        var homeXaml = ReadSource("Nikkiward", "Features", "Settings", "SettingsHomeView.xaml");
        var launcherCode = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml.cs");

        foreach (var itemName in new[]
        {
            "JournalItem",
            "FilesItem",
            "PluginsItem",
            "StatusItem",
            "ComponentsItem",
            "DiagnosticsItem",
            "ContractItem",
        })
        {
            Assert(
                Regex.IsMatch(
                    settingsXaml,
                    $"x:Name=\"{itemName}\"[^>]*Visibility=\"Collapsed\"",
                    RegexOptions.CultureInvariant),
                $"{itemName} must default to collapsed");
        }

        var developerPanel = homeXaml.IndexOf(
            "x:Name=\"DeveloperToolsPanel\"",
            StringComparison.Ordinal);
        Assert(
            homeXaml.Contains("x:Name=\"DeveloperModeToggle\"", StringComparison.Ordinal) &&
            developerPanel >= 0 &&
            homeXaml.IndexOf("Tag=\"journal\"", StringComparison.Ordinal) > developerPanel &&
            homeXaml.IndexOf("Tag=\"files\"", StringComparison.Ordinal) > developerPanel &&
            homeXaml.IndexOf("Tag=\"plugins\"", StringComparison.Ordinal) > developerPanel &&
            !settingsXaml.Contains("外观、插件与输入设置", StringComparison.Ordinal) &&
            settingsCode.Contains("IsDeveloperDestination(destination) && !_developerModeEnabled", StringComparison.Ordinal) &&
            settingsCode.Contains("JournalItem.Visibility = visibility", StringComparison.Ordinal) &&
            settingsCode.Contains("FilesItem.Visibility = visibility", StringComparison.Ordinal) &&
            settingsCode.Contains("PluginsItem.Visibility = visibility", StringComparison.Ordinal) &&
            Regex.IsMatch(
                settingsCode,
                @"IsDeveloperDestination\s*\([^)]*\)\s*=>\s*destination\s+is\s+SettingsDestination\.Journal\s+or\s+SettingsDestination\.Files\s+or\s+SettingsDestination\.Plugins\s+or",
                RegexOptions.CultureInvariant) &&
            settingsCode.Contains("ApplyDeveloperMode(bool enabled)", StringComparison.Ordinal),
            "developer-only settings need an explicit toggle, collapsed navigation, and a guarded path");
        Assert(launcherCode.Contains("StatusRequested"), "the skin host must route player-requested diagnostics");
        return Task.CompletedTask;
    }

    private static Task JournalResourceGalleryStaysHidden()
    {
        var journal = ReadSource(
            "Nikkiward",
            "Features",
            "Journal",
            "JournalSnapshotPanel.xaml");
        var journalCode = ReadSource(
            "Nikkiward",
            "Features",
            "Journal",
            "JournalSnapshotPanel.xaml.cs");
        var journalPageCode = ReadSource(
            "Nikkiward",
            "Features",
            "Journal",
            "JournalPage.xaml.cs");
        var journalCaptureScripts = ReadSource(
            "Nikkiward",
            "ViewModels",
            "JournalWebCaptureScripts.cs");
        var journalBrowserCode = ReadSource(
            "Nikkiward",
            "MainPage.JournalBrowser.cs");
        var resonanceBrowserCode = ReadSource(
            "Nikkiward",
            "MainPage.ResonanceBrowser.cs");
        var applySnapshot = FindMethod(journalPageCode, "ApplySnapshot");
        Assert(
            Regex.IsMatch(
                journal,
                "x:Name=\"ResourceSection\"[\\s\\S]*?Visibility=\"Collapsed\"",
                RegexOptions.CultureInvariant) &&
            journalCode.Contains("ResourceSection.Visibility = Visibility.Collapsed", StringComparison.Ordinal) &&
            !journalCode.Contains("PopulateResourceGroups(snapshot, resources)", StringComparison.Ordinal),
            "cached web artwork may remain available to sync but must never be populated or shown in the reader UI");
        Assert(
            journalPageCode.Contains("JournalWebView.Opacity = isInProgress ? 0 : 1", StringComparison.Ordinal) &&
            journalPageCode.Contains("JournalWebView.IsHitTestVisible = !isInProgress", StringComparison.Ordinal) &&
            journalPageCode.Contains(
                "JournalWebView.CoreWebView2.SourceChanged += OnCoreSourceChanged",
                StringComparison.Ordinal) &&
            journalPageCode.Contains(
                "public Uri? CurrentUri => _currentNavigationUri ?? JournalWebView.Source",
                StringComparison.Ordinal) &&
            journalPageCode.Contains("if (args.IsNewDocument ||", StringComparison.Ordinal) &&
            journalPageCode.Contains("new JournalNavigationEventArgs(", StringComparison.Ordinal) &&
            !journalPageCode.Contains(
                "JournalWebView.Visibility = isInProgress ? Visibility.Collapsed : Visibility.Visible",
                StringComparison.Ordinal) &&
            !applySnapshot.Contains("HideBrowser", StringComparison.Ordinal),
            "sync must visually hide the WebView while keeping its document active for readiness checks");
        Assert(
            journalCaptureScripts.Contains(
                "documentReady: document.readyState === 'complete'",
                StringComparison.Ordinal) &&
            journalCaptureScripts.Contains("stableNodeKeyCount", StringComparison.Ordinal) &&
            journalCaptureScripts.Contains("pendingImageCount", StringComparison.Ordinal) &&
            !journalCaptureScripts.Contains("const expectedTitles =", StringComparison.Ordinal) &&
            journalBrowserCode.Contains(
                "JournalDocumentReadinessProjector.IsOverviewReady(",
                StringComparison.Ordinal) &&
            journalBrowserCode.Contains("IsCurrentOverviewRoute", StringComparison.Ordinal) &&
            journalBrowserCode.Contains("_journalCaptureGate.WaitAsync(0)", StringComparison.Ordinal) &&
            journalBrowserCode.Contains("string.Equals(", StringComparison.Ordinal) &&
            resonanceBrowserCode.Contains("IsCurrentResonanceRoute", StringComparison.Ordinal) &&
            resonanceBrowserCode.Contains("_journalCaptureGate.WaitAsync(0)", StringComparison.Ordinal) &&
            journalBrowserCode.Contains("captureValidated", StringComparison.Ordinal) &&
            journalBrowserCode.Contains(
                "JournalCaptureFailureKind.LocalProcessingFailure",
                StringComparison.Ordinal) &&
            !journalBrowserCode.Contains(
                "attempt < 80 && stableSamples < 3",
                StringComparison.Ordinal) &&
            !resonanceBrowserCode.Contains(
                "attempt < 80 && stableSamples < 3",
                StringComparison.Ordinal),
            "journal readiness must wait for a complete stable document without requiring a fixed official title list");
        return Task.CompletedTask;
    }

    private static Task LauncherResourcesUseSharedThemeAuthority()
    {
        var retiredKeys = new[]
        {
            "LauncherOverlayAcrylicBrush",
            "LauncherControlAcrylicBrush",
            "LauncherPreviewAcrylicBrush",
            "LauncherOnImagePrimaryTextBrush",
            "LauncherOnImageSecondaryTextBrush",
            "LauncherOnImageTertiaryTextBrush",
            "LauncherPageBaseBrush",
            "LauncherPanelStrokeBrush",
            "LauncherComponentFillBrush",
            "LauncherComponentStrokeBrush",
            "LauncherPrimaryActionForegroundBrush",
            "LauncherSelectedIndicatorBrush",
            "LauncherSelectedItemBrush",
            "LauncherWarningFillBrush",
            "LauncherSuccessFillBrush",
            "LauncherJournalHeroBrush",
            "LauncherJournalPaperBrush",
            "LauncherJournalPaperStrokeBrush",
            "LauncherJournalPrimaryTextBrush",
            "LauncherJournalSecondaryTextBrush",
            "LauncherJournalCardFillBrush",
            "LauncherJournalCardStrokeBrush",
            "LauncherOnArtNormalPipStyle",
            "LauncherOnArtSelectedPipStyle",
            "LauncherFieldLabelTextStyle",
            "LauncherSectionTitleTextStyle",
            "LauncherBodyTextStyle",
            "LauncherReadOnlyTextBoxStyle",
            "LauncherMonospaceTextBoxStyle",
            "LauncherEditableMonospaceTextBoxStyle",
            "LauncherComponentBorderStyle",
            "LauncherGlassButtonStyle",
            "LauncherPrimaryActionButtonStyle",
            "LauncherIconButtonStyle",
            "LauncherPrimaryActionFillBrush",
            "LauncherPrimaryActionGradientBrush",
        };
        var sourceRoot = Path.Combine(FindRoot(), "Nikkiward");
        var sourceXaml = Directory
            .EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) &&
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .ToArray();

        foreach (var retiredKey in retiredKeys)
        {
            var consumers = sourceXaml
                .Where(file => file.Source.Contains(retiredKey, StringComparison.Ordinal))
                .Select(file => Path.GetRelativePath(FindRoot(), file.Path))
                .ToArray();
            Assert(
                consumers.Length == 0,
                $"{retiredKey} must be replaced by the shared theme authority: {string.Join(", ", consumers)}");
        }

        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var launcherKeys = sourceXaml
            .SelectMany(file => XDocument.Parse(file.Source).Descendants())
            .Select(element => (string?)element.Attribute(xaml + "Key"))
            .Where(key => key?.StartsWith("Launcher", StringComparison.Ordinal) == true)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        Assert(launcherKeys.Count == 0, "old launcher resources are retired");
        return Task.CompletedTask;
    }

    private static Task PrimaryActionResourceChainStaysIntact()
    {
        var palette = XDocument.Parse(ReadSource("Nikkiward", "Themes", "Palette.xaml"));
        Assert(
            FindResource(palette.Root!, "PrimaryActionSolidBrush")?.Name.LocalName == "SolidColorBrush" &&
            FindResource(palette.Root!, "PrimaryActionGradientBrush") is null,
            "the mutable primary action fill must be a single solid brush in the shared palette");

        var app = XDocument.Parse(ReadSource("Nikkiward", "App.xaml"));
        foreach (var themeName in new[] { "Light", "Dark" })
        {
            var fill = FindResource(FindThemeDictionary(app, themeName), "PrimaryActionFillBrush");
            Assert(
                string.Equals(
                    (string?)fill?.Attribute("ResourceKey"),
                    "PrimaryActionSolidBrush",
                    StringComparison.Ordinal),
                $"{themeName} primary action fill must proxy the mutable solid brush");
        }

        var highContrastFill = FindResource(
            FindThemeDictionary(app, "HighContrast"),
            "PrimaryActionFillBrush");
        Assert(
            ((string?)highContrastFill?.Attribute("Color"))?.Contains(
                "SystemColorHighlightColor",
                StringComparison.Ordinal) == true,
            "HighContrast primary action fill must use the system highlight color");

        var tokens = XDocument.Parse(ReadSource("Nikkiward", "Features", "Launcher", "Skins", "Cinema", "Tokens.xaml"));
        foreach (var mode in new[] { "HighContrast", "Dark", "Light" })
        {
            var theme = FindThemeDictionary(tokens, mode);
            foreach (var key in new[] { "HomeSurfaceBrush", "HomeInkBrush", "HomeStrokeBrush" })
                Assert(FindResource(theme, key) != null, $"{mode} needs {key}");
        }
        return Task.CompletedTask;
    }

    private static Task FocusedFeedbackSurfacesStayCompact()
    {
        var navigation = ReadSource("Nikkiward", "MainPage.ShellNavigation.cs");
        var chrome = ReadSource("Nikkiward", "MainPage.ContentNavigation.cs");
        var status = ReadSource("Nikkiward", "Features", "Diagnostics", "StatusPage.xaml");
        var statusCode = ReadSource("Nikkiward", "Features", "Diagnostics", "StatusPage.xaml.cs");
        var settings = ReadSource("Nikkiward", "Features", "Settings", "SettingsPage.xaml");
        var settingsCode = ReadSource("Nikkiward", "Features", "Settings", "SettingsPage.xaml.cs");
        var settingsNavigation = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "SettingsNavigationContext.cs");
        var settingsHome = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "SettingsHomeView.xaml");
        var gallerySettings = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "GallerySettingsView.xaml");
        var appearanceSettings = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "GeneralAppearanceSettingsView.xaml");
        var gallery = ReadSource("Nikkiward", "Pages", "GalleryPage.xaml");
        var galleryResources = ReadSource("Nikkiward", "Styles", "GalleryResources.xaml");

        var shell = ReadSource("Nikkiward", "Features", "Shell", "HomeNavigation.xaml");
        Assert(shell.IndexOf("Tag=\"gallery\"") < shell.IndexOf("Tag=\"gallery-favorites\"") &&
            navigation.Contains("SetShellNavigationSelection"), "gallery and favorites must keep their order and selection");
        Assert(
            navigation.Contains("case \"gallery-favorites\":", StringComparison.Ordinal) &&
            navigation.Contains(
                "ShowGalleryAsync(viewMode: GalleryViewMode.All)",
                StringComparison.Ordinal) &&
            navigation.Contains(
                "ShowGalleryAsync(viewMode: GalleryViewMode.Favorites)",
                StringComparison.Ordinal) &&
            chrome.Contains(
                "GalleryViewMode viewMode = GalleryViewMode.All",
                StringComparison.Ordinal) &&
            chrome.Contains("new GalleryNavigationContext(", StringComparison.Ordinal),
            "gallery and favorites navigation must reuse GalleryPage with explicit view modes");
        Assert(
            status.Contains("x:Name=\"TechnicalDetailsExpander\"", StringComparison.Ordinal) &&
            status.Contains("IsExpanded=\"False\"", StringComparison.Ordinal) &&
            statusCode.Contains("public void ResetView()", StringComparison.Ordinal) &&
            statusCode.Contains("TechnicalDetailsExpander.IsExpanded = false", StringComparison.Ordinal),
            "status technical evidence must default to collapsed on every entry");
        Assert(
            settings.Contains("游戏截图", StringComparison.Ordinal) &&
            settings.Contains("Tag=\"screenshot\"", StringComparison.Ordinal) &&
            settings.Contains("x:Name=\"GalleryItem\"", StringComparison.Ordinal) &&
            settings.Contains("Tag=\"gallery\"", StringComparison.Ordinal) &&
            settingsCode.Contains(
                "SettingsDestination.Screenshot => Select(_screenshotView",
                StringComparison.Ordinal) &&
            settingsCode.Contains(
                "SettingsDestination.Gallery => Select(_galleryView",
                StringComparison.Ordinal) &&
            settingsNavigation.Contains("Gallery,", StringComparison.Ordinal) &&
            settingsHome.Contains("Tag=\"gallery\"", StringComparison.Ordinal),
            "gallery and screenshot management must stay inside settings");
        Assert(
            gallerySettings.Contains("x:Name=\"ChooseRootButton\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"ResetRootButton\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"ClearCacheButton\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"RegisterNikkiGalleryButton\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"OpenNikkiGalleryButton\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"DisconnectNikkiGalleryButton\"", StringComparison.Ordinal) &&
            shell.Contains("Tag=\"photo-plugin\"") && shell.Contains("Visibility=\"Collapsed\""),
            "low-frequency gallery management must remain in settings and outside the primary rail");
        Assert(
            settings.Contains("OpenPaneLength=\"260\"", StringComparison.Ordinal) &&
            settings.Contains("<NavigationView.Header>", StringComparison.Ordinal) &&
            settings.Contains("MaxWidth=\"980\"", StringComparison.Ordinal) &&
            settings.Contains(
                "Target=\"SettingsNavigation.PaneDisplayMode\" Value=\"LeftCompact\"",
                StringComparison.Ordinal) &&
            settings.Contains("x:Name=\"PaneToggleButton\"", StringComparison.Ordinal) &&
            settings.Contains("Click=\"OnPaneToggleClicked\"", StringComparison.Ordinal) &&
            settings.Contains("x:Name=\"HeaderActions\"", StringComparison.Ordinal) &&
            settingsCode.Contains(
                "MastheadInteractionRegion => HeaderActions",
                StringComparison.Ordinal) &&
            settings.Contains(
                "Target=\"HeaderLayout.Margin\" Value=\"24,10,174,12\"",
                StringComparison.Ordinal) &&
            settings.IndexOf("x:Name=\"CloseButton\"", StringComparison.Ordinal) >
                settings.IndexOf("<NavigationView.Header>", StringComparison.Ordinal) &&
            settings.IndexOf("x:Name=\"CloseButton\"", StringComparison.Ordinal) <
                settings.IndexOf("</NavigationView.Header>", StringComparison.Ordinal),
            "application settings must keep a responsive master-detail hierarchy and reserve caption space");
        Assert(
            appearanceSettings.Contains("Value=\"0,0,0,1\"", StringComparison.Ordinal) &&
            appearanceSettings.Contains("Value=\"Transparent\"", StringComparison.Ordinal),
            "appearance sections must use flat separators instead of nested cards");
        Assert(
            gallery.Contains("<RowDefinition Height=\"48\" />", StringComparison.Ordinal) &&
            !gallery.Contains("x:Name=\"GalleryCommandIsland\"", StringComparison.Ordinal) &&
            gallery.Contains("TextTrimming=\"None\"", StringComparison.Ordinal) &&
            galleryResources.Contains(">48</x:Double>", StringComparison.Ordinal),
            "gallery title and commands must share an unclipped flat command row");
        return Task.CompletedTask;
    }

    private static Task GalleryProtectionManagementStaysScopedToSettings()
    {
        var gallerySettingsPath = Path.Combine(
            "Features",
            "Settings",
            "GallerySettingsView.xaml");
        var gallerySettings = ReadSource("Nikkiward", gallerySettingsPath);
        var gallerySettingsDocument = XDocument.Parse(gallerySettings);
        var galleryPage = ReadSource("Nikkiward", "Pages", "GalleryPage.xaml");
        var galleryPageCode = string.Join(
            Environment.NewLine,
            ReadSource("Nikkiward", "Pages", "GalleryPage.xaml.cs"),
            ReadSource("Nikkiward", "Pages", "GalleryPage.Lifecycle.cs"),
            ReadSource("Nikkiward", "Pages", "GalleryPage.Interactions.cs"));
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var automation = XNamespace.Get("using:Microsoft.UI.Xaml.Automation");

        XElement FindNamed(XDocument document, string name) => document
            .Descendants()
            .SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute(xaml + "Name"),
                    name,
                    StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"XAML element '{name}' was not found.");

        Assert(
            gallerySettingsDocument
                .Descendants()
                .Any(element =>
                    string.Equals(
                        (string?)element.Attribute("Text"),
                        "收藏保护",
                        StringComparison.Ordinal)),
            "gallery settings must expose the favorite protection section");
        foreach (var controlName in new[]
        {
            "ProtectionEnabledToggle",
            "ProtectionPathTextBox",
            "ChooseProtectionRootButton",
            "OpenProtectionRootButton",
            "VerifyProtectionButton",
            "CleanProtectionButton",
        })
        {
            var control = FindNamed(gallerySettingsDocument, controlName);
            Assert(
                !string.IsNullOrWhiteSpace(
                    (string?)control.Attribute(automation + "AutomationProperties.Name")),
                $"{controlName} must retain its stable x:Name automation id and accessible name");
        }

        var sourceRoot = Path.Combine(FindRoot(), "Nikkiward");
        var cleanEntryFiles = Directory
            .EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) &&
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(
                "x:Name=\"CleanProtectionButton\"",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .ToArray();
        Assert(
            cleanEntryFiles.Length == 1 &&
            string.Equals(
                cleanEntryFiles[0],
                gallerySettingsPath,
                StringComparison.OrdinalIgnoreCase),
            "favorite protection cleanup must have one explicit entry in gallery settings only");

        var shell = XDocument.Parse(ReadSource("Nikkiward", "Features", "Shell", "HomeNavigation.xaml"));
        Assert((string?)FindNamed(shell, "PhotoPluginNavigationItem").Attribute("Visibility") == "Collapsed",
            "the plugin entry must stay hidden");

        var retiredScreenshotEntries = Directory
            .EnumerateFiles(sourceRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) &&
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(
                "Tag=\"screenshot\"",
                StringComparison.Ordinal))
            .ToArray();
        Assert(
            retiredScreenshotEntries.Length == 2 &&
            retiredScreenshotEntries.Any(path => string.Equals(
                Path.GetFileName(path),
                "SettingsPage.xaml",
                StringComparison.OrdinalIgnoreCase)) &&
            retiredScreenshotEntries.Any(path => string.Equals(
                Path.GetFileName(path),
                "SettingsHomeView.xaml",
                StringComparison.OrdinalIgnoreCase)),
            "screenshot settings must have navigation and overview entries");
        Assert(
            !galleryPage.Contains("GalleryAdvancedToolsButton", StringComparison.Ordinal) &&
            !galleryPage.Contains("Label=\"高级工具\"", StringComparison.Ordinal) &&
            !galleryPageCode.Contains("OnGalleryAdvancedToolsClicked", StringComparison.Ordinal),
            "the retired advanced-tools command must not return to the gallery surface");
        Assert(
            galleryPageCode.Contains("_favoriteOperationGates.GetOrAdd", StringComparison.Ordinal) &&
            galleryPageCode.Contains("previous.Cancel()", StringComparison.Ordinal) &&
            galleryPageCode.Contains("EnsureCurrentFavoriteOperation", StringComparison.Ordinal) &&
            Regex.IsMatch(
                galleryPageCode,
                @"_favoriteProtectionService\.ProtectAsync\(\s*scopeId,\s*photo\.RelativePath,\s*photo\.FilePath,\s*operationCancellation\.Token\)",
                RegexOptions.CultureInvariant),
            "favorite protection must cancel stale work and commit only to its captured profile scope");
        return Task.CompletedTask;
    }

    private static Task GalleryAndSettingsSurfacesReflowAtNarrowWidths()
    {
        var gallery = XDocument.Parse(ReadSource("Nikkiward", "Pages", "GalleryPage.xaml"));
        var preview = XDocument.Parse(ReadSource("Nikkiward", "Pages", "GalleryPreviewView.xaml"));
        var main = XDocument.Parse(ReadSource("Nikkiward", "MainPage.xaml"));
        var mainCode = ReadSource("Nikkiward", "MainPage.xaml.cs");
        var launchSettings = XDocument.Parse(ReadSource(
            "Nikkiward",
            "Features",
            "Launcher",
            "LaunchSettingsPage.xaml"));
        var gallerySettings = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "GallerySettingsView.xaml");
        var settingsHome = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "SettingsHomeView.xaml");
        var settingsPage = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "SettingsPage.xaml");
        var pluginSettings = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "PluginSettingsView.xaml");
        var pluginSettingsCode = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "PluginSettingsView.xaml.cs");
        var about = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "AboutSettingsView.xaml");
        var aboutCode = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "AboutSettingsView.xaml.cs");
        var previewCode = ReadSource(
            "Nikkiward",
            "Pages",
            "GalleryPreviewView.xaml.cs");
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        XElement FindNamed(XDocument document, string name) => document
            .Descendants()
            .SingleOrDefault(element => string.Equals(
                (string?)element.Attribute(xaml + "Name"),
                name,
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"XAML element '{name}' was not found.");

        var thumbnail = FindNamed(gallery, "GalleryThumbnail");
        Assert(
            string.Equals((string?)thumbnail.Attribute("Stretch"), "Uniform", StringComparison.Ordinal),
            "gallery thumbnails must show the complete photo instead of cropping its edges");
        var emptyState = FindNamed(gallery, "GalleryEmptyState");
        Assert(
            emptyState.Attribute("Width") is null &&
            string.Equals((string?)emptyState.Attribute("MaxWidth"), "420", StringComparison.Ordinal),
            "the gallery empty state must shrink below its preferred width");
        Assert(
            gallery.Descendants().Any(element =>
                string.Equals((string?)element.Attribute(xaml + "Name"), "NarrowGallery", StringComparison.Ordinal)) &&
            gallery.ToString().Contains(
                "Target=\"GalleryGridView.Padding\" Value=\"12,0,12,12\"",
                StringComparison.Ordinal),
            "the gallery must reduce fixed desktop padding at its narrow breakpoint");

        var infoPanel = FindNamed(preview, "GalleryInfoPanel");
        Assert(
            infoPanel.Attribute("Width") is null &&
            infoPanel.Attribute("MaxWidth") is not null,
            "the preview information pane must be allowed to shrink with the viewport");
        Assert(
            preview.Descendants().Any(element =>
                string.Equals((string?)element.Attribute(xaml + "Name"), "NarrowPreview", StringComparison.Ordinal)) &&
            preview.ToString().Contains(
                "Target=\"GalleryPreviewFileNameText.Visibility\" Value=\"Collapsed\"",
                StringComparison.Ordinal) &&
            preview.ToString().Contains(
                "SizeChanged=\"OnGalleryPreviewViewportSizeChanged\"",
                StringComparison.Ordinal) &&
            previewCode.Contains("OnGalleryPreviewViewportSizeChanged", StringComparison.Ordinal) &&
            previewCode.Contains("ZoomFactor <= 1.001f", StringComparison.Ordinal),
            "the preview must simplify its toolbar and keep fit-to-window images centered after resize");

        Assert(
            gallerySettings.Contains("x:Name=\"NarrowGallerySettings\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"GalleryRootActions\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"ProtectionPrimaryActions\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"NikkiGalleryActions\"", StringComparison.Ordinal) &&
            gallerySettings.Contains(
                "Target=\"GalleryRootActions.Orientation\" Value=\"Vertical\"",
                StringComparison.Ordinal),
            "gallery settings commands must stack instead of clipping at narrow widths");
        Assert(
            settingsHome.Contains("x:Name=\"NarrowSettingsHome\"", StringComparison.Ordinal) &&
            settingsHome.Contains("x:Name=\"PrimarySettingsGrid\"", StringComparison.Ordinal) &&
            settingsHome.Contains("x:Name=\"PrimarySettingsSecondColumn\"", StringComparison.Ordinal) &&
            settingsHome.Contains(
                "Target=\"PrimarySettingsSecondColumn.Width\" Value=\"0\"",
                StringComparison.Ordinal),
            "the settings overview must collapse its two-column cards to one column");
        Assert(
            settingsPage.Contains("HorizontalContentAlignment=\"Stretch\"", StringComparison.Ordinal) &&
            settingsPage.Contains("x:Name=\"SettingsContentViewport\"", StringComparison.Ordinal) &&
            settingsPage.Contains("x:Name=\"SettingsContentWidthColumn\"", StringComparison.Ordinal) &&
            settingsPage.Contains("x:Name=\"SettingsContentColumn\"", StringComparison.Ordinal) &&
            settingsPage.Contains("MaxWidth=\"980\"", StringComparison.Ordinal) &&
            settingsPage.Contains("x:Name=\"ContentHost\"", StringComparison.Ordinal) &&
            settingsPage.Contains("HorizontalAlignment=\"Stretch\"", StringComparison.Ordinal),
            "the settings viewport must constrain narrow content while its bounded desktop column stays aligned with the header");
        Assert(
            settingsHome.Contains("x:Key=\"SettingsHomeCardButtonStyle\"", StringComparison.Ordinal) &&
            settingsHome.Contains("<Setter Property=\"Height\" Value=\"Auto\" />", StringComparison.Ordinal) &&
            settingsHome.Contains("<Setter Property=\"MinHeight\" Value=\"72\" />", StringComparison.Ordinal) &&
            settingsHome.Contains("<Setter Property=\"HorizontalAlignment\" Value=\"Stretch\" />", StringComparison.Ordinal) &&
            Count(settingsHome, "Style=\"{StaticResource SettingsHomeCardButtonStyle}\"") == 14 &&
            !settingsHome.Contains("MinHeight=\"52\"", StringComparison.Ordinal),
            "settings overview cards must fill their columns and allow both title and supporting text to use their full line height");
        Assert(
            pluginSettings.Contains("x:Name=\"PluginActions\"", StringComparison.Ordinal) &&
            pluginSettingsCode.Contains("CompactLayoutThreshold", StringComparison.Ordinal) &&
            pluginSettingsCode.Contains("SizeChanged += OnSizeChanged", StringComparison.Ordinal) &&
            pluginSettingsCode.Contains("ApplyLayout(e.NewSize.Width)", StringComparison.Ordinal) &&
            pluginSettingsCode.Contains("Grid.SetRow(PluginActions, useCompactLayout ? 1 : 0)", StringComparison.Ordinal) &&
            pluginSettingsCode.Contains("Grid.SetColumnSpan(PluginActions, useCompactLayout ? 3 : 1)", StringComparison.Ordinal) &&
            pluginSettingsCode.Contains("PluginActions.Orientation = useCompactLayout", StringComparison.Ordinal) &&
            pluginSettingsCode.Contains("PhotoPluginImportButton.HorizontalAlignment = buttonAlignment", StringComparison.Ordinal) &&
            pluginSettingsCode.Contains("PhotoPluginOpenButton.HorizontalAlignment = buttonAlignment", StringComparison.Ordinal) &&
            pluginSettingsCode.Contains("PhotoPluginUninstallButton.HorizontalAlignment = buttonAlignment", StringComparison.Ordinal),
            "plugin commands must move below the card details and stretch vertically when the settings viewport is narrow");

        Assert(
            !about.Contains("SelectionChanged=\"OnUpdateChannelChanged\"", StringComparison.Ordinal) &&
            Regex.IsMatch(
                aboutCode,
                @"InitializeComponent\(\);\s*UpdateChannelSelector\.SelectionChanged\s*\+=\s*OnUpdateChannelChanged;",
                RegexOptions.CultureInvariant),
            "the update selector must subscribe only after all named About controls exist");
        Assert(
            about.Contains("x:Name=\"NarrowAboutSettings\"", StringComparison.Ordinal) &&
            about.Contains("x:Name=\"UpdateActions\"", StringComparison.Ordinal) &&
            about.Contains(
                "Target=\"UpdateActions.Orientation\" Value=\"Vertical\"",
                StringComparison.Ordinal),
            "About update actions must reflow at narrow widths");

        var launchSettingsFrame = FindNamed(main, "LaunchSettingsFrame");
        Assert(
            launchSettingsFrame.Attribute("Width") is null &&
            launchSettingsFrame.Attribute("Height") is null &&
            string.Equals(
                (string?)launchSettingsFrame.Attribute("MaxWidth"),
                "1120",
                StringComparison.Ordinal) &&
            string.Equals(
                (string?)launchSettingsFrame.Attribute("MaxHeight"),
                "690",
                StringComparison.Ordinal) &&
            main.ToString().Contains("x:Name=\"NarrowLaunchSettingsHost\"", StringComparison.Ordinal) &&
            main.ToString().Contains(
                "Target=\"LaunchSettingsFrame.Margin\" Value=\"8,48,8,8\"",
                StringComparison.Ordinal) &&
            mainCode.Contains("ApplyLaunchSettingsHostLayout(e.NewSize.Width)", StringComparison.Ordinal) &&
            mainCode.Contains("new Thickness(8, 48, 8, 8)", StringComparison.Ordinal),
            "the launch-settings host must shrink with the shell instead of retaining a fixed desktop size");
        Assert(
            launchSettings.ToString().Contains(
                "x:Name=\"NarrowLaunchSettings\"",
                StringComparison.Ordinal) &&
            launchSettings.ToString().Contains(
                "Target=\"LaunchSettingsNavigationView.PaneDisplayMode\" Value=\"LeftCompact\"",
                StringComparison.Ordinal) &&
            launchSettings.ToString().Contains(
                "Target=\"BasicDetailsSecondColumn.Width\" Value=\"0\"",
                StringComparison.Ordinal) &&
            launchSettings.ToString().Contains(
                "Target=\"BackgroundActions.Orientation\" Value=\"Vertical\"",
                StringComparison.Ordinal) &&
            launchSettings.ToString().Contains(
                "HorizontalContentAlignment=\"Stretch\"",
                StringComparison.Ordinal),
            "the independent launch-settings surface must switch to compact navigation and single-column content");
        var launchSettingsCode = ReadSource(
            "Nikkiward",
            "Features",
            "Launcher",
            "LaunchSettingsPage.xaml.cs");
        Assert(
            launchSettingsCode.Contains("useCompactLayout = width < CompactLayoutWidth", StringComparison.Ordinal) &&
            launchSettingsCode.Contains("NavigationViewPaneDisplayMode.LeftCompact", StringComparison.Ordinal) &&
            launchSettingsCode.Contains("Grid.SetRow(LocateGameButton, useCompactLayout ? 1 : 0)", StringComparison.Ordinal) &&
            launchSettingsCode.Contains("BackgroundActions.Orientation = useCompactLayout", StringComparison.Ordinal),
            "the launch-settings runtime must project its own width when page-level visual states are unavailable");
        foreach (var itemName in new[]
                 {
                     "BasicNavigationItem",
                     "ArgumentsNavigationItem",
                     "BackgroundNavigationItem",
                     "PackageNavigationItem",
                 })
        {
            var navigationItem = FindNamed(launchSettings, itemName);
            Assert(
                navigationItem.Elements().Any(element =>
                    string.Equals(
                        element.Name.LocalName,
                        "NavigationViewItem.Icon",
                        StringComparison.Ordinal)),
                $"{itemName} must remain identifiable when the navigation pane is compact");
        }
        foreach (var imageName in new[] { "DefaultBackgroundPreviewImage", "BackgroundPreviewImage" })
        {
            var image = FindNamed(launchSettings, imageName);
            Assert(
                string.Equals((string?)image.Attribute("Stretch"), "Uniform", StringComparison.Ordinal),
                $"{imageName} must show the complete background at narrow widths");
        }
        return Task.CompletedTask;
    }

    private static Task LauncherCloseActionStaysSeparate()
    {
        var launcher = ReadSource("Nikkiward", "Features", "Launcher", "Skins", "Cinema", "CinemaView.xaml");
        var launcherCode = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml.cs");
        var commands = ReadSource("Nikkiward", "MainPage.LaunchCommands.cs");
        var hosting = ReadSource("Nikkiward", "MainPage.Hosting.cs");

        Assert(
            launcher.Contains("Tag=\"close\"") && launcher.Contains("HomeCloseVisibility") && launcher.Contains("HomeCloseEnabled"),
            "the launcher needs an independent close-game button bound to runtime state");
        Assert(
            launcherCode.Contains("CloseGameRequested", StringComparison.Ordinal) &&
            commands.Contains("CloseOfficialAssistedGameAsync", StringComparison.Ordinal) &&
            hosting.Contains("CloseGameRequested += OnLauncherCloseGameRequested", StringComparison.Ordinal),
            "the close button must use its own page event and command path");
        return Task.CompletedTask;
    }

    private static Task LauncherRuntimePollingReturnsToLaunchState()
    {
        var viewModel = ReadSource("Nikkiward", "ViewModels", "MainPageViewModel.cs");
        var coordinator = ReadSource(
            "Nikkiward",
            "ViewModels",
            "OfficialAssistedLaunchCoordinator.cs");
        var hosting = ReadSource("Nikkiward", "MainPage.Hosting.cs");
        var commands = ReadSource("Nikkiward", "MainPage.LaunchCommands.cs");

        Assert(
            viewModel.Contains("RefreshOfficialAssistedRuntimeAsync", StringComparison.Ordinal) &&
            viewModel.Contains("_activeProcessBinding = null", StringComparison.Ordinal) &&
            viewModel.Contains("游戏进程已退出", StringComparison.Ordinal),
            "natural game exit must clear the active binding and publish an exited state");
        Assert(
            coordinator.Contains("IsDescendantOf", StringComparison.Ordinal) &&
            coordinator.Contains("process.Kill(entireProcessTree: false)", StringComparison.Ordinal) &&
            coordinator.Contains("GameProcessPaths", StringComparison.Ordinal) &&
            coordinator.Contains("AuxiliaryProcessPaths", StringComparison.Ordinal) &&
            coordinator.Contains("RootExecutablePath", StringComparison.Ordinal),
            "process control must stay scoped to the bound attempt process identities");
        Assert(
            hosting.Contains("_launchStateTimer.Start()", StringComparison.Ordinal) &&
            commands.Contains("ViewModel.RefreshOfficialAssistedRuntimeAsync", StringComparison.Ordinal),
            "the page must periodically refresh runtime state while it is loaded");
        return Task.CompletedTask;
    }

    private static Task BusyCompletionRepublishesLauncherStateProjection()
    {
        var viewModel = ReadSource("Nikkiward", "ViewModels", "MainPageViewModel.cs");
        var start = viewModel.IndexOf("public bool IsBusy", StringComparison.Ordinal);
        var end = viewModel.IndexOf("public bool CanRefresh", start, StringComparison.Ordinal);
        Assert(start >= 0 && end > start, "the IsBusy property must remain discoverable");

        var isBusyProperty = viewModel[start..end];
        Assert(
            isBusyProperty.Contains("NotifyLaunchStateChanged();", StringComparison.Ordinal),
            "every IsBusy transition must republish the launcher state projection");
        return Task.CompletedTask;
    }

    private static Task ExternalChannelsPublishBoundRuntimeLifecycle()
    {
        var channelStore = ReadSource(
            "Nikkiward",
            "ViewModels",
            "MainPageViewModel.ChannelStore.cs");
        Assert(
            channelStore.Contains("ExternalChannelProcessBindingFactory.TryCreate", StringComparison.Ordinal) &&
            channelStore.Contains("_activeProcessBinding = binding", StringComparison.Ordinal) &&
            channelStore.Contains("WaitForExternalChannelGameAsync", StringComparison.Ordinal) &&
            channelStore.Contains("_isOfficialAssistedRunning = true", StringComparison.Ordinal),
            "direct channel launches must bind and observe the current profile game before publishing running state");
        return Task.CompletedTask;
    }

    private static Task ProfilePickerUsesSingleAnchoredServerSurface()
    {
        var profile = ReadSource("Nikkiward", "MainPage.Profile.cs");
        var shell = ReadSource("Nikkiward", "MainPage.xaml");
        Assert(profile.Contains("new ProfilePickerView(ViewModel)") && profile.Contains("_profileFlyout.ShowAt(anchor)") &&
            profile.Contains("picker.ProfileSelected += OnProfileSelected") && !shell.Contains("ProfileQuickSwitchRail"),
            "all home skins must open the shared profile selector at the invoking button");
        var pickerCode = ReadSource("Nikkiward", "Features", "Profile", "ProfilePickerView.xaml.cs");
        Assert(
            pickerCode.Contains("\"中国服\"", StringComparison.Ordinal) &&
            pickerCode.Contains("\"国际服\"", StringComparison.Ordinal) &&
            pickerCode.Contains("\"哔哩哔哩\"", StringComparison.Ordinal) &&
            pickerCode.Contains("ViewModel.DeveloperModeEnabled", StringComparison.Ordinal) &&
            pickerCode.Contains("ServerCountText.Text = $\"{ServerOptions.Count} 个服务器\"", StringComparison.Ordinal),
            "the picker must project the three user-facing channels and keep details developer-gated");
        return Task.CompletedTask;
    }

    private static Task ProfileSelectionReturnsToLauncher()
    {
        var profile = ReadSource("Nikkiward", "MainPage.Profile.cs");
        var selectProfile = FindMethod(profile, "SelectProfileAsync");
        var viewModel = ReadSource("Nikkiward", "ViewModels", "MainPageViewModel.cs");
        var selectCandidate = FindMethod(viewModel, "SelectProfileAsync");
        Assert(
            selectProfile.Contains("var selectionChanged = await ViewModel.SelectProfileAsync", StringComparison.Ordinal) &&
            selectProfile.Contains("if (!selectionChanged)", StringComparison.Ordinal) &&
            selectProfile.IndexOf("if (!selectionChanged)", StringComparison.Ordinal) <
                selectProfile.IndexOf("ShowLauncher();", StringComparison.Ordinal) &&
            !selectProfile.Contains("CloseProfileOverlay();", StringComparison.Ordinal),
            "the profile page must return to LauncherPage only after the requested selection succeeds");
        Assert(
            viewModel.Contains("public async Task<bool> SelectProfileAsync", StringComparison.Ordinal) &&
            selectCandidate.Contains("_selectedCandidate.ProfileId", StringComparison.Ordinal) &&
            selectCandidate.Contains("return selectionSucceeded", StringComparison.Ordinal),
            "profile selection must report whether the requested fresh candidate became active");
        return Task.CompletedTask;
    }

    private static string ReadSource(params string[] segments)
    {
        var root = FindRoot();
        var path = segments.Aggregate(root, Path.Combine);
        return File.ReadAllText(path);
    }

    private static XElement FindThemeDictionary(XDocument document, string themeName)
    {
        var presentation = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        return document
            .Descendants(presentation + "ResourceDictionary")
            .SingleOrDefault(element =>
                string.Equals((string?)element.Attribute(xaml + "Key"), themeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Theme dictionary '{themeName}' was not found.");
    }

    private static XElement? FindResource(XElement dictionary, string resourceKey)
    {
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        return dictionary
            .Elements()
            .SingleOrDefault(element =>
                string.Equals((string?)element.Attribute(xaml + "Key"), resourceKey, StringComparison.Ordinal));
    }

    private static (byte R, byte G, byte B) ReadBrushColor(XElement dictionary, string resourceKey)
    {
        var resource = FindResource(dictionary, resourceKey)
            ?? throw new InvalidOperationException($"Brush '{resourceKey}' was not found.");
        var value = (string?)resource.Attribute("Color")
            ?? throw new InvalidOperationException($"Brush '{resourceKey}' has no Color value.");
        if (!value.StartsWith('#') || (value.Length != 7 && value.Length != 9))
        {
            throw new InvalidOperationException($"Brush '{resourceKey}' must use a literal RGB color.");
        }

        var rgb = value.Length == 9 ? value[3..] : value[1..];
        if (value.Length == 9 && !value.AsSpan(1, 2).Equals("FF", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Brush '{resourceKey}' must be opaque.");
        }

        return (
            Convert.ToByte(rgb[0..2], 16),
            Convert.ToByte(rgb[2..4], 16),
            Convert.ToByte(rgb[4..6], 16));
    }

    private static double ContrastRatio(
        (byte R, byte G, byte B) foreground,
        (byte R, byte G, byte B) background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        var lighter = Math.Max(foregroundLuminance, backgroundLuminance);
        var darker = Math.Min(foregroundLuminance, backgroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance((byte R, byte G, byte B) color) =>
        (0.2126 * Linearize(color.R)) +
        (0.7152 * Linearize(color.G)) +
        (0.0722 * Linearize(color.B));

    private static double Linearize(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static string[] FindThemeResourceConsumers(string resourceKey)
    {
        var root = Path.Combine(FindRoot(), "Nikkiward");
        var binding = $"{{ThemeResource {resourceKey}}}";
        return Directory
            .EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => XDocument
                .Load(path)
                .Descendants()
                .Attributes()
                .Any(attribute => attribute.Value.Contains(binding, StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();
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

        throw new InvalidOperationException("The crossfade stop path must be inside a method.");
    }

    private static string FindMethod(string source, string methodName)
    {
        var declaration = Regex.Match(
            source,
            $@"(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?[A-Za-z0-9_<>,?\[\]\s]+\s+{Regex.Escape(methodName)}\s*\([^;{{}}]*\)\s*\{{",
            RegexOptions.CultureInvariant);
        if (!declaration.Success)
        {
            throw new InvalidOperationException($"Method '{methodName}' was not found.");
        }

        return FindContainingMethod(source, declaration.Index + declaration.Length);
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

    private static void Assert(bool condition, string because)
    {
        if (!condition)
        {
            throw new InvalidOperationException(because);
        }
    }
}
