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
        LineHeight = PickLineHeight(LineHeight, first, second);
    }

    private void AdjustRows()
    {
        if (_report.TextLength == 0) return;

        // Line COUNT, never a pixel position. GetPositionFromCharIndex clamps to the visible area,
        // so anything below the fold reports an in-view Y and cannot signal overflow.
        int totalLines = _report.GetLineFromCharIndex(_report.TextLength - 1) + 1;
        Rows = NextRowCount(Rows, totalLines, VisibleLines(_report.ClientSize.Height, LineHeight));
    }

    // ── The decisions, as pure functions ────────────────────────────────────────────────────────
    //
    // 2026-09-05. Split out of the methods above because the arithmetic was welded to a live
    // RichTextBox: reaching a single branch needed a real control, a created window handle and a
    // rendered layout, so NONE of it was covered. The remarks at the top of this file record four
    // wrong answers arrived at by calculation, one of which could never fail no matter how badly
    // the text overran. That history is precisely why this arithmetic deserves direct tests.
    //
    // The control-facing methods above keep their exact behaviour; they now only READ the control
    // and hand the numbers here.

    /// <summary>Keep the previous height unless the two sampled line tops give a positive spacing.</summary>
    /// <remarks>
    /// A non-positive difference means the control has not laid out yet, or both lines report the
    /// same Y. Adopting a zero would make VisibleLines divide by zero on the very next call.
    /// </remarks>
    public static int PickLineHeight(int current, int firstLineTop, int secondLineTop)
    {
        int height = secondLineTop - firstLineTop;
        return height > 0 ? height : current;
    }

    /// <summary>How many lines fit, never fewer than four.</summary>
    /// <remarks>
    /// The floor of four keeps a collapsed or not-yet-laid-out panel from reporting zero visible
    /// lines, which would make every sample look like a massive overflow and drive Rows to MinRows.
    /// </remarks>
    public static int VisibleLines(int clientHeight, int lineHeight) =>
        lineHeight <= 0 ? 4 : Math.Max(4, clientHeight / lineHeight);

    /// <summary>The next row budget, given what actually rendered.</summary>
    /// <remarks>
    /// ⚠️ ASYMMETRIC ON PURPOSE. Shrinking takes the whole overshoot at once so it converges in a
    ///    single step; growing adds one row at a time and only when at least two lines are spare.
    ///    Symmetric behaviour oscillates across the boundary for ever, adding and removing the same
    ///    row on alternate samples, which looks like a flickering table.
    /// </remarks>
    public static int NextRowCount(int currentRows, int totalLines, int visibleLines)
    {
        int excess = totalLines - visibleLines;
        if (excess > 0) return Math.Max(MinRows, currentRows - Math.Max(1, excess));
        if (excess < -1) return Math.Min(MaxRows, currentRows + 1);
        return currentRows;
    }
}
