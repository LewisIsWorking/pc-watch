using AwesomeAssertions;
using NUnit.Framework;
namespace PcWatch.Tests;

/// <summary>
/// Continued from ReportRendererTests, split at the repo's 200-line limit.
/// </summary>
[TestFixture]
public sealed class ReportTextTests
{
    // ── Age and Wrap ────────────────────────────────────────────────────────────────────────────

    [TestCase(0, 0, 45, "0m", TestName = "under a minute rounds DOWN to 0m, deliberately")]
    [TestCase(0, 5, 0, "5m")]
    [TestCase(0, 59, 59, "59m", TestName = "just under an hour stays in minutes")]
    [TestCase(1, 0, 0, "1h 0m", TestName = "exactly an hour switches units")]
    [TestCase(3, 20, 0, "3h 20m")]
    [TestCase(23, 59, 0, "23h 59m", TestName = "just under a day stays in hours")]
    [TestCase(50, 0, 0, "2d 2h")]
    public void Age_gets_coarser_the_older_it_gets(int hours, int minutes, int seconds, string expected)
    {
        // ⭐ Minutes is the FINEST unit on purpose. Everything this renders has been alive long
        //   enough to matter, and seconds would only add noise to a column read at a glance. A
        //   45-second process showing "0m" is the coarseness working, not a rounding bug.
        ReportRenderer.Age(new TimeSpan(0, hours, minutes, seconds)).Should().Be(expected);
    }

    [Test]
    public void Wrapping_never_exceeds_the_width()
    {
        string text = string.Join(" ", Enumerable.Repeat("wordy", 60));

        IReadOnlyList<string> lines = ReportRenderer.Wrap(text, 40);

        lines.Should().NotBeEmpty();
        lines.Should().AllSatisfy(l => l.Length.Should().BeLessThanOrEqualTo(40));
    }

    [Test]
    public void Wrapping_preserves_every_word()
    {
        string text = "the quick brown fox jumps over the lazy dog";

        string rejoined = string.Join(" ", ReportRenderer.Wrap(text, 12));

        rejoined.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().Equal(text.Split(' '), "wrapping must not lose or reorder words");
    }

    [Test]
    public void A_word_longer_than_the_width_is_not_dropped()
    {
        string monster = new('x', 100);

        ReportRenderer.Wrap(monster, 20).Should().NotBeEmpty("a long path must still appear");
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Wrapping_blank_text_does_not_throw(string text)
    {
        Func<IReadOnlyList<string>> wrap = () => ReportRenderer.Wrap(text, 40);

        wrap.Should().NotThrow();
    }
}
