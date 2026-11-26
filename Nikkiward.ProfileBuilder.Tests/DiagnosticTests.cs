using Nikkiward.Features.Background;
using Nikkiward.Models;
using Nikkiward.Services;
using System.Reflection;
using System.Text.Json;

internal static class DiagnosticTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("signer rejection survives redacted JSON and text export", SignerRejectionIsExported),
        ("diagnostics expose backdrop state without artwork identity", TestBackdropStateIsRedacted),
        ("diagnostics redact entire authorization and cookie headers", () => CheckRedaction(
            "Authorization: Bearer SAMPLE_ONLY_AUTH\r\n" +
            "Authorization: Digest username=\"SAMPLE_ONLY_USER\", response=\"SAMPLE_ONLY_DIGEST\"\r\n" +
            "Cookie: first=SAMPLE_ONLY_COOKIE; second=SAMPLE_ONLY_TAIL\r\n" +
            "Set-Cookie: id=SAMPLE_ONLY_SET; Path=/SAMPLE_ONLY_PATH\r\n\tSAMPLE_ONLY_CONTINUED\r\n" +
            "bearer SAMPLE_ONLY_STANDALONE")),
        ("diagnostics redact quoted and unquoted credential fields", () => CheckRedaction(
            """{"access_token":"SAMPLE_ONLY\"SAMPLE_ONLY_TAIL","refresh_token":"SAMPLE_ONLY_REFRESH"}""" + "\n" +
            "tIcKeT = 'SAMPLE_ONLY TAIL SAMPLE_ONLY'\n" +
            "session_id: SAMPLE_ONLY_SESSION\n" +
            "Authorization=Basic SAMPLE_ONLY_BASIC\n" +
            "token=SAMPLE_ONLY_VALUE WITH SAMPLE_ONLY_SPACES")),
        ("diagnostics preserve safe URL parameters while redacting credentials", () => CheckRedaction(
            "https://fixture.invalid/path?ticket=SAMPLE_ONLY_ONE&access_token=SAMPLE_ONLY%20TWO" +
            "&refresh%5Ftoken=SAMPLE_ONLY_ENCODED&session=SAMPLE_ONLY,TAIL&safe=keep", "safe=keep")),
        ("diagnostics redact wrapped assignments and spaced URL values", () => CheckRedaction(
            "\"session\":\r\n  \"SAMPLE_ONLY_WRAPPED\"\n" +
            "https://fixture.invalid/?authorization=Bearer SAMPLE_ONLY_URL&safe=keep", "safe=keep")),
        ("diagnostics redact entire URI values and structured JSON members", () => CheckRedaction(
            "https://fixture.invalid/?access_token=abc'SAMPLE_ONLY_TAIL&safe=keep\n" +
            "{\"session\"\n:\n\"SAMPLE_ONLY_JSON\",\n\"ticket\":{\n\"id\":\"SAMPLE_ONLY_NESTED\"}}\n" +
            "Command line: \"sample program\" --argument SAMPLE_ONLY_COMMAND", "safe=keep")),
        ("diagnostics redact report paths, identifiers and component fields", TestDiagnosticFields),
    ];

    private static async Task CheckRedaction(string sample, string? preserved = null)
    {
        using var fixture = new TempFixture();
        var profile = new LaunchProfile { ProfileId = "fixture" };
        var snapshot = new LaunchSnapshot { LastFailureReason = sample + "\nFixture end." };
        var result = await new RedactedDiagnosticReportExporter().ExportAsync(profile, snapshot, fixture.Root);
        Assert(result.Succeeded, result.Error ?? "diagnostic export should succeed");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.JsonFilePath!));
        var failure = json.RootElement.GetProperty("snapshot").GetProperty("lastFailureReason").GetString()!;
        var text = await File.ReadAllTextAsync(result.TextFilePath!);
        foreach (var output in new[] { failure, text })
        {
            Assert(!output.Contains("SAMPLE_ONLY", StringComparison.Ordinal), "a synthetic credential survived");
            Assert(output.Contains(preserved ?? "Fixture end.", StringComparison.Ordinal), "safe context was lost");
            Assert(output.Contains("[REDACTED]", StringComparison.Ordinal), "redaction marker is missing");
        }

        Assert(text.Contains(failure, StringComparison.Ordinal), "the reports must use the same redacted value");
    }

    private static async Task TestDiagnosticFields()
    {
        using var fixture = new TempFixture();
        var profile = new LaunchProfile
        {
            ProfileId = "token=SAMPLE_ONLY_ID",
            GameRootPath = fixture.Root,
            GameExecutablePath = Path.Combine(fixture.Root, "ticket=SAMPLE_ONLY_GAME"),
        };
        var snapshot = new LaunchSnapshot
        {
            Components = [new ComponentVerification
            {
                FilePath = Path.Combine(fixture.Root, "session=SAMPLE_ONLY_COMPONENT"),
                Sha256 = "token=SAMPLE_ONLY_HASH",
                SignatureStatusCode = "Bearer SAMPLE_ONLY_STATUS",
            }],
        };
        var result = await new RedactedDiagnosticReportExporter().ExportAsync(profile, snapshot, fixture.Root);
        Assert(result.Succeeded, result.Error ?? "diagnostic export should succeed");
        foreach (var path in new[] { result.JsonFilePath!, result.TextFilePath! })
        {
            var output = await File.ReadAllTextAsync(path);
            Assert(!output.Contains("SAMPLE_ONLY", StringComparison.Ordinal), "a component or path value survived");
            Assert(!Path.GetFileName(path).Contains("SAMPLE_ONLY", StringComparison.Ordinal), "report filename leaked");
        }
    }

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
