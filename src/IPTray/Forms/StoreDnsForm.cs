using System.Diagnostics;
using IPTray.Services;

namespace IPTray.Forms;

/// <summary>The Store edition reads DNS settings and delegates edits to Windows Settings.</summary>
internal sealed class DnsForm : Form
{
    private readonly ComboBox _adapters = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _details = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label _status = new() { AutoSize = true };

    public DnsForm()
    {
        Text = "IPTray - DNS servers";
        Icon = AppIcon.Create();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 320);
        MinimumSize = new Size(500, 340);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Connected adapter:", AutoSize = true });
        _adapters.Dock = DockStyle.Top;
        _adapters.SelectedIndexChanged += (_, _) => ShowAdapter();
        layout.Controls.Add(_adapters);
        _details.Dock = DockStyle.Fill;
        layout.Controls.Add(_details);
        _status.Text = "Change DNS in Windows Settings: select your connection, then edit DNS server assignment.";
        _status.MaximumSize = new Size(510, 0);
        layout.Controls.Add(_status);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var settings = new Button { Text = "Open network settings", AutoSize = true };
        settings.Click += (_, _) => OpenSettings();
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => LoadAdapters();
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        buttons.Controls.AddRange(new Control[] { settings, refresh, close });
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        CancelButton = close;
        Shown += (_, _) => LoadAdapters();
    }

    public void LoadAdapters()
    {
        string? previous = (_adapters.SelectedItem as AdapterDns)?.Id;
        _adapters.Items.Clear();
        foreach (AdapterDns adapter in DnsService.GetAdapters())
        {
            _adapters.Items.Add(adapter);
        }
        int index = _adapters.Items.Cast<AdapterDns>().ToList().FindIndex(a => a.Id == previous);
        if (_adapters.Items.Count > 0)
        {
            _adapters.SelectedIndex = index >= 0 ? index : 0;
        }
        ShowAdapter();
    }

    private void ShowAdapter()
    {
        _details.Text = _adapters.SelectedItem is AdapterDns adapter
            ? $"{adapter.Description}\r\n\r\nSource: {adapter.SourceLabel}\r\n" +
              $"IPv4: {string.Join(", ", adapter.Ipv4Dns)}\r\nIPv6: {string.Join(", ", adapter.Ipv6Dns)}"
            : "No connected network adapter was found.";
    }

    private void OpenSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            _status.Text = "Open Windows Settings > Network & internet to change DNS servers.";
        }
    }
}
