using System.Reflection;
using System.Text.Json;
using IPTray.Forms;
using IPTray.Services;

static class Checks
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var settings = new AppSettings();
            Require(settings.OnlineLookupsAllowed is null, "Fresh installs must ask before connecting.");
            foreach (bool allowed in new[] { true, false })
            {
                settings.OnlineLookupsAllowed = allowed;
                Require(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!
                    .OnlineLookupsAllowed == allowed, "Privacy choice must survive a settings round trip.");
            }

            Assembly assembly = typeof(AppSettings).Assembly;
            Type? helper = assembly.GetType("IPTray.Services.ElevatedHost");
#if STORE_BUILD
            Require(helper is null, "Store binary must not contain the elevated helper.");
            Require(typeof(DnsService).GetMethod("Apply") is null, "Store binary must not expose DNS mutation.");
#else
            Require(helper is not null, "GitHub edition must retain DNS switching.");
            foreach (string invalid in new[] { "", "1.2.3", "1.2.3.4 & calc", "1.2.3.999", "127.1", "01.2.3.4" })
                Require(!ElevatedHost.IsIpv4(invalid), "Reject malformed IPv4: " + invalid);
            Require(ElevatedHost.IsIpv4("1.1.1.1"), "Accept a canonical IPv4 address.");
            Require(ElevatedHost.Run(new[] { "--apply-dns" }) == 10, "Reject incomplete helper calls.");
            Require(ElevatedHost.Run(new[] { "--apply-dns", "unsafe\" & calc", "auto", "none", "noflush" }) == 10,
                "Reject command injection before launching tools.");
            Require(!DnsService.Apply(new DnsRequest { AdapterId = "invalid\"" }, out _),
                "Reject malformed requests before requesting elevation.");
#endif
            // These are real adapter reads, never edits. Do not print network information.
            _ = DnsService.GetAdapters();
            Console.WriteLine("Privacy persistence, distribution boundaries and DNS read checks passed.");

            if (args.Length > 0 && args[0] == "--preview-dns")
            {
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using var form = new DnsForm();
                form.Shown += (_, _) => SetDocumentationAdapter(form);
                Application.Run(form);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    static void SetDocumentationAdapter(DnsForm form)
    {
        var combo = (ComboBox)typeof(DnsForm).GetField("_adapters", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(form)!;
        combo.Items.Clear();
        combo.Items.Add(new AdapterDns("{00000000-0000-0000-0000-000000000000}", "Ethernet",
            "Example network adapter", true, true, new[] { "192.0.2.53" }, new[] { "2001:db8::53" }));
        combo.SelectedIndex = 0;
#if !STORE_BUILD
        foreach (Control control in Descendants(form))
            if (control is Button button && button.Text == "Apply") button.Enabled = false;
#endif
    }

    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (Control child in Descendants(control)) yield return child;
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

}
