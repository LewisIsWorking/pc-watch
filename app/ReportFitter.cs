namespace PcWatch;

/// <summary>
/// Decides how many process rows the report should ask for, by measuring what actually rendered.
/// </summary>
/// <remarks>
/// ⛔ 2026-08-31. Four attempts to CALCULATE this were all wrong, and each produced a confident
///    number plus a window whose findings were pushed off the bottom:
///
///      1. A hardcoded overhead of 26 lines. The two explanatory notes and the findings vary in
///         length by more than ten lines during normal use, so there is no constant to hardcode.
///      2. Self-corrected overhead, with line height from TextRenderer.MeasureText. That is the
///         FONT's line height, not the one a RichTextBox lays out with; it came out short, so the
///         row budget was over-estimated and the list still overflowed.
///      3. Line height measured from the control itself. Closer, still over-budget.
///      4. Overflow detected by asking where the LAST character sits. This one failed silently:
///         GetPositionFromCharIndex CLAMPS to the visible area, so it reports an in-view Y for text
///         far below the fold. The test could never fire no matter how badly the text overran.
///
///    What finally worked was two changes together. This class uses the control's own LINE COUNT
///    rather than any pixel position, and ReportRenderer now emits the findings BEFORE the process
///    table - so if the estimate is still a row or two out, what gets cut is the tail of a sorted
///    list rather than the diagnosis. Correctness no longer depends on getting the number right.
/// </remarks>
public sealed class ReportFitter
{
    private readonly RichTextBox _report;

    public const int MinRows = 8;
    public const int MaxRows = 45;

    /// <summary>Rows to request on the next sample.</summary>
    public int Rows { get; private set; } = 14;

    /// <summary>Rendered height of one line, re-measured from the control after each paint.</summary>
    public int LineHeight { get; private set; } = 16;

    public ReportFitter(RichTextBox report) => _report = report;

    /// <summary>Call immediately after assigning new report text.</summary>
    public void Update()
    {
        RemeasureLineHeight();
        AdjustRows();
    }

    /// <summary>
    /// Line height taken from the rendered positions of line 0 and line 1.
    /// </summary>
    /// <remarks>
    /// Asked of the control rather than of the font: TextRenderer.MeasureText reports the font's
    /// height, which is not the spacing a RichTextBox lays out with, and the difference was enough
    /// to over-budget the list by several rows.
    /// </remarks>
    private void RemeasureLineHeight()
    {
        if (_report.Lines.Length < 2) return;

        int first = _report.GetPositionFromCharIndex(_report.GetFirstCharIndexFromLine(0)).Y;
        int second = _report.GetPositionFromCharIndex(_report.GetFirstCharIndexFromLine(1)).Y;
        int height = second - first;
        if (height > 0) LineHeight = height;
    }

    private void AdjustRows()
    {
        if (_report.TextLength == 0) return;

        // Line COUNT, never a pixel position. GetPositionFromCharIndex clamps to the visible area,
        // so anything below the fold reports an in-view Y and cannot signal overflow.
        int totalLines = _report.GetLineFromCharIndex(_report.TextLength - 1) + 1;
        int visibleLines = Math.Max(4, _report.ClientSize.Height / LineHeight);
        int excess = totalLines - visibleLines;

        // Shrink by the whole overshoot so it converges in one step; grow one row at a time, with a
        // line of deadband, so it settles instead of oscillating across the boundary.
        if (excess > 0) Rows = Math.Max(MinRows, Rows - Math.Max(1, excess));
        else if (excess < -1) Rows = Math.Min(MaxRows, Rows + 1);
    }
}
