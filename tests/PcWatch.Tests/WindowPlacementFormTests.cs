using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// WindowPlacement driven against a real Form that is never shown.
/// </summary>
/// <remarks>
/// 2026-09-06. The pure arithmetic is covered by WindowPlacementTests. This covers the half that
/// touches a real Form and the real settings file, which is where the behaviour a user actually
/// notices lives: whether the window comes back where they left it.
///
/// ⚠️ SettingsStore.Path is redirected to a temp file for every test here. Save() writes to disk,
///    and without the redirect this fixture would overwrite Lewis's genuine saved window position.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class WindowPlacementFormTests : SettingsRedirectFixture
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

    // ── Restore ─────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void With_no_saved_placement_the_window_opens_maximised()
    {
        WindowPlacement.Restore(_form, new Settings { HasPlacement = false });

        _form.WindowState.Should().Be(FormWindowState.Maximized,
            "a first run should fill the screen rather than pick an arbitrary corner");
    }

    [Test]
    public void A_saved_ON_SCREEN_placement_is_restored_exactly()
    {
        Rectangle work = PrimaryWork();
        var saved = new Settings
        {
            HasPlacement = true, Maximized = false,
            X = work.X + 40, Y = work.Y + 40,
            Width = Math.Min(900, work.Width - 80), Height = Math.Min(700, work.Height - 80),
        };

        WindowPlacement.Restore(_form, saved);

        _form.WindowState.Should().Be(FormWindowState.Normal);
        _form.Bounds.Should().Be(new Rectangle(saved.X, saved.Y, saved.Width, saved.Height));
        _form.StartPosition.Should().Be(FormStartPosition.Manual, "otherwise Windows repositions it");
    }

    [Test]
    public void A_saved_placement_on_a_monitor_that_is_GONE_falls_back_to_maximised()
    {
        // ⛔ THE FAILURE THIS PREVENTS. Monitors get unplugged and laptops get undocked. Restoring
        //    blindly puts the window where no screen is, with no reachable title bar, which looks
        //    exactly like the app failing to launch. The user relaunches, and the single-instance
        //    mutex turns that into nothing happening at all.
        var vanished = new Settings
        {
            HasPlacement = true, Maximized = false,
            X = -40000, Y = -40000, Width = 1200, Height = 800,
        };

        WindowPlacement.Restore(_form, vanished);

        _form.WindowState.Should().Be(FormWindowState.Maximized,
            "an unreachable window is worse than an unexpected one");
    }

    [Test]
    public void A_saved_MAXIMISED_placement_reopens_maximised()
    {
        Rectangle work = PrimaryWork();
        var saved = new Settings
        {
            HasPlacement = true, Maximized = true,
            X = work.X + 40, Y = work.Y + 40,
            Width = Math.Min(900, work.Width - 80), Height = Math.Min(700, work.Height - 80),
        };

        WindowPlacement.Restore(_form, saved);

        _form.WindowState.Should().Be(FormWindowState.Maximized);
    }

    [Test]
    public void A_saved_placement_too_small_to_be_usable_is_rejected()
    {
        var tiny = new Settings { HasPlacement = true, X = 0, Y = 0, Width = 50, Height = 30 };

        WindowPlacement.Restore(_form, tiny);

        _form.WindowState.Should().Be(FormWindowState.Maximized);
    }

    // ── Save ────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Saving_a_normal_window_records_its_bounds_and_persists_them()
    {
        Rectangle work = PrimaryWork();
        _form.StartPosition = FormStartPosition.Manual;
        _form.Bounds = new Rectangle(work.X + 60, work.Y + 60, 820, 640);
        var settings = new Settings();

        WindowPlacement.Save(_form, settings);

        settings.HasPlacement.Should().BeTrue();
        settings.Maximized.Should().BeFalse();
        settings.Width.Should().Be(820);
        settings.Height.Should().Be(640);

        // And it reached disk, which is the only reason any of this matters.
        SettingsStore.Load().Width.Should().Be(820, "a placement only helps if it survives the exit");
    }

    [Test]
    public void Saving_a_MAXIMISED_window_records_the_restore_size_not_the_screen()
    {
        // ⛔ THE REGRESSION. Bounds while maximised is the whole screen. Persist that and
        //    un-maximising snaps to full screen for ever; the small window is unrecoverable.
        Rectangle work = PrimaryWork();
        _form.StartPosition = FormStartPosition.Manual;
        _form.Bounds = new Rectangle(work.X + 60, work.Y + 60, 820, 640);
        _form.WindowState = FormWindowState.Maximized;
        var settings = new Settings();

        WindowPlacement.Save(_form, settings);

        settings.Maximized.Should().BeTrue();
        settings.Width.Should().Be(820, "the RESTORE width, not the screen width");
        settings.Width.Should().BeLessThan(work.Width);
    }
}
