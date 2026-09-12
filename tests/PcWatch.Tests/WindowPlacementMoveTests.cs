using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Moving a window to a named monitor.
/// </summary>
/// <remarks>
/// ⚠️ Bounds are set while the window is Normal so its restore size is sensible, and only then
///    is it re-maximised onto the target screen. Maximising a window that still believes it
///    lives on another monitor lands it back there.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class WindowPlacementMoveTests
{
    private Form _form = null!;

    [SetUp]
    public void CreateAnInvisibleForm()
    {
        _form = new Form { ShowInTaskbar = false, Size = new Size(800, 600) };
        _form.CreateControl();
    }

    [TearDown]
    public void DisposeTheForm() => _form.Dispose();

    private static Rectangle PrimaryWork()
    {
        Screen? primary = Screen.PrimaryScreen;
        Assume.That(primary, Is.Not.Null, "needs a display; skipped on a headless agent");
        return primary!.WorkingArea;
    }

    // ── MoveTo ──────────────────────────────────────────────────────────────────────────────────

    [TestCase("left")]
    [TestCase("right")]
    [TestCase("primary")]
    [TestCase("1")]
    public void A_recognised_monitor_name_moves_the_window(string monitor)
    {
        PrimaryWork();

        WindowPlacement.MoveTo(_form, monitor).Should().BeTrue();
        _form.StartPosition.Should().Be(FormStartPosition.Manual);
    }

    [TestCase("nonsense")]
    [TestCase("0")]
    [TestCase("")]
    public void An_unrecognised_monitor_name_moves_nothing_and_says_so(string monitor)
    {
        PrimaryWork();
        Rectangle before = _form.Bounds;

        WindowPlacement.MoveTo(_form, monitor).Should().BeFalse();
        _form.Bounds.Should().Be(before, "a refused move must not half-apply");
    }

    [Test]
    public void Moving_a_maximised_window_leaves_it_maximised()
    {
        // Bounds are set while Normal so the restore size is sensible, then it is re-maximised onto
        // the target screen. Maximising a window that still believes it lives elsewhere lands it back.
        PrimaryWork();
        _form.WindowState = FormWindowState.Maximized;

        WindowPlacement.MoveTo(_form, "primary").Should().BeTrue();

        _form.WindowState.Should().Be(FormWindowState.Maximized);
    }

    [Test]
    public void The_moved_window_lands_inside_the_target_working_area()
    {
        Rectangle work = PrimaryWork();

        WindowPlacement.MoveTo(_form, "primary").Should().BeTrue();

        _form.Bounds.Width.Should().BeLessThanOrEqualTo(work.Width);
        _form.Bounds.Height.Should().BeLessThanOrEqualTo(work.Height);
    }
}
