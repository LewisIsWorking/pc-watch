using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Uptime facts, the disagreement note, and the palette's meaning-to-colour mapping.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-06. The uptime half is the bug that started this project's second act: the app said
///    "up 18.8 days" for a machine that had been on about a day and a half, and Lewis knew it was
///    wrong. GetTickCount64 and WMI LastBootUpTime BOTH said 18.8 days and agreed with each other
///    perfectly while both being useless, because Fast Startup hibernates the kernel session rather
///    than stopping it, so neither counter resets on shutdown.
///
/// ⭐ TWO SOURCES AGREEING IS NOT EVIDENCE THEY ARE RIGHT. They were answering "how long since a
///   full boot", which is a different question from the one being asked.
/// </remarks>
[TestFixture]
public sealed class UptimeAndThemeTests
{
    private static UptimeFacts Facts(double? onForHours, double kernelHours, bool fastStartup = true) =>
        new(onForHours is { } h ? TimeSpan.FromHours(h) : null,
            onForHours is { } o ? DateTime.Now.AddHours(-o) : null,
            TimeSpan.FromHours(kernelHours),
            fastStartup);

    // ── Which figure a person means ─────────────────────────────────────────────────────────────

    [Test]
    public void The_real_power_on_time_is_preferred_over_the_kernel_counter()
    {
        Facts(onForHours: 36, kernelHours: 451).Best.Should().Be(TimeSpan.FromHours(36),
            "'how long has my PC been on' is not 'how long since a full boot'");
    }

    [Test]
    public void With_no_event_log_answer_the_kernel_counter_is_all_there_is()
    {
        Facts(onForHours: null, kernelHours: 40).Best.Should().Be(TimeSpan.FromHours(40),
            "a degraded answer beats no answer");
    }

    // ── Detecting the disagreement ──────────────────────────────────────────────────────────────

    [Test]
    public void A_kernel_counter_far_ahead_of_the_power_on_time_is_flagged()
    {
        // The real case: 18.8 days of kernel counter against about 36 hours actually on.
        Facts(onForHours: 36, kernelHours: 451).CountersDisagree.Should().BeTrue();
    }

    [TestCase(0.0, TestName = "identical")]
    [TestCase(5.9, TestName = "just inside the six hour tolerance")]
    [TestCase(6.0, TestName = "exactly six hours is still tolerated")]
    public void A_small_gap_is_not_a_disagreement(double extraHours)
    {
        // ⚠️ Some gap is normal and meaningless. Flagging it would put an explanatory note on every
        //    machine every day, and a note that always appears is a note nobody reads.
        Facts(onForHours: 30, kernelHours: 30 + extraHours).CountersDisagree.Should().BeFalse();
    }

    [Test]
    public void Just_over_six_hours_IS_a_disagreement()
    {
        Facts(onForHours: 30, kernelHours: 36.1).CountersDisagree.Should().BeTrue();
    }

    [Test]
    public void With_no_power_on_time_there_is_nothing_to_disagree_with()
    {
        Facts(onForHours: null, kernelHours: 451).CountersDisagree.Should().BeFalse(
            "one figure cannot contradict itself");
    }

    // ── The note ────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Agreeing_counters_produce_no_note()
    {
        SystemUptime.DisagreementNote(Facts(onForHours: 30, kernelHours: 31)).Should().BeNull(
            "an explanation nobody needs is noise");
    }

    [Test]
    public void The_note_gives_BOTH_figures_so_the_reader_can_match_them_to_other_tools()
    {
        // ⭐ The whole job: not just to be right, but to say why the rival figure is what it is.
        //    Without both numbers the note cannot be reconciled with what Task Manager shows.
        string? note = SystemUptime.DisagreementNote(Facts(onForHours: 36, kernelHours: 451));

        note.Should().NotBeNull();
        note!.Should().Contain("18.8 days", "the kernel counter, as other tools report it");
        note.Should().Contain("1d 12h", "and the real answer");
        note.Should().Contain("Task Manager", "so the reader knows which tool they are reconciling");
    }

    [Test]
    public void With_FAST_STARTUP_ON_the_note_names_it_as_the_cause()
    {
        string? note = SystemUptime.DisagreementNote(Facts(36, 451, fastStartup: true));

        note.Should().Contain("Fast Startup is ON");
        note.Should().Contain("hibernates the kernel session",
            "naming the mechanism is what makes the number believable");
    }

    [Test]
    public void With_FAST_STARTUP_OFF_the_note_blames_sleep_instead()
    {
        // ⚠️ A different cause, and saying the wrong one is worse than saying nothing: the reader
        //    would go and check a setting that is already off.
        string? note = SystemUptime.DisagreementNote(Facts(36, 451, fastStartup: false));

        note.Should().Contain("resumed from sleep");
        note.Should().NotContain("Fast Startup is ON");
    }

    // ── The palette ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public void NOT_MEASURED_IS_NOT_THE_SAME_AS_ZERO()
    {
        // ⛔ The distinction the whole app rests on. Painting an unmeasured reading in the calm
        //    colour claims the machine is idle at the one moment nothing is known about it.
        Theme.ForLoad(null).Should().Be(Theme.Unknown);
        Theme.ForLoad(null).Should().NotBe(Theme.Low, "'no idea' must not look like 'all fine'");
    }

    [TestCase(0.0)]
    [TestCase(49.9)]
    public void A_light_load_is_calm(double percent) => Theme.ForLoad(percent).Should().Be(Theme.Low);

    [TestCase(50.0)]
    [TestCase(79.9)]
    public void A_moderate_load_is_amber(double percent) =>
        Theme.ForLoad(percent).Should().Be(Theme.Medium);

    [TestCase(80.0)]
    [TestCase(100.0)]
    [TestCase(140.0, TestName = "above 100, which a summed per-process figure can reach")]
    public void A_heavy_load_is_alarming(double percent) =>
        Theme.ForLoad(percent).Should().Be(Theme.High);

    [TestCase(Severity.High)]
    [TestCase(Severity.Medium)]
    [TestCase(Severity.Low)]
    public void Every_severity_maps_to_a_distinct_colour(Severity severity)
    {
        Color colour = Theme.ForSeverity(severity);

        colour.Should().NotBe(Color.Empty);
        new[] { Theme.High, Theme.Medium, Theme.Low }.Should().Contain(colour);
    }

    [Test]
    public void The_three_severity_colours_are_actually_different()
    {
        // A palette where two severities render identically silently removes a distinction the
        // caller went to the trouble of making.
        new[] { Theme.ForSeverity(Severity.High), Theme.ForSeverity(Severity.Medium), Theme.ForSeverity(Severity.Low) }
            .Distinct().Should().HaveCount(3);
    }
}
