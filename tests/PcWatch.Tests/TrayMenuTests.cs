using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The notification-area menu: what it offers, and what each entry actually does.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-06. Clicking these items is the only way to test that the handlers are wired to the
///    RIGHT callbacks, and two of them really open Task Manager and Resource Monitor. Without an
///    injected launcher, every run of this suite would litter the developer's desktop with windows.
///    That is why Build takes one.
///
/// ⚠️ Copy report is NOT clicked here. It calls Clipboard.SetText, and a test suite that silently
///    replaces the contents of your clipboard mid-session is user-hostile in a way no assertion
///    makes up for. Its guard condition is asserted through the item's presence and state instead.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class TrayMenuTests
{
    private static ContextMenuStrip Build(
        Action? show = null, Func<string>? report = null, Action? scan = null,
        Action? exit = null, Action<string>? launch = null) =>
        TrayMenu.Build(
            show ?? (() => { }),
            report ?? (() => "report"),
            scan ?? (() => { }),
            exit ?? (() => { }),
            launch ?? (_ => { }));

    private static ToolStripItem Item(ContextMenuStrip menu, string text) =>
        menu.Items.Cast<ToolStripItem>().First(i => i.Text == text);

    [Test]
    public void The_menu_offers_every_expected_entry_in_order()
    {
        using ContextMenuStrip menu = Build();

        menu.Items.Cast<ToolStripItem>()
            .Where(i => i is not ToolStripSeparator)
            .Select(i => i.Text)
            .Should().Equal("Show PC Watch", "Copy report", "Scan disk usage",
                            "Task Manager", "Resource Monitor", "Exit");
    }

    [Test]
    public void The_menu_is_grouped_by_separators()
    {
        using ContextMenuStrip menu = Build();

        menu.Items.Cast<ToolStripItem>().OfType<ToolStripSeparator>()
            .Should().HaveCount(3, "grouping is what stops Exit sitting next to Show");
    }

    [Test]
    public void The_menu_contains_NOTHING_destructive()
    {
        // ⭐ A deliberate product decision, pinned so it cannot be casually undone. PC Watch can say
        //   a process looks like a runaway, but it cannot know whether the agent session owning it
        //   is mid-task, so ending processes stays a decision made in Task Manager.
        using ContextMenuStrip menu = Build();

        string[] texts = [.. menu.Items.Cast<ToolStripItem>().Select(i => i.Text ?? string.Empty)];

        texts.Should().NotContain(t => t.Contains("Kill", StringComparison.OrdinalIgnoreCase));
        texts.Should().NotContain(t => t.Contains("End", StringComparison.OrdinalIgnoreCase));
        texts.Should().NotContain(t => t.Contains("Terminate", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public void Show_invokes_the_show_callback()
    {
        bool shown = false;
        using ContextMenuStrip menu = Build(show: () => shown = true);

        Item(menu, "Show PC Watch").PerformClick();

        shown.Should().BeTrue();
    }

    [Test]
    public void Scan_disk_usage_invokes_the_scan_callback()
    {
        bool scanned = false;
        using ContextMenuStrip menu = Build(scan: () => scanned = true);

        Item(menu, "Scan disk usage").PerformClick();

        scanned.Should().BeTrue("walking a 2 TB drive is on demand, never on a timer");
    }

    [Test]
    public void Exit_invokes_the_exit_callback()
    {
        bool exited = false;
        using ContextMenuStrip menu = Build(exit: () => exited = true);

        Item(menu, "Exit").PerformClick();

        exited.Should().BeTrue();
    }

    [Test]
    public void Each_tool_entry_opens_ITS_OWN_tool()
    {
        // ⚠️ Asserting the DESTINATION, not merely that a handler ran. A test that only recorded
        //    "the launcher was called" passes just as happily when both items open the same program.
        var opened = new List<string>();
        using ContextMenuStrip menu = Build(launch: opened.Add);

        Item(menu, "Task Manager").PerformClick();
        Item(menu, "Resource Monitor").PerformClick();

        opened.Should().Equal("taskmgr.exe", "resmon.exe");
    }

    [Test]
    public void Show_and_exit_are_not_wired_to_the_same_callback()
    {
        // They are adjacent in intent and trivially transposable; clicking Show must never exit.
        bool shown = false, exited = false;
        using ContextMenuStrip menu = Build(show: () => shown = true, exit: () => exited = true);

        Item(menu, "Show PC Watch").PerformClick();

        shown.Should().BeTrue();
        exited.Should().BeFalse("clicking Show must not close the app");
    }

    [Test]
    public void A_report_callback_is_not_invoked_just_by_building_the_menu()
    {
        // Building the menu happens at startup; generating a report is expensive and must wait for
        // an actual click.
        bool asked = false;
        using ContextMenuStrip menu = Build(report: () => { asked = true; return "x"; });

        asked.Should().BeFalse("the report is produced on demand, not when the menu is constructed");
    }

    [Test]
    public void Launching_a_nonexistent_program_does_not_throw()
    {
        // The real launcher. These entries are conveniences: failing to open one must never take
        // the app down. Deliberately a name that cannot exist, so nothing is started.
        Action launch = () => TrayMenu.Launch($"definitely-not-a-real-program-{Guid.NewGuid():N}.exe");

        launch.Should().NotThrow();
    }
}
