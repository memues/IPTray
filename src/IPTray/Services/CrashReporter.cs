using System.Text;

namespace IPTray.Services;

/// <summary>Last line of defence: never let an unexpected exception kill the tray icon silently.</summary>
internal static class CrashReporter
{
    private static readonly object Gate = new();
    private static bool _dialogShown;

    public static void Report(Exception? ex)
    {
        if (ex is null)
        {
            return;
        }

        Write(ex);

        bool show;
        lock (Gate)
        {
            show = !_dialogShown;
            _dialogShown = true;
        }

        if (!show)
        {
            return;
        }

        MessageBox.Show(
            $"IPTray hit an unexpected error and will keep running.\r\n\r\n{ex.GetType().Name}: {ex.Message}\r\n\r\n" +
            $"Details were written to:\r\n{AppPaths.ErrorFile}",
            Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Records a non-fatal problem. Never shows UI.</summary>
    public static void Write(Exception ex)
    {
        try
        {
            var sb = new StringBuilder()
                .AppendLine($"---- {DateTime.Now:yyyy-MM-dd HH:mm:ss} ----")
                .AppendLine(ex.ToString());

            lock (Gate)
            {
                File.AppendAllText(AppPaths.ErrorFile, sb.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Reporting must never throw.
        }
    }
}
