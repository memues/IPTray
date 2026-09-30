using System.Text.Json;

namespace IPTray.Services;

internal sealed class AppSettings
{
    /// <summary>Intervals offered in the tray menu, in seconds.</summary>
    public static readonly int[] AllowedIntervals = { 30, 60, 300, 900 };

    public int RefreshSeconds { get; set; } = 60;

    public bool NotifyOnChange { get; set; } = true;

    public bool? OnlineLookupsAllowed { get; set; } = true;

    public static AppSettings Load()
    {
        var settings = new AppSettings();

        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                string json = File.ReadAllText(AppPaths.SettingsFile);
                settings = FromJson(json);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            settings = new AppSettings();
        }

        return Normalize(settings);
    }

    internal static AppSettings FromJson(string json) =>
        Normalize(JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings());

    private static AppSettings Normalize(AppSettings settings)
    {
        // Older releases may omit this setting or store null. Keep an explicit opt-out.
        settings.OnlineLookupsAllowed ??= true;
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
