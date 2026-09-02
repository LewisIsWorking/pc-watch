namespace PcWatch;

/// <summary>The controls the window updates each tick.</summary>
public sealed record DashboardControls(
    Label Headline,
    Label Subline,
    CpuHistoryChart Chart,
    RichTextBox Report,
    LongRunningPanel LongRunning);

/// <summary>
/// Builds the window's contents. Split out of MainForm at the 200-line limit.
/// </summary>
/// <remarks>
/// 2026-08-31. The seam is real rather than arbitrary: this knows about docking, fonts and colours
/// and nothing about CPU; MainForm knows about sampling and updating and nothing about layout.
/// </remarks>
public static class DashboardLayout
{
    public static DashboardControls Build(Form form, ProcessAncestry ancestry)
    {
        var headline = new Label
        {
            Font = new Font("Segoe UI", 26f, FontStyle.Bold),
            ForeColor = Theme.Unknown,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 52,
            Text = "measuring...",
        };

        var subline = new Label
        {
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Theme.Dim,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = string.Empty,
        };

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window };
        header.Controls.Add(subline);
        header.Controls.Add(headline);

        var chart = new CpuHistoryChart
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 8),
        };

        var report = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Panel,
            ForeColor = Theme.Body,
            Font = new Font("Consolas", 9.5f),
            DetectUrls = false,

            // ⚠️ Wrapping OFF: the process table is fixed-width, and folding it onto continuation
            //    lines makes one 14.2% row read as two separate entries. Prose is hand-wrapped in
            //    ReportRenderer instead.
            WordWrap = false,
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Window,
            Padding = new Padding(14, 12, 14, 12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // The report and the long-running list share the bottom row. A split rather than a stack:
        // on a maximised 2048-wide window the report used two thirds of the width and left the rest
        // blank, and the kill list is the one part that needs to be clicked rather than read.
        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Window,
            Margin = new Padding(0),
        };
        // ⚠️ Both PROPORTIONAL. An Absolute 600 px right column collapsed to about 150 px in
        //    practice: a TableLayoutPanel shrinks whatever it must to satisfy the rest of the
        //    layout, and an absolute width is a request rather than a guarantee. Percentages divide
        //    the space predictably and scale with the window instead of fighting it.
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

        var longRunning = new LongRunningPanel(ancestry)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(12, 0, 0, 0),
        };

        bottom.Controls.Add(report, 0, 0);
        bottom.Controls.Add(longRunning, 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(chart, 0, 1);
        root.Controls.Add(bottom, 0, 2);
        form.Controls.Add(root);

        return new DashboardControls(headline, subline, chart, report, longRunning);
    }

    /// <summary>
    /// Client size that fits the widest line the report can emit, at the font actually in use.
    /// </summary>
    /// <remarks>
    /// ⛔ 2026-08-31: an assigned ClientSize does NOT arrive as assigned. Measured on a 125% display
    ///    (DPI 120): asked for 800 tall, got 640 - exactly 96/120. Asked for ~906 wide, got 725 -
    ///    the same 0.8 factor. WinForms applies that conversion under PerMonitorV2 regardless of
    ///    AutoScaleMode being None, so the value handed to ClientSize must be pre-divided by it.
    ///    TextRenderer.MeasureText already returns REAL pixels at the current DPI, so the multiply
    ///    cancels the shrink rather than double-counting it.
    ///
    ///    Found only because the table was visibly clipped and GetWindowRect was probed directly.
    ///    The sizes in code and the sizes on screen never matched, and nothing reported an error.
    ///
    ///    Measured rather than hardcoded because a pixel count is only ever correct at the DPI and
    ///    font it was chosen for.
    /// </remarks>
    public static Size MeasuredClientSize(Control report, int deviceDpi)
    {
        const int widestReportLine = 92;
        int width = TextRenderer.MeasureText(new string('M', widestReportLine), report.Font).Width;
        var wanted = new Size(Math.Max(width + 70, 780), 800);

        double correction = deviceDpi / 96.0;
        return new Size((int)(wanted.Width * correction), (int)(wanted.Height * correction));
    }
}
