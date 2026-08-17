using Nikkiward.Features.Background;
using Nikkiward.Models;
using Nikkiward.Services;
using System.Reflection;

internal static class DiagnosticTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("diagnostics expose backdrop state without artwork identity", TestBackdropStateIsRedacted),
    ];

    private static async Task TestBackdropStateIsRedacted()
    {
        var properties = typeof(ArtBackdropDiagnosticState)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expectedProperties = new[]
        {
            nameof(ArtBackdropDiagnosticState.AccentFromFallback),
            nameof(ArtBackdropDiagnosticState.DominantHueWeight),
            nameof(ArtBackdropDiagnosticState.IsReady),
            nameof(ArtBackdropDiagnosticState.PreferredTheme),
        }.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert(
            properties.SequenceEqual(expectedProperties, StringComparer.Ordinal),
            "background diagnostics must expose only the approved fields");

        using var fixture = new TempFixture();
        var profile = new LaunchProfile
        {
            ProfileId = "diagnostic-profile",
            DisplayName = "fixture",
            Channel = "official",
            GameRootPath = Path.Combine(fixture.Root, "game"),
            LauncherPath = Path.Combine(fixture.Root, "launcher.exe"),
            Capability = LaunchCapability.NotVerified,
        };
        var snapshot = new LaunchSnapshot
        {
            ProfileId = profile.ProfileId,
            State = LaunchState.Ready,
            Capability = LaunchCapability.NotVerified,
        };
        var backdrop = new ArtBackdropDiagnosticState
        {
            IsReady = true,
            AccentFromFallback = true,
            DominantHueWeight = 0.05859375,
            PreferredTheme = ArtPreferredTheme.Dark,
        };

        var result = await new RedactedDiagnosticReportExporter().ExportAsync(
            profile,
            snapshot,
            fixture.Root,
            backdrop);

        Assert(result.Succeeded, result.Error ?? "diagnostic export should succeed");
        var json = await File.ReadAllTextAsync(result.JsonFilePath!);
        var text = await File.ReadAllTextAsync(result.TextFilePath!);

        foreach (var output in new[] { json, text })
        {
            Assert(output.Contains("accentFromFallback", StringComparison.Ordinal)
                || output.Contains("Accent from fallback", StringComparison.Ordinal),
                "fallback state must be visible in the report");
            Assert(output.Contains("dominantHueWeight", StringComparison.Ordinal)
                || output.Contains("Dominant hue weight", StringComparison.Ordinal),
                "dominant hue weight must be visible in the report");
            Assert(output.Contains("preferredTheme", StringComparison.Ordinal)
                || output.Contains("Preferred theme", StringComparison.Ordinal),
                "preferred theme must be visible in the report");
            Assert(!output.Contains("artHash", StringComparison.OrdinalIgnoreCase),
                "artwork hash must not enter diagnostics");
            Assert(!output.Contains("blurredArtPath", StringComparison.OrdinalIgnoreCase),
                "blur path must not enter diagnostics");
            Assert(!output.Contains("wallpaper-secret.jpg", StringComparison.OrdinalIgnoreCase),
                "artwork file identities must not enter diagnostics");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TempFixture : IDisposable
    {
        public TempFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "NikkiwardDiagnostic", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch
            {
            }
        }
    }
}
