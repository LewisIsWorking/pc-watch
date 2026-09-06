using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The report, rendered from a fully synthetic snapshot.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-06. Written alongside splitting a 219-line ReportRenderer into per-section methods.
///    "The tests still pass" is a weaker claim than "the output is the same", so the SECTION ORDER
///    is pinned here explicitly - it is the one property the refactor could plausibly have broken
///    while every existing test carried on passing.
///
/// ⭐ THE ORDER IS NOT COSMETIC. Findings come before the process table because the table is
///   open-ended and the findings are not: if the window clips anything it must be the tail of an
///   already-sorted list rather than the diagnosis. Four attempts to compute a row count that
///   "just fits" all produced a confident number and a clipped window, the last one silently.
/// </remarks>
[TestFixture]
public sealed class ReportRendererTests
{
    private static Snapshot Snapshot(
        double? cpu = 42,
        IReadOnlyList<ProcessGroup>? groups = null,
        PowerReport? power = null,
        DateTime? onSince = null)
    {
        var uptime = new UptimeFacts(TimeSpan.FromHours(30), onSince, TimeSpan.FromHours(30), false);
        var machine = new MachineStats(98, 31.5, 63.9, uptime, "AMD Ryzen 9 5900X");

        ProcessLoad[] top =
        [
            new("dotnet", 4321, 12.5, 512L * 1024 * 1024, DateTime.Now.AddHours(-2), 2),
            new("a-very-long-process-name-that-must-be-truncated", 99, 3.2,
                128L * 1024 * 1024, null, 0),
        ];

        return new Snapshot(cpu, 24, top, machine, new DateTime(2026, 9, 6, 14, 30, 0),
            groups ?? [], [], Gpu: null, Power: power);
    }

    private static string Render(Snapshot snapshot, IReadOnlyList<Suspect>? suspects = null) =>
        ReportRenderer.Render(snapshot, suspects ?? [], new ProcessAncestry());

    [Test]
    public void FINDINGS_COME_BEFORE_THE_PROCESS_TABLE()
    {
        // ⛔ The property the whole layout depends on. If this inverts, a maximised window pushes
        //    the diagnosis off the bottom and the app silently stops answering its own question.
        string report = Render(Snapshot(), [new Suspect(Severity.High, 4321, "dotnet is busy", "detail")]);

        int findings = report.IndexOf("WHAT LOOKS WRONG", StringComparison.Ordinal);
        int table = report.IndexOf("TOP PROCESSES", StringComparison.Ordinal);

        findings.Should().BePositive("the findings section must be present");
        table.Should().BePositive("the process table must be present");
        findings.Should().BeLessThan(table, "the diagnosis must never be the part that gets clipped");
    }

    [Test]
    public void Every_section_appears_in_the_documented_order()
    {
        var power = new PowerReport(105, 220, 340, "load model");
        var groups = new[] { new ProcessGroup("dotnet", 56, 18.4, 5600) };
        string report = Render(Snapshot(groups: groups, power: power),
            [new Suspect(Severity.High, 4321, "busy", "detail")]);

        int[] positions =
        [
            report.IndexOf("CPU ", StringComparison.Ordinal),
            report.IndexOf("HOW IT IS RUNNING", StringComparison.Ordinal),
            report.IndexOf("POWER", StringComparison.Ordinal),
            report.IndexOf("BY PROGRAM", StringComparison.Ordinal),
            report.IndexOf("WHAT LOOKS WRONG", StringComparison.Ordinal),
            report.IndexOf("TOP PROCESSES", StringComparison.Ordinal),
        ];

        positions.Should().AllSatisfy(p => p.Should().BePositive("every section must be present"));
        positions.Should().BeInAscendingOrder("the order is load-bearing, not cosmetic");
    }

    [Test]
    public void The_header_reports_cores_ram_and_the_version()
    {
        string report = Render(Snapshot());

        report.Should().Contain("across 24 logical cores");
        report.Should().Contain("31.5 / 63.9 GB");
        report.Should().Contain(AppVersion.Number, "the reader must know which build produced this");
    }

    [Test]
    public void An_unmeasured_cpu_says_so_rather_than_showing_zero()
    {
        // ⭐ A confident 0% before the first sample is a lie at the moment the user decides whether
        //   to trust the tool at all.
        Render(Snapshot(cpu: null)).Should().Contain("measuring...");
    }

    [Test]
    public void A_known_boot_time_is_shown_and_an_unknown_one_says_kernel_counter()
    {
        Render(Snapshot(onSince: new DateTime(2026, 9, 5, 8, 0, 0))).Should().Contain("since");
        Render(Snapshot(onSince: null)).Should().Contain("kernel counter",
            "where the figure came from matters when two tools disagree about uptime");
    }

    [Test]
    public void POWER_LABELS_MEASURED_AND_ESTIMATED_DIFFERENTLY()
    {
        // ⚠️ GPU watts come from NVML and are measured. AMD package power needs a kernel driver, so
        //    the CPU figure is a model. Presenting them as the same kind of quantity is the mistake.
        string report = Render(Snapshot(power: new PowerReport(105, 220, 340, "load model")));

        report.Should().Contain("MEASURED via NVML");
        report.Should().Contain("ESTIMATE (load model)");
    }

    [Test]
    public void No_power_data_means_no_power_section_at_all()
    {
        Render(Snapshot(power: null)).Should().NotContain("MEASURED via NVML");
    }

    [Test]
    public void A_gpu_with_no_enforced_limit_omits_the_limit_clause()
    {
        string report = Render(Snapshot(power: new PowerReport(null, 220, 0, "n/a")));

        report.Should().Contain("220 W");
        report.Should().NotContain("of 0 W limit", "an absent limit must not render as zero");
    }

    [Test]
    public void Grouped_programs_are_shown_with_their_count()
    {
        // ⭐ Without this, 56 build workers at 0.2% each vanish below every top-N list while
        //   together outweighing everything above them.
        string report = Render(Snapshot(groups: [new ProcessGroup("dotnet", 56, 18.4, 5600)]));

        report.Should().Contain("BY PROGRAM").And.Contain("x56");
    }

    [Test]
    public void No_groups_means_no_by_program_section()
    {
        Render(Snapshot(groups: [])).Should().NotContain("BY PROGRAM");
    }

    [Test]
    public void A_long_process_name_is_truncated_so_the_columns_stay_aligned()
    {
        // The table is fixed-width and word wrap is off in the control, so an over-long name would
        // shunt every following column out of line for that row only.
        string report = Render(Snapshot());

        report.Should().NotContain("a-very-long-process-name-that-must-be-truncated");
        report.Should().Contain("a-very-long-process-name");
    }

    [Test]
    public void A_process_with_no_start_time_shows_a_dash_for_age()
    {
        Render(Snapshot()).Should().Contain("up -", "an unknown age must not render as zero");
    }

    [Test]
    public void The_coverage_line_warns_when_the_list_explains_little_of_the_load()
    {
        // ⛔ Measured at 91% CPU with the top twelve summing to 47%: the list named a sixth of the
        //    problem while reading as a complete account. A list with no coverage figure cannot be
        //    told apart from a full one.
        string report = Render(Snapshot(cpu: 90));

        report.Should().Contain("account for");
        report.Should().Contain("Most load is spread below the cutoff.");
    }

    [Test]
    public void A_list_that_explains_most_of_the_load_gets_no_warning()
    {
        string report = Render(Snapshot(cpu: 16));

        report.Should().Contain("account for");
        report.Should().NotContain("Most load is spread below the cutoff.");
    }
}
