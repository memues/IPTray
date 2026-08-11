using System.Globalization;
using System.Net;
using System.Text.Json;

namespace IPTray.Services;

/// <summary>The public IP address as reported by one of the lookup providers.</summary>
internal sealed record IpInfo(string Ip, string CountryCode, string CountryName, string Isp)
{
    public bool HasCountry => CountryCode.Length == 2;

    public string CountryLabel => HasCountry
        ? (CountryName.Length > 0 ? $"{CountryName} ({CountryCode})" : CountryCode)
        : "Unknown location";
}

/// <summary>
/// Resolves the current public IP address. Providers are tried in order so that one being
/// down, rate limited or blocked does not take the whole feature with it.
/// </summary>
internal static class IpLookupService
{
    private static readonly Func<CancellationToken, Task<IpInfo?>>[] Providers =
    {
        FromIpWhoIs,
        FromIpApiCo,
        FromIpifyAndCountryIs,
    };

    /// <summary>Returns the current public IP, or <c>null</c> when every provider failed.</summary>
    public static async Task<IpInfo?> LookupAsync(CancellationToken cancellationToken)
    {
        foreach (var provider in Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromSeconds(9));

            try
            {
                IpInfo? info = await provider(attempt.Token).ConfigureAwait(false);
                if (info is not null)
                {
                    return info;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Provider unreachable or returned something unusable - fall through to the next one.
            }
        }

        return null;
    }

    private static async Task<IpInfo?> FromIpWhoIs(CancellationToken ct)
    {
        using JsonDocument doc = await GetJsonAsync("https://ipwho.is/", ct).ConfigureAwait(false);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("success", out JsonElement success) &&
            success.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        string isp = string.Empty;
        if (root.TryGetProperty("connection", out JsonElement connection) &&
            connection.ValueKind == JsonValueKind.Object)
        {
            isp = ReadString(connection, "isp");
            if (isp.Length == 0)
            {
                isp = ReadString(connection, "org");
            }
        }

        return Build(ReadString(root, "ip"), ReadString(root, "country_code"), ReadString(root, "country"), isp);
    }

    private static async Task<IpInfo?> FromIpApiCo(CancellationToken ct)
    {
        using JsonDocument doc = await GetJsonAsync("https://ipapi.co/json/", ct).ConfigureAwait(false);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        return Build(
            ReadString(root, "ip"),
            ReadString(root, "country_code"),
            ReadString(root, "country_name"),
            ReadString(root, "org"));
    }

    private static async Task<IpInfo?> FromIpifyAndCountryIs(CancellationToken ct)
    {
        string ip;
        using (JsonDocument doc = await GetJsonAsync("https://api.ipify.org?format=json", ct).ConfigureAwait(false))
        {
            ip = ReadString(doc.RootElement, "ip");
        }

        if (!IPAddress.TryParse(ip, out _))
        {
            return null;
        }

        string countryCode = string.Empty;
        try
        {
            using JsonDocument doc = await GetJsonAsync(
                "https://api.country.is/" + Uri.EscapeDataString(ip), ct).ConfigureAwait(false);
            countryCode = ReadString(doc.RootElement, "country");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The address alone is still worth reporting.
        }

        return Build(ip, countryCode, string.Empty, string.Empty);
    }

    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using HttpResponseMessage response = await Web.Client
            .GetAsync(url, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
    }

    private static IpInfo? Build(string ip, string countryCode, string countryName, string isp)
    {
        if (!IPAddress.TryParse(ip, out IPAddress? parsed))
        {
            return null;
        }

        countryCode = NormalizeCountryCode(countryCode);
        string name = ResolveCountryName(countryCode);
        if (name.Length == 0)
        {
            name = countryName.Trim();
        }

        return new IpInfo(parsed.ToString(), countryCode, name, isp.Trim());
    }

    private static string NormalizeCountryCode(string value)
    {
        value = value.Trim().ToUpperInvariant();
        if (value.Length != 2 || !char.IsAsciiLetterUpper(value[0]) || !char.IsAsciiLetterUpper(value[1]))
        {
            return string.Empty;
        }

        return value;
    }

    /// <summary>Maps an ISO country code to its English name so every provider reads the same.</summary>
    private static string ResolveCountryName(string countryCode)
    {
        if (countryCode.Length != 2)
        {
            return string.Empty;
        }

        try
        {
            return new RegionInfo(countryCode).EnglishName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            _ => string.Empty,
        };
    }
}
