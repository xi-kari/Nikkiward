using System.Text.RegularExpressions;
using System.Xml.Linq;

internal static class AppearanceRuntimeContractTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("light and dark paper ink pairs meet the planned contrast", PaperInkPairsMeetContrast),
        ("the adaptive backdrop has one live visual-tree owner", AdaptiveBackdropHasOneOwner),
        ("the adaptive backdrop consumes the appearance motion projection", AdaptiveBackdropConsumesAppearanceMotion),
        ("the active backdrop pipeline bakes and reuses its depth plate", ActiveBackdropBakesDepthPlate),
        ("the backdrop crossfade can be cancelled and promoted", BackdropCrossFadeCanBeCancelled),
        ("every density spacing token has a live visual consumer", DensitySpacingTokensHaveConsumers),
        ("static visual tokens have one live runtime authority", StaticVisualTokensHaveOneAuthority),
        ("overlay composition and XAML transitions stay sequenced", OverlayTransitionsStaySequenced),
        ("CJK page titles retain their full line bounds", CjkPageTitlesRetainFullLineBounds),
        ("custom background exposes the motion wallpaper importer", CustomBackgroundExposesMotionImporter),
        ("custom background exposes the launcher subtitle editor", CustomBackgroundExposesSubtitleEditor),
        ("high contrast focus uses the system focus color", HighContrastFocusUsesSystemColor),
        ("every custom transition consumes the live motion authority", CustomTransitionsConsumeMotionAuthority),
        ("interactive scale and connected animation obey motion off", InteractiveMotionObeysMotionOff),
        ("journal cached images use typed XAML sources", JournalCachedImagesUseTypedSources),
        ("launcher chrome exposes customizable masthead and synchronized capsule styles", LauncherChromeExposesCustomization),
        ("launcher nebula renderer preserves the six reference presets and shader equations", LauncherNebulaRendererPreservesReference),
        ("launcher capsules preserve the compact reference chrome and shared style", LauncherCapsulesUseCompactReferenceChrome),
        ("developer surfaces stay hidden until explicit opt in", DeveloperSurfacesRequireOptIn),
        ("journal resource gallery stays hidden from the reader", JournalResourceGalleryStaysHidden),
        ("launcher resources use the shared theme authority outside the shell rail", LauncherResourcesUseSharedThemeAuthority),
        ("the primary action resource chain stays live and high contrast aware", PrimaryActionResourceChainStaysIntact),
        ("focused feedback surfaces stay compact and reachable", FocusedFeedbackSurfacesStayCompact),
        ("gallery protection management stays scoped to settings", GalleryProtectionManagementStaysScopedToSettings),
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
        var masthead = ReadSource("Nikkiward", "Features", "Launcher", "LauncherMasthead.xaml");
        var notice = ReadSource("Nikkiward", "Features", "Launcher", "LauncherNoticeIsland.xaml");
        var backdrop = ReadSource(
            "Nikkiward",
            "Features",
            "Background",
            "ArtBackdropService.cs");
        Assert(
            masthead.Contains("LocalScrimBrush", StringComparison.Ordinal) &&
            masthead.Contains("ReadabilityRegion=\"Masthead\"", StringComparison.Ordinal) &&
            notice.Contains("LocalScrimBrush", StringComparison.Ordinal) &&
            notice.Contains("ReadabilityRegion=\"Notice\"", StringComparison.Ordinal) &&
            backdrop.Contains("ResolveDoubleResource", StringComparison.Ordinal) &&
            backdrop.Contains("\"LocalScrimReferenceAlpha\"", StringComparison.Ordinal) &&
            backdrop.Contains("required / Math.Max(0.01, reference)", StringComparison.Ordinal) &&
            backdrop.Contains("LocalScrimMinimumFactor = 0.60", StringComparison.Ordinal) &&
            backdrop.Contains("LocalScrimMaximumFactor = 1.40", StringComparison.Ordinal),
            "local readability surfaces must use the runtime reference-alpha factor contract");

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

        Assert(
            settingsXaml.Contains("BackgroundSection", StringComparison.Ordinal) &&
            settingsXaml.Contains("导入动态壁纸", StringComparison.Ordinal) &&
            settingsXaml.Contains("OnChooseMotionBackgroundClicked", StringComparison.Ordinal),
            "custom background must expose a dedicated motion wallpaper command");
        Assert(
            settingsCode.Contains("MotionBackgroundChooseRequested", StringComparison.Ordinal) &&
            overlay.Contains("OnLaunchSettingsMotionBackgroundChooseRequested", StringComparison.Ordinal) &&
            appearance.Contains(
                "foreach (var extension in MotionSourceRules.SupportedExtensions)",
                StringComparison.Ordinal) &&
            appearance.Contains(
                "picker.FileTypeFilter.Add(\"*\")",
                StringComparison.Ordinal) &&
            appearance.Contains(
                "MotionSourceRules.IsSupportedExtension(file.FileType)",
                StringComparison.Ordinal) &&
            appearance.Contains("ImportMotionBackgroundAsync", StringComparison.Ordinal),
            "the visible motion command must reach the system-decoded multi-container import path");
        return Task.CompletedTask;
    }

    private static Task CustomBackgroundExposesSubtitleEditor()
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
            settingsXaml.Contains("MastheadSubtitleBox", StringComparison.Ordinal) &&
            settingsXaml.Contains("主页副标题", StringComparison.Ordinal) &&
            settingsXaml.Contains("OnSaveMastheadSubtitleClicked", StringComparison.Ordinal) &&
            settingsXaml.Contains("OnResetMastheadSubtitleClicked", StringComparison.Ordinal),
            "custom background must expose save and reset controls for the homepage subtitle");
        var subtitleBox = settingsDocument
            .Descendants()
            .SingleOrDefault(element =>
                element.Name.LocalName == "TextBox" &&
                element.Attribute(xaml + "Name")?.Value == "MastheadSubtitleBox");
        Assert(
            subtitleBox?.Attribute("Height")?.Value == "Auto",
            "the headered homepage subtitle editor must not inherit the fixed 44px TextBox height");
        Assert(
            settingsCode.Contains("MastheadSubtitleSaveRequested", StringComparison.Ordinal) &&
            overlay.Contains("OnLaunchSettingsMastheadSubtitleSaveRequested", StringComparison.Ordinal) &&
            overlay.Contains("LauncherMastheadSubtitle = e.Subtitle", StringComparison.Ordinal) &&
            runtime.Contains("_hostedLaunchSettingsPage?.ApplyAppearanceSettings(settings)", StringComparison.Ordinal),
            "the subtitle editor must save through appearance settings and stay synchronized");
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
        var launcher = ReadSource(
            "Nikkiward",
            "Features",
            "Launcher",
            "LauncherPage.xaml.cs");
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
        foreach (var source in new[] { journal, launcher, gallery })
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

    private static Task LauncherChromeExposesCustomization()
    {
        var masthead = ReadSource("Nikkiward", "Features", "Launcher", "LauncherMasthead.xaml");
        var mastheadCode = ReadSource("Nikkiward", "Features", "Launcher", "LauncherMasthead.xaml.cs");
        var launcher = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml.cs");
        var launcherXaml = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml");
        var settings = ReadSource("Nikkiward", "Models", "AppearanceSettings.cs");
        var appearanceView = ReadSource("Nikkiward", "Features", "Settings", "GeneralAppearanceSettingsView.xaml");
        var appearanceCode = ReadSource("Nikkiward", "Features", "Settings", "GeneralAppearanceSettingsView.xaml.cs");
        var appearanceRuntime = ReadSource("Nikkiward", "MainPage.AppearanceRuntime.cs");
        var applyAppearance = FindMethod(launcher, "ApplyAppearanceSettings");
        var applySettings = FindMethod(appearanceCode, "ApplySettings");
        var styleHandler = FindMethod(appearanceCode, "OnLauncherCapsuleStyleChecked");

        Assert(
            masthead.Contains("EyebrowText", StringComparison.Ordinal) &&
            masthead.Contains("SubtitleText", StringComparison.Ordinal) &&
            mastheadCode.Contains("ApplySettings(AppearanceSettings", StringComparison.Ordinal),
            "masthead must expose typed customizable copy");
        Assert(
            launcher.Contains("NoticeHost.Visibility = Visibility.Collapsed", StringComparison.Ordinal) &&
            launcher.Contains(
                "ActionCluster.Visibility = Visibility.Visible",
                StringComparison.Ordinal) &&
            launcherXaml.Contains("Click=\"OnOfficialFlowClicked\"", StringComparison.Ordinal) &&
            launcherXaml.Contains("Click=\"OnLaunchSettingsClicked\"", StringComparison.Ordinal) &&
            launcherXaml.Contains("x:Name=\"PrimaryActionDisabledPlate\"", StringComparison.Ordinal) &&
            launcherXaml.Contains("IsEnabledChanged=\"OnPrimaryActionIsEnabledChanged\"", StringComparison.Ordinal) &&
            launcher.Contains("SetPrimaryActionScaleIfEnabled", StringComparison.Ordinal) &&
            launcherXaml.Contains("Glyph=\"&#xE8F1;\"", StringComparison.Ordinal),
            "launcher must hide only the notice panel, preserve launch actions, and use a visible journal glyph");
        Assert(
            settings.Contains("LauncherMastheadLabel", StringComparison.Ordinal) &&
            settings.Contains("ShowLauncherUtilityPanels", StringComparison.Ordinal) &&
            settings.Contains("enum LauncherCapsuleStyle", StringComparison.Ordinal) &&
            settings.Contains("LauncherCapsuleStyle LauncherCapsuleStyle", StringComparison.Ordinal) &&
            appearanceView.Contains("LauncherMastheadTitleBox", StringComparison.Ordinal) &&
            appearanceView.Contains("Header=\"副标题（可留空）\"", StringComparison.Ordinal) &&
            appearanceView.Contains("x:Name=\"LauncherUtilityPanelsToggle\"", StringComparison.Ordinal) &&
            appearanceView.Contains("Visibility=\"Collapsed\"", StringComparison.Ordinal),
            "appearance settings must persist optional masthead copy and keep the retired notice toggle hidden");
        var capsuleTags = new[] { "original", "ocean", "klein", "ultraviolet", "chrome", "plus" };
        Assert(
            capsuleTags.All(tag =>
                appearanceView.Contains($"Tag=\"{tag}\"", StringComparison.Ordinal)) &&
            Count(appearanceView, "GroupName=\"LauncherCapsuleStyle\"") == capsuleTags.Length &&
            Count(appearanceView, "Checked=\"OnLauncherCapsuleStyleChecked\"") == capsuleTags.Length,
            "appearance settings must expose six mutually exclusive clickable capsule styles");
        Assert(
            applySettings.Contains(
                "ApplyCapsuleStyleSelection(settings.LauncherCapsuleStyle)",
                StringComparison.Ordinal) &&
            capsuleTags.Skip(1).All(tag =>
                styleHandler.Contains(
                    $"LauncherCapsuleStyle.{char.ToUpperInvariant(tag[0])}{tag[1..]}",
                    StringComparison.Ordinal)) &&
            styleHandler.Contains("Commit(_settings with", StringComparison.Ordinal),
            "the selector must restore persisted state and commit every non-default style");
        Assert(
            launcherXaml.Contains("x:Name=\"PrimaryCapsuleVisual\"", StringComparison.Ordinal) &&
            launcherXaml.Contains("x:Name=\"UtilityCapsuleVisual\"", StringComparison.Ordinal) &&
            applyAppearance.Contains(
                "PrimaryCapsuleVisual.ApplyStyle(settings.LauncherCapsuleStyle)",
                StringComparison.Ordinal) &&
            applyAppearance.Contains(
                "UtilityCapsuleVisual.ApplyStyle(settings.LauncherCapsuleStyle)",
                StringComparison.Ordinal) &&
            appearanceRuntime.Contains(
                "_hostedLauncherPage?.ApplyAppearanceSettings(settings)",
                StringComparison.Ordinal),
            "one persisted style must update both launcher capsules without restarting");
        return Task.CompletedTask;
    }

    private static Task LauncherNebulaRendererPreservesReference()
    {
        var renderer = ReadSource(
            "Nikkiward",
            "Features",
            "Launcher",
            "LauncherNebulaFrameRenderer.cs");
        var shader = ReadSource("Nikkiward", "Shaders", "LauncherNebula.hlsl");
        var capsuleVisual = ReadSource("Nikkiward", "Controls", "LauncherCapsuleVisual.cs");
        var compactRenderer = Regex.Replace(
            renderer,
            @"\s+",
            string.Empty,
            RegexOptions.CultureInvariant);

        var expectedPresets = new[]
        {
            (Branch: "_", Seed: "1.7f", Speed: "0.50f", Colors: new[] { "FFF3EA", "F5B27A", "F67BC6", "A978E8" }),
            (Branch: "LauncherCapsuleStyle.Ocean", Seed: "8.2f", Speed: "0.48f", Colors: new[] { "EAF6FF", "8FD0FF", "3B87F6", "6B58E9" }),
            (Branch: "LauncherCapsuleStyle.Klein", Seed: "14.1f", Speed: "0.49f", Colors: new[] { "EDF2FF", "2F58D5", "1B2040", "E07A43" }),
            (Branch: "LauncherCapsuleStyle.Ultraviolet", Seed: "23.4f", Speed: "0.47f", Colors: new[] { "F2EEFF", "B99AF1", "8F74DB", "D7D85C" }),
            (Branch: "LauncherCapsuleStyle.Chrome", Seed: "37.8f", Speed: "0.42f", Colors: new[] { "F5F6F8", "B9C0CC", "7F8793", "4A4F59" }),
            (Branch: "LauncherCapsuleStyle.Plus", Seed: "51.3f", Speed: "0.50f", Colors: new[] { "FFF0E6", "F6C26B", "F98A64", "E86D74" }),
        };
        foreach (var preset in expectedPresets)
        {
            var expected =
                $"{preset.Branch}=>new({preset.Seed},{preset.Speed}," +
                $"\"{preset.Colors[0]}\",\"{preset.Colors[1]}\"," +
                $"\"{preset.Colors[2]}\",\"{preset.Colors[3]}\")";
            Assert(
                compactRenderer.Contains(expected, StringComparison.Ordinal),
                $"launcher nebula preset '{preset.Branch}' must preserve its exact seed, speed, and four colors");
        }

        Assert(
            Regex.Matches(
                compactRenderer,
                @"(?:LauncherCapsuleStyle\.\w+|_)=>new\(",
                RegexOptions.CultureInvariant).Count == expectedPresets.Length,
            "the launcher nebula renderer must expose exactly six preset branches");

        var compactShader = Regex.Replace(
            shader,
            @"\s+",
            string.Empty,
            RegexOptions.CultureInvariant);
        foreach (var formula in new[]
        {
            "p=frac(p*float2(123.34,456.21));",
            "p+=dot(p,p+45.32+seed);",
            "f=f*f*(3.0-2.0*f);",
            "floatamplitude=0.52;",
            "for(inti=0;i<6;i++)",
            "0.80*p.x-0.60*p.y",
            "0.60*p.x+0.80*p.y)*2.03+17.7",
            "float3shadow=lerp(colorA,colorB,smoothstep(0.06,0.62,t));",
            "float3body=lerp(colorB,colorC,smoothstep(0.30,0.82,t));",
            "float3highlight=lerp(colorC,colorD,smoothstep(0.74,1.0,t));",
            "float2drift=float2(t*0.22,-t*0.13);",
            "fbm(p*1.35+drift+seed)",
            "fbm(p*2.0+3.6*q+float2(1.7,9.2)+t*0.10)",
            "floatcloud=fbm(p*1.7+4.2*r);",
            "floatveins=fbm(p*4.0-2.0*q+t*0.065);",
            "smoothstep(0.18,0.91,cloud*0.9+veins*0.22)",
            "float2(132.0,58.0)",
            "step(0.989,starRandom)",
            "smoothstep(0.94,0.18,length((uv-0.5)*float2(1.0,1.35)))",
            "color=pow(max(color,0.0),0.88);",
            "D2D_PS_ENTRY(main)",
        })
        {
            Assert(
                compactShader.Contains(formula, StringComparison.Ordinal),
                $"LauncherNebula.hlsl must preserve the reference equation '{formula}'");
        }

        Assert(
            capsuleVisual.Contains("PixelShaderEffect", StringComparison.Ordinal) &&
            capsuleVisual.Contains("LauncherNebula.bin", StringComparison.Ordinal) &&
            capsuleVisual.Contains("_shader.Properties[\"seed\"]", StringComparison.Ordinal) &&
            capsuleVisual.Contains("_shader.Properties[\"colorD\"]", StringComparison.Ordinal),
            "the capsule visual must execute the compiled HLSL with the selected preset uniforms");
        return Task.CompletedTask;
    }

    private static Task LauncherCapsulesUseCompactReferenceChrome()
    {
        var launcherXaml = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml");
        var launcherCode = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml.cs");
        var capsuleVisual = ReadSource("Nikkiward", "Controls", "LauncherCapsuleVisual.cs");
        var capsuleRenderer = ReadSource(
            "Nikkiward",
            "Features",
            "Launcher",
            "LauncherNebulaFrameRenderer.cs");
        var document = XDocument.Parse(launcherXaml);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        XElement FindNamed(string name) => document
            .Descendants()
            .SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute(xaml + "Name"),
                    name,
                    StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Launcher element '{name}' was not found.");

        var actionCluster = FindNamed("ActionCluster");
        var primarySurface = FindNamed("PrimaryActionSurface");
        var utilitySurface = FindNamed("ActionUtilityPill");
        Assert(
            (string?)actionCluster.Attribute("Width") == "300" &&
            (string?)primarySurface.Attribute("Height") == "56" &&
            (string?)utilitySurface.Attribute("Width") == "252" &&
            (string?)utilitySurface.Attribute("Height") == "44" &&
            (string?)utilitySurface.Attribute("HorizontalAlignment") == "Right",
            "launcher capsule chrome must keep a dominant 300px primary action and a compact 252px utility row");

        var referenceChrome = string.Join(
            Environment.NewLine,
            launcherXaml,
            launcherCode,
            capsuleVisual,
            capsuleRenderer);
        Assert(
            referenceChrome.Contains("MonoFontFamily", StringComparison.Ordinal) &&
            referenceChrome.Contains("LauncherCapsuleCode", StringComparison.Ordinal) &&
            referenceChrome.Contains("LauncherCapsuleName", StringComparison.Ordinal) &&
            referenceChrome.Contains("LauncherCapsuleStudy", StringComparison.Ordinal) &&
            referenceChrome.Contains("LIVE COSMIC STUDY", StringComparison.Ordinal),
            "each compact capsule must expose mono NC code, theme name, and LIVE COSMIC STUDY fields");

        foreach (var (code, name) in new[]
        {
            ("NC-01", "ORIGINAL"),
            ("NC-02", "OCEAN"),
            ("NC-03", "KLEIN"),
            ("NC-04", "ULTRAVIOLET"),
            ("NC-05", "CHROME"),
            ("NC-06", "PLUS"),
        })
        {
            Assert(
                referenceChrome.Contains(code, StringComparison.Ordinal) &&
                referenceChrome.Contains(name, StringComparison.Ordinal),
                $"capsule chrome must map {code} to {name}");
        }

        var applyAppearance = FindMethod(launcherCode, "ApplyAppearanceSettings");
        Assert(
            launcherXaml.Contains("x:Name=\"PrimaryCapsuleVisual\"", StringComparison.Ordinal) &&
            launcherXaml.Contains("x:Name=\"UtilityCapsuleVisual\"", StringComparison.Ordinal) &&
            applyAppearance.Contains(
                "PrimaryCapsuleVisual.ApplyStyle(settings.LauncherCapsuleStyle)",
                StringComparison.Ordinal) &&
            applyAppearance.Contains(
                "UtilityCapsuleVisual.ApplyStyle(settings.LauncherCapsuleStyle)",
                StringComparison.Ordinal),
            "primary and utility capsules must consume the same persisted launcher style");
        return Task.CompletedTask;
    }

    private static Task DeveloperSurfacesRequireOptIn()
    {
        var settingsXaml = ReadSource("Nikkiward", "Features", "Settings", "SettingsPage.xaml");
        var settingsCode = ReadSource("Nikkiward", "Features", "Settings", "SettingsPage.xaml.cs");
        var homeXaml = ReadSource("Nikkiward", "Features", "Settings", "SettingsHomeView.xaml");
        var launcherXaml = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml");
        var launcherCode = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml.cs");

        foreach (var itemName in new[]
        {
            "JournalItem",
            "FilesItem",
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
            settingsCode.Contains("IsDeveloperDestination(destination) && !_developerModeEnabled", StringComparison.Ordinal) &&
            settingsCode.Contains("JournalItem.Visibility = visibility", StringComparison.Ordinal) &&
            settingsCode.Contains("FilesItem.Visibility = visibility", StringComparison.Ordinal) &&
            Regex.IsMatch(
                settingsCode,
                @"IsDeveloperDestination\s*\([^)]*\)\s*=>\s*destination\s+is\s+SettingsDestination\.Journal\s+or\s+SettingsDestination\.Files\s+or",
                RegexOptions.CultureInvariant) &&
            settingsCode.Contains("ApplyDeveloperMode(bool enabled)", StringComparison.Ordinal),
            "developer-only settings need an explicit toggle, collapsed navigation, and a guarded path");
        Assert(
            !launcherXaml.Contains("当前运行状态", StringComparison.Ordinal) &&
            !launcherXaml.Contains("OnStatusClicked", StringComparison.Ordinal) &&
            !launcherCode.Contains("public event EventHandler? StatusRequested;", StringComparison.Ordinal),
            "the launcher must not expose the technical status control");
        return Task.CompletedTask;
    }

    private static Task JournalResourceGalleryStaysHidden()
    {
        var journal = ReadSource(
            "Nikkiward",
            "Features",
            "Journal",
            "JournalSnapshotPanel.xaml");
        Assert(
            Regex.IsMatch(
                journal,
                "x:Name=\"ResourceSection\"[\\s\\S]*?Visibility=\"Collapsed\"",
                RegexOptions.CultureInvariant),
            "cached web artwork may remain available to sync but must stay out of the reader UI");
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

        var app = XDocument.Parse(ReadSource("Nikkiward", "App.xaml"));
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var launcherKeys = sourceXaml
            .SelectMany(file => XDocument.Parse(file.Source).Descendants())
            .Select(element => (string?)element.Attribute(xaml + "Key"))
            .Where(key => key?.StartsWith("Launcher", StringComparison.Ordinal) == true)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        Assert(
            launcherKeys.SetEquals(new[] { "LauncherPaneAcrylicBrush", "LauncherThemeShadow" }),
            $"only shell rail compatibility keys may remain: {string.Join(", ", launcherKeys.Order())}");

        foreach (var themeName in new[] { "Light", "Dark", "HighContrast" })
        {
            var theme = FindThemeDictionary(app, themeName);
            Assert(
                FindResource(theme, "LauncherPaneAcrylicBrush") is not null &&
                FindResource(theme, "NavigationViewDefaultPaneBackground") is not null,
                $"{themeName} must preserve the shell rail resources");
        }

        var main = ReadSource("Nikkiward", "MainPage.xaml");
        Assert(
            main.Contains("Width=\"56\"", StringComparison.Ordinal) &&
            main.Contains(
                "Background=\"{ThemeResource LauncherPaneAcrylicBrush}\"",
                StringComparison.Ordinal) &&
            Count(main, "{ThemeResource LauncherThemeShadow}") == 2,
            "the preserved 56px shell rail must keep its dedicated background and profile shadow resources");
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

        var launcher = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml");
        var launcherCode = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml.cs");
        var capsuleVisualCode = ReadSource(
            "Nikkiward",
            "Controls",
            "LauncherCapsuleVisual.cs");
        var controls = ReadSource("Nikkiward", "Themes", "Controls.xaml");
        var service = ReadSource(
            "Nikkiward",
            "Features",
            "Background",
            "ArtBackdropService.cs");
        Assert(
            launcher.Contains(
                "{ThemeResource PrimaryActionFillBrush}",
                StringComparison.Ordinal) &&
            controls.Contains(
                "{ThemeResource PrimaryActionFillBrush}",
                StringComparison.Ordinal) &&
            controls.Contains(
                "x:Key=\"PrimaryActionButtonStyle\"",
                StringComparison.Ordinal) &&
            service.Contains(
                "ActionAccentBrushKey = \"PrimaryActionSolidBrush\"",
                StringComparison.Ordinal),
            "the page, shared button style, and runtime accent publisher must use the same resource chain");
        var launcherDocument = XDocument.Parse(launcher);
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        XElement FindLauncherElement(string name) => launcherDocument
            .Descendants()
            .Single(element => element.Attribute(xaml + "Name")?.Value == name);
        var primaryAction = FindLauncherElement("PrimaryActionSurface");
        var utilityAction = FindLauncherElement("ActionUtilityPill");
        var settingsButton = FindLauncherElement("PrimarySettingsButton");
        static bool HasDuplicateOutline(XElement element) => element
            .Descendants()
            .Any(descendant =>
                descendant.Attribute("BorderBrush")?.Value ==
                    "{ThemeResource OnArtStrokeBrush}");
        Assert(
            !HasDuplicateOutline(primaryAction) &&
            !HasDuplicateOutline(utilityAction) &&
            primaryAction.Attribute("Shadow") is null &&
            utilityAction.Attribute("Shadow") is null &&
            primaryAction.Attribute("Translation")?.Value == "0,0,32" &&
            utilityAction.Attribute("Translation")?.Value == "0,0,4",
            "the two launcher capsules must avoid duplicate outlines and rectangular ThemeShadow spill while keeping their own elevation values");
        Assert(
            capsuleVisualCode.Contains(
                "CanvasGeometry.CreateRoundedRectangle",
                StringComparison.Ordinal) &&
            capsuleVisualCode.Contains(
                "DrawingSession.CreateLayer(1, roundedClip)",
                StringComparison.Ordinal) &&
            capsuleVisualCode.IndexOf(
                "DrawingSession.CreateLayer(1, roundedClip)",
                StringComparison.Ordinal) < capsuleVisualCode.IndexOf(
                "DrawingSession.DrawImage(_shader)",
                StringComparison.Ordinal),
            "the animated Win2D surface must clip its shader output to rounded bounds before drawing");
        Assert(
            settingsButton.Attribute("Foreground")?.Value == "{ThemeResource InkPrimaryBrush}" &&
            !launcherCode.Contains("PrimarySettingsButton.Foreground", StringComparison.Ordinal),
            "the launcher settings icon must retain its live ThemeResource foreground");
        return Task.CompletedTask;
    }

    private static Task FocusedFeedbackSurfacesStayCompact()
    {
        var main = ReadSource("Nikkiward", "MainPage.xaml");
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
        var photoPluginCode = ReadSource("Nikkiward", "MainPage.PhotoPlugin.cs");
        var appearanceSettings = ReadSource(
            "Nikkiward",
            "Features",
            "Settings",
            "GeneralAppearanceSettingsView.xaml");
        var gallery = ReadSource("Nikkiward", "Pages", "GalleryPage.xaml");
        var galleryResources = ReadSource("Nikkiward", "Styles", "GalleryResources.xaml");

        Assert(
            !main.Contains("IsSelected=\"True\"", StringComparison.Ordinal) &&
            navigation.Contains("SetShellNavigationSelection", StringComparison.Ordinal) &&
            chrome.Contains("ProfileQuickSwitchHost.Visibility = Visibility.Visible", StringComparison.Ordinal),
            "shell selection must have one programmatic authority and keep the profile icon visible");
        var galleryNavigationIndex = main.IndexOf(
            "x:Name=\"GalleryNavigationItem\"",
            StringComparison.Ordinal);
        var favoritesNavigationIndex = main.IndexOf(
            "x:Name=\"FavoritesNavigationItem\"",
            StringComparison.Ordinal);
        var profilesNavigationIndex = main.IndexOf(
            "x:Name=\"ProfilesNavigationItem\"",
            StringComparison.Ordinal);
        Assert(
            galleryNavigationIndex >= 0 &&
            favoritesNavigationIndex > galleryNavigationIndex &&
            favoritesNavigationIndex < profilesNavigationIndex &&
            main.Contains(
                "automation:AutomationProperties.AutomationId=\"GalleryFavoritesNavigationItem\"",
                StringComparison.Ordinal) &&
            main.Contains("Tag=\"gallery-favorites\"", StringComparison.Ordinal),
            "the favorites navigation item must follow gallery with a stable automation contract");
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
        var launcherCode = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml.cs");
        var launcherXaml = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml");
        Assert(
            launcherCode.Contains("ShellRailWidth = 56", StringComparison.Ordinal) &&
            launcherCode.Contains("windowWidth = width + ShellRailWidth", StringComparison.Ordinal) &&
            !launcherXaml.Contains("AdaptiveTrigger MinWindowWidth", StringComparison.Ordinal),
            "launcher breakpoints must use actual window width from one authority");
        Assert(
            status.Contains("x:Name=\"TechnicalDetailsExpander\"", StringComparison.Ordinal) &&
            status.Contains("IsExpanded=\"False\"", StringComparison.Ordinal) &&
            statusCode.Contains("public void ResetView()", StringComparison.Ordinal) &&
            statusCode.Contains("TechnicalDetailsExpander.IsExpanded = false", StringComparison.Ordinal),
            "status technical evidence must default to collapsed on every entry");
        Assert(
            !settings.Contains("游戏截图", StringComparison.Ordinal) &&
            !settings.Contains("Tag=\"screenshot\"", StringComparison.Ordinal) &&
            settings.Contains("x:Name=\"GalleryItem\"", StringComparison.Ordinal) &&
            settings.Contains("Tag=\"gallery\"", StringComparison.Ordinal) &&
            settingsCode.Contains(
                "SettingsDestination.Gallery => Select(_galleryView",
                StringComparison.Ordinal) &&
            settingsNavigation.Contains("Gallery,", StringComparison.Ordinal) &&
            settingsHome.Contains("Tag=\"gallery\"", StringComparison.Ordinal),
            "gallery management must stay inside settings and replace the retired screenshot destination");
        Assert(
            gallerySettings.Contains("x:Name=\"ChooseRootButton\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"ResetRootButton\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"ClearCacheButton\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"RegisterNikkiGalleryButton\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"OpenNikkiGalleryButton\"", StringComparison.Ordinal) &&
            gallerySettings.Contains("x:Name=\"DisconnectNikkiGalleryButton\"", StringComparison.Ordinal) &&
            main.Contains("x:Name=\"PhotoPluginNavigationItem\"", StringComparison.Ordinal) &&
            main.Contains("Visibility=\"Collapsed\"", StringComparison.Ordinal) &&
            photoPluginCode.Contains(
                "PhotoPluginNavigationItem.Visibility = Visibility.Collapsed",
                StringComparison.Ordinal),
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
        var main = XDocument.Parse(ReadSource("Nikkiward", "MainPage.xaml"));
        var photoPluginCode = ReadSource("Nikkiward", "MainPage.PhotoPlugin.cs");
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

        var photoPluginItem = FindNamed(main, "PhotoPluginNavigationItem");
        Assert(
            string.Equals(
                (string?)photoPluginItem.Attribute("Visibility"),
                "Collapsed",
                StringComparison.Ordinal) &&
            photoPluginCode.Contains(
                "PhotoPluginNavigationItem.Visibility = Visibility.Collapsed",
                StringComparison.Ordinal),
            "the PhotoPlugin navigation item must remain collapsed in XAML and runtime projection");

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
            retiredScreenshotEntries.Length == 0,
            "the retired screenshot settings destination must not return");
        Assert(
            !galleryPage.Contains("GalleryAdvancedToolsButton", StringComparison.Ordinal) &&
            !galleryPage.Contains("Label=\"高级工具\"", StringComparison.Ordinal) &&
            !galleryPageCode.Contains("OnGalleryAdvancedToolsClicked", StringComparison.Ordinal),
            "the retired advanced-tools command must not return to the gallery surface");
        Assert(
            Regex.IsMatch(
                galleryPageCode,
                @"_favoriteProtectionService\.ProtectAsync\(\s*_annotationScopeId,\s*photo\.RelativePath,\s*photo\.FilePath,\s*CancellationToken\.None\)",
                RegexOptions.CultureInvariant),
            "a saved favorite must finish its protection copy independently of gallery reload cancellation");
        return Task.CompletedTask;
    }

    private static Task LauncherCloseActionStaysSeparate()
    {
        var launcher = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml");
        var launcherCode = ReadSource("Nikkiward", "Features", "Launcher", "LauncherPage.xaml.cs");
        var commands = ReadSource("Nikkiward", "MainPage.LaunchCommands.cs");
        var hosting = ReadSource("Nikkiward", "MainPage.Hosting.cs");

        Assert(
            launcher.Contains("x:Name=\"CloseGameButton\"", StringComparison.Ordinal) &&
            launcher.Contains("Click=\"OnCloseGameClicked\"", StringComparison.Ordinal) &&
            launcher.Contains("automation:AutomationProperties.Name=\"关闭游戏\"", StringComparison.Ordinal) &&
            launcher.Contains("Visibility=\"{x:Bind ViewModel.CloseGameButtonVisibility}\"", StringComparison.Ordinal),
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
        var picker = ReadSource(
            "Nikkiward",
            "Features",
            "Profile",
            "ProfilePickerView.xaml");
        var pickerCode = ReadSource(
            "Nikkiward",
            "Features",
            "Profile",
            "ProfilePickerView.xaml.cs");
        var page = ReadSource(
            "Nikkiward",
            "Features",
            "Profile",
            "ProfilePage.xaml");
        var pageCode = ReadSource(
            "Nikkiward",
            "Features",
            "Profile",
            "ProfilePage.xaml.cs");
        var main = ReadSource("Nikkiward", "MainPage.xaml");
        var profile = ReadSource("Nikkiward", "MainPage.Profile.cs");
        var showProfileOverlay = FindMethod(profile, "ShowProfileOverlay");
        var quickSwitchVisibility = FindMethod(
            profile,
            "SetProfileQuickSwitchRailVisibility");
        var quickSwitchRailStart = main.IndexOf(
            "x:Name=\"ProfileQuickSwitchRail\"",
            StringComparison.Ordinal);
        var quickSwitchRailContentStart = main.IndexOf(
            "<ScrollViewer",
            quickSwitchRailStart,
            StringComparison.Ordinal);
        Assert(
            quickSwitchRailStart >= 0 && quickSwitchRailContentStart > quickSwitchRailStart,
            "the quick-switch rail header must remain discoverable");
        var quickSwitchRailHeader = main[quickSwitchRailStart..quickSwitchRailContentStart];

        Assert(
            picker.Contains("x:Name=\"ServerSelectorSurface\"", StringComparison.Ordinal) &&
            picker.Contains("Width=\"280\"", StringComparison.Ordinal) &&
            picker.Contains("Margin=\"12,8,12,12\"", StringComparison.Ordinal) &&
            picker.Contains("Background=\"{ThemeResource GlassIslandBrush}\"", StringComparison.Ordinal) &&
            picker.Contains("x:Name=\"ServerCountText\"", StringComparison.Ordinal) &&
            picker.Contains("x:Name=\"ServerOptionButton\"", StringComparison.Ordinal) &&
            picker.Contains("Height=\"48\"", StringComparison.Ordinal) &&
            picker.Contains("ItemsSource=\"{x:Bind ServerOptions}\"", StringComparison.Ordinal) &&
            Count(picker, "Source=\"Assets/NikkiGameIcon.png\"") == 2 &&
            picker.Contains("<Style TargetType=\"ContentPresenter\">", StringComparison.Ordinal) &&
            picker.Contains("Property=\"HorizontalAlignment\" Value=\"Stretch\"", StringComparison.Ordinal) &&
            picker.Contains("Property=\"HorizontalContentAlignment\" Value=\"Stretch\"", StringComparison.Ordinal) &&
            picker.Contains("Text=\"{x:Bind ServerName}\"", StringComparison.Ordinal) &&
            picker.Contains("Tag=\"{x:Bind ProfileId}\"", StringComparison.Ordinal) &&
            picker.Contains("SubtleFillColorSecondaryBrush", StringComparison.Ordinal) &&
            picker.Contains("Visibility=\"{x:Bind SelectionVisibility}\"", StringComparison.Ordinal) &&
            !picker.Contains("ActualWidth", StringComparison.Ordinal) &&
            !picker.Contains("GameServerCard", StringComparison.Ordinal) &&
            !picker.Contains("<Button.Flyout>", StringComparison.Ordinal) &&
            !picker.Contains("<Flyout", StringComparison.Ordinal) &&
            !picker.Contains("Width=\"420\"", StringComparison.Ordinal) &&
            !picker.Contains("DiscoveredServersText", StringComparison.Ordinal),
            "the everyday profile surface must expose direct server rows in one compact anchored panel");
        Assert(
            !picker.Contains("CapabilitySummary", StringComparison.Ordinal) &&
            !picker.Contains("PlatformEvidence", StringComparison.Ordinal) &&
            !picker.Contains("GameRootPath", StringComparison.Ordinal) &&
            !picker.Contains("VerifiedOneClick", StringComparison.Ordinal),
            "technical capability and path evidence must stay out of the everyday server picker");
        Assert(
            pickerCode.Contains("\"中国服\"", StringComparison.Ordinal) &&
            pickerCode.Contains("\"国际服\"", StringComparison.Ordinal) &&
            pickerCode.Contains("\"哔哩哔哩\"", StringComparison.Ordinal) &&
            pickerCode.Contains("ViewModel.DeveloperModeEnabled", StringComparison.Ordinal) &&
            pickerCode.Contains("ServerCountText.Text = $\"{ServerOptions.Count} 个服务器\"", StringComparison.Ordinal),
            "the picker must project the three user-facing channels and keep details developer-gated");
        Assert(
            page.Contains("HorizontalContentAlignment=\"Stretch\"", StringComparison.Ordinal) &&
            page.Contains("VerticalContentAlignment=\"Stretch\"", StringComparison.Ordinal) &&
            pageCode.Contains("_picker.HorizontalAlignment = HorizontalAlignment.Stretch", StringComparison.Ordinal) &&
            pageCode.Contains("_picker.VerticalAlignment = VerticalAlignment.Stretch", StringComparison.Ordinal) &&
            main.Contains("Margin=\"56,0,0,0\"", StringComparison.Ordinal),
            "the compact selector must remain anchored beside the shell game icon");
        Assert(
            main.Contains("x:Name=\"ProfileOverlayScrim\"", StringComparison.Ordinal) &&
            main.Contains("Background=\"#30000000\"", StringComparison.Ordinal) &&
            Count(main, "Source=\"Assets/NikkiGameIcon.png\"") == 2 &&
            main.Contains("Canvas.ZIndex=\"10\"", StringComparison.Ordinal) &&
            quickSwitchRailHeader.Contains("Visibility=\"Collapsed\"", StringComparison.Ordinal) &&
            profile.Contains("ShowProfileOverlay(selectNavigationItem: false)", StringComparison.Ordinal) &&
            profile.Contains(
                "if (ProfileOverlayScrim.Visibility == Visibility.Visible)",
                StringComparison.Ordinal) &&
            showProfileOverlay.Contains(
                "SetProfileQuickSwitchRailVisibility(false)",
                StringComparison.Ordinal) &&
            !showProfileOverlay.Contains(
                "SetProfileQuickSwitchRailVisibility(true)",
                StringComparison.Ordinal) &&
            quickSwitchVisibility.Contains(
                "ProfileQuickSwitchRail.Visibility = isVisible",
                StringComparison.Ordinal) &&
            profile.Contains("ProfileOverlayScrim.Visibility = Visibility.Visible", StringComparison.Ordinal) &&
            profile.Contains("ProfileOverlayScrim.Visibility = Visibility.Collapsed", StringComparison.Ordinal),
            "the selector must preserve one top-left game icon, suppress the shortcut rail, and use a light scrim");
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
            if (File.Exists(Path.Combine(current.FullName, "NIKKIWARD_MASTER_PLAN_V2.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "Nikkiward")))
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
