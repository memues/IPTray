namespace IPTray.Services;

/// <summary>Locations IPTray writes to. All under %APPDATA%\IPTray so an uninstall can drop them.</summary>
internal static class AppPaths
{
    public static string DataDirectory { get; } = EnsureDirectory(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Program.AppName));

    public static string FlagCacheDirectory => EnsureDirectory(Path.Combine(DataDirectory, "flags"));

    public static string LogFile => Path.Combine(DataDirectory, "ip-log.csv");

    public static string ArchivedLogFile => Path.Combine(DataDirectory, "ip-log.previous.csv");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string ErrorFile => Path.Combine(DataDirectory, "error.log");

    private static string EnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception)
        {
            // A read-only or redirected profile should not stop the tray icon from working;
            // the callers all tolerate a missing directory.
        }

        return path;
    }
}
