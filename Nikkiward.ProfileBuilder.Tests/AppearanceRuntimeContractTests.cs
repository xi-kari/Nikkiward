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
        ("static visual tokens have one live runtime authority", StaticVisualTokensHaveOneAuthority),
        ("CJK page titles retain their full line bounds", CjkPageTitlesRetainFullLineBounds),
        ("custom background exposes the motion wallpaper importer", CustomBackgroundExposesMotionImporter),
        ("Wallpaper package recovery persists canonical paths and authoritative modes", WallpaperPackageRecoveryPersistsCanonicalState),
        ("custom background exposes the artwork title editor", CustomBackgroundExposesArtworkTitleEditor),
        ("high contrast focus uses the system focus color", HighContrastFocusUsesSystemColor),
        ("every custom transition consumes the live motion authority", CustomTransitionsConsumeMotionAuthority),
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
        (nameof(ReminderDeliverySurvivesSaveFailure), ReminderDeliverySurvivesSaveFailure),
        (nameof(PlannerCommandsAndPersistence), PlannerCommandsAndPersistence),
        (nameof(SessionPhotosAndHomeFields), SessionPhotosAndHomeFields),
        (nameof(PageStateFailureReportsReason), PageStateFailureReportsReason),
        (nameof(SafeHomeUsesLaunchState), SafeHomeUsesLaunchState),
        (nameof(FailedPageCanRetryOrReturnHome), FailedPageCanRetryOrReturnHome),
        (nameof(PageNavigationBeforeState), PageNavigationBeforeState),
        (nameof(WebPageFallbackSurvivesLaterErrors), WebPageFallbackSurvivesLaterErrors),
        (nameof(WebHomeStateMatchesLauncher), WebHomeStateMatchesLauncher),
        (nameof(HomeOverlayFixtureMatchesNative), HomeOverlayFixtureMatchesNative),
        (nameof(HomeOverlayDistinguishesFailedChecks), HomeOverlayDistinguishesFailedChecks),
        (nameof(HomeOverlayShowsNativeChecks), HomeOverlayShowsNativeChecks),
        (nameof(HomeOverlayActionsAreBounded), HomeOverlayActionsAreBounded),
        (nameof(HomeOverlayPreviewRejectsActions), HomeOverlayPreviewRejectsActions),
        (nameof(WebHomeActionsAreBounded), WebHomeActionsAreBounded),
        (nameof(AtelierPageUsesBridge), AtelierPageUsesBridge),
        (nameof(WebHomeNavigationIsLocal), WebHomeNavigationIsLocal),
        (nameof(HomePreviewGuardsAllCommands), HomePreviewGuardsAllCommands),
        ("home skin catalog reserves five extension points", HomeSkinCatalogReservesAllDesigns),
        ("launcher resources use the shared theme authority outside the shell rail", LauncherResourcesUseSharedThemeAuthority),
        ("the primary action resource chain stays live and high contrast aware", PrimaryActionResourceChainStaysIntact),
        ("launcher close action stays separate from the launch action", LauncherCloseActionStaysSeparate),
        ("launcher runtime polling returns to the launch state", LauncherRuntimePollingReturnsToLaunchState),
        ("busy completion republishes the launcher state projection", BusyCompletionRepublishesLauncherStateProjection),
        ("external channels publish a bound runtime lifecycle", ExternalChannelsPublishBoundRuntimeLifecycle),
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

    private static Task StaticVisualTokensHaveOneAuthority()
    {
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
            "GlassIslandBrush",
            "GlassOverlayBrush",
            "PaperGlassBrush",
            "PaperGlassStrongBrush",
            "IslandEdgeHighlightBrush",
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

    private static Task CustomBackgroundExposesMotionImporter()
    {
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
            overlay.Contains("ChooseWallpaperAsync(null)", StringComparison.Ordinal) &&
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
        return Task.CompletedTask;
    }

    private static Task CustomBackgroundExposesArtworkTitleEditor()
    {
        var overlay = ReadSource("Nikkiward", "MainPage.Overlays.cs");
        Assert(overlay.Contains("WithArtworkTitle(request.ArtKey, request.Title!)"), "artwork title persistence");
        return Task.CompletedTask;
    }

    private static Task CjkPageTitlesRetainFullLineBounds()
    {
        var journal = XDocument.Parse(ReadSource(
            "Nikkiward",
            "Features",
            "Journal",
            "JournalPage.xaml"));

        AssertFullLineTitle(journal, "奇想手账");
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

    private static Task ReminderDeliverySurvivesSaveFailure()
    {
        DateTimeOffset now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Local);
        var daily = new LocalReminder("daily", "拍照", "daily", new(12, 0), null, null, now);
        var once = daily with { Id = "once", Repeat = "once", Date = new(2026, 11, 26) };
        int saves = 0;
        var delivered = new List<LocalReminder>();
        PlannerData? saved = null;
        var session = new PlannerSession(PlannerData.Empty with { Reminders = [once, daily] }, data =>
        {
            if (++saves <= 2) throw new IOException("disk full");
            saved = data;
        });
        for (int i = 0; i < 2; i++)
        {
            try { session.Refresh(now.AddSeconds(10 * i), default, delivered.AddRange); throw new Exception("save failure hidden"); }
            catch (IOException ex) when (ex.Message == "disk full") { }
        }
        Assert(session.Refresh(now.AddSeconds(20), default, delivered.AddRange) && saves == 3 &&
            delivered.SequenceEqual(new[] { once, daily }) && saved!.Reminders[0].Due == null, "retry saves without resending");
        Assert(!session.Refresh(now.AddSeconds(30), default, delivered.AddRange) && saves == 3, "successful save clears retry");
        session.Refresh(now.AddDays(1), default, delivered.AddRange);
        Assert(delivered.Count == 3 && delivered[2].Id == daily.Id && delivered[2].Due == now.AddDays(1), "next occurrence fires");
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

    private static Task PageStateFailureReportsReason()
    {
        var vm = new MainPageViewModel(new JsonUserSettingsStore(Path.GetTempPath()),
            new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
        void Check(Func<object> state, string expected)
        {
            var adapter = new Nikkiward.Features.Pages.PageAdapter("settings", [], state, (_, _) => Task.CompletedTask);
            bool posted = false;
            string? failure = null;
            adapter.PostState("settings", "cinema", "dark", _ => posted = true, reason =>
            {
                failure = reason;
                vm.ReportWebPageFallback("settings", reason);
            });
            Assert(!posted && failure?.Contains(expected) == true, "failed snapshot must request fallback without posting");
            var diagnostic = HomeOverlayState.From(vm).WebPageFallback;
            Assert(diagnostic.Contains("settings") && diagnostic.Contains(expected) &&
                !diagnostic.Contains(ApplicationDataPaths.Root), "drawer retains route and sanitized exception");
        }
        Check(() => throw new InvalidOperationException("取状态失败 " + ApplicationDataPaths.Root),
            "InvalidOperationException: 取状态失败");
        Check(() => new { unsupported = typeof(int) }, "NotSupportedException:");
        return Task.CompletedTask;
    }

    private static Task SafeHomeUsesLaunchState()
    {
        var view = ReadSource("Nikkiward", "Features", "Launcher", "Skins", "Cinema", "CinemaView.xaml");
        foreach (var binding in new[] { "HomeLaunchLabel", "HomeLaunchEnabled", "HomeLaunchDetail", "ProfileDisplayName", "HomeFallback" })
            Assert(view.Contains("{Binding " + binding + "}"), binding);
        Assert(view.Contains("重试新界面") && !view.Contains("nav-"), "safe home actions");
        return Task.CompletedTask;
    }

    private static Task FailedPageCanRetryOrReturnHome()
    {
        var shell = ReadSource("Nikkiward", "MainPage.WebPages.cs");
        var host = ReadSource("Nikkiward", "Features", "Pages", "WebPagesView.cs");
        Assert(shell.Contains("typeof(WebPageErrorView)") && shell.Contains("() => ShowWebPage(page), ShowLauncher"),
            "failure actions target the failed destination and home");
        Assert(host.Contains("NavigationCacheMode.Disabled") && host.Contains("Web.Close()"), "failed browser is retired");
        return Task.CompletedTask;
    }

    private static Task PageNavigationBeforeState()
    {
        foreach (var route in new[] { "home", "gallery", "favorites", "journal", "wish", "settings" })
        {
            var message = JsonSerializer.SerializeToElement(new { action = "navigate", to = route });
            Assert(Nikkiward.Features.Pages.PageRoutes.NavigationDestination(message, null, false) == route, route);
        }
        foreach (var json in new[] { """{"action":"import","to":"wish"}""", """{"action":"navigate","to":"unknown"}""",
            """{"action":"navigate","to":123}""" })
        {
            using var doc = JsonDocument.Parse(json);
            Assert(Nikkiward.Features.Pages.PageRoutes.NavigationDestination(doc.RootElement, null, false) == null, "shell navigation whitelist");
        }
        var scoped = JsonSerializer.SerializeToElement(new { page = "wish", action = "navigate", to = "gallery" });
        Assert(Nikkiward.Features.Pages.PageRoutes.NavigationDestination(scoped, "wish", true) == "gallery" &&
            Nikkiward.Features.Pages.PageRoutes.NavigationDestination(scoped, "wish", false) == null &&
            Nikkiward.Features.Pages.PageRoutes.NavigationDestination(scoped, "journal", true) == null,
            "module navigation requires a ready matching route");
        var script = ReadSource("Nikkiward", "Web", "pages", "pages.js");
        Assert(script.Contains("function navigate(to) { bridge?.postMessage({action:'navigate', to}); }") &&
            script.Contains("b.onclick = () => navigate(to)") && script.Contains("['前往',label,() => navigate(to)]"),
            "top bar and command panel send navigation before a module is mounted");
        return Task.CompletedTask;
    }

    private static Task WebPageFallbackSurvivesLaterErrors()
    {
        var vm = new MainPageViewModel(new JsonUserSettingsStore(Path.GetTempPath()),
            new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
        vm.WebPageFallback = $"功能页面加载失败（wish）：{ApplicationDataPaths.Root}";
        vm.ReportUiError("后续错误");
        var drawer = HomeOverlayState.From(vm);
        Assert(drawer.WebPageFallback == "功能页面加载失败（wish）：%NIKKIWARD_DATA%" &&
            drawer.Technical.LastErrorText.Contains("后续错误"), "retain the redacted fallback alongside a later error");
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

    private static Task HomeOverlayShowsNativeChecks()
    {
        var vm = new MainPageViewModel(new JsonUserSettingsStore(Path.GetTempPath()),
            new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(MainPageViewModel).GetField("_checkingInstallation", fields)!.SetValue(vm, false);
        var result = typeof(MainPageViewModel).GetField("_unselectedState", fields)!;
        foreach (var kind in Enum.GetValues<LaunchButtonState>())
        {
            result.SetValue(vm, kind);
            var state = WebHomeState.Create(vm, "dark", 0xffabcdef, false, []);
            Assert(state.State == kind && state.LaunchLabel == kind.Label() &&
                state.Detail == vm.LaunchButtonDetail && state.Detail.Length > 0, kind + " retains native reason");
            var checks = state.Overlay.Checks;
            bool? expectedGate = kind switch {
                LaunchButtonState.Ready => true,
                LaunchButtonState.Checking or LaunchButtonState.Launching or LaunchButtonState.Running => null,
                _ => false
            };
            Assert(checks.Gate.Passed == expectedGate, kind + " check result");
            AssertPlayerCopy(state);
            if (expectedGate == false)
                Assert(state.Overlay.Conclusion.Contains('请') || state.Overlay.Conclusion.Contains("再试"),
                    kind + " includes a next step");
            Assert(!state.OfficialFlow, kind + " requires verified official capability");
            Assert((state with { Overlay = state.Overlay with { OfficialCapability = true } }).OfficialFlow ==
                (kind == LaunchButtonState.Ready), kind + " official action");
            Assert(state.Overlay.Technical.ProviderPreflightText == vm.ProviderPreflightText &&
                state.Overlay.Technical.LatestControlledReceiptText == vm.LatestControlledReceiptText &&
                state.Overlay.Technical.ProviderArgumentVector == vm.ProviderArgumentVector &&
                !state.Overlay.CanOpenRoot, "native diagnostics and unconfigured path");
        }
        return Task.CompletedTask;
    }

    private static void AssertPlayerCopy(WebHomeState state)
    {
        var o = state.Overlay;
        var text = string.Join(" ", o.Conclusion, o.SelectedChannelLaunchRoute, o.Signature, o.ArgumentsIntro,
            o.Arguments, o.ChannelText, o.Checks.Channel.Text, o.Checks.Integrity.Text, o.Checks.Processes.Text,
            o.Checks.Signature.Text, o.Checks.Gate.Text, string.Join(" ", state.Components.Select(c => c.DisplayName + c.StatusText)));
        foreach (var word in new[] { "ExecutionGate", "ExecutionEnabled", "OfficialAssisted", "attempt", "plan", "0x",
            "catalog", "contract", "provider", "bootstrap", "ObservedSuccess", "LOGIN_STATUS", "UAC", "MaximumCapability" })
            Assert(!text.Contains(word, StringComparison.OrdinalIgnoreCase), word + " in " + state.State);
    }

    private static MainPageViewModel OverlaySampleModel(bool blocked)
    {
        const string root = @"F:\游戏安装目录\长期保留的历史版本与多渠道安装档案\InfinityNikki";
        var contract = LaunchProviderCatalog.CreateCnWindows("1.3.2");
        var profile = new LaunchProfile { ProfileId = "cn", DisplayName = "国服 · 官方", GameRootPath = Path.Combine(root, "InfinityNikki Game") };
        var candidate = new InstallationProfileCandidate {
            ProfileId = profile.ProfileId, Profile = profile, GameRootPath = profile.GameRootPath, LauncherRootPath = root,
            State = InstallationCandidateState.ReadyForStaticVerification,
            Identity = new() { RegionFamily = RegionFamily.MainlandChina, DistributionChannel = DistributionChannel.Official,
                AccountAuthority = AccountAuthority.Papergames },
            Provider = new() { ProviderId = contract.ContractId, BackendExecutablePath = Path.Combine(root, contract.BackendRelativeExecutablePath),
                WorkingDirectory = root, ArgumentList = contract.ArgumentList }
        };
        var vm = new MainPageViewModel(new JsonUserSettingsStore(Path.GetTempPath()), new WindowsInstallationInspector(), new RedactedDiagnosticReportExporter());
        void Set(string name, object value) => typeof(MainPageViewModel).GetField(name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(vm,value);
        Set("_checkingInstallation",false);
        Set("_profile",profile);
        Set("_selectedCandidate",candidate);
        Set("_snapshot", new LaunchSnapshot { ProfileId = profile.ProfileId });
        var receipts = new List<PreflightComponentResult>();
        foreach (var required in contract.RequiredComponents)
        {
            var path = Path.Combine(required.RootRole == "GameRoot" ? profile.GameRootPath : root,required.RelativePath);
            vm.Components.Add(new ComponentItemViewModel(new ComponentVerification {
                ComponentId = required.ComponentId == "anti-cheat-artifact" ? "anti-cheat" : required.ComponentId,
                Exists = true, InspectionSucceeded = true, FilePath = path, FileVersion = "1.3.2",
                SignatureStatus = AuthenticodeSignatureStatus.Valid, SignatureStatusCode = "0x00000000", Sha256 = new string('A',64)
            }));
            if (!blocked || receipts.Count == 0)
                receipts.Add(new() { ComponentId = required.ComponentId, FilePath = path, Passed = !blocked,
                    SignatureStatus = AuthenticodeSignatureStatus.Valid,
                    FailureCode = blocked ? LaunchPreflightFailureCode.SignerMismatch : LaunchPreflightFailureCode.None });
        }
        Set("_preflightResult",new LaunchPreflightResult { StaticIdentityPassed = !blocked, Contract = contract, Components = receipts,
            FailureCode = blocked ? LaunchPreflightFailureCode.SignerMismatch : LaunchPreflightFailureCode.ExecutionGateClosed });
        if (!blocked) Set("_observedInstall", vm.HomePreflight!);
        return vm;
    }

    private static WebHomeState OverlaySample(bool blocked) =>
        WebHomeState.Create(OverlaySampleModel(blocked), "dark", 0xffabcdef, false, []);

    private static Task HomeOverlayDistinguishesFailedChecks()
    {
        var vm = OverlaySampleModel(false);
        var field = typeof(MainPageViewModel).GetField("_preflightResult",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var failure in new[] { LaunchPreflightFailureCode.ArtifactHashMismatch, LaunchPreflightFailureCode.VersionMismatch,
            LaunchPreflightFailureCode.MarkerMismatch })
        {
            field.SetValue(vm, vm.HomePreflight! with { StaticIdentityPassed = false, FailureCode = failure });
            var state = HomeOverlayState.From(vm);
            Assert((state.Checks.Channel.Passed == false) == (failure == LaunchPreflightFailureCode.MarkerMismatch),
                "channel status for " + failure);
            Assert(state.Technical.FailureCode == failure.ToString(), "preserve " + failure);
        }
        vm.RecordRuntimeRefusal("Runtime.ProcessStartFailed", "Windows start failed", []);
        var runtime = HomeOverlayState.From(vm);
        Assert(runtime.Conclusion.Contains("Windows 没能打开启动程序") && runtime.Technical.FailureCode == "Runtime.ProcessStartFailed",
            "process creation failure and next step");
        return Task.CompletedTask;
    }

    private static string OverlayFixture()
    {
        object Payload(bool blocked)
        {
            var s = OverlaySample(blocked);
            return new { s.Overlay, s.Components, s.OfficialFlow };
        }
        var options = new JsonSerializerOptions(WebHomeBridge.Json) {
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        return "window.homeOverlaySamples = " + JsonSerializer.Serialize(new { ready = Payload(false), blocked = Payload(true) },options) + ";\n";
    }

    internal static void WriteOverlayFixture() => File.WriteAllText(Path.Combine(FindRoot(), "Nikkiward", "Web.dev", "overlay-samples.js"), OverlayFixture());

    private static Task HomeOverlayFixtureMatchesNative()
    {
        Assert(ReadSource("Nikkiward","Web.dev","overlay-samples.js").ReplaceLineEndings("\n") ==
            OverlayFixture().ReplaceLineEndings("\n"), "refresh fixtures with --emit-overlay-samples");
        var ready = OverlaySample(false);
        var blocked = OverlaySample(true);
        AssertPlayerCopy(ready);
        AssertPlayerCopy(blocked);
        Assert(ready.OfficialFlow && ready.Overlay.ChannelText == "国服 · 官方 / Windows" &&
            ready.Overlay.Arguments == "-skiplauncher" && ready.Overlay.Checks.Processes.Passed == true &&
            ready.Overlay.Checks.Integrity.Text == "5 个必要组件都已核对" && ready.Components.All(c => c.StatusText == "已核对"), "verified install");
        Assert(!blocked.OfficialFlow && blocked.Overlay.Checks.Signature.Passed == false &&
            blocked.Overlay.Conclusion.Contains("签名与官方发布的不一致") && blocked.Components[0].StatusText == "未通过" &&
            blocked.Components.Skip(1).All(c => c.StatusText != "已核对") && blocked.Overlay.Technical.Signature.Contains("0x00000000") &&
            blocked.Overlay.Technical.FailureCode == "SignerMismatch", "untrusted publisher despite valid Windows signature");
        return Task.CompletedTask;
    }

    private static Task HomeOverlayActionsAreBounded()
    {
        foreach (string action in new[] { "open-root", "import-background", "official-flow", "export-diagnostics" })
            Assert(WebHomeBridge.Parse(JsonSerializer.Serialize(new { action }))?.Action == action, action);
        string Title(string title) => JsonSerializer.Serialize(new { action = "art-title", title, artKey = "background" });
        Assert(WebHomeBridge.Parse(Title(""))?.Title == "" && WebHomeBridge.Parse(Title(new string('题',120))) != null,
            "reset and save title");
        Assert(WebHomeBridge.Parse(Title(new string('题',121))) == null &&
            WebHomeBridge.Parse("""{"action":"art-title","title":"未指定背景"}""") == null &&
            WebHomeBridge.Parse("""{"action":"override-arguments"}""") == null, "reject invalid overlay edits");
        return Task.CompletedTask;
    }

    private static Task HomeOverlayPreviewRejectsActions()
    {
        foreach (string action in new[] { "locate", "open-root", "import-background", "art-title", "reset",
            "recheck", "official-flow", "export-diagnostics" })
        {
            var json = JsonSerializer.Serialize(new { action, artKey = "current", title = "画名" });
            Assert(WebHomeBridge.Parse(json) != null && WebHomeBridge.Parse(json, preview: true) == null, action);
        }
        return Task.CompletedTask;
    }

    private static Task WebHomeActionsAreBounded()
    {
        Assert(WebHomeBridge.Parse("""{"action":"photo","photoId":"one"}""")?.PhotoId == "one", "photo");
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
            var json = JsonSerializer.Serialize(new { action });
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

        return Task.CompletedTask;
    }

    private static Task LauncherCloseActionStaysSeparate()
    {
        var launcher = ReadSource("Nikkiward", "Web", "skins", "cinema", "index.html");
        var launcherCode = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml.cs");
        var commands = ReadSource("Nikkiward", "MainPage.LaunchCommands.cs");
        var hosting = ReadSource("Nikkiward", "MainPage.Hosting.cs");

        Assert(
            launcher.Contains("data-action=\"close\""),
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
