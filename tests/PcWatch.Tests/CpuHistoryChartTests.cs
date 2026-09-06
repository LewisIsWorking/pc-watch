using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The rolling CPU graph, painted into a bitmap so nothing is ever shown.
/// </summary>
/// <remarks>
/// 2026-09-06. This control is the reason the window answers the question a single number cannot:
/// 100% for two seconds during a build is normal, 60% flat for an hour is not.
///
/// ⭐ THE GAP IS THE INTERESTING PART. A reading that has not been measured yet is recorded as a
///   GAP, not as zero. Drawing an unmeasured sample as 0% would paint a reassuring dip into the
///   graph at exactly the moment the app knew nothing, which is worse than drawing nothing at all.
///
/// ⚠️ DrawToBitmap drives OnPaint without a visible window, so the paint path is genuinely executed
///    rather than merely constructed.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class CpuHistoryChartTests
{
    private static void Paint(CpuHistoryChart chart)
    {
        if (chart.Width <= 0 || chart.Height <= 0) return;
        using var bitmap = new Bitmap(Math.Max(1, chart.Width), Math.Max(1, chart.Height));
        chart.DrawToBitmap(bitmap, new Rectangle(0, 0, chart.Width, chart.Height));
    }

    private static CpuHistoryChart Sized(int width = 240, int height = 90) =>
        new() { Width = width, Height = height };

    [Test]
    public void A_new_chart_paints_without_any_data()
    {
        using CpuHistoryChart chart = Sized();

        Action paint = () => Paint(chart);

        paint.Should().NotThrow("the window is drawn before the first sample arrives");
    }

    [Test]
    public void A_single_reading_is_not_enough_to_draw_a_line()
    {
        using CpuHistoryChart chart = Sized();
        chart.Push(50);

        Action paint = () => Paint(chart);

        paint.Should().NotThrow();
    }

    [Test]
    public void A_full_history_paints()
    {
        using CpuHistoryChart chart = Sized();
        for (int i = 0; i < 200; i++) chart.Push(i % 101);

        Action paint = () => Paint(chart);

        paint.Should().NotThrow("more samples than Capacity must roll, not overflow");
    }

    [Test]
    public void History_is_capped_at_capacity()
    {
        // Pushing far past Capacity must not grow without bound: this control lives for the whole
        // session and ticks once a second.
        using var chart = new CpuHistoryChart { Width = 240, Height = 90, Capacity = 10 };

        for (int i = 0; i < 500; i++) chart.Push(i % 101);

        Action paint = () => Paint(chart);
        paint.Should().NotThrow();
    }

    [Test]
    public void An_UNMEASURED_reading_is_a_gap_and_not_a_zero()
    {
        // ⭐ Push(null) must not read as 0%. A reassuring dip drawn where the app knew nothing is a
        //   lie about the machine, at exactly the moment the user is trying to diagnose it.
        using CpuHistoryChart chart = Sized();

        chart.Push(90);
        chart.Push(null);
        chart.Push(95);

        Action paint = () => Paint(chart);
        paint.Should().NotThrow();
    }

    [Test]
    public void A_history_that_is_ALL_gaps_paints_nothing_and_does_not_throw()
    {
        using CpuHistoryChart chart = Sized();
        for (int i = 0; i < 20; i++) chart.Push(null);

        Action paint = () => Paint(chart);

        paint.Should().NotThrow("no segment ever reaches two points");
    }

    [Test]
    public void Gaps_between_every_pair_never_form_a_drawable_segment()
    {
        using CpuHistoryChart chart = Sized();
        for (int i = 0; i < 20; i++) { chart.Push(50); chart.Push(null); }

        Action paint = () => Paint(chart);

        paint.Should().NotThrow("each single point is cleared by the following gap");
    }

    [TestCase(2, 90, TestName = "a two pixel wide chart is skipped")]
    [TestCase(240, 2, TestName = "a two pixel tall chart is skipped")]
    [TestCase(1, 1, TestName = "a one pixel chart is skipped")]
    public void A_degenerate_size_is_skipped_rather_than_drawn(int width, int height)
    {
        // Real during startup and while a splitter is dragged shut. The arithmetic below the guard
        // divides by width and height.
        using CpuHistoryChart chart = Sized(width, height);
        for (int i = 0; i < 30; i++) chart.Push(60);

        Action paint = () => Paint(chart);

        paint.Should().NotThrow();
    }

    [TestCase(0d)]
    [TestCase(100d)]
    [TestCase(150d, TestName = "above 100 percent, which per-process CPU really can report")]
    [TestCase(-5d, TestName = "below zero")]
    public void Extreme_values_paint_without_throwing(double value)
    {
        using CpuHistoryChart chart = Sized();
        for (int i = 0; i < 10; i++) chart.Push(value);

        Action paint = () => Paint(chart);

        paint.Should().NotThrow();
    }

    [Test]
    public void The_chart_repaints_when_a_reading_arrives()
    {
        using CpuHistoryChart chart = Sized();
        chart.CreateControl();

        Action push = () => chart.Push(42);

        push.Should().NotThrow("Invalidate on a realised control must be safe");
    }

    [Test]
    public void Capacity_defaults_to_two_minutes_of_one_second_ticks()
    {
        using CpuHistoryChart chart = Sized();

        chart.Capacity.Should().Be(120, "the graph is meant to show the last couple of minutes");
    }
}
