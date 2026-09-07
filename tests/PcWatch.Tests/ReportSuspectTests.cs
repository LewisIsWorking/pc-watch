using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The findings section, and the ownership line that must precede any advice.
/// </summary>
/// <remarks>
/// ⛔ OWNERSHIP BEFORE ADVICE. A 16% emulator driven by a live agent session and an abandoned one
///    look identical from the load figure alone; only one of them is safe to close. This app once
///    advised closing an emulator that turned out to belong to a running session, which is the
///    reason the owner line exists at all.
/// </remarks>
[TestFixture]
public sealed class ReportSuspectTests
{
    private static Snapshot Snapshot(IReadOnlyList<ProcessLoad>? top = null) =>
        new(42, 24, top ?? [], new MachineStats(98, 31.5, 63.9,
                new UptimeFacts(TimeSpan.FromHours(30), null, TimeSpan.FromHours(30), false),
                "AMD Ryzen 9 5900X"),
            DateTime.Now, [], []);

    private static string Render(params Suspect[] suspects) =>
        ReportRenderer.Render(Snapshot(), suspects, new ProcessAncestry());

    [Test]
    public void A_suspect_with_a_RESOLVABLE_pid_gets_an_ownership_line()
    {
        // This test's own process definitely resolves, so the ancestry walk has something to find.
        string report = Render(new Suspect(Severity.High, Environment.ProcessId, "busy thing", "detail"));

        report.Should().Contain("busy thing");
        report.Should().Contain("launched by",
            "the owner is what tells you whether closing it is safe");
    }

    [Test]
    public void A_suspect_with_NO_pid_still_renders_its_finding()
    {
        // Not every finding is about one process. A machine-wide observation has no owner, and must
        // not be dropped for lacking one.
        string report = Render(new Suspect(Severity.Medium, null, "memory is tight", "detail here"));

        report.Should().Contain("memory is tight").And.Contain("detail here");
        report.Should().NotContain("launched by");
    }

    [Test]
    public void A_suspect_whose_pid_has_since_EXITED_still_renders()
    {
        // The snapshot is a moment old by the time it is rendered. A process that has gone must
        // degrade to no owner line, never to a crash or an invented one.
        string report = Render(new Suspect(Severity.High, int.MaxValue, "gone already", "detail"));

        report.Should().Contain("gone already");
        report.Should().NotContain("launched by");
    }

    [Test]
    public void Long_finding_detail_is_wrapped_rather_than_running_off_the_edge()
    {
        // Word wrap is off in the report control, so anything not hand-wrapped here is simply lost
        // beyond the right edge of the window.
        string detail = string.Join(" ", Enumerable.Repeat("explanation", 40));

        string[] lines = Render(new Suspect(Severity.Low, null, "wordy", detail))
            .Split('\n')
            .Where(l => l.Contains("explanation"))
            .ToArray();

        lines.Should().HaveCountGreaterThan(1, "it must be broken across lines");
        lines.Should().AllSatisfy(l => l.TrimEnd().Length.Should().BeLessThanOrEqualTo(80));
    }

    [Test]
    public void Several_findings_all_appear_in_order()
    {
        string report = Render(
            new Suspect(Severity.High, null, "first finding", "a"),
            new Suspect(Severity.Medium, null, "second finding", "b"));

        report.IndexOf("first finding", StringComparison.Ordinal)
            .Should().BeLessThan(report.IndexOf("second finding", StringComparison.Ordinal),
                "the analyzer sorts by severity, and rendering must not reorder them");
    }

    [Test]
    public void With_NO_findings_the_heading_still_appears()
    {
        // ⭐ An empty findings section is itself information: it says the app looked and found
        //   nothing, which is different from the app not having looked.
        Render().Should().Contain("WHAT LOOKS WRONG");
    }

    [Test]
    public void A_process_name_at_exactly_the_column_width_is_not_truncated()
    {
        // Off by one here either clips a legitimate name or lets one shunt the columns out of line.
        string exactly24 = new('n', 24);
        var top = new ProcessLoad[] { new(exactly24, 1, 5, 1024 * 1024, DateTime.Now.AddHours(-1), 1) };

        string report = ReportRenderer.Render(Snapshot(top), [], new ProcessAncestry());

        report.Should().Contain(exactly24);
    }
}
