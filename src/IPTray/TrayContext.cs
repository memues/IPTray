using System.Net.NetworkInformation;
using System.Reflection;
using IPTray.Forms;
using IPTray.Services;

namespace IPTray;

/// <summary>
/// Owns the notification-area icon and everything hanging off it: the polling loop, the icon
/// artwork, the context menu and the two windows.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private const string ProjectUrl = "https://github.com/memues/IPTray";

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripLabel _addressLabel = new();
    private readonly ToolStripLabel _countryLabel = new();
    private readonly ToolStripLabel _ispLabel = new();
    private readonly ToolStripMenuItem _startupItem = new("Start with Windows");
    private readonly ToolStripMenuItem _notifyItem = new("Notify me when the IP changes");
    private readonly ToolStripMenuItem _intervalItem = new("Check every");
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly System.Windows.Forms.Timer _watchdog = new();
    private readonly CancellationTokenSource _shutdown = new();

    private Font? _boldFont;
    private OwnedIcon? _icon;
    private Bitmap? _flag;
    private Bitmap? _headerImage;
    private string _flagCountry = string.Empty;
    private IpInfo? _current;
    private string? _lastLogged;
    private bool _refreshing;
    private bool _firstTickDone;
    private long _networkChangedTicks;
    private LogsForm? _logsForm;
    private DnsForm? _dnsForm;

    public TrayContext()
    {
        BuildMenu();

        _tray.ContextMenuStrip = _menu;
        _tray.Text = "IPTray - checking...";
        _tray.MouseUp += OnTrayMouseUp;
        SetIcon(FlagIconFactory.CreateTrayIcon(null, offline: false));
        _tray.Visible = true;

        _refreshTimer.Interval = _settings.RefreshSeconds * 1000;
        _refreshTimer.Tick += (_, _) => BeginRefresh();
        _refreshTimer.Start();

        // Everything that touches the UI runs on this timer, which only ticks once the message
        // loop is running - so the first lookup is started from the UI thread, not the constructor.
        _watchdog.Interval = 400;
        _watchdog.Tick += OnWatchdogTick;
        _watchdog.Start();

        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    private void BuildMenu()
    {
        _boldFont = new Font(_menu.Font, FontStyle.Bold);
        _addressLabel.Font = _boldFont;
        _addressLabel.Text = "Checking...";
        _countryLabel.Text = string.Empty;
        _countryLabel.Visible = false;
        _ispLabel.Text = string.Empty;
        _ispLabel.Visible = false;

        foreach (int seconds in AppSettings.AllowedIntervals)
        {
            var item = new ToolStripMenuItem(DescribeInterval(seconds))
            {
                Tag = seconds,
                Checked = seconds == _settings.RefreshSeconds,
            };
            item.Click += OnIntervalClick;
            _intervalItem.DropDownItems.Add(item);
        }

        _startupItem.Checked = StartupManager.IsEnabled();
        _startupItem.Click += OnStartupClick;

        _notifyItem.Checked = _settings.NotifyOnChange;
        _notifyItem.Click += OnNotifyClick;

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _addressLabel,
            _countryLabel,
            _ispLabel,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Copy IP address", null, (_, _) => CopyIpAddress()),
            new ToolStripMenuItem("Check now", null, (_, _) => BeginRefresh()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("IP history...", null, (_, _) => ShowLogs()),
            new ToolStripMenuItem("DNS servers...", null, (_, _) => ShowDns()),
            new ToolStripSeparator(),
            _startupItem,
            _notifyItem,
            _intervalItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("About IPTray", null, (_, _) => ShowAbout()),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()),
        });
    }

    private static string DescribeInterval(int seconds) => seconds switch
    {
        30 => "30 seconds",
        60 => "1 minute",
        300 => "5 minutes",
        900 => "15 minutes",
        _ => seconds + " seconds",
    };

    private void OnWatchdogTick(object? sender, EventArgs e)
    {
        if (!_firstTickDone)
        {
            _firstTickDone = true;
            _watchdog.Interval = 2000;
            BeginRefresh();
            return;
        }

        // Re-check shortly after the adapters settle down following a network change.
        long ticks = Interlocked.Read(ref _networkChangedTicks);
        if (ticks == 0)
        {
            return;
        }

        if (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < TimeSpan.FromSeconds(3))
        {
            return;
        }

        Interlocked.Exchange(ref _networkChangedTicks, 0);
        BeginRefresh();
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e) =>
        Interlocked.Exchange(ref _networkChangedTicks, DateTime.UtcNow.Ticks);

    private void BeginRefresh()
    {
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _shutdown.IsCancellationRequested)
        {
            return;
        }

        _refreshing = true;
        try
        {
            IpInfo? info = await IpLookupService.LookupAsync(_shutdown.Token).ConfigureAwait(true);
            if (_shutdown.IsCancellationRequested)
            {
                return;
            }

            if (info is not null && info.HasCountry && info.CountryCode != _flagCountry)
            {
                Bitmap? flag = await FlagIconFactory.GetFlagAsync(info.CountryCode, _shutdown.Token)
                    .ConfigureAwait(true);

                if (_shutdown.IsCancellationRequested)
                {
                    flag?.Dispose();
                    return;
                }

                if (flag is not null)
                {
                    _flag?.Dispose();
                    _flag = flag;
                    _flagCountry = info.CountryCode;
                }
            }
            else if (info is not null && !info.HasCountry)
            {
                _flag?.Dispose();
                _flag = null;
                _flagCountry = string.Empty;
            }

            Apply(info);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Apply(IpInfo? info)
    {
        _current = info;

        SetIcon(FlagIconFactory.CreateTrayIcon(info is null ? null : _flag, offline: info is null));
        UpdateHeaderImage();

        if (info is null)
        {
            _tray.Text = Truncate("IPTray - no connection");
            _addressLabel.Text = "No connection";
            _countryLabel.Text = "The public IP could not be looked up.";
            _ispLabel.Visible = false;
        }
        else
        {
            _tray.Text = Truncate($"{info.Ip}\n{info.CountryLabel}");
            _addressLabel.Text = info.Ip;
            _countryLabel.Text = info.CountryLabel;
            _ispLabel.Text = info.Isp;
            _ispLabel.Visible = info.Isp.Length > 0;
        }

        _countryLabel.Visible = _countryLabel.Text.Length > 0;

        RecordChange(info);
    }

    private void RecordChange(IpInfo? info)
    {
        string key = info?.Ip ?? string.Empty;
        if (_lastLogged == key)
        {
            return;
        }

        string? previous = _lastLogged;
        _lastLogged = key;

        IpLogStore.Append(new LogEntry(
            DateTime.Now,
            info?.Ip ?? string.Empty,
            info?.CountryCode ?? string.Empty,
            info?.CountryName ?? string.Empty,
            info?.Isp ?? string.Empty));

        if (_logsForm is { IsDisposed: false })
        {
            _logsForm.Reload();
        }

        // Only announce genuine changes, not the first reading after start-up.
        if (previous is null || !_settings.NotifyOnChange)
        {
            return;
        }

        if (info is null)
        {
            _tray.ShowBalloonTip(6000, "IPTray", "The public IP could not be reached.", ToolTipIcon.Warning);
        }
        else
        {
            string from = previous.Length == 0 ? "no connection" : previous;
            _tray.ShowBalloonTip(6000, "Public IP changed", $"{from}  ->  {info.Ip}", ToolTipIcon.Info);
        }
    }

    private void SetIcon(OwnedIcon icon)
    {
        OwnedIcon? previous = _icon;
        _icon = icon;
        _tray.Icon = icon.Icon;
        previous?.Dispose();
    }

    private void UpdateHeaderImage()
    {
        Bitmap? previous = _headerImage;

        try
        {
            _headerImage = _flag is null ? null : new Bitmap(_flag, new Size(16, 16));
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
            _headerImage = null;
        }

        _addressLabel.Image = _headerImage;
        previous?.Dispose();
    }

    private static string Truncate(string text) => text.Length <= 63 ? text : text[..63];

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // The notification area only opens the menu on right-click; mirror it for left-click.
        try
        {
            MethodInfo? show = typeof(NotifyIcon).GetMethod(
                "ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

            if (show is not null)
            {
                show.Invoke(_tray, null);
                return;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
        }

        _menu.Show(Control.MousePosition);
    }

    private void OnIntervalClick(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem clicked || clicked.Tag is not int seconds)
        {
            return;
        }

        _settings.RefreshSeconds = seconds;
        _settings.Save();

        foreach (ToolStripItem item in _intervalItem.DropDownItems)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.Checked = ReferenceEquals(menuItem, clicked);
            }
        }

        _refreshTimer.Stop();
        _refreshTimer.Interval = seconds * 1000;
        _refreshTimer.Start();
    }

    private void OnStartupClick(object? sender, EventArgs e)
    {
        bool wanted = !_startupItem.Checked;

        if (StartupManager.SetEnabled(wanted))
        {
            _startupItem.Checked = wanted;
        }
        else
        {
            MessageBox.Show(
                "The start-up entry could not be updated.",
                Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnNotifyClick(object? sender, EventArgs e)
    {
        _settings.NotifyOnChange = !_notifyItem.Checked;
        _notifyItem.Checked = _settings.NotifyOnChange;
        _settings.Save();
    }

    private void CopyIpAddress()
    {
        if (_current is null)
        {
            _tray.ShowBalloonTip(3000, "IPTray", "No IP address to copy yet.", ToolTipIcon.Info);
            return;
        }

        try
        {
            Clipboard.SetText(_current.Ip);
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
        }
    }

    private void ShowLogs()
    {
        if (_logsForm is null || _logsForm.IsDisposed)
        {
            _logsForm = new LogsForm();
            _logsForm.FormClosed += (_, _) => _logsForm = null;
            _logsForm.Show();
        }
        else
        {
            _logsForm.Reload();
        }

        Surface(_logsForm);
    }

    private void ShowDns()
    {
        if (_dnsForm is null || _dnsForm.IsDisposed)
        {
            _dnsForm = new DnsForm();
            _dnsForm.FormClosed += (_, _) => _dnsForm = null;
            _dnsForm.Show();
        }
        else
        {
            _dnsForm.LoadAdapters();
        }

        Surface(_dnsForm);
    }

    private static void Surface(Form? form)
    {
        if (form is null || form.IsDisposed)
        {
            return;
        }

        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }

        form.Activate();
        form.BringToFront();
    }

    private void ShowAbout()
    {
        string message =
            $"IPTray {Application.ProductVersion}\r\n\r\n" +
            "Shows the public IP address you are currently seen as, the country it belongs to, " +
            "a timestamped history of every change, and the DNS servers in use.\r\n\r\n" +
            $"Data folder:\r\n{AppPaths.DataDirectory}\r\n\r\n" +
            $"Project:\r\n{ProjectUrl}";

        MessageBox.Show(message, "About IPTray", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ExitApplication()
    {
        _tray.Visible = false;
        _shutdown.Cancel();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

            _watchdog.Stop();
            _watchdog.Dispose();
            _refreshTimer.Stop();
            _refreshTimer.Dispose();

            if (!_shutdown.IsCancellationRequested)
            {
                _shutdown.Cancel();
            }

            // The source is deliberately not disposed: a lookup may still be unwinding and
            // would otherwise fault on an already-disposed token source.
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();

            _icon?.Dispose();
            _flag?.Dispose();
            _headerImage?.Dispose();
            _boldFont?.Dispose();

            _logsForm?.Dispose();
            _dnsForm?.Dispose();
        }

        base.Dispose(disposing);
    }
}
