using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IPTray.Services;

/// <summary>
/// The privileged half of the DNS feature. IPTray itself runs unelevated; when the user applies
/// a DNS change it relaunches this executable once through UAC with <see cref="Verb"/>.
/// <para>
/// Everything this type does is deliberately constrained, because it runs with administrator
/// rights while the process that asked for it does not:
/// </para>
/// <list type="bullet">
///   <item>The request arrives as command-line arguments, which are fixed when the process is
///   created and cannot be altered afterwards. An earlier design passed a file in %TEMP%, which
///   a same-user process could rewrite between the UAC prompt and the read.</item>
///   <item>Every argument is validated here rather than trusted from the caller.</item>
///   <item>The outcome is reported through the exit code only. Writing a result file into a
///   user-writable directory from an elevated process is an arbitrary-overwrite primitive.</item>
///   <item>Helper executables are launched by absolute path out of the system directory, so a
///   planted <c>netsh.exe</c> next to the application or in the working directory cannot be
///   picked up instead.</item>
/// </list>
/// </summary>
internal static class ElevatedHost
{
    public const string Verb = "--apply-dns";

    /// <summary>Stands in for the primary address when the adapter should go back to DHCP.</summary>
    public const string AutomaticToken = "auto";

    /// <summary>Stands in for an omitted secondary address.</summary>
    public const string NoneToken = "none";

    public const string FlushToken = "flush";
    public const string NoFlushToken = "noflush";

    public const int ExitSuccess = 0;
    public const int ExitUnexpected = 1;
    public const int ExitInvalidArguments = 10;
    public const int ExitAdapterNotFound = 11;
    public const int ExitSetFailed = 12;
    public const int ExitAddFailed = 13;
    public const int ExitFlushFailed = 14;

    private static string SystemDirectory => Environment.GetFolderPath(Environment.SpecialFolder.System);

    public static int Run(string[] args)
    {
        try
        {
            return Execute(args);
        }
        catch (Exception)
        {
            return ExitUnexpected;
        }
    }

    /// <summary>
    /// Adapter identifiers come from Windows and look like <c>{2E1B45C1-....}</c>. Accepting only
    /// this shape keeps anything resembling a path, a switch or a quote out of the command line.
    /// </summary>
    public static bool IsSafeAdapterId(string value)
    {
        if (value.Length is 0 or > 96)
        {
            return false;
        }

        foreach (char c in value)
        {
            bool allowed = char.IsAsciiLetterOrDigit(c) || c is '{' or '}' or '-' or '_';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsIpv4(string value) =>
        IPAddress.TryParse(value, out IPAddress? address) &&
        address.AddressFamily == AddressFamily.InterNetwork &&
        address.ToString() == value;

    private static int Execute(string[] args)
    {
        // --apply-dns <adapter-id> <primary|auto> <secondary|none> <flush|noflush>
        if (args.Length != 5 || !string.Equals(args[0], Verb, StringComparison.Ordinal))
        {
            return ExitInvalidArguments;
        }

        string adapterId = args[1];
        string primary = args[2];
        string secondary = args[3];
        string flush = args[4];

        if (!IsSafeAdapterId(adapterId))
        {
            return ExitInvalidArguments;
        }

        bool automatic = string.Equals(primary, AutomaticToken, StringComparison.Ordinal);
        if (!automatic && !IsIpv4(primary))
        {
            return ExitInvalidArguments;
        }

        bool hasSecondary = !string.Equals(secondary, NoneToken, StringComparison.Ordinal);
        if (hasSecondary && (automatic || !IsIpv4(secondary)))
        {
            return ExitInvalidArguments;
        }

        if (flush is not (FlushToken or NoFlushToken))
        {
            return ExitInvalidArguments;
        }

        bool flushCache = string.Equals(flush, FlushToken, StringComparison.Ordinal);

        string? adapterName = ResolveAdapterName(adapterId);
        if (adapterName is null)
        {
            return ExitAdapterNotFound;
        }

        string netsh = Path.Combine(SystemDirectory, "netsh.exe");
        string ipconfig = Path.Combine(SystemDirectory, "ipconfig.exe");

        if (automatic)
        {
            if (!RunTool(netsh, "interface", "ipv4", "set", "dnsservers",
                    "name=" + adapterName, "source=dhcp"))
            {
                return ExitSetFailed;
            }

            // Best effort: an adapter without IPv6 simply reports an error here.
            RunTool(netsh, "interface", "ipv6", "set", "dnsservers",
                "name=" + adapterName, "source=dhcp");
        }
        else
        {
            if (!RunTool(netsh, "interface", "ipv4", "set", "dnsservers",
                    "name=" + adapterName, "source=static",
                    "address=" + primary, "register=primary", "validate=no"))
            {
                return ExitSetFailed;
            }

            if (hasSecondary && !RunTool(netsh, "interface", "ipv4", "add", "dnsservers",
                    "name=" + adapterName, "address=" + secondary, "index=2", "validate=no"))
            {
                return ExitAddFailed;
            }
        }

        if (flushCache && !RunTool(ipconfig, "/flushdns"))
        {
            return ExitFlushFailed;
        }

        return ExitSuccess;
    }

    /// <summary>Maps the adapter identifier back to the connection name netsh expects.</summary>
    private static string? ResolveAdapterName(string adapterId)
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (string.Equals(nic.Id, adapterId, StringComparison.OrdinalIgnoreCase))
            {
                return nic.Name;
            }
        }

        return null;
    }

    private static bool RunTool(string fileName, params string[] arguments)
    {
        if (!File.Exists(fileName))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = SystemDirectory,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        // Drain both pipes before waiting so a chatty tool cannot fill a buffer and block.
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();

        return process.WaitForExit(60_000) && process.ExitCode == 0;
    }
}
