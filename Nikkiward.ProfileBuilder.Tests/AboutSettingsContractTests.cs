internal static class AboutSettingsContractTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("settings keeps About in the navigation footer", TestAboutNavigationContract),
        ("About exposes version update links and disclaimer", TestAboutViewContract),
        ("About author card follows all theme resource dictionaries", TestAuthorCardThemeContract),
    ];

    private static Task TestAboutNavigationContract()
    {
        var root = FindWorkspaceRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Nikkiward", "Features", "Settings", "SettingsPage.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "Nikkiward", "Features", "Settings", "SettingsPage.xaml.cs"));
        var context = File.ReadAllText(Path.Combine(root, "Nikkiward", "Features", "Settings", "SettingsNavigationContext.cs"));

        AssertContains(xaml, "<NavigationView.FooterMenuItems>", "About footer owner");
        AssertContains(xaml, "AutomationId=\"SettingsAboutNavigationItem\"", "About automation id");
        AssertContains(xaml, "Tag=\"about\"", "About navigation tag");
        AssertContains(code, "SettingsDestination.About", "About destination branch");
        AssertContains(context, "About,", "About destination enum");
        return Task.CompletedTask;
    }

    private static Task TestAuthorCardThemeContract()
    {
        var root = FindWorkspaceRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Nikkiward", "Features", "Settings", "AboutSettingsView.xaml"));
        var themePath = Path.Combine(root, "Nikkiward", "Themes", "OnArt.xaml");
        var document = System.Xml.Linq.XDocument.Load(themePath);
        var xNamespace = (System.Xml.Linq.XNamespace)"http://schemas.microsoft.com/winfx/2006/xaml";
        var presentationNamespace = (System.Xml.Linq.XNamespace)"http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var keys = new[]
        {
            "AuthorCardSurfaceBrush",
            "AuthorCardImageTintBrush",
            "AuthorCardBottomScrimBrush",
            "AuthorCardEdgeBrush",
            "AuthorCardInnerRingBrush",
            "AuthorCardTitleBrush",
            "AuthorCardMetaBrush",
            "AuthorCardPanelBrush",
            "AuthorCardPanelEdgeBrush",
            "AuthorCardTagBrush",
            "AuthorCardTagTextBrush",
        };

        foreach (var themeName in new[] { "Light", "Dark", "HighContrast" })
        {
            var theme = document
                .Descendants(presentationNamespace + "ResourceDictionary")
                .Single(element => string.Equals(
                    (string?)element.Attribute(xNamespace + "Key"),
                    themeName,
                    StringComparison.Ordinal));
            foreach (var key in keys)
            {
                Assert(
                    theme.Descendants().Any(element => string.Equals(
                        (string?)element.Attribute(xNamespace + "Key"),
                        key,
                        StringComparison.Ordinal)),
                    $"{themeName} author card resource: {key}");
            }
        }

        foreach (var legacyColor in new[]
        {
            "#FF090A0D",
            "#52FFFFFF",
            "#2407080B",
            "#B8000000",
            "#8A11141B",
            "#42FFFFFF",
            "#FFF8FAFF",
            "#D9E2E8F4",
            "#FFF7F9FD",
            "#C9D8DFEA",
            "#24FFFFFF",
            "#EAF7F9FD",
        })
        {
            Assert(!xaml.Contains(legacyColor, StringComparison.Ordinal), $"legacy author card color removed: {legacyColor}");
        }

        foreach (var key in keys)
        {
            AssertContains(xaml, $"{{ThemeResource {key}}}", $"author card theme binding: {key}");
        }

        return Task.CompletedTask;
    }

    private static Task TestAboutViewContract()
    {
        var root = FindWorkspaceRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Nikkiward", "Features", "Settings", "AboutSettingsView.xaml"));
        var code = File.ReadAllText(Path.Combine(root, "Nikkiward", "Features", "Settings", "AboutSettingsView.xaml.cs"));

        AssertContains(xaml, "Text=\"Nikkiward\"", "product identity");
        AssertContains(xaml, "AutomationId=\"AuthorProfileCard\"", "author card automation id");
        AssertContains(xaml, "Width=\"406\"", "author card width");
        AssertContains(xaml, "Height=\"564\"", "author card height");
        AssertContains(xaml, "XikariAvatar.jpg", "author avatar asset");
        AssertContains(xaml, "AutomationProperties.Name=\"Xikari 卡片底图\"", "single full-card avatar");
        AssertContains(xaml, "Stretch=\"UniformToFill\"", "full-card avatar backdrop");
        AssertContains(xaml, "x:Name=\"AuthorProfileHitSurface\"", "stable pointer hit surface");
        AssertContains(xaml, "x:Name=\"AuthorTitleLayer\"", "floating title layer");
        AssertContains(xaml, "x:Name=\"AuthorBottomLayer\"", "floating bottom layer");
        AssertContains(xaml, "Draw=\"OnAuthorHologramDraw\"", "holographic draw owner");
        AssertContains(xaml, "PointerMoved=\"OnAuthorProfilePointerMoved\"", "pointer tracking");
        AssertContains(code, "PlaneProjection", "three-dimensional card projection");
        AssertContains(code, "RotationX", "vertical card tilt");
        AssertContains(code, "RotationY", "horizontal card tilt");
        AssertContains(code, "AuthorTitleLayer.Translation", "title parallax");
        AssertContains(code, "AuthorBottomLayer.Translation", "bottom parallax");
        Assert(!code.Contains("CompositionTarget.Rendering", StringComparison.Ordinal), "author card must not own a frame loop");
        AssertContains(code, "CanvasRadialGradientBrush", "pointer-centered radial shine");
        AssertContains(code, "\"Xikari\",", "Xikari glyph mask");
        AssertContains(code, "AuthorPatternColorsLight", "light theme letter palette");
        AssertContains(code, "AuthorPatternColorsDark", "dark theme letter palette");
        AssertContains(code, "sender.ActualTheme", "letter palette follows the active theme");
        AssertContains(code, "ActualThemeChanged += OnAuthorCardThemeChanged", "theme changes redraw the letter canvas");
        AssertContains(code, "patternDrift", "pointer-following wordmark pattern");
        Assert(!code.Contains("DispatcherQueueTimer", StringComparison.Ordinal), "author card must not run a repeating timer");
        Assert(!code.Contains("StartAnimation", StringComparison.Ordinal), "author card must not start Composition animation");
        AssertContains(xaml, "AutomationProperties.Name=\"检查更新\"", "update action");
        AssertContains(code, "Version.IsPrerelease ? 1 : 0", "preview builds use the preview update channel");
        AssertContains(code, "ApplyUpdateResult(result, channel)", "update result keeps its selected channel");
        AssertContains(code, "已连接 GitHub", "empty channel result confirms the GitHub connection");
        AssertContains(code, "连接 GitHub 失败", "network failures identify GitHub as the failed endpoint");
        AssertContains(code, "连接超时", "update timeouts are shown instead of escaping the async event");
        AssertContains(xaml, "https://github.com/xi-kari/Nikkiward/releases", "release history link");
        AssertContains(xaml, "非官方项目", "independence notice");
        Assert(!xaml.Contains("<ScrollViewer", StringComparison.Ordinal), "About must use SettingsPage's scroll owner");
        Assert(File.Exists(Path.Combine(root, "Nikkiward", "Assets", "XikariAvatar.jpg")), "author avatar file");
        return Task.CompletedTask;
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nikkiward", "Nikkiward.csproj")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Nikkiward workspace root was not found.");
    }

    private static void AssertContains(string text, string expected, string message)
    {
        Assert(text.Contains(expected, StringComparison.Ordinal), $"{message}: {expected}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
