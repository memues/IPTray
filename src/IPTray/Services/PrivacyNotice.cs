using System.Diagnostics;

namespace IPTray.Services;

internal static class PrivacyNotice
{
    public const string PolicyUrl = "https://github.com/memues/IPTray/blob/main/PRIVACY.md";

    public static void OpenPolicy()
    {
        try
        {
            Process.Start(new ProcessStartInfo(PolicyUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            MessageBox.Show(PolicyUrl, "IPTray - privacy policy", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
