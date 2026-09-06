using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The command line: which flags are recognised, and what they do.
/// </summary>
/// <remarks>
/// 2026-09-06. Main itself ends in Application.Run, which blocks for the life of the app, so it
/// cannot be called from a test at all. The decisions it makes were extracted so the parsing - which
/// has real edge cases and no UI - can be exercised directly.
///
/// ⚠️ SettingsStore.Path is redirected, because the update opt-out is deliberately PERMANENT: it
///    writes to settings rather than applying to one run, so testing it touches disk.
/// </remarks>
[TestFixture]
public sealed class ProgramTests : SettingsRedirectFixture
{


    // ── Flags ───────────────────────────────────────────────────────────────────────────────────

    [TestCase("--self-test")]
    [TestCase("--SELF-TEST")]
    [TestCase("--Self-Test")]
    public void The_self_test_flag_is_case_insensitive(string flag)
    {
        Program.WantsSelfTest([flag]).Should().BeTrue();
    }

    [Test]
    public void The_self_test_flag_is_found_among_others()
    {
        Program.WantsSelfTest(["--monitor", "right", "--self-test"]).Should().BeTrue();
    }

    [TestCase("--selftest", TestName = "missing hyphen")]
    [TestCase("-self-test", TestName = "single leading hyphen")]
    [TestCase("self-test", TestName = "no hyphens")]
    [TestCase("--self-test-please", TestName = "longer flag that merely starts the same")]
    public void A_near_miss_does_NOT_trigger_the_self_test(string flag)
    {
        // ⚠️ Exact match, not StartsWith. A prefix match would make "--self-test-please" run the
        //    test suite instead of the app, and the user would see a console and no window.
        Program.WantsSelfTest([flag]).Should().BeFalse();
    }

    [Test]
    public void No_arguments_means_run_the_app()
    {
        Program.WantsSelfTest([]).Should().BeFalse();
        Program.WantsNoUpdateCheck([]).Should().BeFalse();
        Program.ArgumentValue([], "--monitor").Should().BeNull();
    }

    [TestCase("--no-update-check")]
    [TestCase("--NO-UPDATE-CHECK")]
    public void The_update_opt_out_flag_is_case_insensitive(string flag)
    {
        Program.WantsNoUpdateCheck([flag]).Should().BeTrue();
    }

    [Test]
    public void The_opt_out_is_PERMANENT_and_written_to_settings()
    {
        // ⭐ Not for this run only. An opt-out you have to remember on every launch is a chore, and
        //   a chore that is forgotten once has already sent the request it was meant to prevent.
        SettingsStore.Load().CheckForUpdates.Should().BeTrue("premise: on by default");

        Program.DisableUpdateChecks();

        SettingsStore.Load().CheckForUpdates.Should().BeFalse("it must survive the next launch");
    }

    [Test]
    public void The_opt_out_preserves_everything_else_in_settings()
    {
        // A read-modify-write that dropped the saved placement would move the user's window as a
        // side effect of turning off update checks.
        SettingsStore.Save(new Settings
        {
            HasPlacement = true, X = 100, Y = 200, Width = 900, Height = 700, Maximized = false,
        });

        Program.DisableUpdateChecks();

        Settings after = SettingsStore.Load();
        after.CheckForUpdates.Should().BeFalse();
        after.HasPlacement.Should().BeTrue();
        after.Width.Should().Be(900, "the window position must not be collateral damage");
    }
}
