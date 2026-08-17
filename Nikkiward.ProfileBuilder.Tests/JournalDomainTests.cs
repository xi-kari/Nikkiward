using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nikkiward.ViewModels;

internal static class JournalDomainTests
{
    public static (string Name, Func<Task> Run)[] All =>
    [
        ("journal section keys prefer stable routes over presentation anchors", SectionKeysPreferStableRoutes),
        ("journal section keys fall back to normalized DOM anchors", SectionKeysFallBackToDomAnchors),
        ("journal fields require capture source before showing a value", JournalFieldsRequireSource),
        ("journal failure projection distinguishes all three failure states", FailureProjectionDistinguishesStates),
        ("journal navigation ignores canceled and stale completions", NavigationIgnoresCanceledAndStaleCompletions),
        ("journal capture assessment separates sign-in and selector drift", CaptureAssessmentSeparatesFailures),
        ("journal document readiness waits for complete content", DocumentReadinessWaitsForCompleteContent),
        ("journal automatic sync never schedules below thirty minutes", AutomaticSyncHonorsMinimumInterval),
        ("journal automatic sync applies bounded exponential backoff", AutomaticSyncAppliesBackoff),
        ("journal URLs accept only normalized official HTTPS hosts", UrlPolicyAcceptsOnlyOfficialHttpsHosts),
        ("journal image validation trusts supported byte signatures", ImageValidationUsesByteSignatures),
        ("journal route intent follows SPA resonance routes", RouteIntentFollowsSpaResonanceRoutes),
    ];

    private static Task SectionKeysPreferStableRoutes()
    {
        var first = JournalSectionKey.Derive(
            "https://myl.nuanpaper.com/tools/journal/clothesPress?from=home#temporary",
            "old-label");
        var second = JournalSectionKey.Derive(
            "/tools/journal/clothesPress?campaign=new",
            "new-label");

        AssertEqual("route:/tools/journal/clothespress", first, "absolute route key");
        AssertEqual(first, second, "route identity must ignore query, fragment, and presentation anchor");
        return Task.CompletedTask;
    }

    private static Task SectionKeysFallBackToDomAnchors()
    {
        AssertEqual(
            "anchor:wish-resonance",
            JournalSectionKey.Derive("/tools/journal", "#Wish_Resonance"),
            "root route anchor key");
        AssertEqual(
            "anchor:exploration-overview",
            JournalSectionKey.Derive("https://example.test/tools/journal/overview", " Exploration Overview "),
            "untrusted route must not override a stable anchor");
        AssertEqual(
            JournalSectionKey.Unknown,
            JournalSectionKey.Derive(null, null),
            "missing identity");
        AssertEqual(
            JournalSectionKey.Unknown,
            JournalSectionKey.Derive("/tools/journal", "心愿共鸣"),
            "localized presentation title must not become structural identity");
        return Task.CompletedTask;
    }

    private static Task JournalFieldsRequireSource()
    {
        var captured = JournalSourcedField.FromCapture(
            " 128 ",
            " [data-stat='login-days'] ");
        var unproven = JournalSourcedField.FromCapture("128", null);

        Assert(captured.IsAvailable, "captured field should be available");
        AssertEqual("128", captured.Value, "captured value");
        AssertEqual("[data-stat='login-days']", captured.Source, "capture selector");
        AssertEqual("128", captured.ProjectText(), "captured display text");
        Assert(!unproven.IsAvailable, "a value without its selector must stay unavailable");
        AssertEqual("暂无数据", unproven.ProjectText(), "missing display text");
        return Task.CompletedTask;
    }

    private static Task FailureProjectionDistinguishesStates()
    {
        var network = JournalCaptureFailureProjector.Project(JournalCaptureFailureKind.NetworkFailure);
        var signedOut = JournalCaptureFailureProjector.Project(JournalCaptureFailureKind.NotSignedIn);
        var structure = JournalCaptureFailureProjector.Project(JournalCaptureFailureKind.StructureChanged);

        AssertEqual("网络连接失败，请稍后重试。", network.Message, "network message");
        AssertEqual("尚未登录奇想手账，请先登录后同步。", signedOut.Message, "signed-out message");
        AssertEqual("官方页面结构可能已更新，请稍后再试。", structure.Message, "structure message");
        Assert(network.Kind != signedOut.Kind && signedOut.Kind != structure.Kind, "failure kinds must remain distinct");
        return Task.CompletedTask;
    }

    private static Task CaptureAssessmentSeparatesFailures()
    {
        var network = JournalCaptureAssessmentProjector.Assess(false, false, null, 0, 0);
        var signedOut = JournalCaptureAssessmentProjector.Assess(true, false, null, 0, 0);
        var changed = JournalCaptureAssessmentProjector.Assess(true, true, "/tools/journal", 0, 0);
        var usable = JournalCaptureAssessmentProjector.Assess(true, true, "/tools/journal", 2, 0);

        AssertEqual(JournalCaptureFailureKind.NetworkFailure, network.FailureKind, "network assessment");
        AssertEqual(JournalCaptureFailureKind.NotSignedIn, signedOut.FailureKind, "signed-out assessment");
        AssertEqual(JournalCaptureFailureKind.StructureChanged, changed.FailureKind, "selector assessment");
        Assert(usable.IsUsable && usable.FailureKind is null, "sourced fields should be usable");
        return Task.CompletedTask;
    }

    private static Task DocumentReadinessWaitsForCompleteContent()
    {
        Assert(
            !JournalDocumentReadinessProjector.IsOverviewReady(6, 40),
            "overview must keep waiting while expected sections are missing");
        Assert(
            !JournalDocumentReadinessProjector.IsOverviewReady(7, 19),
            "overview must keep waiting while visible content is incomplete");
        Assert(
            JournalDocumentReadinessProjector.IsOverviewReady(7, 20),
            "overview should be ready after the minimum content arrives");
        Assert(
            !JournalDocumentReadinessProjector.IsResonanceReady(0, 20),
            "resonance must keep waiting before cards arrive");
        Assert(
            JournalDocumentReadinessProjector.IsResonanceReady(1, 1),
            "resonance should be ready after a card and image arrive");
        return Task.CompletedTask;
    }

    private static Task NavigationIgnoresCanceledAndStaleCompletions()
    {
        AssertEqual<JournalCaptureFailureKind?>(
            null,
            JournalNavigationFailureProjector.Project(
                isSuccess: true,
                isCurrentNavigation: true,
                webErrorStatus: "Unknown"),
            "successful navigation");
        AssertEqual<JournalCaptureFailureKind?>(
            null,
            JournalNavigationFailureProjector.Project(
                isSuccess: false,
                isCurrentNavigation: false,
                webErrorStatus: "HostNameNotResolved"),
            "stale navigation completion");
        AssertEqual<JournalCaptureFailureKind?>(
            null,
            JournalNavigationFailureProjector.Project(
                isSuccess: false,
                isCurrentNavigation: true,
                webErrorStatus: "OperationCanceled"),
            "superseded navigation cancellation");
        AssertEqual<JournalCaptureFailureKind?>(
            JournalCaptureFailureKind.NotSignedIn,
            JournalNavigationFailureProjector.Project(
                isSuccess: false,
                isCurrentNavigation: true,
                webErrorStatus: "ValidAuthenticationCredentialsRequired"),
            "authentication challenge");
        AssertEqual<JournalCaptureFailureKind?>(
            JournalCaptureFailureKind.NetworkFailure,
            JournalNavigationFailureProjector.Project(
                isSuccess: false,
                isCurrentNavigation: true,
                webErrorStatus: "HostNameNotResolved"),
            "real network failure");
        return Task.CompletedTask;
    }

    private static Task AutomaticSyncHonorsMinimumInterval()
    {
        var attemptedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        var projection = JournalSyncScheduleProjector.Project(
            attemptedAt,
            TimeSpan.FromMinutes(5),
            consecutiveFailures: 0);

        AssertEqual(TimeSpan.FromMinutes(30), projection.Delay, "minimum automatic interval");
        AssertEqual(attemptedAt.AddMinutes(30), projection.NextAttemptAtUtc, "next automatic attempt");
        return Task.CompletedTask;
    }

    private static Task AutomaticSyncAppliesBackoff()
    {
        var attemptedAt = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

        AssertEqual(
            TimeSpan.FromMinutes(30),
            JournalSyncScheduleProjector.Project(attemptedAt, TimeSpan.FromMinutes(30), 1).Delay,
            "first failure delay");
        AssertEqual(
            TimeSpan.FromHours(2),
            JournalSyncScheduleProjector.Project(attemptedAt, TimeSpan.FromMinutes(30), 3).Delay,
            "third consecutive failure delay");
        AssertEqual(
            TimeSpan.FromHours(24),
            JournalSyncScheduleProjector.Project(attemptedAt, TimeSpan.FromMinutes(30), 20).Delay,
            "backoff ceiling");
        return Task.CompletedTask;
    }

    private static Task UrlPolicyAcceptsOnlyOfficialHttpsHosts()
    {
        AssertEqual(
            "https://myl.nuanpaper.com/tools/journal/banner.png",
            JournalUrlPolicy.NormalizeOfficialUrl(
                "https://MYL.nuanpaper.com/tools/journal/banner.png?token=discard#preview"),
            "normalized official URL");
        AssertEqual(
            "https://papegames.com/",
            JournalUrlPolicy.NormalizeOfficialUrl("https://papegames.com"),
            "apex official URL");

        var rejected = new[]
        {
            "http://myl.nuanpaper.com/image.png",
            "https://nuanpaper.com.evil.test/image.png",
            "https://evilnuanpaper.com/image.png",
            "https://user@assets.papegames.com/image.png",
            "https://assets.papegames.com:444/image.png",
        };
        Assert(
            rejected.All(value => JournalUrlPolicy.NormalizeOfficialUrl(value) is null),
            "non-official URL must be rejected");
        return Task.CompletedTask;
    }

    private static Task ImageValidationUsesByteSignatures()
    {
        AssertFormat(
            JournalImageFormat.Png,
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A },
            "PNG");
        AssertFormat(
            JournalImageFormat.Jpeg,
            new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 },
            "JPEG");
        AssertFormat(
            JournalImageFormat.Gif,
            "GIF89a"u8.ToArray(),
            "GIF");
        AssertFormat(
            JournalImageFormat.WebP,
            new byte[] { 0x52, 0x49, 0x46, 0x46, 0x08, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 },
            "WebP");
        AssertFormat(
            JournalImageFormat.Bmp,
            new byte[] { 0x42, 0x4D, 0x20, 0x00 },
            "BMP");

        Assert(
            !JournalImageMagic.TryDetect("<html>not an image</html>"u8, out _),
            "HTML bytes must not pass as an image");
        Assert(
            !JournalImageMagic.TryDetect(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x41, 0x56, 0x49, 0x20 }, out _),
            "a RIFF container without the WEBP brand must be rejected");
        return Task.CompletedTask;
    }

    private static Task RouteIntentFollowsSpaResonanceRoutes()
    {
        AssertEqual(
            JournalRouteIntent.ResonanceHistory,
            JournalRouteIntentProjector.Project(new Uri("https://myl.nuanpaper.com/tools/journal/clothesPress/")),
            "direct resonance route");
        AssertEqual(
            JournalRouteIntent.ResonanceHistory,
            JournalRouteIntentProjector.Project(new Uri("https://myl.nuanpaper.com/tools/journal#/tools/journal/clothesPress")),
            "hash resonance route");
        AssertEqual(
            JournalRouteIntent.ResonanceHistory,
            JournalRouteIntentProjector.Project(new Uri("https://myl.nuanpaper.com/tools/journal?route=%2Ftools%2Fjournal%2FclothesPress")),
            "query resonance route");
        AssertEqual(
            JournalRouteIntent.Overview,
            JournalRouteIntentProjector.Project(new Uri("https://myl.nuanpaper.com/tools/journal")),
            "overview route");
        Assert(
            JournalRouteIntentProjector.ShouldRedirectToResonance(
                JournalRouteIntent.ResonanceHistory,
                new Uri("https://myl.nuanpaper.com/tools/journal")),
            "pending resonance target survives the login redirect");
        Assert(
            !JournalRouteIntentProjector.ShouldRedirectToResonance(
                JournalRouteIntent.Overview,
                new Uri("https://myl.nuanpaper.com/tools/journal")),
            "overview target must not redirect");
        AssertEqual(
            JournalRouteIntent.Unknown,
            JournalRouteIntentProjector.Project(new Uri("https://evil.example/tools/journal/clothesPress")),
            "untrusted route");
        return Task.CompletedTask;
    }

    private static void AssertFormat(JournalImageFormat expected, byte[] bytes, string message)
    {
        Assert(JournalImageMagic.TryDetect(bytes, out var actual), $"{message} signature was not detected");
        AssertEqual(expected, actual, $"{message} format");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message}: expected '{expected}', actual '{actual}'.");
        }
    }
}
