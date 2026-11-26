using Nikkiward.Features.Background;
using Nikkiward.Models;
using Nikkiward.Services;
using System.Reflection;

internal static class DiagnosticTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("signer rejection survives redacted JSON and text export", SignerRejectionIsExported),
        ("diagnostics expose backdrop state without artwork identity", TestBackdropStateIsRedacted),
    ];

    private static async Task SignerRejectionIsExported()
    {
        using var fixture = new TempFixture();
        var profile = new LaunchProfile { ProfileId = "fixture", GameRootPath = fixture.Root };
        var preflight = new LaunchPreflightResult
        {
            FailureCode = LaunchPreflightFailureCode.SignerMismatch,
            FailureDetail = $"untrusted publisher at {fixture.Root}",
        };
        var snapshot = new LaunchSnapshot
        {
            ProfileId = profile.ProfileId,
            LastFailureReason = LaunchSnapshot.ResolveFailureReason(preflight, LaunchState.Ready, false, null),
        };
        var gate = new LaunchPreflightResult
        {
            StaticIdentityPassed = true,
            FailureCode = LaunchPreflightFailureCode.ExecutionGateClosed,
        };
        var readySnapshot = snapshot with
        {
            LastFailureReason = LaunchSnapshot.ResolveFailureReason(gate, LaunchState.Ready, false, null),
        };
        Assert(readySnapshot.LastFailureReason == "静态身份检查完成，但 Nikkiward 当前执行能力仍未启用。", "normal gate reason");
        var rejected = readySnapshot.WithPreflightFailure(preflight);
        Assert(rejected.State == LaunchState.Failed && rejected.LastFailureReason == snapshot.LastFailureReason,
            "a click-time refusal must replace the previous gate reason");
        var result = await new RedactedDiagnosticReportExporter().ExportAsync(profile, rejected, fixture.Root);
        Assert(result.Succeeded, result.Error ?? "export failed");
        using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(result.JsonFilePath!));
        var reason = json.RootElement.GetProperty("snapshot").GetProperty("lastFailureReason").GetString()!;
        foreach (var report in new[] { reason, await File.ReadAllTextAsync(result.TextFilePath!) })
        {
            Assert(report.Contains("SignerMismatch") && report.Contains("untrusted publisher"), "missing signer verdict");
            Assert(!report.Contains(fixture.Root, StringComparison.OrdinalIgnoreCase), "game path leaked");
        }
    }

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
