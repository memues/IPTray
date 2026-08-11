namespace IPTray.Services;

/// <summary>A ready-made choice in the DNS window.</summary>
internal sealed record DnsPreset(string Name, string Primary, string Secondary)
{
    public bool IsAutomatic => Primary.Length == 0 && !IsCustom;

    public bool IsCustom { get; private init; }

    public static DnsPreset Custom { get; } = new("Custom", string.Empty, string.Empty) { IsCustom = true };

    public static IReadOnlyList<DnsPreset> All { get; } = new[]
    {
        new DnsPreset("Automatic (DHCP)", string.Empty, string.Empty),
        new DnsPreset("Cloudflare", "1.1.1.1", "1.0.0.1"),
        new DnsPreset("Cloudflare (blocks malware)", "1.1.1.2", "1.0.0.2"),
        new DnsPreset("Google Public DNS", "8.8.8.8", "8.8.4.4"),
        new DnsPreset("Quad9", "9.9.9.9", "149.112.112.112"),
        new DnsPreset("AdGuard DNS", "94.140.14.14", "94.140.15.15"),
        new DnsPreset("OpenDNS", "208.67.222.222", "208.67.220.220"),
        Custom,
    };

    public override string ToString() =>
        IsCustom || IsAutomatic ? Name : $"{Name}  -  {Primary}, {Secondary}";
}
