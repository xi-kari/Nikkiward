using Nikkiward.Models;

namespace Nikkiward.Services;

public interface IUserSettingsStore
{
    string SettingsFilePath { get; }

    Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default);
}

public sealed class UserSettingsStoreException : Exception
{
    public UserSettingsStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
