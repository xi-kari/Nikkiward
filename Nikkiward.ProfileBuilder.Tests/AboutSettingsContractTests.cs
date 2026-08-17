internal static class AboutSettingsContractTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All =>
    [
        ("settings keeps About in the navigation footer", TestAboutNavigationContract),
        ("About exposes version update links and disclaimer", TestAboutViewContract),
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

    private static Task TestAboutViewContract()
    {
        var root = FindWorkspaceRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "Nikkiward", "Features", "Settings", "AboutSettingsView.xaml"));

        AssertContains(xaml, "Text=\"Nikkiward\"", "product identity");
        AssertContains(xaml, "AutomationProperties.Name=\"检查更新\"", "update action");
        AssertContains(xaml, "https://github.com/xi-kari/Nikkiward/releases", "release history link");
        AssertContains(xaml, "非官方项目", "independence notice");
        Assert(!xaml.Contains("<ScrollViewer", StringComparison.Ordinal), "About must use SettingsPage's scroll owner");
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
