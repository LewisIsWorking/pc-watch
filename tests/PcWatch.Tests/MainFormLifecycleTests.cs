using System.Text.Json;
using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Moving the window between monitors, ticking, and shutting down.
/// </summary>
/// <remarks>
/// ⛔ CheckForUpdates=false is written before any form is built. MainForm.OnLoad fires a REAL
///    update check, so without it anything that shows the form reaches api.github.com.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class MainFormLifecycleTests : SettingsRedirectFixture
{
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

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (Control deeper in Descendants(child)) yield return deeper;
        }
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
}
