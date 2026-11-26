using Microsoft.Win32;

namespace Nikkiward.Services;

public static class ApplicationDataPaths
{
    private const string RegistryPath = @"Software\Nikkiward";
    private const string RegistryValueName = "UserDataFolder";
    private static readonly Lazy<string?> ConfiguredRoot = new(ReadConfiguredRoot);
    private static readonly Lazy<string?> RootPath = new(() => ResolveRoot(ConfiguredRoot.Value, DefaultRoot));
    private static bool useDefaultForSession;

    internal static string? UnavailableRoot => RootPath.Value is null ? ConfiguredRoot.Value : null;

    internal static void UseDefaultForSession() => useDefaultForSession = true;

    public static string DefaultRoot
    {
        get
        {
            var localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException(
                    "The LocalApplicationData directory is unavailable.");
            }

            return Path.Combine(
                Path.GetFullPath(localApplicationData),
                "Nikkiward");
        }
    }

    public static string Root => RootPath.Value ?? (useDefaultForSession
        ? DefaultRoot
        : throw new DirectoryNotFoundException("选定的数据目录不可用。"));

    public static string SettingsFilePath => Path.Combine(Root, "settings.json");

    public static void ConfigureRoot(string folderPath, Action? startSuccessor = null)
    {
        var normalized = ValidateExistingRoot(folderPath, requireSettings: true);
        var previous = ConfiguredRoot.Value ?? DefaultRoot;
        WriteConfiguredRoot(normalized);
        try
        {
            startSuccessor?.Invoke();
        }
        catch
        {
            WriteConfiguredRoot(previous);
            throw;
        }
    }

    private static void WriteConfiguredRoot(string normalized)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("The Nikkiward registry key could not be opened.");
        if (string.Equals(normalized, DefaultRoot, StringComparison.OrdinalIgnoreCase))
        {
            key.DeleteValue(RegistryValueName, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(RegistryValueName, normalized, RegistryValueKind.String);
        }
    }

    public static string? ResolveRoot(string? configuredRoot, string defaultRoot)
    {
        var normalizedDefault = Path.GetFullPath(defaultRoot);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return normalizedDefault;
        }

        try
        {
            var normalized = Path.GetFullPath(configuredRoot.Trim());
            return Directory.Exists(normalized) ? normalized : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public static string ValidateExistingRoot(
        string folderPath,
        bool requireSettings)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("The data folder is empty.", nameof(folderPath));
        }

        var normalized = Path.GetFullPath(folderPath.Trim());
        if (!Directory.Exists(normalized))
        {
            throw new DirectoryNotFoundException(
                $"The data folder does not exist: {normalized}");
        }

        if (requireSettings && !File.Exists(Path.Combine(normalized, "settings.json")))
        {
            throw new FileNotFoundException(
                "The selected data folder does not contain settings.json.",
                Path.Combine(normalized, "settings.json"));
        }

        var probePath = Path.Combine(
            normalized,
            $".nikkiward-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
            stream.WriteByte(0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UnauthorizedAccessException(
                $"The data folder is not writable: {normalized}",
                ex);
        }
        finally
        {
            try
            {
                File.Delete(probePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return normalized;
    }

    private static string? ReadConfiguredRoot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        return key?.GetValue(RegistryValueName) as string;
    }
}
