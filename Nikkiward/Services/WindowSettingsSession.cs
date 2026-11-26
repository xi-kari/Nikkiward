using Nikkiward.Models;

namespace Nikkiward.Services;

internal sealed class WindowSettingsSession(IUserSettingsStore store) : IUserSettingsStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UserSettings? _current;
    private bool _deleted;

    public string SettingsFilePath => store.SettingsFilePath;

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _current ??= await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Page snapshots own the preferences; only the window owns its placement.
    public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default) =>
        WriteAsync(() =>
        {
            var updated = _current is null ? settings : settings with { WindowPlacement = _current.WindowPlacement };
            return PersistAsync(updated, cancellationToken);
        }, cancellationToken);

    internal Task SaveWindowPlacementAsync(WindowPlacementSettings placement) =>
        WriteAsync(async () =>
        {
            var latest = await store.LoadAsync().ConfigureAwait(false);
            if (latest.WindowPlacement == placement)
            {
                _current = latest;
                return;
            }

            await PersistAsync(latest with { WindowPlacement = placement }, CancellationToken.None).ConfigureAwait(false);
        }, CancellationToken.None);

    internal Task DeleteAsync(Func<CancellationToken, Task> delete, CancellationToken cancellationToken) =>
        WriteAsync(async () =>
        {
            await delete(cancellationToken).ConfigureAwait(false);
            _deleted = true;
            _current = new UserSettings();
        }, cancellationToken);

    private async Task PersistAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        await store.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        _current = settings;
    }

    private async Task WriteAsync(Func<Task> write, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_deleted)
            {
                await write().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
