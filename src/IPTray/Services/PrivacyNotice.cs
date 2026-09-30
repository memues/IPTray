using System.Diagnostics;

namespace IPTray.Services;

internal static class PrivacyNotice
{
    public const string PolicyUrl = "https://github.com/memues/IPTray/blob/main/PRIVACY.md";

    public static void EnsureChoice()
    {
        AppSettings settings = AppSettings.Load();
        if (settings.OnlineLookupsAllowed is not null)
        {
            return;
        }

        settings.OnlineLookupsAllowed = MessageBox.Show(
            "IPTray runs in the notification area. Click its flag or globe icon to open the menu.\r\n\r\n" +
            "To look up your public IP and country, IPTray contacts ipwho.is, ipapi.co, " +
            "ipify and country.is over HTTPS. These services receive your public IP; " +
            "country.is may also receive it in the request URL. Flag downloads contact " +
            "flagcdn.com or flagsapi.com.\r\n\r\n" +
            "IP history and settings stay on this PC. IPTray has no accounts, ads or telemetry. " +
            "You can turn off online lookups at any time in the tray menu. " +
            "The Privacy policy menu item opens the full policy in your browser.\r\n\r\n" +
            "Allow online lookups? Choose No to use IPTray offline and view DNS servers.",
            "IPTray - privacy and first use", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        settings.Save();
    }

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
