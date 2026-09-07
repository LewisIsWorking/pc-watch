using System.Text;

namespace PcWatch;

/// <summary>
/// The process-oriented halves of the report: grouping, findings, and the table.
/// </summary>
/// <remarks>
/// 2026-09-07. Split from ReportSections when the user/kernel line pushed that file past the
/// 200-line limit. The seam is real rather than arbitrary: everything here answers WHICH
/// PROGRAMS, while ReportSections answers HOW THE MACHINE IS.
///
/// ⛔ The ORDER these are emitted in still lives in ReportRenderer.Render, because it is a
///    property of the whole report rather than of any one section.
/// </remarks>
internal static class ReportProcessSections
{
    /// <summary>
    /// Several processes sharing one name, added up.
    /// </summary>
    /// <remarks>
    /// ⭐ Without this, 56 build workers at 0.2% each vanish below every top-N list while together
    ///   accounting for more than everything shown above them.
    /// </remarks>
    public static void AppendByProgram(StringBuilder sb, Snapshot snapshot)
    {
        if (snapshot.Groups.Count == 0) return;

        sb.AppendLine("  BY PROGRAM   (several processes sharing one name, added up)");
        foreach (ProcessGroup g in snapshot.Groups)
        {
            sb.AppendLine($"   {g.Percent,6:N1}%  {g.Name,-24} x{g.Count,-4} {g.MemoryMb,8:N0} MB");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// What looks wrong, with ownership stated before any advice.
    /// </summary>
    /// <remarks>
    /// ⛔ OWNERSHIP BEFORE ADVICE. A 16% emulator driven by a live agent session and an abandoned
    ///    one look identical from the load figure; only one of them is safe to close.
    /// </remarks>
    public static void AppendSuspects(
        StringBuilder sb, IReadOnlyList<Suspect> suspects, ProcessAncestry ancestry)
    {
        sb.AppendLine("  WHAT LOOKS WRONG");
        foreach (Suspect s in suspects)
        {
            sb.AppendLine($"   * {s.Title}");

            if (s.ProcessId is { } id && ancestry.OwnerLabelFor(id) is { } owner)
            {
                sb.AppendLine($"     {owner}");
            }
            foreach (string line in ReportRenderer.Wrap(s.Detail, 74))
            {
                sb.AppendLine($"     {line}");
            }
        }
    }

    /// <summary>
    /// The heaviest processes, as a share of the WHOLE machine.
    /// </summary>
    /// <remarks>
    /// ⚠️ Raw per-process CPU on Windows runs to 100 x cores, so an unnormalised figure reads as
    ///    1600% on a 16-thread machine. The coverage line below exists because a list that accounts
    ///    for only a third of the load is telling you the answer is NOT in the list.
    /// </remarks>
    public static void AppendTopProcesses(StringBuilder sb, Snapshot snapshot)
    {
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
            string age = p.Started is { } s ? ReportRenderer.Age(DateTime.Now - s) : "-";
            sb.AppendLine($"   {p.Percent,6:N1}%  {name,-24} {p.Id,7}  {p.MemoryMb,7:N0} MB  up {age}");
        }
    }
}
