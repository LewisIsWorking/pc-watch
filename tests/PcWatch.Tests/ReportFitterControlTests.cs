using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// ReportFitter driven against a real RichTextBox that is never shown.
/// </summary>
/// <remarks>
/// 2026-09-06. The arithmetic is covered by ReportFitterTests. This covers the half that READS the
/// control, which is where the four historical bugs actually lived: every one of them was a wrong
/// question asked of a real control, not wrong maths.
///
/// ⭐ A window handle is created WITHOUT SHOWING ANYTHING. Form.CreateControl() realises the control
///   tree so layout and text metrics work, while the form stays invisible and off the taskbar. No
///   window flashes on screen during a test run.
///
/// ⚠️ STA is mandatory. WinForms controls throw or misbehave on an MTA thread, and NUnit's default
///    is MTA, so the attribute below is load-bearing rather than decorative.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ReportFitterControlTests
{
    private Form _form = null!;
    private RichTextBox _report = null!;

    [SetUp]
    public void BuildAnInvisibleControlTree()
    {
        _form = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Normal, Size = new Size(700, 500) };
        _report = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9f),
            WordWrap = false,
            Multiline = true,
        };
        _form.Controls.Add(_report);

        // Realises handles and runs layout. Show() is deliberately NOT called.
        _form.CreateControl();
        _report.CreateControl();
        _ = _report.Handle;
    }

    [TearDown]
    public void Dispose()
    {
        _report.Dispose();
        _form.Dispose();
    }

    private static string Lines(int count) =>
        string.Join(Environment.NewLine, Enumerable.Range(1, count).Select(i => $"line {i}"));

    [Test]
    public void A_fitter_starts_with_sensible_defaults()
    {
        var fitter = new ReportFitter(_report);

        fitter.Rows.Should().BeInRange(ReportFitter.MinRows, ReportFitter.MaxRows);
        fitter.LineHeight.Should().BePositive();
    }

    [Test]
    public void Empty_text_leaves_the_row_budget_alone()
    {
        var fitter = new ReportFitter(_report);
        int before = fitter.Rows;

        _report.Text = string.Empty;
        fitter.Update();

        fitter.Rows.Should().Be(before, "there is nothing to measure, so nothing to conclude");
    }

    [Test]
    public void A_single_line_cannot_yield_a_line_height_and_keeps_the_old_one()
    {
        var fitter = new ReportFitter(_report);
        int before = fitter.LineHeight;

        _report.Text = "just the one line";
        fitter.Update();

        fitter.LineHeight.Should().Be(before, "two line tops are needed to measure spacing");
    }

    [Test]
    public void Line_height_is_measured_from_the_control_once_there_are_two_lines()
    {
        var fitter = new ReportFitter(_report);

        _report.Text = Lines(30);
        fitter.Update();

        // ⚠️ Asked of the CONTROL, not of the font. TextRenderer.MeasureText reports the font's
        //    height, which is not the spacing a RichTextBox lays out with, and the difference was
        //    enough to over-budget the list by several rows.
        fitter.LineHeight.Should().BePositive();
        fitter.LineHeight.Should().BeLessThan(100, "a plausible line height, not a stray pixel value");
    }

    [Test]
    public void Text_that_overflows_the_box_shrinks_the_row_budget()
    {
        var fitter = new ReportFitter(_report);
        int before = fitter.Rows;

        _report.Text = Lines(400);
        fitter.Update();

        fitter.Rows.Should().BeLessThan(before, "far more text than fits must reduce what is asked for");
        fitter.Rows.Should().BeGreaterThanOrEqualTo(ReportFitter.MinRows);
    }

    [Test]
    public void A_nearly_empty_report_grows_the_row_budget()
    {
        var fitter = new ReportFitter(_report);

        _report.Text = Lines(2);
        fitter.Update();

        fitter.Rows.Should().BeGreaterThan(14, "there is plenty of room, so ask for more rows");
        fitter.Rows.Should().BeLessThanOrEqualTo(ReportFitter.MaxRows);
    }

    [Test]
    public void Repeated_updates_settle_rather_than_oscillate()
    {
        // ⭐ The real-control version of the convergence property. The pure test proves the
        //   arithmetic settles; this proves it still settles against genuine measured metrics.
        var fitter = new ReportFitter(_report);
        var seen = new List<int>();

        for (int i = 0; i < 25; i++)
        {
            _report.Text = Lines(fitter.Rows + 6);
            fitter.Update();
            seen.Add(fitter.Rows);
        }

        seen[^1].Should().Be(seen[^2], "the budget must come to rest");
        seen[^1].Should().Be(seen[^3], "and not alternate between two values");
    }

    [Test]
    public void The_budget_never_leaves_its_bounds_however_extreme_the_text()
    {
        var fitter = new ReportFitter(_report);

        foreach (int count in new[] { 1, 2, 5, 50, 500, 5000, 1 })
        {
            _report.Text = Lines(count);
            fitter.Update();
            fitter.Rows.Should().BeInRange(ReportFitter.MinRows, ReportFitter.MaxRows,
                $"after rendering {count} lines");
        }
    }

    [Test]
    public void A_collapsed_panel_does_not_divide_by_zero()
    {
        // A zero-height client area is real during startup and while a splitter is dragged shut.
        _form.Size = new Size(700, 1);
        _form.PerformLayout();
        var fitter = new ReportFitter(_report);

        Action update = () => { _report.Text = Lines(40); fitter.Update(); };

        update.Should().NotThrow<DivideByZeroException>();
        fitter.Rows.Should().BeInRange(ReportFitter.MinRows, ReportFitter.MaxRows);
    }
}
