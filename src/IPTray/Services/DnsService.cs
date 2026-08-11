using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace IPTray.Services;

/// <summary>DNS configuration of one network adapter.</summary>
internal sealed record AdapterDns(
    string Id,
    string Name,
    string Description,
    bool HasGateway,
    bool IsAutomatic,
    IReadOnlyList<string> Ipv4Dns,
    IReadOnlyList<string> Ipv6Dns)
{
    public string SourceLabel => IsAutomatic ? "Automatic (DHCP)" : "Static";

    public override string ToString() => HasGateway ? Name + "  •  active" : Name;
}

/// <summary>What the elevated helper is asked to do. Never leaves this process as a file.</summary>
internal sealed class DnsRequest
{
    public string AdapterId { get; init; } = string.Empty;

    public string AdapterName { get; init; } = string.Empty;

    /// <summary>Empty means "switch this adapter back to automatic (DHCP)".</summary>
    public string Primary { get; init; } = string.Empty;

    public string Secondary { get; init; } = string.Empty;

    public bool FlushCache { get; init; }
}

internal static class DnsService
{
    /// <summary>Reads the DNS configuration of every connected adapter.</summary>
    public static List<AdapterDns> GetAdapters()
    {
        var adapters = new List<AdapterDns>();

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                IPInterfaceProperties properties;
                try
                {
                    properties = nic.GetIPProperties();
                }
                catch (NetworkInformationException)
                {
                    continue;
                }

                var ipv4 = new List<string>();
                var ipv6 = new List<string>();
                foreach (IPAddress address in properties.DnsAddresses)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ipv4.Add(address.ToString());
                    }
                    else if (address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        ipv6.Add(address.ToString());
                    }
                }

                bool hasGateway = properties.GatewayAddresses
                    .Any(g => g.Address is not null && !IsUnspecified(g.Address));

                adapters.Add(new AdapterDns(
                    nic.Id, nic.Name, nic.Description, hasGateway, IsAutomatic(nic.Id), ipv4, ipv6));
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
        }

        return adapters
            .OrderByDescending(a => a.HasGateway)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Applies a DNS change through a UAC-elevated copy of this executable. Returns false and an
    /// explanation when elevation was declined or the change did not go through.
    /// </summary>
    public static bool Apply(DnsRequest request, out string message)
    {
        bool automatic = request.Primary.Length == 0;

        if (!ElevatedHost.IsSafeAdapterId(request.AdapterId) ||
            (!automatic && !ElevatedHost.IsIpv4(request.Primary)) ||
            (request.Secondary.Length > 0 && (automatic || !ElevatedHost.IsIpv4(request.Secondary))))
        {
            message = "The selected adapter or address is not valid.";
            return false;
        }

        // Every token below is restricted to letters, digits, dots, braces, dashes and
        // underscores by the checks above, so the command line cannot be steered elsewhere.
        string arguments = string.Join(' ',
            ElevatedHost.Verb,
            Quote(request.AdapterId),
            Quote(automatic ? ElevatedHost.AutomaticToken : request.Primary),
            Quote(request.Secondary.Length > 0 ? request.Secondary : ElevatedHost.NoneToken),
            request.FlushCache ? ElevatedHost.FlushToken : ElevatedHost.NoFlushToken);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? Application.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                message = "The elevated helper could not be started.";
                return false;
            }

            if (!process.WaitForExit(120_000))
            {
                message = "The DNS change did not finish in time.";
                return false;
            }

            message = DescribeExitCode(process.ExitCode, request.AdapterName);
            return process.ExitCode == ElevatedHost.ExitSuccess;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            message = "Administrator approval is required to change DNS servers.";
            return false;
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            message = ex.Message;
            return false;
        }
    }

    private static string DescribeExitCode(int exitCode, string adapterName) => exitCode switch
    {
        ElevatedHost.ExitSuccess => string.Empty,
        ElevatedHost.ExitInvalidArguments => "The request was rejected as malformed.",
        ElevatedHost.ExitAdapterNotFound => $"{adapterName} is no longer available.",
        ElevatedHost.ExitSetFailed => "Windows refused to set the DNS servers on this adapter.",
        ElevatedHost.ExitAddFailed => "The primary server was set, but the secondary one was refused.",
        ElevatedHost.ExitFlushFailed => "The servers were set, but the resolver cache could not be flushed.",
        _ => "The DNS change did not go through.",
    };

    private static string Quote(string value) => '"' + value + '"';

    /// <summary>True when Windows, not the user, supplies the DNS servers for this adapter.</summary>
    private static bool IsAutomatic(string adapterId)
    {
        foreach (string root in new[]
                 {
                     @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\",
                     @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters\Interfaces\",
                 })
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(root + adapterId);
                if (key?.GetValue("NameServer") is string nameServer && nameServer.Trim().Length > 0)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Write(ex);
            }
        }

        return true;
    }

    private static bool IsUnspecified(IPAddress address) =>
        address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
}
