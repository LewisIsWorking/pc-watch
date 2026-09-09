using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The overall verdict, and which indicator it is actually about.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-09, REPORTED BY LEWIS: "my PC is sitting at like 49% but it still says FLAT OUT".
///    Every individual reading was correct. The CPU really was at 49% and Memory really was the
///    worst performance indicator. What was wrong was putting a SUBJECTLESS VERDICT next to a
///    number: the headline read "49%  CPU     FLAT OUT", and a verdict with no subject gets given
///    one by whoever reads it.
///
/// ⭐ This file exists because SystemHealth had NO unit tests at all - it was exercised only by the
///   in-app self-test, which checked severities and never once checked what the words said.
/// </remarks>
[TestFixture]
public sealed class SystemHealthTests
{
    private static Snapshot Snapshot(
        double? cpu = 20, double ramUsedGb = 20, double ramTotalGb = 64,
        GpuReading? gpu = null, double? freeGb = null, double? totalGb = null)
    {
        var uptime = new UptimeFacts(TimeSpan.FromHours(30), null, TimeSpan.FromHours(30), false);
        var machine = new MachineStats(98, ramUsedGb, ramTotalGb, uptime, "AMD Ryzen 9 5900X");
        return new Snapshot(cpu, 24, [], machine, DateTime.Now, [], [],
            Gpu: gpu, SystemDriveFreeGb: freeGb, SystemDriveTotalGb: totalGb);
    }

    private static (string Word, Severity Severity, string? Driver) Verdict(Snapshot s) =>
        SystemHealth.Overall(SystemHealth.Assess(s));

    // ── The reported bug ────────────────────────────────────────────────────────────────────────

    [Test]
    public void A_CALM_CPU_WITH_FULL_MEMORY_NAMES_MEMORY_NOT_THE_CPU()
    {
        // ⛔ THE EXACT REPORTED CASE. 49% CPU, memory at 91%. Before the fix this graded High and
        //    said only "FLAT OUT", which sat directly under "CPU 49%" and read as self-contradiction.
        var (word, severity, driver) = Verdict(Snapshot(cpu: 49, ramUsedGb: 58.2, ramTotalGb: 64));

        severity.Should().Be(Severity.High, "memory at 91% really is the worst indicator");
        word.Should().Be("FLAT OUT");
        driver.Should().Be("Memory", "WITHOUT THIS the verdict attaches itself to the CPU figure");
    }

    [Test]
    public void The_report_headline_says_WHAT_is_flat_out()
    {
        string report = ReportRenderer.Render(
            Snapshot(cpu: 49, ramUsedGb: 58.2, ramTotalGb: 64), [], new ProcessAncestry());

        report.Should().Contain("FLAT OUT: Memory");

        // ⭐ And the contradiction is gone: the CPU row on the very next line says it is fine.
        report.Should().Contain("plenty of headroom",
            "the CPU at 49% must still read as healthy, right below the verdict");
    }

    [Test]
    public void A_CPU_that_really_IS_flat_out_names_the_CPU()
    {
        // The naming must not simply always say Memory. This is the other half of the same claim.
        var (_, severity, driver) = Verdict(Snapshot(cpu: 97, ramUsedGb: 10, ramTotalGb: 64));

        severity.Should().Be(Severity.High);
        driver.Should().Be("CPU");
    }

    [Test]
    public void A_HOT_GPU_SAYS_RUNNING_HOT_NOT_FLAT_OUT()
    {
        // ⛔ A card at 88 C while only 40% busy is not flat out, and the difference is not pedantry:
        //    "FLAT OUT" tells you to stop giving it work, when what it needs is airflow. Wrong word,
        //    wrong remedy.
        var hot = new GpuReading("RTX 3080", 320, 340, 40, 88);

        var (word, severity, driver) = Verdict(Snapshot(cpu: 20, gpu: hot));

        severity.Should().Be(Severity.High, "84 C and above is throttling territory");
        driver.Should().Be("GPU", "a hot GPU on an idle machine must not read as a busy CPU");
        word.Should().Be("RUNNING HOT");
        word.Should().NotBe("FLAT OUT", "the card is only 40% busy");
    }

    [Test]
    public void A_FULLY_LOADED_but_COOL_GPU_is_only_WORKING_HARD()
    {
        // The other side: 95% busy at 60 C is genuinely load, and must NOT say RUNNING HOT.
        var busy = new GpuReading("RTX 3080", 320, 340, 95, 60);

        var (word, severity, driver) = Verdict(Snapshot(cpu: 20, gpu: busy));

        severity.Should().Be(Severity.Medium);
        word.Should().Be("WORKING HARD", "a cool card under load is not a heat problem");
        driver.Should().Be("GPU");
    }

    [Test]
    public void A_hot_GPU_beats_a_busy_CPU_and_keeps_its_own_word()
    {
        // Both High is impossible here (CPU High needs >= 90), so this pins that the WORD follows
        // the indicator that actually won, not whichever happens to be listed first.
        var hot = new GpuReading("RTX 3080", 320, 340, 40, 90);

        var (word, _, driver) = Verdict(Snapshot(cpu: 70, gpu: hot));

        driver.Should().Be("GPU", "High beats Medium");
        word.Should().Be("RUNNING HOT", "the CPU's WORKING HARD must not overwrite it");
    }

    // ── When there is nothing to name ───────────────────────────────────────────────────────────

    [Test]
    public void A_HEALTHY_MACHINE_NAMES_NOTHING()
    {
        // ⚠️ "HEALTHY: CPU" would imply the OTHER indicators are not healthy, which is the exact
        //    opposite of what a Low worst-case means. Silence is the correct answer here.
        var (word, severity, driver) = Verdict(Snapshot(cpu: 12, ramUsedGb: 8, ramTotalGb: 64));

        word.Should().Be("HEALTHY");
        severity.Should().Be(Severity.Low);
        driver.Should().BeNull();
    }

    [Test]
    public void A_report_on_a_calm_machine_has_no_colon_in_its_verdict()
    {
        string report = ReportRenderer.Render(
            Snapshot(cpu: 12, ramUsedGb: 8, ramTotalGb: 64), [], new ProcessAncestry());

        report.Should().Contain("HOW IT IS RUNNING: HEALTHY   ");
        report.Should().NotContain("HEALTHY: ");
    }

    [Test]
    public void A_middling_machine_is_WORKING_HARD_and_still_says_which_part()
    {
        var (word, severity, driver) = Verdict(Snapshot(cpu: 70, ramUsedGb: 10, ramTotalGb: 64));

        word.Should().Be("WORKING HARD");
        severity.Should().Be(Severity.Medium);
        driver.Should().Be("CPU", "a warning nobody can act on is barely a warning");
    }

    [Test]
    public void Before_the_first_sample_there_is_nothing_to_judge()
    {
        // Memory alone is always available, so the "measuring" case needs every indicator absent.
        var (word, _, driver) = SystemHealth.Overall([]);

        word.Should().Be("measuring");
        driver.Should().BeNull("naming a driver for an unmeasured machine would be inventing one");
    }

    // ── Capacity must never drive the verdict ───────────────────────────────────────────────────

    [Test]
    public void A_NEARLY_FULL_DISK_NEVER_SETS_THE_VERDICT()
    {
        // ⛔ The earlier misjudgement this still guards: a drive at 3% free made a machine running
        //    at 54% CPU with almost no page file in use report as though it were struggling.
        //    Capacity is about LATER; the verdict answers how it is running NOW.
        var (word, severity, driver) = Verdict(
            Snapshot(cpu: 20, ramUsedGb: 10, ramTotalGb: 64, freeGb: 20, totalGb: 1861));

        severity.Should().Be(Severity.Low);
        word.Should().Be("HEALTHY");
        driver.Should().BeNull();
    }

    [Test]
    public void But_the_nearly_full_disk_is_still_reported_as_a_warning()
    {
        // Excluded from the verdict is not the same as hidden.
        IReadOnlyList<HealthIndicator> health = SystemHealth.Assess(
            Snapshot(cpu: 20, ramUsedGb: 10, ramTotalGb: 64, freeGb: 20, totalGb: 1861));

        SystemHealth.Warnings(health).Should().ContainSingle()
            .Which.Name.Should().Be("Disk");
    }

    [Test]
    public void The_worst_indicator_wins_even_when_others_are_calm()
    {
        // Not an average. Two machines both averaging "fine" can be unwell in different ways, and
        // averaging hides the one thing that is actually wrong.
        var hot = new GpuReading("RTX 3080", 320, 340, 40, 90);

        var (_, severity, driver) = Verdict(
            Snapshot(cpu: 5, ramUsedGb: 4, ramTotalGb: 64, gpu: hot));

        severity.Should().Be(Severity.High);
        driver.Should().Be("GPU");
    }
}
