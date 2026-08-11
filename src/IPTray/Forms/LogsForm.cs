using System.Diagnostics;
using System.Text;
using IPTray.Services;

namespace IPTray.Forms;

/// <summary>Timestamped history of every public-IP change IPTray has observed.</summary>
internal sealed class LogsForm : Form
{
    private readonly ListView _list;
    private readonly Label _status;
    private readonly ContextMenuStrip _listMenu;

    public LogsForm()
    {
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;

        Text = "IPTray - IP history";
        Icon = AppIcon.Create();
        ClientSize = new Size(800, 440);
        MinimumSize = new Size(600, 320);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = true,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
        };
        _list.Columns.Add("Time", 150);
        _list.Columns.Add("IP address", 150);
        _list.Columns.Add("Country", 190);
        _list.Columns.Add("ISP", 290);

        _listMenu = new ContextMenuStrip();
        _listMenu.Items.Add(new ToolStripMenuItem("Copy selected rows", null, (_, _) => CopySelection()));
        _list.ContextMenuStrip = _listMenu;

        _status = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 8, 3, 0),
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        buttons.Controls.Add(MakeButton("Refresh", (_, _) => Reload()));
        buttons.Controls.Add(MakeButton("Copy", (_, _) => CopySelection()));
        buttons.Controls.Add(MakeButton("Open folder", (_, _) => OpenDataFolder()));
        buttons.Controls.Add(MakeButton("Clear log", (_, _) => ClearLog()));

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 6, 10, 8),
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_status, 0, 0);
        bottom.Controls.Add(buttons, 1, 0);

        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 0) };
        content.Controls.Add(_list);

        Controls.Add(content);
        Controls.Add(bottom);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _listMenu.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Reload();
    }

    /// <summary>Re-reads the log file and repaints the list.</summary>
    public void Reload()
    {
        List<LogEntry> entries = IpLogStore.Read();

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (LogEntry entry in entries)
            {
                var item = new ListViewItem(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(entry.IsOffline ? "no connection" : entry.Ip);
                item.SubItems.Add(FormatCountry(entry));
                item.SubItems.Add(entry.Isp);

                if (entry.IsOffline)
                {
                    item.ForeColor = Color.FromArgb(178, 34, 34);
                }

                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        _status.Text = entries.Count switch
        {
            0 => "No entries yet. IPTray records a row whenever your public IP changes.",
            1 => "1 entry",
            _ => $"{entries.Count} entries",
        };
    }

    private static string FormatCountry(LogEntry entry)
    {
        if (entry.CountryName.Length > 0 && entry.CountryCode.Length > 0)
        {
            return $"{entry.CountryName} ({entry.CountryCode})";
        }

        return entry.CountryName.Length > 0 ? entry.CountryName : entry.CountryCode;
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(96, 28),
            Margin = new Padding(6, 0, 0, 0),
        };
        button.Click += onClick;
        return button;
    }

    private void CopySelection()
    {
        IEnumerable<ListViewItem> items = _list.SelectedItems.Count > 0
            ? _list.SelectedItems.Cast<ListViewItem>()
            : _list.Items.Cast<ListViewItem>();

        var sb = new StringBuilder();
        foreach (ListViewItem item in items)
        {
            sb.AppendLine(string.Join('\t', item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text)));
        }

        if (sb.Length == 0)
        {
            _status.Text = "Nothing to copy.";
            return;
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            _status.Text = "Copied to the clipboard.";
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            _status.Text = "The clipboard is not available right now.";
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.DataDirectory) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            _status.Text = "The folder could not be opened.";
        }
    }

    private void ClearLog()
    {
        DialogResult answer = MessageBox.Show(
            this,
            "Delete every entry from the IP history?",
            "IPTray",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (answer == DialogResult.Yes)
        {
            IpLogStore.Clear();
            Reload();
        }
    }
}
