using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// What the kill confirmation says, and how its outcome is reported.
/// </summary>
/// <remarks>
/// ⚠️ The kill button is never CLICKED. Its handler opens a modal confirmation, which hangs a
///    test rather than failing it, and past that it really terminates a process. The wording
///    and the outcome are asserted through the extracted members instead.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class LongRunningPanelKillTests : LongRunningPanelFixture
{
    // ── The confirmation, and the outcome ───────────────────────────────────────────────────────

    [Test]
    public void The_confirmation_states_EVERY_fact_the_decision_needs()
    {
        // ⭐ The last thing a user reads before a process dies. Every clause is load-bearing.
        string body = LongRunningPanel.ConfirmationBody(
            Process(name: "qemu-system-x86_64", id: 9876, memoryMb: 6144, ageHours: 50),
            owner: "claude session",
            warning: null);

        body.Should().Contain("qemu-system-x86_64", "what it is");
        body.Should().Contain("9876", "which one, exactly");
        body.Should().Contain("6,144 MB", "what it is holding");
        body.Should().Contain("claude session", "WHO LAUNCHED IT");
        body.Should().Contain("no chance to save", "that it is irreversible");
    }

    [Test]
    public void A_program_specific_warning_is_included_when_there_is_one()
    {
        var (_, warning) = ProcessKiller.CanKill("qemu-system-x86_64");
        warning.Should().NotBeNull("premise: an emulator carries a warning");

        string body = LongRunningPanel.ConfirmationBody(
            Process(name: "qemu-system-x86_64"), "explorer", warning);

        body.Should().Contain("virtual machine",
            "losing everything inside the VM is the consequence that matters");
    }

    [Test]
    public void No_warning_leaves_no_empty_gap_in_the_message()
    {
        string body = LongRunningPanel.ConfirmationBody(Process(name: "dotnet"), "pwsh", warning: null);

        // ⚠️ Three LINE FEEDS, not Environment.NewLine. ConfirmationBody is built with "\n"
        //    literals, so a CRLF comparison would never match and this assertion would pass
        //    vacuously - green while checking nothing at all.
        body.Should().NotContain(new string('\n', 3),
            "an ordinary process must not get a blank paragraph where the warning would be");
    }

    [Test]
    public void An_unknown_age_says_unknown_rather_than_inventing_one()
    {
        var noStart = new ProcessLoad("mystery", 111, 0.5, 1024 * 1024, null, 0);

        LongRunningPanel.ConfirmationBody(noStart, "-", null)
            .Should().Contain("Running for unknown");
    }

    [Test]
    public void A_SUCCESS_and_a_REFUSAL_do_not_look_alike()
    {
        // ⛔ A refusal shown in the success colour reads as confirmation that something was ended,
        //    which is the worst possible misreading for this particular status line.
        Label status = Panel.Controls.OfType<Label>().First(l => l.Dock == DockStyle.Bottom);

        Panel.ShowResult(killed: true, "Ended dotnet (pid 1) and its child processes.");
        Color success = status.ForeColor;
        status.Text.Should().Contain("Ended");

        Panel.ShowResult(killed: false, "pid 1 is now 'chrome', not 'dotnet'. The pid was reused.");
        status.ForeColor.Should().NotBe(success);
        status.Text.Should().Contain("reused");
    }

    [Test]
    public void Repeated_updates_with_the_same_data_are_stable()
    {
        var same = new[] { Process(id: 1), Process(id: 2) };

        for (int i = 0; i < 20; i++) Panel.Update(same);

        List.Items.Cast<ListViewItem>().Should().HaveCount(2, "once a second, all day");
    }
}
