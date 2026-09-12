using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// What the update dialog actually says.
/// </summary>
/// <remarks>
/// A dialog taller than the screen has no visible buttons, so the notes are truncated. The
/// wording is asserted rather than the dialog, which blocks and would hang the suite.
/// </remarks>
[TestFixture]
public sealed class UpdatePromptMessageTests
{
    private static AvailableUpdate Update(string version = "9.9.9", string? notes = "some notes") =>
        new(version, "https://example.invalid/release", "https://example.invalid/download", notes ?? "");

    // ── The wording ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public void The_message_names_both_versions()
    {
        string message = UpdatePrompt.BuildMessage(Update("9.9.9"));

        message.Should().Contain("9.9.9").And.Contain(AppVersion.Number,
            "'an update is available' is useless without knowing what you are on");
    }

    [Test]
    public void Long_release_notes_are_truncated_so_the_dialog_stays_usable()
    {
        string message = UpdatePrompt.BuildMessage(Update(notes: new string('x', 5000)));

        message.Length.Should().BeLessThan(1000, "a dialog taller than the screen has no buttons");
        message.Should().Contain("...");
    }

    [TestCase("", TestName = "empty notes")]
    [TestCase("   ", TestName = "whitespace notes")]
    public void Blank_notes_produce_no_filler(string notes)
    {
        UpdatePrompt.Truncate(notes, 400).Should().BeEmpty();
    }

    [Test]
    public void Notes_shorter_than_the_limit_are_left_alone()
    {
        UpdatePrompt.Truncate("short", 400).Should().Be("short");
    }

    [Test]
    public void Notes_exactly_at_the_limit_are_left_alone()
    {
        string exact = new('y', 400);

        UpdatePrompt.Truncate(exact, 400).Should().Be(exact, "off by one here adds a stray ellipsis");
    }
}
