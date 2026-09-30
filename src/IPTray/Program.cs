using IPTray.Services;

namespace IPTray;

internal static class Program
{
    internal const string AppName = "IPTray";

    [STAThread]
    private static int Main(string[] args)
    {
#if !STORE_BUILD
        // Elevated helper mode. A second, UAC-elevated copy of this executable performs the
        // privileged DNS work for the user-level instance. This has to run before the
        // single-instance guard, because the normal instance is still running. Anything starting
        // with the verb is handled there and never falls through to the tray icon, so a malformed
        // invocation exits with a status code instead of quietly starting a second copy.
        if (args.Length > 0 && string.Equals(args[0], ElevatedHost.Verb, StringComparison.Ordinal))
        {
            return ElevatedHost.Run(args);
        }
#else
        // The Store package contains no privileged helper, including through direct invocation.
        if (args.Length > 0 && args[0] == "--apply-dns")
        {
            return 10;
        }
#endif

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
