using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Copying the report from the tray menu.
/// </summary>
/// <remarks>
/// ⚠️ 2026-09-09. THE REAL CLIPBOARD IS NEVER TOUCHED. Clipboard.SetText is injected, so these tests
///    cannot clobber whatever the person running them had copied. A test that quietly replaces the
///    user's clipboard is a bug in the test, not an acceptable side effect.
///
/// ⛔ Clipboard.SetText THROWS on empty or null. The blank guard is therefore load-bearing: without
///    it, "Copy report" raises during the seconds before the first sample lands, which is exactly
///    when a new user is most likely to be poking at the tray menu.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class TrayMenuCopyTests
{
    [Test]
    public void A_real_report_reaches_the_clipboard_unchanged()
    {
        var copied = new List<string>();

        TrayMenu.CopyReport(() => "  CPU   84%\n  RAM   31.5 / 63.9 GB", copied.Add);

        copied.Should().ContainSingle();
        copied[0].Should().Contain("CPU").And.Contain("RAM",
            "the whole report is the point; a summary would lose the detail being reported");
    }

    [TestCase("", TestName = "empty")]
    [TestCase("   ", TestName = "spaces")]
    [TestCase("\n\n", TestName = "blank lines")]
    public void NOTHING_IS_COPIED_WHEN_THERE_IS_NOTHING_TO_COPY(string report)
    {
        // ⛔ THE REGRESSION. Clipboard.SetText throws on blank input, so dropping this guard turns a
        //    harmless click into an exception on a tray menu, before the first sample exists.
        var copied = new List<string>();

        Action copy = () => TrayMenu.CopyReport(() => report, copied.Add);

        copy.Should().NotThrow();
        copied.Should().BeEmpty("an empty clipboard write is both pointless and fatal");
    }

    [Test]
    public void The_report_is_read_ONCE_per_copy()
    {
        // Rendering the report is the expensive part of a tick. Reading it twice per click would
        // double that cost for no gain.
        int reads = 0;

        TrayMenu.CopyReport(() => { reads++; return "something"; }, _ => { });

        reads.Should().Be(1);
    }

    [Test]
    public void The_report_is_read_LAZILY_when_the_menu_is_merely_built()
    {
        // The menu is built once at startup and lives for the life of the app. Reading the report
        // eagerly there would capture a snapshot from before the first sample and copy that for ever.
        int reads = 0;

        using ContextMenuStrip menu = TrayMenu.Build(
            () => { }, () => { reads++; return "text"; }, () => { }, () => { }, () => { },
            launch: _ => { }, copyText: _ => { });

        reads.Should().Be(0, "building is not copying");
    }

    [Test]
    public void Clicking_Copy_report_copies_the_CURRENT_text_not_the_text_at_build_time()
    {
        // ⭐ The reason the callback is a Func rather than a string. The report changes every second;
        //   a captured string would put a stale reading on the clipboard for the life of the app.
        string current = "first";
        var copied = new List<string>();

        using ContextMenuStrip menu = TrayMenu.Build(
            () => { }, () => current, () => { }, () => { }, () => { },
            launch: _ => { }, copyText: copied.Add);

        current = "second";
        menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "Copy report").PerformClick();

        copied.Should().Equal("second");
    }

    [Test]
    public void The_injected_clipboard_is_used_INSTEAD_of_the_real_one()
    {
        // Proves the seam actually diverts the write. If the parameter were ignored, this test
        // would silently write to the machine's clipboard and still pass on the count.
        var copied = new List<string>();

        using ContextMenuStrip menu = TrayMenu.Build(
            () => { }, () => "report body", () => { }, () => { }, () => { },
            launch: _ => { }, copyText: copied.Add);

        menu.Items.Cast<ToolStripItem>().Single(i => i.Text == "Copy report").PerformClick();

        copied.Should().Equal("report body");
    }
}
