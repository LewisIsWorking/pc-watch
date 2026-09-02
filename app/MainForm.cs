namespace PcWatch;

/// <summary>
/// The window: a live CPU graph, the process table, and what looks wrong.
/// </summary>
/// <remarks>
/// 2026-08-31. ShowInTaskbar is TRUE and the border is sizable, which is the whole difference
/// between this and the tray-only tool it replaces: Windows will only pin an executable, and only a
/// real window gives the pinned button something to activate.
/// </remarks>
public sealed class MainForm : Form
{
    private readonly CpuSampler _sampler = new();
    private readonly ProcessAncestry _ancestry = new();
    private readonly TrayIconRenderer _trayRenderer = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly NotifyIcon _tray = new();
    private readonly DashboardControls _ui;
    private readonly ReportFitter _fitter;
    private readonly Settings _settings = SettingsStore.Load();
    private readonly UpdateChecker _updates = new();

    /// <summary>
    /// The state to come back to from the tray.
    /// </summary>
    /// <remarks>
    /// Restoring unconditionally to Normal would silently demote a maximised window every time it
    /// was minimised and brought back, so the last non-minimised state is remembered instead.
    /// </remarks>
    private FormWindowState _restoreTo = FormWindowState.Maximized;

    public MainForm()
    {
        Text = "PC Watch";
        BackColor = Theme.Window;
        ForeColor = Theme.Body;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Icon = LoadAppIcon();

        // ⚠️ AutoScaleMode.Font (the Form default) multiplies any size we assign by the ratio of
        //    design-time to runtime font metrics. It shrank an explicit 880 px to 718 and clipped
        //    the process table. Controls here are docked and fonts are in points, so nothing needs
        //    auto-scaling - but see DashboardLayout.MeasuredClientSize: None is not sufficient alone.
        AutoScaleMode = AutoScaleMode.None;
        MinimumSize = new Size(660, 460);

        _ui = DashboardLayout.Build(this, _ancestry);
        _fitter = new ReportFitter(_ui.Report);

        // ⚠️ Order matters. ClientSize is assigned while the window is still Normal so that it
        //    becomes the RESTORE bounds; maximising first would leave un-maximising to fall back on
        //    a WinForms default that does not fit the fixed-width table.
        ClientSize = DashboardLayout.MeasuredClientSize(_ui.Report, DeviceDpi);
        WindowPlacement.Restore(this, _settings);
        _restoreTo = WindowState;

        _tray.Icon = _trayRenderer.Render(null);
        _tray.Text = "PC Watch - starting";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        _tray.ContextMenuStrip = TrayMenu.Build(
            RestoreFromTray,
            () => _ui.Report.Text,
            () => { _tray.Visible = false; Application.Exit(); });

        _timer.Interval = 1000;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (!_settings.HasPlacement)
        {
            Size target = DashboardLayout.MeasuredClientSize(_ui.Report, DeviceDpi);
            if (ClientSize.Width < target.Width || ClientSize.Height < target.Height)
            {
                ClientSize = target;
            }
        }

        _ = UpdatePrompt.CheckAsync(this, _updates, _settings);
    }

    /// <summary>Move the window to a named monitor: "left", "right", "primary", or an index.</summary>
    public bool MoveToMonitor(string monitor) => WindowPlacement.MoveTo(this, monitor);

    /// <summary>Bring the window back from minimised or hidden and put it in front.</summary>
    public void RestoreFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = _restoreTo;
        Activate();
        BringToFront();
    }

    /// <summary>Remember the last non-minimised state, so the tray can restore to it.</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized) _restoreTo = WindowState;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            _sampler.TopCount = _fitter.Rows;
            Snapshot snapshot = _sampler.Sample();
            IReadOnlyList<Suspect> suspects = SuspectAnalyzer.Analyze(snapshot);

            _ui.Chart.Push(snapshot.TotalCpuPercent);
            _tray.Icon = _trayRenderer.Render(snapshot.TotalCpuPercent);
            _tray.Text = TrayIconRenderer.Tooltip(snapshot);

            IReadOnlyList<HealthIndicator> health = SystemHealth.Assess(snapshot);
            var (word, worst) = SystemHealth.Overall(health);

            // ⚠️ POWER IS NOT IN THE HEADLINE. It used to be, and it inherited the headline's
            //    severity colour - so a perfectly normal 244 W on a 5900X plus a 3080 rendered in
            //    alarm red and read as a fault. Nothing here knows the PSU rating, so the app has no
            //    basis on which to call any wattage bad. It belongs with the other plain facts.
            _ui.Headline.Text = snapshot.TotalCpuPercent is { } v ? $"{v:N0}%  CPU     {word}" : "measuring...";
            _ui.Headline.ForeColor = Theme.ForSeverity(worst);

            string power = snapshot.Power?.EstimatedSystemWatts is { } watts ? $"~{watts:N0} W   -   " : "";
            _ui.Subline.Text =
                $"{snapshot.Machine.CpuName}   -   {snapshot.LogicalCores} logical cores   -   "
                + $"RAM {snapshot.Machine.RamUsedGb:N1}/{snapshot.Machine.RamTotalGb:N1} GB   -   "
                + power
                + $"on {ReportRenderer.Age(snapshot.Machine.Uptime.Best)}   -   {AppVersion.Display}";

            _ui.LongRunning.Update(snapshot.LongLived);

            // Only rebuild the report when the window is actually visible: it is the expensive part,
            // and a minimised window still ticks so the tray icon and the graph stay current.
            if (Visible && WindowState != FormWindowState.Minimized)
            {
                _ui.Report.Text = ReportRenderer.Render(snapshot, suspects, _ancestry);
                _fitter.Update();

                // ⚠️ Always show the top. Preserving the previous scroll offset carried the view
                //    down as the report changed length and silently hid the RAM row - a header line
                //    scrolled out of sight reads as a MISSING MEASUREMENT, not as a scrolled window.
                _ui.Report.SelectionStart = 0;
                _ui.Report.ScrollToCaret();
            }
        }
        catch (Exception ex)
        {
            // One bad sample must not kill the app. Say so in the tooltip and keep ticking.
            string message = $"PC Watch - sample failed: {ex.Message}";
            _tray.Text = message.Length > 127 ? message[..127] : message;
        }
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "PcWatch.ico");
            if (File.Exists(path)) return new Icon(path);
        }
        catch
        {
            // Fall through: a missing icon is cosmetic, not a reason to fail to start.
        }
        return SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            WindowPlacement.Save(this, _settings);
            _timer.Stop();
            _timer.Dispose();
            _sampler.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _trayRenderer.Dispose();
        }
        base.Dispose(disposing);
    }
}
