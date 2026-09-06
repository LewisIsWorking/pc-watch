using System.Diagnostics;
using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The most dangerous function in the app: ending a process, and refusing to.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-06. The termination path itself was untested, because testing it means really killing
///    something. It is tested here against processes THIS FIXTURE SPAWNS AND OWNS - never anything
///    already running on the machine - so a real kill is exercised with nothing at risk.
///
/// ⚠️ Every spawned process is a hidden `timeout` that would exit on its own within seconds anyway,
///    so even a crashed test run leaves nothing behind.
/// </remarks>
[TestFixture]
public sealed class ProcessKillerTests
{
    private readonly List<Process> _spawned = [];

    /// <summary>Start a harmless, hidden child process that this fixture owns.</summary>
    private Process Spawn()
    {
        var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c timeout /t 30 /nobreak")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        _spawned.Add(process);
        return process;
    }

    [TearDown]
    public void KillAnythingLeftOver()
    {
        foreach (Process process in _spawned)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
        }
        _spawned.Clear();
    }

    // ── Refusals ────────────────────────────────────────────────────────────────────────────────

    [TestCase("csrss")]
    [TestCase("wininit")]
    [TestCase("winlogon")]
    [TestCase("services")]
    [TestCase("smss")]
    [TestCase("lsass")]
    [TestCase("svchost")]
    [TestCase("System")]
    [TestCase("Idle")]
    [TestCase("Registry")]
    [TestCase("dwm")]
    [TestCase("fontdrvhost")]
    public void A_critical_process_is_refused_and_the_reason_names_the_consequence(string name)
    {
        // ⛔ Terminating any of these BUGCHECKS the machine. A blue screen is the DESIGNED Windows
        //    response, not a bug, so the refusal is the entire safety mechanism.
        var (allowed, reason) = ProcessKiller.CanKill(name);

        allowed.Should().BeFalse();
        reason.Should().Contain("bugcheck", "the user deserves to know WHY it was refused");
        reason.Should().Contain(name);
    }

    [Test]
    public void A_refusal_is_case_insensitive()
    {
        // Windows reports process names in varying case; a case-sensitive denylist would let CSRSS
        // straight through the one check that prevents a blue screen.
        ProcessKiller.CanKill("CSRSS").Allowed.Should().BeFalse();
        ProcessKiller.CanKill("LsaSS").Allowed.Should().BeFalse();
    }

    [Test]
    public void Kill_refuses_a_critical_process_WITHOUT_EVEN_LOOKING_IT_UP()
    {
        // pid 4 really is System on Windows. The refusal must come from the name check before any
        // process is opened, so the id is irrelevant.
        var (killed, message) = ProcessKiller.Kill(4, "csrss");

        killed.Should().BeFalse();
        message.Should().Contain("bugcheck");
    }

    // ── Warnings ────────────────────────────────────────────────────────────────────────────────

    [TestCase("explorer", "taskbar")]
    [TestCase("qemu-system-x86_64", "virtual machine")]
    [TestCase("vmmem", "WSL")]
    public void A_survivable_but_disruptive_process_is_allowed_WITH_a_warning(string name, string mentions)
    {
        // ⚠️ Being long-lived is not a fault, and these are safe to end. The warning exists so the
        //    consequence is known BEFORE the click, not discovered afterwards.
        var (allowed, reason) = ProcessKiller.CanKill(name);

        allowed.Should().BeTrue();
        reason.Should().NotBeNull().And.Subject.ToString().Should().Contain(mentions);
    }

    [Test]
    public void An_ordinary_process_is_allowed_with_no_warning_at_all()
    {
        var (allowed, reason) = ProcessKiller.CanKill("dotnet");

        allowed.Should().BeTrue();
        reason.Should().BeNull("a warning on everything trains people to ignore warnings");
    }

    // ── Really ending something ─────────────────────────────────────────────────────────────────

    [Test]
    public void A_process_this_test_owns_is_actually_ended()
    {
        Process victim = Spawn();
        victim.HasExited.Should().BeFalse("premise: it is running");

        var (killed, message) = ProcessKiller.Kill(victim.Id, "cmd");

        killed.Should().BeTrue(message);
        victim.WaitForExit(TimeSpan.FromSeconds(10)).Should().BeTrue("it must really be gone");
        message.Should().Contain("child processes", "the whole tree goes, not just the parent");
    }

    [Test]
    public void A_PID_THAT_NOW_BELONGS_TO_SOMETHING_ELSE_IS_NOT_KILLED()
    {
        // ⛔ THE REGRESSION THIS PREVENTS. Pids are recycled fast, and the row the user clicked was
        //    rendered up to a second earlier - long enough for that pid to belong to something
        //    entirely different. Killing by a stale pid is how a monitor ends the wrong program and
        //    nobody can work out why.
        Process bystander = Spawn();

        var (killed, message) = ProcessKiller.Kill(bystander.Id, "definitely-not-cmd");

        killed.Should().BeFalse();
        message.Should().Contain("was reused").And.Contain("nothing was ended");
        bystander.HasExited.Should().BeFalse("the innocent process must still be running");
    }

    [Test]
    public void A_process_that_has_already_exited_is_reported_plainly()
    {
        Process gone = Spawn();
        gone.Kill(entireProcessTree: true);
        gone.WaitForExit(TimeSpan.FromSeconds(10)).Should().BeTrue("premise");
        int deadPid = gone.Id;

        var (killed, message) = ProcessKiller.Kill(deadPid, "cmd");

        killed.Should().BeFalse();
        message.Should().Contain("already exited");
    }

    [Test]
    public void An_id_that_never_existed_is_reported_rather_than_thrown()
    {
        Action kill = () => ProcessKiller.Kill(int.MaxValue, "dotnet");

        kill.Should().NotThrow("a stale row must never crash the app");
        ProcessKiller.Kill(int.MaxValue, "dotnet").Killed.Should().BeFalse();
    }

    [Test]
    public void Killing_reports_the_name_and_pid_it_actually_ended()
    {
        Process victim = Spawn();

        var (_, message) = ProcessKiller.Kill(victim.Id, "cmd");

        message.Should().Contain("cmd").And.Contain(victim.Id.ToString(),
            "the status line is the only record of what just happened");
    }
}
