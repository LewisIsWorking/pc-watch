using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Where the window reopens, and which monitor a name refers to.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-05. WindowPlacement was at 0%: every branch needed real monitors and a real window, so
///    the arithmetic deciding where your window reopens was never once tested.
///
///    The failure it guards against is nastier than it sounds. Restore a window onto a monitor that
///    is no longer attached and it lands somewhere with no reachable title bar, which is
///    INDISTINGUISHABLE from the app failing to launch. The user's next move is to launch it again,
///    which the single-instance mutex turns into nothing happening at all.
/// </remarks>
[TestFixture]
public sealed class WindowPlacementTests
{
    // ── Choosing a monitor ──────────────────────────────────────────────────────────────────────

    [TestCase("left", 0)]
    [TestCase("first", 0)]
    [TestCase("LEFT", 0)]
    [TestCase("right", 2)]
    [TestCase("last", 2)]
    [TestCase("Right", 2)]
    public void Position_names_map_to_the_ends_of_the_x_ordered_list(string name, int expected)
    {
        // ⚠️ X-ORDERED, not enumeration order. "right monitor" is a spatial statement; the order
        //    Windows enumerates displays in is an installation detail that does not track physical
        //    arrangement, so using it would send the window to an arbitrary screen.
        ReportIndex(name, screens: 3).Should().Be(expected);
    }

    [TestCase("1", 0)]
    [TestCase("2", 1)]
    [TestCase("3", 2)]
    public void Numbers_are_one_based_because_people_are(string name, int expected)
    {
        ReportIndex(name, screens: 3).Should().Be(expected);
    }

    [TestCase("0")]
    [TestCase("4")]
    [TestCase("-1")]
    [TestCase("99")]
    public void An_out_of_range_number_selects_nothing(string name)
    {
        WindowPlacement.SelectMonitorIndex(name, screenCount: 3).Should().BeNull();
    }

    [TestCase("bogus")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("2.5")]
    public void An_unrecognised_name_selects_nothing(string name)
    {
        WindowPlacement.SelectMonitorIndex(name, screenCount: 3).Should().BeNull();
    }

    [Test]
    public void Primary_is_deliberately_not_an_index()
    {
        // It is the one name meaning a specific DISPLAY rather than a position, so it is resolved
        // against Screen.PrimaryScreen by the caller, not by position in the list.
        WindowPlacement.SelectMonitorIndex("primary", screenCount: 3).Should().BeNull();
    }

    [Test]
    public void With_a_single_screen_left_and_right_are_the_same_screen()
    {
        WindowPlacement.SelectMonitorIndex("left", 1).Should().Be(0);
        WindowPlacement.SelectMonitorIndex("right", 1).Should().Be(0);
    }

    [Test]
    public void With_no_screens_nothing_is_selected()
    {
        // Should not be reachable, but returning an index into an empty array would throw.
        WindowPlacement.SelectMonitorIndex("left", 0).Should().BeNull();
        WindowPlacement.SelectMonitorIndex("1", 0).Should().BeNull();
    }

    private static int? ReportIndex(string name, int screens) =>
        WindowPlacement.SelectMonitorIndex(name, screens);

    // ── Centring ────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void A_window_is_centred_in_the_working_area()
    {
        Rectangle work = new(100, 50, 1000, 800);

        Rectangle placed = WindowPlacement.CentreWithin(work, new Size(400, 200));

        placed.Should().Be(new Rectangle(400, 350, 400, 200));
    }

    [Test]
    public void Centring_respects_a_monitor_that_does_not_start_at_the_origin()
    {
        // The second monitor's coordinates are offset by the first monitor's width. Forgetting the
        // offset puts the window on the wrong screen entirely.
        Rectangle secondMonitor = new(1920, 0, 1920, 1080);

        Rectangle placed = WindowPlacement.CentreWithin(secondMonitor, new Size(920, 80));

        placed.X.Should().Be(1920 + 500);
        placed.X.Should().BeGreaterThanOrEqualTo(secondMonitor.X, "it must land on that monitor");
    }

    [Test]
    public void A_window_too_wide_for_the_screen_is_shrunk_to_fit()
    {
        Rectangle work = new(0, 0, 800, 600);

        Rectangle placed = WindowPlacement.CentreWithin(work, new Size(2000, 1500));

        placed.Should().Be(new Rectangle(0, 0, 800, 600), "an oversized window must not overhang");
    }

    [Test]
    public void A_window_exactly_the_size_of_the_screen_fills_it()
    {
        Rectangle work = new(10, 20, 800, 600);

        WindowPlacement.CentreWithin(work, new Size(800, 600))
            .Should().Be(new Rectangle(10, 20, 800, 600));
    }

    // ── What is worth saving ────────────────────────────────────────────────────────────────────

    [Test]
    public void A_normal_window_saves_its_actual_bounds()
    {
        Rectangle bounds = new(10, 20, 800, 600);

        WindowPlacement.PlacementToSave(FormWindowState.Normal, bounds, new Rectangle(1, 1, 5, 5))
            .Should().Be(bounds);
    }

    [Test]
    public void A_MAXIMISED_window_saves_its_RESTORE_bounds_not_the_whole_screen()
    {
        // ⛔ THE REGRESSION. Bounds while maximised is the entire screen. Save that and un-maximising
        //    snaps to full screen FOR EVER afterwards - the small window can never be recovered.
        Rectangle wholeScreen = new(0, 0, 3840, 2160);
        Rectangle restore = new(300, 200, 900, 700);

        WindowPlacement.PlacementToSave(FormWindowState.Maximized, wholeScreen, restore)
            .Should().Be(restore);
    }

    [Test]
    public void A_MINIMISED_window_also_saves_its_restore_bounds()
    {
        Rectangle restore = new(300, 200, 900, 700);

        WindowPlacement.PlacementToSave(FormWindowState.Minimized, new Rectangle(-32000, -32000, 160, 28), restore)
            .Should().Be(restore);
    }

    [TestCase(0, 600)]
    [TestCase(800, 0)]
    [TestCase(0, 0)]
    [TestCase(-5, 600)]
    public void A_degenerate_rectangle_is_not_saved(int width, int height)
    {
        // Persisting a zero-sized window means the next launch restores something invisible.
        WindowPlacement.PlacementToSave(FormWindowState.Normal, new Rectangle(0, 0, width, height), Rectangle.Empty)
            .Should().BeNull();
    }
}
