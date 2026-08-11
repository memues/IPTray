using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IPTray.Services;

/// <summary>
/// The privileged half of the DNS feature. IPTray itself runs unelevated; when the user applies
/// a DNS change it relaunches this executable once through UAC with <see cref="Verb"/>, which
/// runs the netsh commands and writes the outcome back next to the request file.
/// </summary>
internal static class ElevatedHost
{
    public const string Verb = "--apply-dns";

    public static int Run(string requestPath)
    {
        DnsResult result;

        try
        {
            DnsRequest request = JsonSerializer.Deserialize<DnsRequest>(File.ReadAllText(requestPath))
                ?? throw new InvalidOperationException("The DNS request file could not be read.");
            result = Execute(request);
        }
        catch (Exception ex)
        {
            result = new DnsResult { Success = false, Message = ex.Message };
        }

        try
        {
            File.WriteAllText(requestPath + ".result", JsonSerializer.Serialize(result));
        }
        catch (Exception)
        {
            // The caller falls back to the exit code.
        }

        return result.Success ? 0 : 1;
    }

    private static DnsResult Execute(DnsRequest request)
    {
        var output = new StringBuilder();

        if (request.Adapter.Length > 0)
        {
            if (request.Primary.Length == 0)
            {
                if (!Run(output, "netsh", "interface", "ipv4", "set", "dnsservers",
                        "name=" + request.Adapter, "source=dhcp"))
                {
                    return Failure(output);
                }

                // Best effort: an adapter without IPv6 simply reports an error here.
                var ignored = new StringBuilder();
                Run(ignored, "netsh", "interface", "ipv6", "set", "dnsservers",
                    "name=" + request.Adapter, "source=dhcp");
            }
            else
            {
                if (!Run(output, "netsh", "interface", "ipv4", "set", "dnsservers",
                        "name=" + request.Adapter, "source=static",
                        "address=" + request.Primary, "register=primary", "validate=no"))
                {
                    return Failure(output);
                }

                if (request.Secondary.Length > 0 &&
                    !Run(output, "netsh", "interface", "ipv4", "add", "dnsservers",
                        "name=" + request.Adapter, "address=" + request.Secondary,
                        "index=2", "validate=no"))
                {
                    return Failure(output);
                }
            }
        }

        if (request.FlushCache && !Run(output, "ipconfig", "/flushdns"))
        {
            return Failure(output);
        }

        return new DnsResult { Success = true, Message = output.ToString().Trim() };
    }

    private static DnsResult Failure(StringBuilder output)
    {
        string message = output.ToString().Trim();
        return new DnsResult
        {
            Success = false,
            Message = message.Length > 0 ? message : "The command reported an error.",
        };
    }

    private static bool Run(StringBuilder output, string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            output.AppendLine($"Could not start {fileName}.");
            return false;
        }

        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(60_000))
        {
            output.AppendLine($"{fileName} did not finish in time.");
            return false;
        }

        Append(output, standardOutput);
        Append(output, standardError);
        return process.ExitCode == 0;
    }

    private static void Append(StringBuilder output, string text)
    {
        text = text.Trim();
        if (text.Length > 0)
        {
            output.AppendLine(text);
        }
    }
}
