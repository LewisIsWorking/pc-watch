using System.Text;

namespace PcWatch;

/// <summary>
/// Renders a snapshot as the plain-text report shown in the window and copied to the clipboard.
/// </summary>
/// <remarks>
/// 2026-08-31. Design rule: EVERY NUMBER CARRIES ITS BASIS. The app exists because three tools
/// showed three different "CPU %" and none said which question it was answering. So the process
/// column states "% of whole machine", the coverage line states how much of the load the list
/// explains, and the clock ratio is never printed without the word CLOCK beside it.
/// </remarks>
public static class ReportRenderer
{
    /// <summary>
    /// The largest folders found by the last disk scan, with the scan's age stated.
    /// </summary>
    /// <remarks>
    /// ⚠️ Always prints WHEN it was measured. A full scan takes minutes and is cached across
    /// launches, so a size here can be days old - and a stale figure shown without its age reads
    /// exactly like a fresh one.
    /// </remarks>
    public static void RenderStorage(System.Text.StringBuilder sb, DiskScanner scanner)
    {
        if (scanner.IsScanning)
        {
            sb.AppendLine("  LARGEST FOLDERS   scanning in the background, this takes a few minutes...");
            sb.AppendLine();
            return;
        }

        if (scanner.Last is not { } scan)
        {
            sb.AppendLine("  LARGEST FOLDERS   not scanned yet - right-click the tray icon, Scan disk usage");
            sb.AppendLine();
            return;
        }

        string age = Age(DateTime.Now - scan.TakenAt);
        string partial = scan.Complete ? "" : ", CANCELLED so incomplete";
        sb.AppendLine($"  LARGEST FOLDERS   (measured {age} ago{partial}; totals are a floor - "
                    + "folders Windows would not let us read are missing)");

        foreach (FolderSize folder in scan.Folders.Take(12))
        {
            string name = folder.Path.Length > 62 ? "..." + folder.Path[^59..] : folder.Path;
            sb.AppendLine($"   {folder.Gb,7:N1} GB  {name}");
        }
        sb.AppendLine();
    }

    public static string Render(
        Snapshot snapshot, IReadOnlyList<Suspect> suspects, ProcessAncestry ancestry, DiskScanner? storage = null)
    {
        var sb = new StringBuilder();

        string cpu = snapshot.TotalCpuPercent is { } v ? $"{v:N0}%" : "measuring...";
        sb.AppendLine($"  CPU   {cpu}   across {snapshot.LogicalCores} logical cores");
        sb.AppendLine($"  RAM   {snapshot.Machine.RamUsedGb:N1} / {snapshot.Machine.RamTotalGb:N1} GB  ({snapshot.Machine.RamPercent:N0}%)");
        UptimeFacts up = snapshot.Machine.Uptime;
        string since = up.OnSince is { } onSince ? $"  (since {onSince:ddd d MMM HH:mm})" : "  (kernel counter)";
        sb.AppendLine($"  ON    {Age(up.Best)}{since}");
        sb.AppendLine($"  AT    {snapshot.TakenAt:HH:mm:ss}   PC Watch {AppVersion.Display}");
        sb.AppendLine();

        // Both notes explain a number some OTHER tool is showing. That is the whole job of this
        // app: not just to be right, but to say why the rival figure is what it is.
        foreach (string? note in new[] { SuspectAnalyzer.FrequencyNote(snapshot), SystemUptime.DisagreementNote(up) })
        {
            if (note is null) continue;
            foreach (string line in Wrap(note, 76))
            {
                sb.AppendLine($"  ! {line}");
            }
            sb.AppendLine();
        }

        // ⛔ 2026-08-31: FINDINGS BEFORE THE PROCESS TABLE, and this order is load-bearing.
        //    They used to come last, and on a maximised window the process list grew until it
        //    pushed them off the bottom. Four attempts to compute a row count that "just fits" all
        //    produced a confident number and a clipped window - the last one silently, because
        //    GetPositionFromCharIndex CLAMPS to the visible area, so the overflow test could never
        //    fire no matter how far the text overran.
        //
        //    Ordering by importance retires the whole problem. The list is open-ended and the
        //    findings are not, so if anything is cut off it is now the tail of the process table -
        //    the least valuable rows, already sorted last. Nothing has to be predicted correctly.
        // HOW WELL IS IT RUNNING - named indicators, never one composite score. Two machines both
        // scoring "72" can be unwell in completely different ways, and nobody can act on a 72.
        IReadOnlyList<HealthIndicator> health = SystemHealth.Assess(snapshot);
        if (health.Count > 0)
        {
            var (word, _) = SystemHealth.Overall(health);
            sb.AppendLine($"  HOW IT IS RUNNING: {word}   (worst PERFORMANCE indicator, not an average)");
            foreach (HealthIndicator h in health.Where(h => h.Kind == IndicatorKind.Performance))
            {
                sb.AppendLine($"   {h.Name,-7} {h.Value,-34} {h.Verdict}");
            }
            sb.AppendLine();

            // Capacity is reported separately and never sets the verdict above. A drive at 3% free
            // is a real problem about LATER; it is not the machine struggling NOW.
            IReadOnlyList<HealthIndicator> warnings = SystemHealth.Warnings(health);
            if (warnings.Count > 0)
            {
                sb.AppendLine("  WORTH FIXING SOON   (not slowing you down yet)");
                foreach (HealthIndicator w in warnings)
                {
                    sb.AppendLine($"   {w.Name,-7} {w.Value,-34} {w.Verdict}");
                }
                sb.AppendLine();
            }
        }

        if (snapshot.Power is { } power)
        {
            sb.AppendLine("  POWER");
            if (power.GpuWatts is { } gpuWatts)
            {
                string limit = power.GpuLimitWatts is > 0 ? $" of {power.GpuLimitWatts:N0} W limit" : "";
                sb.AppendLine($"   GPU     {gpuWatts,5:N0} W{limit}   MEASURED via NVML");
            }
            if (power.CpuWatts is { } cpuWatts)
            {
                // ⚠️ Always labelled. AMD package power needs a kernel driver, so this is a model.
                sb.AppendLine($"   CPU     {cpuWatts,5:N0} W   ESTIMATE ({power.CpuBasis})");
            }
            if (power.EstimatedSystemWatts is { } system)
            {
                sb.AppendLine($"   System  {system,5:N0} W   rough, includes a flat "
                            + $"{PowerReport.OtherComponentsWatts:N0} W for board, drives and fans");
            }
            sb.AppendLine();
        }

        if (storage is not null) RenderStorage(sb, storage);

        if (snapshot.Groups.Count > 0)
        {
            sb.AppendLine("  BY PROGRAM   (several processes sharing one name, added up)");
            foreach (ProcessGroup g in snapshot.Groups)
            {
                sb.AppendLine($"   {g.Percent,6:N1}%  {g.Name,-24} x{g.Count,-4} {g.MemoryMb,8:N0} MB");
            }
            sb.AppendLine();
        }

        sb.AppendLine("  WHAT LOOKS WRONG");
        foreach (Suspect s in suspects)
        {
            sb.AppendLine($"   * {s.Title}");

            // Ownership BEFORE advice. A 16% emulator driven by a live agent session and an
            // abandoned one look identical from the load figure; only one is safe to close.
            if (s.ProcessId is { } id && ancestry.OwnerLabelFor(id) is { } owner)
            {
                sb.AppendLine($"     {owner}");
            }
            foreach (string line in Wrap(s.Detail, 74))
            {
                sb.AppendLine($"     {line}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("  TOP PROCESSES   (% of the WHOLE machine, not per-core)");

        if (snapshot.ExplainedPercent is { } share)
        {
            double listed = snapshot.TopProcesses.Sum(p => p.Percent);
            sb.Append($"   -> these {snapshot.TopProcesses.Count} account for {listed:N1}% of the "
                    + $"{snapshot.TotalCpuPercent:N0}% in use ({share:N0}%).");
            sb.AppendLine(share < 60 ? " Most load is spread below the cutoff." : string.Empty);
        }

        foreach (ProcessLoad p in snapshot.TopProcesses)
        {
            string name = p.Name.Length > 24 ? p.Name[..24] : p.Name;
            string age = p.Started is { } s ? Age(DateTime.Now - s) : "-";
            sb.AppendLine($"   {p.Percent,6:N1}%  {name,-24} {p.Id,7}  {p.MemoryMb,7:N0} MB  up {age}");
        }

        return sb.ToString();
    }

    /// <summary>Compact age. Coarser the older it gets: the minute a three-week-old process started is noise.</summary>
    public static string Age(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h"
        : t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m"
        : $"{(int)t.TotalMinutes}m";

    /// <summary>
    /// Wrap prose to a column width.
    /// </summary>
    /// <remarks>
    /// Hand-wrapped because the report is displayed in a fixed-width control with wrapping OFF: it
    /// has to be, or the process table folds onto continuation lines and one 13.5% row reads as two
    /// separate entries. Prose still needs wrapping, so it gets it here.
    /// </remarks>
    public static IReadOnlyList<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > width)
            {
                lines.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0) current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }
}
