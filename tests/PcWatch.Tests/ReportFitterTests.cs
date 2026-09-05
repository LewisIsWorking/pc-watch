using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The row-budget arithmetic that decides how much of the report fits.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-05. ReportFitter was at 0%, and its own header records FOUR wrong answers reached by
///    calculation. The fourth is the reason this file exists: overflow was detected by asking where
///    the last character sat, and GetPositionFromCharIndex CLAMPS to the visible area, so it
///    reported an in-view Y for text far below the fold. The check could never fire, no matter how
///    badly the text overran. It did not fail loudly; it silently always said "fits".
///
/// ⚠️ These tests drive the arithmetic directly. That is the point: the previous design could only
///    be exercised through a rendered control, which is exactly why four wrong versions shipped.
/// </remarks>
[TestFixture]
public sealed class ReportFitterTests
{
    // ── Line height ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Line_height_is_the_gap_between_two_line_tops()
    {
        ReportFitter.PickLineHeight(current: 16, firstLineTop: 10, secondLineTop: 29).Should().Be(19);
    }

    [Test]
    public void A_zero_gap_keeps_the_previous_height()
    {
        // Both lines at the same Y means the control has not laid out yet. Adopting 0 would make the
        // next VisibleLines call divide by zero.
        ReportFitter.PickLineHeight(current: 16, firstLineTop: 10, secondLineTop: 10).Should().Be(16);
    }

    [Test]
    public void A_negative_gap_keeps_the_previous_height()
    {
        ReportFitter.PickLineHeight(current: 16, firstLineTop: 40, secondLineTop: 10).Should().Be(16);
    }

    // ── Visible lines ───────────────────────────────────────────────────────────────────────────

    [Test]
    public void Visible_lines_is_height_divided_by_line_height()
    {
        ReportFitter.VisibleLines(clientHeight: 200, lineHeight: 20).Should().Be(10);
    }

    [Test]
    public void Visible_lines_never_drops_below_four()
    {
        // A collapsed panel reporting 0 visible lines would look like a massive overflow on every
        // sample and drive the budget straight to MinRows.
        ReportFitter.VisibleLines(clientHeight: 10, lineHeight: 20).Should().Be(4);
        ReportFitter.VisibleLines(clientHeight: 0, lineHeight: 20).Should().Be(4);
    }

    [Test]
    public void A_zero_line_height_cannot_divide_by_zero()
    {
        Func<int> act = () => ReportFitter.VisibleLines(clientHeight: 200, lineHeight: 0);

        act.Should().NotThrow<DivideByZeroException>();
        act().Should().Be(4);
    }

    [Test]
    public void A_negative_line_height_is_also_survived()
    {
        ReportFitter.VisibleLines(clientHeight: 200, lineHeight: -5).Should().Be(4);
    }

    // ── The row budget ──────────────────────────────────────────────────────────────────────────

    [Test]
    public void Text_that_fits_exactly_changes_nothing()
    {
        ReportFitter.NextRowCount(currentRows: 14, totalLines: 20, visibleLines: 20).Should().Be(14);
    }

    [Test]
    public void One_spare_line_is_deadband_and_changes_nothing()
    {
        // ⚠️ THE ANTI-OSCILLATION RULE. Growing on a single spare line would add a row, overflow,
        //    remove it, and repeat for ever - a table that visibly flickers.
        ReportFitter.NextRowCount(currentRows: 14, totalLines: 19, visibleLines: 20).Should().Be(14);
    }

    [Test]
    public void Two_spare_lines_grows_by_exactly_one_row()
    {
        ReportFitter.NextRowCount(currentRows: 14, totalLines: 18, visibleLines: 20).Should().Be(15);
    }

    [Test]
    public void A_large_surplus_still_grows_by_only_one_row()
    {
        // Asymmetric on purpose: cautious upward, decisive downward.
        ReportFitter.NextRowCount(currentRows: 14, totalLines: 2, visibleLines: 40).Should().Be(15);
    }

    [Test]
    public void Overflow_shrinks_by_the_whole_overshoot_in_one_step()
    {
        // 7 lines over, so 7 rows come off at once rather than converging one at a time.
        ReportFitter.NextRowCount(currentRows: 20, totalLines: 27, visibleLines: 20).Should().Be(13);
    }

    [Test]
    public void A_single_line_of_overflow_removes_a_row()
    {
        ReportFitter.NextRowCount(currentRows: 20, totalLines: 21, visibleLines: 20).Should().Be(19);
    }

    [Test]
    public void Shrinking_stops_at_the_minimum()
    {
        ReportFitter.NextRowCount(currentRows: 10, totalLines: 500, visibleLines: 20)
            .Should().Be(ReportFitter.MinRows, "a useless report is worse than a slightly clipped one");
    }

    [Test]
    public void Growing_stops_at_the_maximum()
    {
        ReportFitter.NextRowCount(ReportFitter.MaxRows, totalLines: 1, visibleLines: 500)
            .Should().Be(ReportFitter.MaxRows);
    }

    [Test]
    public void The_budget_converges_instead_of_oscillating()
    {
        // ⭐ The property that matters, and the one no single-step assertion can show. Feed the
        //   result back in repeatedly against a fixed window and it must settle, not cycle.
        const int visible = 20;
        int rows = 40;
        var seen = new List<int>();

        for (int i = 0; i < 40; i++)
        {
            // A report whose length tracks the row budget, plus fixed overhead.
            int totalLines = rows + 6;
            rows = ReportFitter.NextRowCount(rows, totalLines, visible);
            seen.Add(rows);
        }

        seen[^1].Should().Be(seen[^2], "the budget must come to rest");
        seen[^1].Should().Be(seen[^3], "and stay there rather than alternating between two values");
        rows.Should().BeInRange(ReportFitter.MinRows, ReportFitter.MaxRows);
    }

    [Test]
    public void The_settled_budget_actually_fits_the_window()
    {
        const int visible = 25;
        int rows = 45;
        for (int i = 0; i < 60; i++) rows = ReportFitter.NextRowCount(rows, rows + 6, visible);

        (rows + 6).Should().BeLessThanOrEqualTo(visible,
            "the whole purpose is that the rendered report stops overflowing");
    }
}
