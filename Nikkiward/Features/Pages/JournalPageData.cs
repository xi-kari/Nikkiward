using System.Text.Json;
using Nikkiward.Services;
using Nikkiward.ViewModels;

namespace Nikkiward.Features.Pages;

internal static class JournalPageData
{
    internal static readonly string[] Actions = ["official", "preview-image", "time-settings", "check-add", "check-toggle", "link-add", "link-open"];

    internal static PlannerCommand ReadCommand(string action, JsonElement message)
    {
        string? Field(string name) => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        var command = new PlannerCommand(action, Field("id"), Field("text"), Field("url"));
        if (!Actions.Contains(action) || !command.Valid()) throw new InvalidDataException("清单内容无效，请检查名称和 HTTPS 链接。");
        return command;
    }

    internal static object State(JournalSnapshot? snapshot, bool enabled, IReadOnlyList<PlaySession>? sessions,
        string? error, PlannerData planner, DateTimeOffset now, TimeZoneInfo zone)
    {
        var metrics = new[] {
            new { label = "登录天数", value = snapshot?.LoginDays, unit = "天" },
            new { label = "游戏时长", value = snapshot?.GameHours, unit = "小时" },
            new { label = "服装数量", value = snapshot?.OutfitCount, unit = "件" },
            new { label = "大喵斗篷", value = snapshot?.MomoCloakCount, unit = "件" },
            new { label = "设计图", value = snapshot?.SketchCount, unit = "张" }
        };
        var images = snapshot?.Resources.Where(r => !string.IsNullOrEmpty(r.LocalFilePath))
            .ToLookup(r => r.Url, r => Path.GetFileName(r.LocalFilePath), StringComparer.OrdinalIgnoreCase);
        var modules = snapshot?.Sections.Select(section => new {
            title = section.Title, text = section.Text,
            rows = section.Metrics.Select(m => new { label = m.Label, value = m.Value, image = (string?)null })
                .Concat(section.Blocks.OrderBy(b => b.Order).Select(b => {
                    var group = b.ParentKey;
                    if (b.Kind == "wardrobe-card" && b.Source?.Split(':') is { Length: >= 3 } parts) group = parts[^2];
                    return new {
                        label = Join(group, b.Label),
                        value = Join(b.Value, b.Current != null && b.Total != null ? $"{b.Current}/{b.Total}" : null, b.Status, b.Unit),
                        image = b.ResourceUrl == null ? null : images?[b.ResourceUrl].FirstOrDefault()
                    };
                }))
        });
        return new {
            date = snapshot == null ? null : TimeZoneInfo.ConvertTime(snapshot.CapturedAtUtc, zone).ToString("yyyy-MM-dd HH:mm"),
            metrics, modules, enabled, error = enabled ? error : null,
            play = enabled && sessions != null ? PlayTime(sessions, now, zone) : null,
            items = planner.Items.Select(i => new { id = i.Id, text = i.Text, done = i.Done }),
            links = planner.Links.Select(l => new { id = l.Id, text = l.Text })
        };
    }

    private static string Join(params string?[] fields) =>
        string.Join(" · ", fields.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());

    private static DateTimeOffset Midnight(DateTime day, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(day)) day = day.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(day) ? zone.GetAmbiguousTimeOffsets(day).Max() : zone.GetUtcOffset(day);
        return new(day, offset);
    }

    private static object PlayTime(IReadOnlyList<PlaySession> sessions, DateTimeOffset now, TimeZoneInfo zone)
    {
        var first = TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(-6);
        var start = Midnight(first, zone);
        var days = Enumerable.Range(0, 7).Select(index => {
            var day = first.AddDays(index);
            var end = index == 6 ? now : Midnight(day.AddDays(1), zone);
            double hours = 0;
            foreach (var session in sessions)
            {
                var left = session.StartUtc > start ? session.StartUtc : start;
                var right = session.EndUtc < end ? session.EndUtc : end;
                if (right > left) hours += (right - left).TotalHours;
            }
            start = end;
            return new { label = "日一二三四五六"[(int)day.DayOfWeek].ToString(), hours };
        }).ToArray();
        return new {
            total = sessions.Sum(s => s.Duration.TotalHours), week = days.Sum(d => d.hours), days,
            recent = sessions.OrderByDescending(s => s.EndUtc).Take(3).Select(s => new {
                time = TimeZoneInfo.ConvertTime(s.StartUtc, zone).ToString("MM-dd HH:mm"), channel = s.Channel,
                duration = $"{(long)s.Duration.TotalHours}:{s.Duration.Minutes:00}"
            })
        };
    }
}
