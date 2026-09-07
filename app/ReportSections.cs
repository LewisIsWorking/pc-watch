using System.Text;

namespace PcWatch;

/// <summary>
/// The individual sections of the report, in the order they are emitted.
/// </summary>
/// <remarks>
/// 2026-09-06. Split out of ReportRenderer, which had reached 219 lines against a hard 200-line
/// limit. Extracted rather than trimmed: Render was one long method that produced seven distinct
/// sections, so each is now separately readable and separately testable, and Render is the
/// coordinator that says what order they come in.
///
/// ⛔ THE ORDER IS LOAD-BEARING AND LIVES IN Render, NOT HERE. Findings come before the process
///    table because the table is open-ended and the findings are not: if anything is cut off it
///    must be the tail of a sorted list rather than the diagnosis. Four attempts to compute a row
///    count that "just fits" all produced a confident number and a clipped window.
/// </remarks>
internal static class ReportSections
{
    /// <summary>The four facts every reading starts with.</summary>
    public static void AppendHeader(StringBuilder sb, Snapshot snapshot)
    {
        string cpu = snapshot.TotalCpuPercent is { } v ? $"{v:N0}%" : "measuring...";
        sb.AppendLine($"  CPU   {cpu}   across {snapshot.LogicalCores} logical cores");

        // ⭐ THE SPLIT, because a bare total says there is no headroom without saying what filled it.
        //   User time is your programs computing; kernel time is the OS working on their behalf -
        //   I/O, drivers, antivirus, virtualisation. Same number, different remedy.
        if (snapshot.Cpu is { } split)
        {
            string note = split.KernelHeavy
                ? "   <- kernel-heavy: I/O, drivers, antivirus or a VM, not raw computation"
                : "";
            sb.AppendLine($"        {split.UserPercent:N0}% your programs, "
                        + $"{split.KernelPercent:N0}% the operating system{note}");
        }
        sb.AppendLine($"  RAM   {snapshot.Machine.RamUsedGb:N1} / {snapshot.Machine.RamTotalGb:N1} GB  ({snapshot.Machine.RamPercent:N0}%)");

        UptimeFacts up = snapshot.Machine.Uptime;
        string since = up.OnSince is { } onSince ? $"  (since {onSince:ddd d MMM HH:mm})" : "  (kernel counter)";
        sb.AppendLine($"  ON    {ReportRenderer.Age(up.Best)}{since}");
        sb.AppendLine($"  AT    {snapshot.TakenAt:HH:mm:ss}   PC Watch {AppVersion.Display}");
        sb.AppendLine();
    }

    /// <summary>
    /// Notes explaining a number some OTHER tool is showing.
    /// </summary>
    /// <remarks>
    /// ⭐ That is the whole job of this app: not just to be right, but to say why the rival figure
    ///   is what it is. A user looking at Task Manager and at this app wants the disagreement
    ///   explained, not arbitrated silently.
    /// </remarks>
    public static void AppendNotes(StringBuilder sb, Snapshot snapshot)
    {
        string?[] notes =
        [
            SuspectAnalyzer.FrequencyNote(snapshot),
            SystemUptime.DisagreementNote(snapshot.Machine.Uptime),
        ];

        foreach (string? note in notes)
        {
            if (note is null) continue;
            foreach (string line in ReportRenderer.Wrap(note, 76))
            {
                sb.AppendLine($"  ! {line}");
            }
            sb.AppendLine();
        }
    }

    /// <summary>
    /// Named indicators, never one composite score.
    /// </summary>
    /// <remarks>
    /// ⚠️ Two machines both scoring "72" can be unwell in completely different ways, and nobody can
    ///    act on a 72. Capacity is reported separately and never sets the verdict: a drive at 3%
    ///    free is a real problem about LATER, not the machine struggling NOW.
    /// </remarks>
    public static void AppendHealth(StringBuilder sb, Snapshot snapshot)
    {
        IReadOnlyList<HealthIndicator> health = SystemHealth.Assess(snapshot);
        if (health.Count == 0) return;

        var (word, _) = SystemHealth.Overall(health);
        sb.AppendLine($"  HOW IT IS RUNNING: {word}   (worst PERFORMANCE indicator, not an average)");
        foreach (HealthIndicator h in health.Where(h => h.Kind == IndicatorKind.Performance))
        {
            sb.AppendLine($"   {h.Name,-7} {h.Value,-34} {h.Verdict}");
        }
        sb.AppendLine();

        IReadOnlyList<HealthIndicator> warnings = SystemHealth.Warnings(health);
        if (warnings.Count == 0) return;

        sb.AppendLine("  WORTH FIXING SOON   (not slowing you down yet)");
        foreach (HealthIndicator w in warnings)
        {
            sb.AppendLine($"   {w.Name,-7} {w.Value,-34} {w.Verdict}");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// Watts, with every figure labelled as measured or estimated.
    /// </summary>
    /// <remarks>
    /// ⚠️ NEVER PRESENT THE TWO AS THE SAME KIND OF QUANTITY. GPU power comes from NVML and is
    ///    measured; AMD package power needs a kernel driver, so the CPU figure is a model and says
    ///    so on the line itself rather than in a footnote nobody reads.
    /// </remarks>
    public static void AppendPower(StringBuilder sb, PowerReport power)
    {
        sb.AppendLine("  POWER");

        if (power.GpuWatts is { } gpuWatts)
        {
            string limit = power.GpuLimitWatts is > 0 ? $" of {power.GpuLimitWatts:N0} W limit" : "";
            sb.AppendLine($"   GPU     {gpuWatts,5:N0} W{limit}   MEASURED via NVML");
        }
        if (power.CpuWatts is { } cpuWatts)
        {
            sb.AppendLine($"   CPU     {cpuWatts,5:N0} W   ESTIMATE ({power.CpuBasis})");
        }
        if (power.EstimatedSystemWatts is { } system)
        {
            sb.AppendLine($"   System  {system,5:N0} W   rough, includes a flat "
                        + $"{PowerReport.OtherComponentsWatts:N0} W for board, drives and fans");
        }
        sb.AppendLine();
    }
}
