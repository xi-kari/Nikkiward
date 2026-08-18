using Microsoft.Win32;

namespace Nikkiward.Services;

public static class ApplicationDataPaths
{
    private const string RegistryPath = @"Software\Nikkiward";
    private const string RegistryValueName = "UserDataFolder";
    private static readonly Lazy<string> RootPath = new(ResolveRoot);

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

    public static string Root => RootPath.Value;

    public static string SettingsFilePath => Path.Combine(Root, "settings.json");

    public static void ConfigureRoot(string folderPath)
    {
        var normalized = ValidateExistingRoot(folderPath, requireSettings: true);
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

    public static string ResolveRoot(string? configuredRoot, string defaultRoot)
    {
        var normalizedDefault = Path.GetFullPath(defaultRoot);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return normalizedDefault;
        }

        try
        {
            var normalized = Path.GetFullPath(configuredRoot.Trim());
            return Directory.Exists(normalized) &&
                   File.Exists(Path.Combine(normalized, "settings.json"))
                ? normalized
                : normalizedDefault;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return normalizedDefault;
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

    private static string ResolveRoot()
    {
        string? configuredRoot = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            configuredRoot = key?.GetValue(RegistryValueName) as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return ResolveRoot(configuredRoot, DefaultRoot);
    }
}
