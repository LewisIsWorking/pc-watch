using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Clock steps, and when on-for running ahead of the kernel counter is explained by them.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-17. Numbers below are the real ones from the dev VM: on-for 12.5 min ahead of the kernel
///    count, explained exactly by a logged forward step of 751.3 s after a VM stall.
/// </remarks>
[TestFixture]
public sealed class ClockStepsTests
{
    private static readonly TimeSpan Slack = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Kernel = TimeSpan.FromHours(10.54);

    [Test]
    public void THE_MEASURED_VM_STALL_IS_EXPLAINED_BY_ITS_LOGGED_CLOCK_STEP()
    {
        TimeSpan onFor = Kernel + TimeSpan.FromMinutes(12.5);
        TimeSpan step = TimeSpan.FromSeconds(751.3);

        ClockSteps.OnForIsExplained(onFor, Kernel, step, Slack).Should().BeTrue(
            "both counters were right; the wall clock was corrected forward after the VM stalled");
    }

    [Test]
    public void THE_SAME_GAP_WITH_NO_CLOCK_STEP_IS_STILL_A_FAILURE()
    {
        // This is exactly what the old check did for every gap past 5 minutes. It stays true when
        // nothing in the log explains the gap.
        ClockSteps.OnForIsExplained(Kernel + TimeSpan.FromMinutes(12.5), Kernel, TimeSpan.Zero, Slack)
            .Should().BeFalse();
    }

    [Test]
    public void AN_OLDER_BOOT_EVENT_READ_BY_MISTAKE_IS_STILL_CAUGHT()
    {
        // ⛔ The bug the check exists for: a boot event from days before the real one. A clock step of
        //    a few minutes must not excuse a gap of days.
        ClockSteps.OnForIsExplained(Kernel + TimeSpan.FromDays(3), Kernel, TimeSpan.FromMinutes(12.5), Slack)
            .Should().BeFalse();
    }

    [Test]
    public void Small_differences_stay_within_the_existing_slack()
    {
        ClockSteps.OnForIsExplained(Kernel + TimeSpan.FromMinutes(2), Kernel, TimeSpan.Zero, Slack)
            .Should().BeTrue("boot event timing and tick granularity differ by seconds, not a real fault");
    }

    [Test]
    public void ONLY_FORWARD_STEPS_COUNT()
    {
        // A backward correction shortens the wall-clock distance to boot. Netting it off would let it
        // cancel the forward step that actually explains the gap.
        var steps = new[]
        {
            (Old: new DateTime(2026, 9, 17, 0, 45, 27, DateTimeKind.Utc), New: new DateTime(2026, 9, 17, 0, 57, 58, DateTimeKind.Utc)),
            (Old: new DateTime(2026, 9, 17, 2, 0, 0, DateTimeKind.Utc), New: new DateTime(2026, 9, 17, 1, 50, 0, DateTimeKind.Utc)),
        };

        ClockSteps.SumForward(steps).Should().Be(TimeSpan.FromSeconds(751));
    }

    [Test]
    public void No_steps_sum_to_zero()
    {
        ClockSteps.SumForward([]).Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void The_real_event_xml_shape_is_parsed()
    {
        // Shape of the Kernel-General 1 record read on the dev VM, 2026-09-17.
        const string xml = """
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <System><Provider Name="Microsoft-Windows-Kernel-General"/><EventID>1</EventID></System>
              <EventData>
                <Data Name="NewTime">2026-09-17T00:57:58.9836000Z</Data>
                <Data Name="OldTime">2026-09-17T00:45:27.7182000Z</Data>
                <Data Name="Reason">1</Data>
              </EventData>
            </Event>
            """;

        var step = ClockSteps.Parse(xml);

        step.Should().NotBeNull();
        (step!.Value.New - step.Value.Old).TotalSeconds.Should().BeApproximately(751.3, 0.1);
    }

    [Test]
    public void An_event_missing_either_time_is_ignored_rather_than_guessed()
    {
        const string xml = """
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <EventData><Data Name="NewTime">2026-09-17T00:57:58Z</Data></EventData>
            </Event>
            """;

        ClockSteps.Parse(xml).Should().BeNull();
    }

    [Test]
    public void Reading_the_live_log_never_throws()
    {
        Func<TimeSpan?> read = () => ClockSteps.ForwardSince(DateTime.Now.AddDays(-1));

        read.Should().NotThrow();
        (read() ?? TimeSpan.Zero).Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }
}
