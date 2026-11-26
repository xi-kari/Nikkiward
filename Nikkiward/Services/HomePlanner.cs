using System.Text.Json;

namespace Nikkiward.Services;

internal sealed record ChecklistItem(string Id, string Text, bool Done);
internal sealed record PinnedLink(string Id, string Text, string Url);
internal sealed record LocalReminder(string Id, string Text, string Repeat, TimeOnly Time, DateOnly? Date, DayOfWeek? Weekday, DateTimeOffset? Due)
{
    internal DateTimeOffset? Next(DateTimeOffset after)
    {
        var today = DateOnly.FromDateTime(after.LocalDateTime);
        for (var i = 0; i <= 7; i++)
        {
            var day = Repeat == "once" ? Date!.Value : today.AddDays(i);
            if (Repeat == "weekly" && day.DayOfWeek != Weekday) continue;
            var local = day.ToDateTime(Time);
            while (TimeZoneInfo.Local.IsInvalidTime(local)) local = local.AddMinutes(1);
            DateTimeOffset due = new(local, TimeZoneInfo.Local.GetUtcOffset(local));
            if (due > after) return due;
            if (Repeat == "once") break;
        }
        return null;
    }
}

internal sealed record PlannerData(DateOnly Day, ChecklistItem[] Items, LocalReminder[] Reminders, PinnedLink[] Links)
{
    internal static PlannerData Empty => new(default, [], [], []);
    internal PlannerData Reset(DateTimeOffset now, TimeOnly reset)
    {
        var day = DateOnly.FromDateTime(now.LocalDateTime - reset.ToTimeSpan());
        return day == Day ? this : this with { Day = day, Items = Items.Select(i => i with { Done = false }).ToArray() };
    }

    internal PlannerData Advance(DateTimeOffset now, out LocalReminder[] due)
    {
        due = Reminders.Where(r => r.Due <= now).ToArray();
        return due.Length == 0 ? this : this with
        {
            Reminders = Reminders.Select(r => r.Due <= now ? r with { Due = r.Next(now) } : r).ToArray()
        };
    }

    internal static bool IsHttps(string? url) =>
        url?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0 && uri.UserInfo.Length == 0;

    internal PlannerData Change(PlannerCommand c, DateTimeOffset now)
    {
        var id = c.Id ?? Guid.NewGuid().ToString("N");
        var title = c.Text?.Trim() ?? "";
        switch (c.Action)
        {
            case "check-add": return this with { Items = [.. Items, new(id, title, false)] };
            case "check-edit": return this with { Items = Items.Select(i => i.Id == id ? i with { Text = title } : i).ToArray() };
            case "check-toggle": return this with { Items = Items.Select(i => i.Id == id ? i with { Done = !i.Done } : i).ToArray() };
            case "check-delete": return this with { Items = Items.Where(i => i.Id != id).ToArray() };
            case "check-move": return this with { Items = Move(Items, Array.FindIndex(Items, i => i.Id == id), c.Step!.Value) };
            case "link-add": return this with { Links = [.. Links, new(id, title, c.Url!)] };
            case "link-edit": return this with { Links = Links.Select(i => i.Id == id ? new PinnedLink(id, title, c.Url!) : i).ToArray() };
            case "link-delete": return this with { Links = Links.Where(i => i.Id != id).ToArray() };
            case "link-move": return this with { Links = Move(Links, Array.FindIndex(Links, i => i.Id == id), c.Step!.Value) };
            case "reminder-delete": return this with { Reminders = Reminders.Where(i => i.Id != id).ToArray() };
            case "reminder-add":
            case "reminder-edit":
                var r = new LocalReminder(id, title, c.Repeat!, c.Time!.Value, c.Date, c.Weekday, null);
                r = r with { Due = r.Next(now) ?? throw new ArgumentException("请选择未来的提醒时间。") };
                return this with { Reminders = c.Action == "reminder-add" ? [.. Reminders, r] : Reminders.Select(i => i.Id == id ? r : i).ToArray() };
            default: throw new ArgumentOutOfRangeException(nameof(c));
        }
    }

    private static T[] Move<T>(T[] source, int index, int step)
    {
        int to = index + step;
        if (index < 0 || to < 0 || to >= source.Length) return source;
        var copy = source.ToArray();
        (copy[to], copy[index]) = (copy[index], copy[to]);
        return copy;
    }
}

internal record PlannerCommand(string Action, string? Id = null, string? Text = null, string? Url = null,
    string? Repeat = null, TimeOnly? Time = null, DateOnly? Date = null, DayOfWeek? Weekday = null, int? Step = null)
{
    internal bool Valid()
    {
        bool existing = !string.IsNullOrWhiteSpace(Id);
        if (Id != null && !existing) return false;
        bool named = !string.IsNullOrWhiteSpace(Text);
        bool schedule = Time.HasValue && (Repeat switch { "daily" => true, "weekly" => Weekday is >= DayOfWeek.Sunday and <= DayOfWeek.Saturday, "once" => Date.HasValue, _ => false });
        return Action switch
        {
            "check-add" => !existing && named,
            "check-edit" => existing && named,
            "link-add" or "link-edit" => existing == (Action == "link-edit") && named && PlannerData.IsHttps(Url),
            "reminder-add" or "reminder-edit" => existing == (Action == "reminder-edit") && named && schedule,
            "check-toggle" or "check-delete" or "link-delete" or "link-open" or "reminder-delete" => existing,
            "check-move" or "link-move" => existing && Step is -1 or 1,
            _ => false
        };
    }
}

internal sealed class PlannerStore(string root)
{
    internal const string RelativePath = "home-planner.json";
    private readonly string _file = Path.Combine(root, RelativePath);
    internal PlannerData Load()
    {
        try
        {
            var data = JsonSerializer.Deserialize<PlannerData>(File.ReadAllText(_file));
            if (data is not { Items: not null, Reminders: not null, Links: not null } ||
                data.Items.Any(i => i == null || string.IsNullOrWhiteSpace(i.Text) || string.IsNullOrWhiteSpace(i.Id)) ||
                data.Reminders.Any(r => r == null || !new PlannerCommand("reminder-edit", r.Id, r.Text,
                    Repeat: r.Repeat, Time: r.Time, Date: r.Date, Weekday: r.Weekday).Valid()) ||
                data.Links.Any(l => l == null || !new PlannerCommand("link-edit", l.Id, l.Text, l.Url).Valid()))
                throw new JsonException("清单文件内容无效，原文件已保留。");
            return data;
        }
        catch (FileNotFoundException) { return PlannerData.Empty; }
        catch (DirectoryNotFoundException) { return PlannerData.Empty; }
    }

    internal void Save(PlannerData data)
    {
        Directory.CreateDirectory(root);
        using var file = new AtomicFile(_file);
        JsonSerializer.Serialize(file.Stream, data);
        file.Commit(overwrite: true);
    }
}
