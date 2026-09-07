using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Dividing busy time into your programs and the operating system.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-07. THE TRAP THIS ENCODES. GetSystemTimes reports kernelTime with idleTime ALREADY
///    INSIDE IT. Kernel work is therefore (total - idle - user), never the raw kernel figure.
///
///    Using the raw figure does not look broken: it produces a number that is plausible at every
///    load level, so nothing ever looks off enough to check. On an idle machine it would report the
///    kernel as doing almost all the work, which is exactly backwards, and the only symptom would be
///    a percentage that felt slightly odd.
///
/// ⭐ WHY THE SPLIT EXISTS AT ALL. A bare "100%" says there is no headroom without saying what filled
///   it, and the two halves have completely different remedies. User time is programs computing, so
///   the process table names the culprit. Kernel time is the OS working on their behalf - I/O,
///   drivers, antivirus, virtualisation - where closing something may not help at all.
/// </remarks>
[TestFixture]
public sealed class CpuSplitTests
{
    // Ticks are 100 ns units; the absolute scale is irrelevant, only the ratios matter.
    private const long Window = 240_000_000;

    [Test]
    public void A_COMPLETELY_IDLE_MACHINE_HAS_NO_KERNEL_LOAD()
    {
        // ⛔ THE REGRESSION. Read raw, kernelTime equals the whole window here (it contains all the
        //    idle time), so a naive implementation reports 100% kernel on a machine doing nothing.
        CpuSplit split = CpuSampler.SplitOf(totalDelta: Window, idleDelta: Window, userDelta: 0);

        split.UserPercent.Should().Be(0);
        split.KernelPercent.Should().Be(0, "an idle machine is not secretly busy in the kernel");
    }

    [Test]
    public void Pure_user_work_is_all_attributed_to_programs()
    {
        CpuSplit split = CpuSampler.SplitOf(Window, idleDelta: 0, userDelta: Window);

        split.UserPercent.Should().Be(100);
        split.KernelPercent.Should().Be(0);
    }

    [Test]
    public void Pure_kernel_work_is_all_attributed_to_the_operating_system()
    {
        CpuSplit split = CpuSampler.SplitOf(Window, idleDelta: 0, userDelta: 0);

        split.UserPercent.Should().Be(0);
        split.KernelPercent.Should().Be(100);
    }

    [Test]
    public void THE_TWO_SHARES_ADD_UP_TO_THE_HEADLINE_BUSY_FIGURE()
    {
        // ⭐ The invariant that makes the line readable. They are shares of the WHOLE machine, so
        //   together they equal the busy percentage rather than summing to 100 among themselves.
        //   Half idle, and the busy half split evenly.
        CpuSplit split = CpuSampler.SplitOf(Window, idleDelta: Window / 2, userDelta: Window / 4);

        split.UserPercent.Should().Be(25);
        split.KernelPercent.Should().Be(25);
        (split.UserPercent + split.KernelPercent).Should().Be(50, "which is the busy total");
    }

    [Test]
    public void The_measured_real_world_shape_is_reproduced()
    {
        // Measured on this machine at saturation, 2026-09-06: user 52%, kernel 48%, idle 0.
        CpuSplit split = CpuSampler.SplitOf(Window, idleDelta: 0, userDelta: (long)(Window * 0.52));

        split.UserPercent.Should().BeApproximately(52, 0.2);
        split.KernelPercent.Should().BeApproximately(48, 0.2);
        split.KernelHeavy.Should().BeFalse("48 is below 52, narrowly");
    }

    [TestCase(0.50, 0.50, true, TestName = "an even split counts as kernel heavy")]
    [TestCase(0.20, 0.80, true, TestName = "kernel dominating")]
    [TestCase(0.80, 0.20, false, TestName = "programs dominating")]
    public void Kernel_heavy_is_flagged_when_the_os_does_at_least_as_much(
        double userShare, double kernelShare, bool expected)
    {
        // An even split is unusual on a desktop and points at I/O, security software or a
        // hypervisor, so it is worth calling out rather than leaving two numbers to be compared.
        var split = new CpuSplit(userShare * 100, kernelShare * 100);

        split.KernelHeavy.Should().Be(expected);
    }

    [Test]
    public void An_idle_machine_is_never_called_kernel_heavy()
    {
        // Both zero compares as "kernel >= user", which would put an alarming note on a machine
        // doing absolutely nothing.
        new CpuSplit(0, 0).KernelHeavy.Should().BeFalse();
    }

    [Test]
    public void Shares_are_clamped_rather_than_allowed_to_go_negative()
    {
        // Counters are read at slightly different instants and can produce a tick delta that
        // implies more than the window. A negative percentage rendered as "-3% your programs" is
        // worse than a clamped zero.
        CpuSplit split = CpuSampler.SplitOf(Window, idleDelta: Window, userDelta: Window);

        split.UserPercent.Should().BeInRange(0, 100);
        split.KernelPercent.Should().BeInRange(0, 100);
    }

    [Test]
    public void The_split_is_reported_to_one_decimal_place()
    {
        CpuSplit split = CpuSampler.SplitOf(3000, idleDelta: 0, userDelta: 1000);

        split.UserPercent.Should().Be(33.3, "a long trail of digits is noise in a one-line summary");
    }

    // ── In the report ───────────────────────────────────────────────────────────────────────────

    private static Snapshot Snapshot(CpuSplit? split) =>
        new(90, 24, [], new MachineStats(98, 31.5, 63.9,
                new UptimeFacts(TimeSpan.FromHours(30), null, TimeSpan.FromHours(30), false),
                "AMD Ryzen 9 5900X"),
            DateTime.Now, [], [], Cpu: split);

    [Test]
    public void The_report_names_both_halves_in_plain_words()
    {
        string report = ReportRenderer.Render(Snapshot(new CpuSplit(52, 48)), [], new ProcessAncestry());

        report.Should().Contain("52% your programs");
        report.Should().Contain("48% the operating system",
            "'privileged time' means nothing to someone wondering why their PC is slow");
    }

    [Test]
    public void A_kernel_heavy_machine_is_told_where_to_look()
    {
        // ⭐ The actionable half. Knowing the kernel is busy is useless without knowing what puts
        //   work there, because none of it looks like a busy process row.
        string report = ReportRenderer.Render(Snapshot(new CpuSplit(20, 70)), [], new ProcessAncestry());

        report.Should().Contain("kernel-heavy");
        report.Should().Contain("I/O, drivers, antivirus or a VM");
    }

    [Test]
    public void An_ordinary_machine_gets_no_alarming_note()
    {
        string report = ReportRenderer.Render(Snapshot(new CpuSplit(70, 20)), [], new ProcessAncestry());

        report.Should().Contain("70% your programs");
        report.Should().NotContain("kernel-heavy", "a note that always appears is a note nobody reads");
    }

    [Test]
    public void Before_the_first_sample_there_is_no_split_line_at_all()
    {
        // The first tick has no previous reading to difference against, and inventing 0/0 would
        // claim the machine is idle at the one moment nothing is known.
        string report = ReportRenderer.Render(Snapshot(null), [], new ProcessAncestry());

        report.Should().NotContain("your programs");
    }
}
