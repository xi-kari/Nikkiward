namespace Nikkiward.Services;

internal sealed record ObservedGame(string Path, int Pid, DateTimeOffset CreatedUtc);
internal sealed record PlaySession(string Channel, DateTimeOffset StartUtc, DateTimeOffset EndUtc, TimeSpan Duration);

internal sealed class PlayTimeLedger(IReadOnlyList<PlaySession> history)
{
    internal static readonly TimeSpan Period = TimeSpan.FromSeconds(30);
    private readonly List<PlaySession> _sessions = [.. history];
    private Dictionary<(int, DateTimeOffset), int> _active = [];
    private readonly HashSet<(int Pid, DateTimeOffset CreatedUtc)> _recentlySeen = [];
    private DateTimeOffset? _lastObservation;
    private DateTimeOffset? _recordingBoundary = history.Count == 0 ? null : history.Max(s => s.EndUtc);
    public IReadOnlyList<PlaySession> Sessions => _sessions;

    public void Disconnect(DateTimeOffset now)
    {
        _active.Clear();
        _recordingBoundary = now;
        _lastObservation = null;
    }

    public bool Observe(DateTimeOffset now, IEnumerable<ObservedGame> games,
        IReadOnlyDictionary<string, string> installations, bool enabled = true)
    {
        if (!enabled)
        {
            Disconnect(now);
            return false;
        }

        _recentlySeen.RemoveWhere(key => now - key.CreatedUtc > Period);
        var next = new Dictionary<(int, DateTimeOffset), int>();
        var changed = false;
        foreach (var game in games)
        {
            var key = (game.Pid, game.CreatedUtc);
            if (!installations.TryGetValue(game.Path, out var channel) || next.ContainsKey(key))
            {
                continue;
            }


            if (_active.TryGetValue(key, out var index) && _sessions[index].Channel == channel &&
                now >= _lastObservation && now - _lastObservation <= Period * 2)
            {
                var old = _sessions[index];
                _sessions[index] = old with { EndUtc = now, Duration = now - old.StartUtc };
            }
            else
            {
                var start = now - game.CreatedUtc <= Period && game.CreatedUtc <= now &&
                    (_recordingBoundary is null || game.CreatedUtc > _recordingBoundary) && _recentlySeen.Add(key)
                    ? game.CreatedUtc : now;
                index = _sessions.Count;
                _sessions.Add(new(channel, start, now, now - start));
            }
            next.Add(key, index);
            changed = true;
        }
        _active = next;
        _lastObservation = now;
        return changed;
    }
}
