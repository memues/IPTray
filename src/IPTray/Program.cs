using IPTray.Services;

namespace IPTray;

internal static class Program
{
    internal const string AppName = "IPTray";

    [STAThread]
    private static int Main(string[] args)
    {
        // Elevated helper mode. A second, UAC-elevated copy of this executable performs the
        // privileged DNS work for the user-level instance. This has to run before the
        // single-instance guard, because the normal instance is still running.
        if (args.Length >= 2 && string.Equals(args[0], ElevatedHost.Verb, StringComparison.OrdinalIgnoreCase))
        {
            return ElevatedHost.Run(args[1]);
        }

        using var singleInstance = new Mutex(true, @"Local\IPTray.SingleInstance.v1", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "IPTray is already running.\r\n\r\nLook for the flag icon in the notification area.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashReporter.Report(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashReporter.Report(e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();

        using var context = new TrayContext();
        Application.Run(context);
        return 0;
    }
}
