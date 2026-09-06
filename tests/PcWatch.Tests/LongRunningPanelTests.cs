using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The long-running list and its kill button, driven headlessly.
/// </summary>
/// <remarks>
/// ⚠️ THE KILL BUTTON IS NEVER CLICKED. Its handler opens a modal confirmation, which does not fail
///    a test but HANGS it, and past that it really terminates a process. Everything up to the
///    dialog - what is listed, what is selected, whether the button is even enabled and what it
///    says - is asserted here, and that is where the safety actually lives.
///
/// ⭐ The owner column is not decoration. The emulator this app once advised closing turned out to
///   belong to a live agent session; a kill button without an owner column would have made that a
///   single click.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class LongRunningPanelTests : LongRunningPanelFixture
{
    // ── What gets listed ────────────────────────────────────────────────────────────────────────

    [Test]
    public void An_empty_list_shows_no_rows_and_disables_the_button()
    {
        Panel.Update([]);

        List.Items.Cast<ListViewItem>().Should().BeEmpty();
        KillButton.Enabled.Should().BeFalse();
        KillButton.Text.Should().Be("Select a process to end");
    }

    [Test]
    public void Each_process_becomes_a_row_with_every_column_filled()
    {
        Panel.Update([Process(name: "qemu-system-x86_64", id: 9876, memoryMb: 6144, ageHours: 50)]);

        ListViewItem row = List.Items[0];
        row.Text.Should().Be("qemu-system-x86_64");
        row.SubItems[1].Text.Should().Be("9876");
        row.SubItems[2].Text.Should().Be("6,144 MB", "memory is grouped so 6 GB does not read as 6144");
        row.SubItems[3].Text.Should().NotBe("-", "a known start time must produce an age");
        row.SubItems.Count.Should().Be(5, "Process, PID, Memory, Age, Launched by");
    }

    [Test]
    public void A_process_whose_start_time_windows_refuses_shows_a_dash_not_a_wrong_age()
    {
        // Windows genuinely refuses the start time for some processes. Rendering that as 0, or as
        // "since the epoch", would be a confident wrong answer in the column the sort depends on.
        var unknown = new ProcessLoad("mystery", 111, 0.5, 1024 * 1024, null, 0);

        Panel.Update([unknown]);

        List.Items[0].SubItems[3].Text.Should().Be("-");
    }

    [Test]
    public void The_list_is_rebuilt_on_every_update_without_accumulating_rows()
    {
        Panel.Update([Process(id: 1), Process(id: 2)]);
        Panel.Update([Process(id: 3)]);

        List.Items.Cast<ListViewItem>().Should().HaveCount(1,
            "this runs once a second and must not grow without bound");
    }

    // ── Selection ───────────────────────────────────────────────────────────────────────────────

    [Test]
    public void SELECTION_SURVIVES_A_REFRESH_even_when_the_sort_order_moves()
    {
        // ⛔ THE REGRESSION. This refreshes once a second. A naive Clear() steals the selection every
        //    tick, so the Kill button can never be reached: it enables for a fraction of a second
        //    and disables again before anyone can click it. Restored by PID, not row index,
        //    precisely because the memory sort reorders rows underneath the user.
        Panel.Update([Process(id: 10, name: "alpha"), Process(id: 20, name: "beta")]);
        List.Items[1].Selected = true;

        // Same processes, opposite order, as a memory-sorted list does constantly.
        Panel.Update([Process(id: 20, name: "beta"), Process(id: 10, name: "alpha")]);

        List.SelectedItems.Cast<ListViewItem>().Should().ContainSingle();
        ((ProcessLoad)List.SelectedItems[0].Tag!).Id.Should().Be(20, "the SAME PROCESS, not the same row");
    }

    [Test]
    public void A_selection_that_disappears_from_the_list_is_simply_lost()
    {
        Panel.Update([Process(id: 10), Process(id: 20)]);
        List.Items[1].Selected = true;

        Panel.Update([Process(id: 10)]);

        List.SelectedItems.Cast<ListViewItem>().Should().BeEmpty();
        KillButton.Enabled.Should().BeFalse("nothing is selected, so nothing can be ended");
    }

    // ── The kill button ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void Selecting_an_ordinary_process_enables_the_button_and_names_it()
    {
        Panel.Update([Process(name: "dotnet", id: 4321)]);

        List.Items[0].Selected = true;

        KillButton.Enabled.Should().BeTrue();
        KillButton.Text.Should().Be("End dotnet (pid 4321)",
            "the button must say exactly what it is about to end, including the pid");
    }

    [TestCase("csrss")]
    [TestCase("wininit")]
    [TestCase("lsass")]
    [TestCase("services")]
    [TestCase("svchost")]
    public void A_CRITICAL_process_can_be_selected_but_never_ended(string name)
    {
        // ⛔ Terminating any of these bugchecks the machine; a blue screen is the DESIGNED response.
        //    They are listed because they are genuinely long-lived and hiding them would be
        //    dishonest, but the button must refuse and say why.
        Panel.Update([Process(name: name)]);

        List.Items[0].Selected = true;

        KillButton.Enabled.Should().BeFalse();
        KillButton.Text.Should().Contain("protected").And.Contain(name);
    }

    [Test]
    public void A_protected_row_is_visibly_dimmed_in_the_list()
    {
        // Before it is even selected. The colour is the first hint that the row is different.
        Panel.Update([Process(name: "csrss", id: 4), Process(name: "dotnet", id: 5)]);

        List.Items[0].ForeColor.Should().Be(Theme.Dim);
        List.Items[1].ForeColor.Should().NotBe(Theme.Dim);
    }

    [Test]
    public void Deselecting_disables_the_button_again()
    {
        Panel.Update([Process()]);
        List.Items[0].Selected = true;
        KillButton.Enabled.Should().BeTrue("premise");

        List.Items[0].Selected = false;

        KillButton.Enabled.Should().BeFalse();
        KillButton.Text.Should().Be("Select a process to end");
    }

    [Test]
    public void Only_one_row_can_be_selected_at_a_time()
    {
        // A kill button acting on a multi-selection is a much bigger gun than this app should carry.
        Panel.Update([Process(id: 1), Process(id: 2), Process(id: 3)]);

        List.MultiSelect.Should().BeFalse();
    }

    [Test]
    public void The_owner_column_exists_and_is_the_widest_after_the_name()
    {
        // ⭐ It is the column that stops a single click ending a live agent session.
        Panel.Update([Process()]);

        List.Columns[4].Text.Should().Be("Launched by");
        List.Columns[4].Width.Should().BeGreaterThan(List.Columns[1].Width,
            "an owner needs room to be read; a pid does not");
    }

    [Test]
    public void An_unknown_owner_shows_a_dash_rather_than_a_guess()
    {
        // Pid 999999 will not resolve. Inventing a plausible owner here would be the worst possible
        // failure: the column exists precisely to be trusted before something is ended.
        Panel.Update([Process(id: 999_999)]);

        List.Items[0].SubItems[4].Text.Should().Be("-");
    }
}
