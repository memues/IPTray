using System.Net;
using System.Net.Sockets;
using IPTray.Services;

namespace IPTray.Forms;

/// <summary>Shows the DNS servers currently in use and lets the user change them.</summary>
internal sealed class DnsForm : Form
{
    private readonly ComboBox _adapters = new();
    private readonly ComboBox _presets = new();
    private readonly TextBox _primary = new();
    private readonly TextBox _secondary = new();
    private readonly CheckBox _flush = new();
    private readonly Label _source = new();
    private readonly Label _ipv4 = new();
    private readonly Label _ipv6 = new();
    private readonly Label _status = new();
    private readonly Button _apply = new();
    private readonly Button _refresh = new();
    private readonly Button _close = new();

    private bool _loading;

    public DnsForm()
    {
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;

        Text = "IPTray - DNS servers";
        Icon = AppIcon.Create();
        ClientSize = new Size(524, 396);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var adapterLabel = new Label { Text = "Adapter:", Location = new Point(12, 16), Size = new Size(70, 20) };
        _adapters.Location = new Point(88, 12);
        _adapters.Size = new Size(424, 24);
        _adapters.DropDownStyle = ComboBoxStyle.DropDownList;
        _adapters.SelectedIndexChanged += (_, _) => ShowSelectedAdapter();

        var currentBox = new GroupBox
        {
            Text = "In use now",
            Location = new Point(12, 48),
            Size = new Size(500, 108),
        };
        currentBox.Controls.Add(new Label { Text = "Source:", Location = new Point(14, 28), Size = new Size(70, 20) });
        currentBox.Controls.Add(new Label { Text = "IPv4:", Location = new Point(14, 52), Size = new Size(70, 20) });
        currentBox.Controls.Add(new Label { Text = "IPv6:", Location = new Point(14, 76), Size = new Size(70, 20) });

        _source.Location = new Point(88, 28);
        _source.Size = new Size(398, 20);
        _source.AutoEllipsis = true;
        _ipv4.Location = new Point(88, 52);
        _ipv4.Size = new Size(398, 20);
        _ipv4.AutoEllipsis = true;
        _ipv6.Location = new Point(88, 76);
        _ipv6.Size = new Size(398, 20);
        _ipv6.AutoEllipsis = true;
        currentBox.Controls.Add(_source);
        currentBox.Controls.Add(_ipv4);
        currentBox.Controls.Add(_ipv6);

        var changeBox = new GroupBox
        {
            Text = "Change to",
            Location = new Point(12, 164),
            Size = new Size(500, 140),
        };
        changeBox.Controls.Add(new Label { Text = "Preset:", Location = new Point(14, 30), Size = new Size(70, 20) });
        _presets.Location = new Point(88, 26);
        _presets.Size = new Size(398, 24);
        _presets.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (DnsPreset preset in DnsPreset.All)
        {
            _presets.Items.Add(preset);
        }

        _presets.SelectedIndexChanged += (_, _) => ApplyPresetToFields();
        changeBox.Controls.Add(_presets);

        changeBox.Controls.Add(new Label { Text = "Primary:", Location = new Point(14, 66), Size = new Size(70, 20) });
        _primary.Location = new Point(88, 63);
        _primary.Size = new Size(150, 23);
        changeBox.Controls.Add(_primary);

        changeBox.Controls.Add(new Label { Text = "Secondary:", Location = new Point(256, 66), Size = new Size(78, 20) });
        _secondary.Location = new Point(336, 63);
        _secondary.Size = new Size(150, 23);
        changeBox.Controls.Add(_secondary);

        _flush.Text = "Also flush the DNS resolver cache";
        _flush.Location = new Point(88, 100);
        _flush.Size = new Size(398, 22);
        _flush.Checked = true;
        changeBox.Controls.Add(_flush);

        _status.Location = new Point(12, 312);
        _status.Size = new Size(500, 34);
        _status.Text = "Applying a change asks for administrator approval.";
        _status.ForeColor = SystemColors.GrayText;

        _refresh.Text = "Refresh";
        _refresh.Location = new Point(232, 356);
        _refresh.Size = new Size(88, 28);
        _refresh.Click += (_, _) => LoadAdapters();

        _apply.Text = "Apply";
        _apply.Location = new Point(328, 356);
        _apply.Size = new Size(88, 28);
        _apply.Click += async (_, _) => await ApplyAsync().ConfigureAwait(true);

        _close.Text = "Close";
        _close.Location = new Point(424, 356);
        _close.Size = new Size(88, 28);
        _close.Click += (_, _) => Close();

        Controls.Add(adapterLabel);
        Controls.Add(_adapters);
        Controls.Add(currentBox);
        Controls.Add(changeBox);
        Controls.Add(_status);
        Controls.Add(_refresh);
        Controls.Add(_apply);
        Controls.Add(_close);

        CancelButton = _close;

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        LoadAdapters();

        if (_presets.SelectedIndex < 0 && _presets.Items.Count > 0)
        {
            _presets.SelectedIndex = 0;
        }
    }

    /// <summary>Re-reads every adapter, keeping the current selection when it still exists.</summary>
    public void LoadAdapters()
    {
        string? previous = (_adapters.SelectedItem as AdapterDns)?.Name;

        _loading = true;
        try
        {
            _adapters.Items.Clear();
            foreach (AdapterDns adapter in DnsService.GetAdapters())
            {
                _adapters.Items.Add(adapter);
            }
        }
        finally
        {
            _loading = false;
        }

        if (_adapters.Items.Count == 0)
        {
            ShowSelectedAdapter();
            _status.Text = "No connected network adapter was found.";
            return;
        }

        int index = 0;
        if (previous is not null)
        {
            for (int i = 0; i < _adapters.Items.Count; i++)
            {
                if (_adapters.Items[i] is AdapterDns adapter &&
                    string.Equals(adapter.Name, previous, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }
        }

        _adapters.SelectedIndex = index;
    }

    private void ShowSelectedAdapter()
    {
        if (_loading)
        {
            return;
        }

        if (_adapters.SelectedItem is not AdapterDns adapter)
        {
            _source.Text = "-";
            _ipv4.Text = "-";
            _ipv6.Text = "-";
            _apply.Enabled = false;
            return;
        }

        _apply.Enabled = true;
        _source.Text = adapter.SourceLabel;
        _ipv4.Text = adapter.Ipv4Dns.Count > 0 ? string.Join(", ", adapter.Ipv4Dns) : "none";
        _ipv6.Text = adapter.Ipv6Dns.Count > 0 ? string.Join(", ", adapter.Ipv6Dns) : "none";
    }

    private void ApplyPresetToFields()
    {
        if (_presets.SelectedItem is not DnsPreset preset)
        {
            return;
        }

        if (preset.IsCustom)
        {
            _primary.ReadOnly = false;
            _secondary.ReadOnly = false;
            _primary.BackColor = SystemColors.Window;
            _secondary.BackColor = SystemColors.Window;
            return;
        }

        _primary.Text = preset.Primary;
        _secondary.Text = preset.Secondary;
        _primary.ReadOnly = true;
        _secondary.ReadOnly = true;
        _primary.BackColor = SystemColors.Control;
        _secondary.BackColor = SystemColors.Control;
    }

    private async Task ApplyAsync()
    {
        if (_adapters.SelectedItem is not AdapterDns adapter ||
            _presets.SelectedItem is not DnsPreset preset)
        {
            return;
        }

        string primary = _primary.Text.Trim();
        string secondary = _secondary.Text.Trim();

        if (!preset.IsAutomatic)
        {
            if (!IsIpv4(primary))
            {
                _status.ForeColor = Color.FromArgb(178, 34, 34);
                _status.Text = "The primary server must be a valid IPv4 address.";
                _primary.Focus();
                return;
            }

            if (secondary.Length > 0 && !IsIpv4(secondary))
            {
                _status.ForeColor = Color.FromArgb(178, 34, 34);
                _status.Text = "The secondary server must be a valid IPv4 address, or left empty.";
                _secondary.Focus();
                return;
            }
        }
        else
        {
            primary = string.Empty;
            secondary = string.Empty;
        }

        var request = new DnsRequest
        {
            Adapter = adapter.Name,
            Primary = primary,
            Secondary = secondary,
            FlushCache = _flush.Checked,
        };

        SetBusy(true);
        _status.ForeColor = SystemColors.GrayText;
        _status.Text = "Waiting for administrator approval...";

        (bool success, string message) = await Task.Run(() =>
        {
            bool applied = DnsService.Apply(request, out string detail);
            return (applied, detail);
        }).ConfigureAwait(true);

        SetBusy(false);
        LoadAdapters();

        if (success)
        {
            _status.ForeColor = Color.FromArgb(21, 128, 61);
            _status.Text = primary.Length == 0
                ? $"{adapter.Name} is back to automatic DNS."
                : $"{adapter.Name} now uses {primary}{(secondary.Length > 0 ? " and " + secondary : string.Empty)}.";
        }
        else
        {
            _status.ForeColor = Color.FromArgb(178, 34, 34);
            _status.Text = message.Length > 0 ? Shorten(message) : "The DNS change did not go through.";
        }
    }

    private void SetBusy(bool busy)
    {
        _apply.Enabled = !busy;
        _refresh.Enabled = !busy;
        _adapters.Enabled = !busy;
        _presets.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private static bool IsIpv4(string value) =>
        IPAddress.TryParse(value, out IPAddress? address) &&
        address.AddressFamily == AddressFamily.InterNetwork;

    private static string Shorten(string message)
    {
        string single = message.Replace("\r", " ").Replace("\n", " ").Trim();
        while (single.Contains("  ", StringComparison.Ordinal))
        {
            single = single.Replace("  ", " ", StringComparison.Ordinal);
        }

        return single.Length <= 160 ? single : single[..157] + "...";
    }
}
