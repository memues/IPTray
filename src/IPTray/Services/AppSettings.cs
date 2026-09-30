using System.Text.Json;

namespace IPTray.Services;

internal sealed class AppSettings
{
    /// <summary>Intervals offered in the tray menu, in seconds.</summary>
    public static readonly int[] AllowedIntervals = { 30, 60, 300, 900 };

    public int RefreshSeconds { get; set; } = 60;

    public bool NotifyOnChange { get; set; } = true;

    public bool? OnlineLookupsAllowed { get; set; }

    public static AppSettings Load()
    {
        var settings = new AppSettings();

        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                string json = File.ReadAllText(AppPaths.SettingsFile);
                settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            settings = new AppSettings();
        }

        if (!AllowedIntervals.Contains(settings.RefreshSeconds))
        {
            settings.RefreshSeconds = 60;
        }

        return settings;
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppPaths.SettingsFile, json);
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
        }
    }
}
