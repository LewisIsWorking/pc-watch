using AwesomeAssertions;
using NUnit.Framework;
namespace PcWatch.Tests;

/// <summary>
/// Continued from ProgramTests, split at the repo's 200-line limit.
/// </summary>
[TestFixture]
public sealed class ProgramMonitorArgumentTests
{
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
