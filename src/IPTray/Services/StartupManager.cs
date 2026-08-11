using Microsoft.Win32;

namespace IPTray.Services;

/// <summary>Per-user "run at sign-in" registration.</summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(Program.AppName) is string value && value.Length > 0;
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            return false;
        }
    }

    /// <summary>Returns false when the registry could not be updated.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                string executable = Environment.ProcessPath ?? Application.ExecutablePath;
                key.SetValue(Program.AppName, '"' + executable + '"', RegistryValueKind.String);
            }
            else if (key.GetValue(Program.AppName) is not null)
            {
                key.DeleteValue(Program.AppName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            return false;
        }
    }
}
