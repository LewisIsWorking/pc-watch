using System.Text.Json;
using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The window itself, constructed and driven without ever being shown to a user.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-06. THREE HAZARDS, all closed by redirecting SettingsStore.Path before the form is
///    built. A naive test of this class would:
///
///      1. READ AND THEN OVERWRITE THE REAL SETTINGS. The constructor calls SettingsStore.Load()
///         and Dispose calls WindowPlacement.Save(), so running the suite would move the user's
///         actual window position.
///      2. CALL GITHUB. OnLoad fires UpdatePrompt.CheckAsync against a REAL UpdateChecker, so
///         anything that shows the form reaches the network. The temp settings below set
///         CheckForUpdates=false, which is honoured BEFORE the request is made.
///      3. LEAVE A TRAY ICON BEHIND. The constructor sets NotifyIcon.Visible = true. Dispose
///         clears it, so every test disposes in teardown rather than relying on finalisation.
///
/// ⚠️ A System.Windows.Forms.Timer needs a message pump, so OnTick does NOT fire on its own in a
///    test. The one test that exercises it pumps deliberately and says so.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class MainFormTests : SettingsRedirectFixture
{
    /// <summary>
    /// Write settings that switch the update check OFF, before any MainForm is built.
    /// </summary>
    /// <remarks>
    /// ⛔ LOAD-BEARING, and nearly lost in a scripted refactor on 2026-09-06. MainForm.OnLoad fires
    ///    UpdatePrompt.CheckAsync against a REAL UpdateChecker, so anything that shows the form
    ///    reaches api.github.com. CheckForUpdates=false is honoured BEFORE the request is made, so
    ///    this is what keeps the suite off the network.
    ///
    /// ⚠️ NUnit runs base-class SetUp first, so SettingsStore.Path has already been redirected by
    ///    the time this writes. Reversing that order would write into the user's real settings.
    /// </remarks>
    [SetUp]
    public void SilenceTheNetworkBeforeAnyFormIsBuilt()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings
        {
            CheckForUpdates = false,
            HasPlacement = false,
        }));
    }

    private static MainForm Build() => new();

    [Test]
    public void The_window_is_a_REAL_pinnable_window()
    {
        // ⭐ This is the whole difference from the tray-only tool it replaced. Windows pins only an
        //    executable, and only a real window gives the pinned button something to activate.
        using MainForm form = Build();

        form.ShowInTaskbar.Should().BeTrue("a tray-only tool cannot be pinned");
        form.Text.Should().Be("PC Watch");
        form.FormBorderStyle.Should().Be(FormBorderStyle.Sizable);
    }

    [Test]
    public void AUTO_SCALING_IS_OFF()
    {
        // ⛔ AutoScaleMode.Font (the Form default) multiplies any assigned size by the ratio of
        //    design-time to runtime font metrics. It shrank an explicit 880 px to 718 and clipped
        //    the fixed-width process table, with nothing reporting an error.
        using MainForm form = Build();

        form.AutoScaleMode.Should().Be(AutoScaleMode.None);
    }

    [Test]
    public void A_minimum_size_stops_the_window_being_dragged_useless()
    {
        using MainForm form = Build();

        form.MinimumSize.Should().Be(new Size(660, 460));
    }

    [Test]
    public void The_dashboard_is_built_into_the_window()
    {
        using MainForm form = Build();

        form.Controls.Count.Should().BeGreaterThan(0, "the layout is attached in the constructor");
    }

    [Test]
    public void With_no_saved_placement_the_window_opens_maximised()
    {
        using MainForm form = Build();

        form.WindowState.Should().Be(FormWindowState.Maximized);
    }

    [Test]
    public void A_saved_placement_is_honoured_at_construction()
    {
        Screen? primary = Screen.PrimaryScreen;
        Assume.That(primary, Is.Not.Null, "needs a display");
        Rectangle work = primary!.WorkingArea;

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings
        {
            CheckForUpdates = false,
            HasPlacement = true,
            Maximized = false,
            X = work.X + 50, Y = work.Y + 50,
            Width = Math.Min(1000, work.Width - 100), Height = Math.Min(700, work.Height - 100),
        }));

        using MainForm form = Build();

        form.WindowState.Should().Be(FormWindowState.Normal);
        form.Bounds.X.Should().Be(work.X + 50);
    }

    // ── Restoring from the tray ─────────────────────────────────────────────────────────────────

    [Test]
    public void Restoring_from_the_tray_comes_back_MAXIMISED_not_normal()
    {
        // ⛔ THE REGRESSION. Restoring unconditionally to Normal silently demotes a maximised window
        //    every time it is minimised and brought back, so the window shrinks a little each round
        //    trip and the user never sees why.
        using MainForm form = Build();
        form.WindowState.Should().Be(FormWindowState.Maximized, "premise");

        form.WindowState = FormWindowState.Minimized;
        form.RestoreFromTray();

        form.WindowState.Should().Be(FormWindowState.Maximized);
    }

    [Test]
    public void A_window_left_NORMAL_comes_back_normal()
    {
        using MainForm form = Build();

        // ⚠️ THIS ONE NEEDS A GENUINELY SHOWN WINDOW. OnResize does not fire on a form that has
        //    never been shown - not even after its handle is created - so the state-to-return-to is
        //    never recorded and the assignment below looks ignored. Opacity 0 makes it real to
        //    Windows while invisible to the person running the suite: nothing flashes on screen.
        form.Opacity = 0;
        form.Show();
        form.WindowState = FormWindowState.Normal;
        Application.DoEvents();

        form.WindowState = FormWindowState.Minimized;
        Application.DoEvents();
        form.RestoreFromTray();

        form.WindowState.Should().Be(FormWindowState.Normal, "it must not be promoted either");
    }

    [Test]
    public void Restoring_a_window_that_is_not_minimised_leaves_its_state_alone()
    {
        using MainForm form = Build();

        form.RestoreFromTray();

        form.WindowState.Should().Be(FormWindowState.Maximized);
    }

    [Test]
    public void Minimising_does_not_overwrite_the_state_to_return_to()
    {
        // If OnResize recorded Minimized, restoring from the tray would restore to minimised, which
        // is a window that appears to do nothing when clicked.
        using MainForm form = Build();

        form.WindowState = FormWindowState.Minimized;
        form.WindowState = FormWindowState.Minimized;
        form.RestoreFromTray();

        form.WindowState.Should().NotBe(FormWindowState.Minimized);
    }
}
