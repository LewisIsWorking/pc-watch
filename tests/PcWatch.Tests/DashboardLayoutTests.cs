using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The window's contents, built against a Form that is never shown.
/// </summary>
/// <remarks>
/// 2026-09-06. Layout code looks untestable and mostly is not. What is worth pinning here is not
/// "a label exists" but the handful of settings that were arrived at THE HARD WAY and would be
/// silently undone by a tidy-up: word wrap off, proportional columns, and the DPI correction.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class DashboardLayoutTests
{
    private Form _form = null!;
    private ProcessAncestry _ancestry = null!;

    [SetUp]
    public void Setup()
    {
        _form = new Form { ShowInTaskbar = false, Size = new Size(1200, 800) };
        _ancestry = new ProcessAncestry();
    }

    [TearDown]
    public void Teardown() => _form.Dispose();

    private DashboardControls Build() => DashboardLayout.Build(_form, _ancestry);

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control deeper in Descendants(child)) yield return deeper;
        }
    }

    [Test]
    public void Every_control_the_window_updates_each_tick_is_produced()
    {
        DashboardControls controls = Build();

        controls.Headline.Should().NotBeNull();
        controls.Subline.Should().NotBeNull();
        controls.Chart.Should().NotBeNull();
        controls.Report.Should().NotBeNull();
        controls.LongRunning.Should().NotBeNull();
    }

    [Test]
    public void The_contents_are_actually_attached_to_the_form()
    {
        // A control that is built but never parented renders nothing, and every property assertion
        // below would still pass.
        DashboardControls controls = Build();

        Descendants(_form).Should().Contain(controls.Report);
        Descendants(_form).Should().Contain(controls.Chart);
        Descendants(_form).Should().Contain(controls.LongRunning);
    }

    [Test]
    public void The_headline_starts_by_admitting_it_does_not_know_yet()
    {
        // ⭐ Not "0%". A monitor that shows a confident zero before it has measured anything is
        //   lying at the one moment the user is deciding whether to trust it.
        Build().Headline.Text.Should().Be("measuring...");
    }

    [Test]
    public void WORD_WRAP_IS_OFF_on_the_report()
    {
        // ⛔ THE REGRESSION. The process table is fixed width. Folding it onto continuation lines
        //    makes a single 14.2% row read as two separate entries, which is a wrong answer to the
        //    only question the table exists to answer. Prose is hand-wrapped in ReportRenderer.
        Build().Report.WordWrap.Should().BeFalse();
    }

    [Test]
    public void The_report_is_read_only_and_does_not_hunt_for_links()
    {
        DashboardControls controls = Build();

        controls.Report.ReadOnly.Should().BeTrue("it is a report, not an editor");
        controls.Report.DetectUrls.Should().BeFalse(
            "a process path turning blue and clickable is noise, and a misfire opens a browser");
    }

    [Test]
    public void The_bottom_row_splits_PROPORTIONALLY_never_absolutely()
    {
        // ⛔ THE REGRESSION. An Absolute 600 px right column rendered at about 150 px: a
        //    TableLayoutPanel shrinks whatever it must to satisfy the rest of the layout, so an
        //    absolute width is a REQUEST, not a guarantee. The kill list is the part that needs
        //    clicking rather than reading, and it was the part that collapsed.
        Build();

        TableLayoutPanel[] tables = [.. Descendants(_form).OfType<TableLayoutPanel>()];
        TableLayoutPanel bottom = tables.First(t => t.ColumnCount == 2);

        bottom.ColumnStyles.Cast<ColumnStyle>().Select(c => c.SizeType)
            .Should().AllBeEquivalentTo(SizeType.Percent, "absolute widths collapse under pressure");
        bottom.ColumnStyles.Cast<ColumnStyle>().Sum(c => c.Width)
            .Should().BeApproximately(100f, 0.01f, "the two shares must account for the whole row");
    }

    [Test]
    public void The_root_lays_out_header_chart_and_content_in_three_rows()
    {
        Build();

        TableLayoutPanel root = Descendants(_form).OfType<TableLayoutPanel>().First(t => t.ColumnCount == 1);

        root.RowCount.Should().Be(3);
        root.RowStyles.Cast<RowStyle>().Last().SizeType
            .Should().Be(SizeType.Percent, "the content row must absorb the spare height");
    }

    // ── The DPI correction ──────────────────────────────────────────────────────────────────────

    [Test]
    public void At_96_dpi_the_measured_size_is_left_alone()
    {
        DashboardControls controls = Build();

        Size size = DashboardLayout.MeasuredClientSize(controls.Report, 96);

        size.Height.Should().Be(800, "96 dpi is the identity case");
        size.Width.Should().BeGreaterThanOrEqualTo(780);
    }

    [Test]
    public void At_120_DPI_THE_SIZE_IS_PRE_MULTIPLIED_to_survive_WinForms_shrinking_it()
    {
        // ⛔ THE BUG THIS ENCODES. An assigned ClientSize does NOT arrive as assigned. Measured on a
        //    125% display: asked for 800 tall, got 640 - exactly 96/120. WinForms applies that
        //    conversion under PerMonitorV2 regardless of AutoScaleMode being None, so the value
        //    handed over must be pre-divided by it. Nothing reports an error; the table is simply
        //    clipped, and the sizes in code never match the sizes on screen.
        DashboardControls controls = Build();

        Size at96 = DashboardLayout.MeasuredClientSize(controls.Report, 96);
        Size at120 = DashboardLayout.MeasuredClientSize(controls.Report, 120);

        at120.Height.Should().Be(1000, "800 * 120/96");
        at120.Height.Should().BeGreaterThan(at96.Height, "the correction must scale UP, not down");
        ((double)at120.Width / at96.Width).Should().BeApproximately(1.25, 0.01);
    }

    [Test]
    public void At_144_dpi_it_scales_further_still()
    {
        DashboardControls controls = Build();

        DashboardLayout.MeasuredClientSize(controls.Report, 144).Height
            .Should().Be(1200, "800 * 144/96");
    }

    [Test]
    public void The_width_never_falls_below_a_usable_floor()
    {
        // Measured from the font rather than hardcoded, because a pixel count is only correct at the
        // DPI and font it was chosen for. The floor stops a tiny font producing a useless window.
        using var narrow = new RichTextBox { Font = new Font("Consolas", 1f) };

        DashboardLayout.MeasuredClientSize(narrow, 96).Width.Should().BeGreaterThanOrEqualTo(780);
    }

    [Test]
    public void A_larger_font_asks_for_a_wider_window()
    {
        using var small = new RichTextBox { Font = new Font("Consolas", 9.5f) };
        using var large = new RichTextBox { Font = new Font("Consolas", 20f) };

        DashboardLayout.MeasuredClientSize(large, 96).Width
            .Should().BeGreaterThan(DashboardLayout.MeasuredClientSize(small, 96).Width,
                "the width exists to fit the widest line the report can emit");
    }
}
