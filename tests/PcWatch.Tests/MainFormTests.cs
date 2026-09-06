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
public sealed class MainFormTests
{
    private string _realPath = string.Empty;
    private string _temp = string.Empty;

    [SetUp]
    public void RedirectSettingsAndSilenceTheNetwork()
    {
        _realPath = SettingsStore.Path;
        _temp = Path.Combine(Path.GetTempPath(), $"pcwatch-main-{Guid.NewGuid():N}", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_temp)!);

        // CheckForUpdates=false is the load-bearing part: it is honoured before the HTTP request,
        // so no test here can reach api.github.com however the form is driven.
        File.WriteAllText(_temp, JsonSerializer.Serialize(new Settings
        {
            CheckForUpdates = false,
            HasPlacement = false,
        }));
        SettingsStore.Path = _temp;
    }

    [TearDown]
    public void RestoreSettings()
    {
        SettingsStore.Path = _realPath;
        string? dir = Path.GetDirectoryName(_temp);
        if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
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

        File.WriteAllText(_temp, JsonSerializer.Serialize(new Settings
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

    // ── Moving between monitors ─────────────────────────────────────────────────────────────────

    [TestCase("left")]
    [TestCase("right")]
    [TestCase("primary")]
    public void A_recognised_monitor_name_is_accepted(string monitor)
    {
        Assume.That(Screen.PrimaryScreen, Is.Not.Null, "needs a display");
        using MainForm form = Build();

        form.MoveToMonitor(monitor).Should().BeTrue();
    }

    [TestCase("nonsense")]
    [TestCase("0")]
    public void An_unrecognised_monitor_name_is_refused(string monitor)
    {
        Assume.That(Screen.PrimaryScreen, Is.Not.Null, "needs a display");
        using MainForm form = Build();

        form.MoveToMonitor(monitor).Should().BeFalse();
    }

    // ── The tick ────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void A_TICK_SAMPLES_THE_MACHINE_AND_FILLS_THE_HEADLINE()
    {
        // ⚠️ A System.Windows.Forms.Timer only fires on a message pump, so this pumps deliberately.
        //    Everything before the first tick reads "measuring...", which is itself the assertion
        //    that the app does not show a confident 0% before it knows anything.
        using MainForm form = Build();

        Label headline = form.Controls.Cast<Control>()
            .SelectMany(Descendants).OfType<Label>()
            .First(l => l.Text == "measuring...");

        DateTime deadline = DateTime.Now.AddSeconds(8);
        while (DateTime.Now < deadline && headline.Text == "measuring...")
        {
            Application.DoEvents();
            Thread.Sleep(50);
        }

        headline.Text.Should().NotBe("measuring...", "a tick must replace the placeholder");
        headline.Text.Should().Contain("CPU");
    }

    [Test]
    public void Disposing_persists_the_placement_and_removes_the_tray_icon()
    {
        MainForm form = Build();
        form.Dispose();

        // The save is the observable half; the tray icon going is asserted by the absence of a
        // leaked icon after the suite, which nothing can check from inside it.
        SettingsStore.Load().HasPlacement.Should().BeTrue("the window position must survive an exit");
    }

    [Test]
    public void Disposing_twice_does_not_throw()
    {
        MainForm form = Build();

        Action twice = () => { form.Dispose(); form.Dispose(); };

        twice.Should().NotThrow("Application.Exit and an explicit close can both land");
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (Control deeper in Descendants(child)) yield return deeper;
        }
    }
}
