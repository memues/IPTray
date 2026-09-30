using Microsoft.Win32;

namespace IPTray.Services;

/// <summary>Per-user "run at sign-in" registration.</summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static async Task<bool> IsEnabledAsync()
    {
        try
        {
#if STORE_BUILD
            Windows.ApplicationModel.StartupTask task = await Windows.ApplicationModel.StartupTask
                .GetAsync("IPTrayStartup");
            return task.State is Windows.ApplicationModel.StartupTaskState.Enabled or
                Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
#else
            await Task.CompletedTask;
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(Program.AppName) is string value && value.Length > 0;
#endif
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            return false;
        }
    }

    /// <summary>Returns false when the registry could not be updated.</summary>
    public static async Task<bool> SetEnabledAsync(bool enabled)
    {
        try
        {
#if STORE_BUILD
            Windows.ApplicationModel.StartupTask task = await Windows.ApplicationModel.StartupTask
                .GetAsync("IPTrayStartup");
            if (enabled)
            {
                Windows.ApplicationModel.StartupTaskState state = await task.RequestEnableAsync();
                return state is Windows.ApplicationModel.StartupTaskState.Enabled or
                    Windows.ApplicationModel.StartupTaskState.EnabledByPolicy;
            }
            task.Disable();
            return task.State is not (Windows.ApplicationModel.StartupTaskState.Enabled or
                Windows.ApplicationModel.StartupTaskState.EnabledByPolicy);
#else
            await Task.CompletedTask;
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
#endif
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            return false;
        }
    }
}
