using System;
using System.Linq;
using System.Text.Json.Serialization;

namespace Nikkiward.ViewModels;

public static class JournalSectionKey
{
    public const string Unknown = "unknown";

    private const string JournalRootPath = "/tools/journal";
    private static readonly Uri JournalBaseUri = new("https://myl.nuanpaper.com");

    public static bool IsStable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var key = value.Trim();
        if (key.StartsWith("route:", StringComparison.OrdinalIgnoreCase))
        {
            return Derive(key["route:".Length..], null).Equals(
                key,
                StringComparison.OrdinalIgnoreCase);
        }

        if (key.StartsWith("anchor:", StringComparison.OrdinalIgnoreCase))
        {
            return Derive(null, key["anchor:".Length..]).Equals(
                key,
                StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static string Derive(string? route, string? anchor)
    {
        var routePath = NormalizeRoutePath(route);
        if (routePath is not null &&
            !routePath.Equals(JournalRootPath, StringComparison.OrdinalIgnoreCase))
        {
            return $"route:{routePath.ToLowerInvariant()}";
        }

        var anchorKey = NormalizeAnchor(anchor);
        return anchorKey is null ? Unknown : $"anchor:{anchorKey}";
    }

    private static string? NormalizeRoutePath(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        var trimmed = route.Trim();
        if (!Uri.TryCreate(JournalBaseUri, trimmed, out var uri) ||
            !JournalUrlPolicy.IsAllowedOfficialUri(uri))
        {
            return null;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
        {
            path = "/";
        }

        if (!path.Equals(JournalRootPath, StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith($"{JournalRootPath}/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return path.Length <= 240 ? path : null;
    }

    private static string? NormalizeAnchor(string? anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor))
        {
            return null;
        }

        var value = anchor.Trim().TrimStart('#');
        if (value.Length == 0 || value.Length > 160)
        {
            return null;
        }

        var buffer = new char[value.Length];
        var length = 0;
        var separatorPending = false;
        foreach (var character in value)
        {
            if (IsAsciiLetterOrDigit(character))
            {
                if (separatorPending && length > 0)
                {
                    buffer[length++] = '-';
                }

                buffer[length++] = char.ToLowerInvariant(character);
                separatorPending = false;
            }
            else
            {
                separatorPending = length > 0;
            }
        }

        return length == 0 ? null : new string(buffer, 0, length);
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9';
}

public enum JournalRouteIntent
{
    Unknown,
    Login,
    Overview,
    ResonanceHistory,
}

public static class JournalRouteIntentProjector
{
    private const string JournalRootPath = "/tools/journal";
    private const string ResonancePath = "/tools/journal/clothespress";
    public static JournalRouteIntent Project(Uri? uri)
    {
        if (uri is null || !JournalUrlPolicy.IsAllowedOfficialUri(uri))
        {
            return JournalRouteIntent.Unknown;
        }

        var route = NormalizePath(uri.AbsolutePath);
        if (route is null)
        {
            return JournalRouteIntent.Unknown;
        }

        if (route.Equals(ResonancePath, StringComparison.OrdinalIgnoreCase))
        {
            return JournalRouteIntent.ResonanceHistory;
        }

        if (route.Equals("/tools/journal/login", StringComparison.OrdinalIgnoreCase))
        {
            return JournalRouteIntent.Login;
        }

        if (route.Equals(JournalRootPath, StringComparison.OrdinalIgnoreCase))
        {
            var fragmentRoute = ExtractRoute(uri.Fragment);
            if (fragmentRoute is not null &&
                fragmentRoute.Equals(ResonancePath, StringComparison.OrdinalIgnoreCase))
            {
                return JournalRouteIntent.ResonanceHistory;
            }

            var queryRoute = ExtractRoute(uri.Query);
            if (queryRoute is not null &&
                queryRoute.Equals(ResonancePath, StringComparison.OrdinalIgnoreCase))
            {
                return JournalRouteIntent.ResonanceHistory;
            }

            return JournalRouteIntent.Overview;
        }

        return JournalRouteIntent.Unknown;
    }

    public static bool ShouldRedirectToResonance(JournalRouteIntent pending, Uri? uri) =>
        pending == JournalRouteIntent.ResonanceHistory &&
        Project(uri) == JournalRouteIntent.Overview;

    private static string? ExtractRoute(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim().TrimStart('#', '?', '/');
        if (candidate.StartsWith("route=", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("path=", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("target=", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[(candidate.IndexOf('=') + 1)..];
        }

        candidate = Uri.UnescapeDataString(candidate)
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/');
        if (!candidate.StartsWith("tools/journal", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        candidate = "/" + candidate;
        var queryStart = candidate.IndexOfAny(['?', '#']);
        if (queryStart >= 0)
        {
            candidate = candidate[..queryStart];
        }

        return NormalizePath(candidate);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var value = path.Trim();
        if (!value.StartsWith("/", StringComparison.Ordinal))
        {
            value = "/" + value;
        }

        value = value.TrimEnd('/');
        return value.Length == 0 ? "/" : value;
    }
}

public sealed record JournalSourcedField
{
    [JsonConstructor]
    public JournalSourcedField(string? value, string? source)
    {
        Value = value;
        Source = source;
    }

    public string? Value { get; }

    public string? Source { get; }

    public bool IsAvailable => Value is not null && Source is not null;

    public static JournalSourcedField FromCapture(string? value, string? source)
    {
        var normalizedValue = Normalize(value);
        var normalizedSource = Normalize(source);
        return normalizedValue is null || normalizedSource is null
            ? new JournalSourcedField(null, normalizedSource)
            : new JournalSourcedField(normalizedValue, normalizedSource);
    }

    public string ProjectText(string unavailableText = "暂无数据")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unavailableText);
        return IsAvailable ? Value! : unavailableText;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum JournalCaptureFailureKind
{
    NetworkFailure,
    NotSignedIn,
    StructureChanged,
}

public sealed record JournalCaptureFailureProjection(
    JournalCaptureFailureKind Kind,
    string Message,
    bool CanRetryAutomatically,
    bool RequiresInteractiveLogin);

public static class JournalCaptureFailureProjector
{
    public static JournalCaptureFailureProjection Project(JournalCaptureFailureKind kind) =>
        kind switch
        {
            JournalCaptureFailureKind.NetworkFailure => new(
                kind,
                "网络连接失败，请稍后重试。",
                CanRetryAutomatically: true,
                RequiresInteractiveLogin: false),
            JournalCaptureFailureKind.NotSignedIn => new(
                kind,
                "尚未登录奇想手账，请先登录后同步。",
                CanRetryAutomatically: false,
                RequiresInteractiveLogin: true),
            JournalCaptureFailureKind.StructureChanged => new(
                kind,
                "官方页面结构可能已更新，请稍后再试。",
                CanRetryAutomatically: true,
                RequiresInteractiveLogin: false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown journal capture failure."),
        };
}

public static class JournalNavigationFailureProjector
{
    public static JournalCaptureFailureKind? Project(
        bool isSuccess,
        bool isCurrentNavigation,
        string? webErrorStatus)
    {
        if (isSuccess ||
            !isCurrentNavigation ||
            string.Equals(
                webErrorStatus,
                "OperationCanceled",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return string.Equals(
            webErrorStatus,
            "ValidAuthenticationCredentialsRequired",
            StringComparison.OrdinalIgnoreCase)
            ? JournalCaptureFailureKind.NotSignedIn
            : JournalCaptureFailureKind.NetworkFailure;
    }
}

public sealed record JournalCaptureAssessment(
    bool IsUsable,
    JournalCaptureFailureKind? FailureKind);

public static class JournalDocumentReadinessProjector
{
    public const int OverviewMinimumTitleCount = 7;
    public const int OverviewMinimumVisibleLineCount = 20;

    public static bool IsOverviewReady(int titleCount, int visibleLineCount) =>
        titleCount >= OverviewMinimumTitleCount &&
        visibleLineCount >= OverviewMinimumVisibleLineCount;

    public static bool IsResonanceReady(int cardCount, int imageCount) =>
        cardCount > 0 && imageCount > 0;
}

public static class JournalCaptureAssessmentProjector
{
    public static JournalCaptureAssessment Assess(
        bool navigationSucceeded,
        bool isOfficialJournalPage,
        string? sourcePagePath,
        int sourcedFieldCount,
        int sourcedSectionCount)
    {
        if (!navigationSucceeded)
        {
            return new(false, JournalCaptureFailureKind.NetworkFailure);
        }

        if (!isOfficialJournalPage || string.IsNullOrWhiteSpace(sourcePagePath))
        {
            return new(false, JournalCaptureFailureKind.NotSignedIn);
        }

        return sourcedFieldCount > 0 || sourcedSectionCount > 0
            ? new(true, null)
            : new(false, JournalCaptureFailureKind.StructureChanged);
    }
}

public sealed record JournalSyncScheduleProjection(
    DateTimeOffset NextAttemptAtUtc,
    TimeSpan Delay,
    TimeSpan BaseInterval,
    int ConsecutiveFailures);

public static class JournalSyncScheduleProjector
{
    public static readonly TimeSpan MinimumAutomaticInterval = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MaximumBackoffInterval = TimeSpan.FromHours(24);

    public static JournalSyncScheduleProjection Project(
        DateTimeOffset attemptedAtUtc,
        TimeSpan configuredInterval,
        int consecutiveFailures)
    {
        if (configuredInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configuredInterval),
                configuredInterval,
                "The automatic sync interval must be positive.");
        }

        if (consecutiveFailures < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(consecutiveFailures),
                consecutiveFailures,
                "The failure count cannot be negative.");
        }

        var baseInterval = configuredInterval < MinimumAutomaticInterval
            ? MinimumAutomaticInterval
            : configuredInterval;
        var ceiling = baseInterval > MaximumBackoffInterval
            ? baseInterval
            : MaximumBackoffInterval;
        var delay = baseInterval;
        for (var index = 1; index < consecutiveFailures && delay < ceiling; index++)
        {
            delay = delay > TimeSpan.FromTicks(ceiling.Ticks / 2)
                ? ceiling
                : TimeSpan.FromTicks(delay.Ticks * 2);
        }

        return new JournalSyncScheduleProjection(
            attemptedAtUtc.Add(delay),
            delay,
            baseInterval,
            consecutiveFailures);
    }
}

public static class JournalUrlPolicy
{
    private static readonly string[] AllowedHostSuffixes =
    [
        "nuanpaper.com",
        "papegames.com",
    ];

    public static string? NormalizeOfficialUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            !IsAllowedOfficialUri(uri))
        {
            return null;
        }

        var normalized = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return normalized.Uri.AbsoluteUri;
    }

    public static bool IsAllowedOfficialUri(Uri? uri)
    {
        if (uri is null ||
            !uri.IsAbsoluteUri ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.');
        return AllowedHostSuffixes.Any(allowed =>
            host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase));
    }
}

public enum JournalImageFormat
{
    Png,
    Jpeg,
    Gif,
    WebP,
    Bmp,
    Tiff,
    Avif,
}

public static class JournalImageMagic
{
    public static bool TryDetect(ReadOnlySpan<byte> bytes, out JournalImageFormat format)
    {
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            format = JournalImageFormat.Png;
            return true;
        }

        if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            format = JournalImageFormat.Jpeg;
            return true;
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            format = JournalImageFormat.Gif;
            return true;
        }

        if (bytes.Length >= 12 &&
            bytes[..4].SequenceEqual("RIFF"u8) &&
            bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            format = JournalImageFormat.WebP;
            return true;
        }

        if (bytes.StartsWith("BM"u8))
        {
            format = JournalImageFormat.Bmp;
            return true;
        }

        if (bytes.StartsWith(new byte[] { 0x49, 0x49, 0x2A, 0x00 }) ||
            bytes.StartsWith(new byte[] { 0x4D, 0x4D, 0x00, 0x2A }))
        {
            format = JournalImageFormat.Tiff;
            return true;
        }

        if (bytes.Length >= 12 &&
            bytes.Slice(4, 4).SequenceEqual("ftyp"u8) &&
            (bytes.Slice(8, 4).SequenceEqual("avif"u8) ||
             bytes.Slice(8, 4).SequenceEqual("avis"u8)))
        {
            format = JournalImageFormat.Avif;
            return true;
        }

        format = default;
        return false;
    }

    public static string GetFileExtension(JournalImageFormat format) =>
        format switch
        {
            JournalImageFormat.Png => ".png",
            JournalImageFormat.Jpeg => ".jpg",
            JournalImageFormat.Gif => ".gif",
            JournalImageFormat.WebP => ".webp",
            JournalImageFormat.Bmp => ".bmp",
            JournalImageFormat.Tiff => ".tiff",
            JournalImageFormat.Avif => ".avif",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown image format."),
        };
}
