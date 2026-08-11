using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Win32;

namespace IPTray.Services;

/// <summary>DNS configuration of one network adapter.</summary>
internal sealed record AdapterDns(
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

/// <summary>What the elevated helper instance is asked to do.</summary>
internal sealed class DnsRequest
{
    public string Adapter { get; set; } = string.Empty;

    /// <summary>Empty means "switch this adapter back to automatic (DHCP)".</summary>
    public string Primary { get; set; } = string.Empty;

    public string Secondary { get; set; } = string.Empty;

    public bool FlushCache { get; set; }
}

internal sealed class DnsResult
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;
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
                    nic.Name, nic.Description, hasGateway, IsAutomatic(nic.Id), ipv4, ipv6));
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
    /// Applies a DNS change through a UAC-elevated copy of this executable. Returns false and
    /// an explanation when elevation was declined or netsh reported a problem.
    /// </summary>
    public static bool Apply(DnsRequest request, out string message)
    {
        message = string.Empty;

        string requestPath = Path.Combine(
            Path.GetTempPath(), $"iptray-dns-{Guid.NewGuid():N}.json");
        string resultPath = requestPath + ".result";

        try
        {
            File.WriteAllText(requestPath, JsonSerializer.Serialize(request));

            string executable = Environment.ProcessPath ?? Application.ExecutablePath;
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"{ElevatedHost.Verb} \"{requestPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                message = "The elevated helper could not be started.";
                return false;
            }

            if (!process.WaitForExit(90_000))
            {
                message = "The DNS change timed out.";
                return false;
            }

            if (File.Exists(resultPath))
            {
                DnsResult? result = JsonSerializer.Deserialize<DnsResult>(File.ReadAllText(resultPath));
                if (result is not null)
                {
                    message = result.Message;
                    return result.Success;
                }
            }

            if (process.ExitCode == 0)
            {
                return true;
            }

            message = $"The elevated helper exited with code {process.ExitCode}.";
            return false;
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
        finally
        {
            TryDelete(requestPath);
            TryDelete(resultPath);
        }
    }

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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // A leftover file in %TEMP% is harmless.
        }
    }
}
