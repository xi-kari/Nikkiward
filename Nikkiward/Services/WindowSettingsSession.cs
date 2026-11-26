using Nikkiward.Models;

namespace Nikkiward.Services;

internal sealed class WindowSettingsSession(IUserSettingsStore store) : IUserSettingsStore
{
    private static readonly UserSettings Defaults = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile UserSettings? _current;
    private volatile UserSettingsStoreException? _readError;
    private bool _closed;

    public string SettingsFilePath => store.SettingsFilePath;

    internal UserSettings Current => _current ?? Defaults;

    internal string? ProtectionMessage => _readError?.Message;

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAsync(false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UserSettings> ReadAsync(bool reload, CancellationToken cancellationToken)
    {
        if (_readError is { } error)
        {
            throw error;
        }

        if (!reload && _current is { } current)
        {
            return current;
        }

        try
        {
            return _current = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (UserSettingsStoreException ex)
        {
            var reason = ex.InnerException switch
            {
                UnsupportedSettingsVersionException version => $"本版本无法读取版本号为 {version.Version} 的设置文件。",
                System.Text.Json.JsonException or ArgumentException => "设置文件格式无效。",
                _ => "无法读取设置文件。",
            };
            _readError = new UserSettingsStoreException(
                $"{reason}已启用只读保护。请在设置的「文件管理」中从备份恢复，或重置为默认设置。", ex);
            throw _readError;
        }
    }

    public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default) =>
        UpdateAsync(current => settings with { WindowPlacement = current.WindowPlacement }, cancellationToken);

    internal Task SaveWindowPlacementAsync(WindowPlacementSettings placement) =>
        UpdateAsync(current => current.WindowPlacement == placement ? current : current with { WindowPlacement = placement },
            CancellationToken.None, automatic: true, reload: true);

    internal async Task<UserSettings> UpdateAsync(Func<UserSettings, UserSettings> change,
        CancellationToken cancellationToken, bool automatic = false, bool reload = false)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_closed)
            {
                if (automatic)
                {
                    return Current;
                }

                throw new InvalidOperationException("设置会话已关闭。");
            }

            UserSettings current;
            try
            {
                current = await ReadAsync(reload, cancellationToken).ConfigureAwait(false);
            }
            catch (UserSettingsStoreException) when (automatic)
            {
                return Current;
            }

            var updated = change(current);
            if (!ReferenceEquals(current, updated))
            {
                await store.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
                _current = updated;
            }

            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task RecoverUnreadableAsync(string? backupPath, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_readError is null || _closed || store is not JsonUserSettingsStore json)
            {
                throw new InvalidOperationException("当前无法执行设置恢复。");
            }

            _current = await json.RecoverUnreadableAsync(backupPath, cancellationToken).ConfigureAwait(false);
            _closed = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task DeleteAsync(Func<CancellationToken, Task> delete, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_closed)
            {
                return;
            }

            if (_readError is { } error)
            {
                throw error;
            }

            await delete(cancellationToken).ConfigureAwait(false);
            _closed = true;
            _current = Defaults;
        }
        finally
        {
            _gate.Release();
        }
    }
}
