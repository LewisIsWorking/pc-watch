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
public sealed class ProgramTests
{
    private string _realPath = string.Empty;
    private string _temp = string.Empty;

    [SetUp]
    public void RedirectSettings()
    {
        _realPath = SettingsStore.Path;
        _temp = Path.Combine(Path.GetTempPath(), $"pcwatch-args-{Guid.NewGuid():N}", "settings.json");
        SettingsStore.Path = _temp;
    }

    [TearDown]
    public void RestoreSettings()
    {
        SettingsStore.Path = _realPath;
        string? dir = Path.GetDirectoryName(_temp);
        if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

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

    // ── --monitor, both spellings ───────────────────────────────────────────────────────────────

    [Test]
    public void A_separated_value_is_read()
    {
        Program.ArgumentValue(["--monitor", "right"], "--monitor").Should().Be("right");
    }

    [Test]
    public void An_equals_value_is_read()
    {
        Program.ArgumentValue(["--monitor=right"], "--monitor").Should().Be("right");
    }

    [Test]
    public void An_equals_value_is_case_insensitive_on_the_NAME_but_not_the_value()
    {
        Program.ArgumentValue(["--MONITOR=Right"], "--monitor")
            .Should().Be("Right", "the flag name is ours, the value is the user's");
    }

    [Test]
    public void A_value_containing_an_equals_sign_survives_intact()
    {
        Program.ArgumentValue(["--monitor=a=b"], "--monitor")
            .Should().Be("a=b", "only the FIRST equals separates name from value");
    }

    [Test]
    public void An_empty_equals_value_is_empty_rather_than_missing()
    {
        Program.ArgumentValue(["--monitor="], "--monitor").Should().BeEmpty();
    }

    [Test]
    public void A_TRAILING_FLAG_WITH_NO_VALUE_returns_null_rather_than_reading_past_the_end()
    {
        // ⛔ The obvious way to write this parser indexes args[i + 1] unguarded and throws
        //    IndexOutOfRange, which crashes the app at startup for a typo on the command line.
        Program.ArgumentValue(["--monitor"], "--monitor").Should().BeNull();
    }

    [Test]
    public void The_first_occurrence_wins()
    {
        Program.ArgumentValue(["--monitor", "left", "--monitor", "right"], "--monitor")
            .Should().Be("left");
    }

    [Test]
    public void A_different_flag_is_not_confused_for_this_one()
    {
        Program.ArgumentValue(["--no-update-check", "--monitor", "right"], "--monitor")
            .Should().Be("right");
    }

    [Test]
    public void The_flag_is_found_when_it_is_not_first()
    {
        Program.ArgumentValue(["--self-test", "--monitor", "2"], "--monitor").Should().Be("2");
    }

    [Test]
    public void An_absent_flag_returns_null()
    {
        Program.ArgumentValue(["--self-test"], "--monitor").Should().BeNull();
    }

    [Test]
    public void A_value_that_looks_like_another_flag_is_still_taken()
    {
        // Deliberate: "--monitor --self-test" is a user error, and taking it literally produces a
        // refused monitor name rather than silently running the self test.
        Program.ArgumentValue(["--monitor", "--self-test"], "--monitor").Should().Be("--self-test");
    }

    [Test]
    public void Every_documented_monitor_value_survives_a_round_trip()
    {
        // Ties the parser to the thing that consumes it: anything ArgumentValue returns must be
        // something WindowPlacement can actually resolve.
        foreach (string value in new[] { "left", "right", "primary", "first", "last", "1", "2" })
        {
            string? parsed = Program.ArgumentValue([$"--monitor={value}"], "--monitor");
            parsed.Should().Be(value);

            if (value != "primary")
            {
                WindowPlacement.SelectMonitorIndex(parsed!, screenCount: 2)
                    .Should().NotBeNull($"'{value}' is documented, so it must resolve");
            }
        }
    }
}
