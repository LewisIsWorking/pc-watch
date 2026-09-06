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

        // ⛔ THE ORDER IS THE LOAD-BEARING PART, which is why it lives here rather than inside any
        //    section. FINDINGS COME BEFORE THE PROCESS TABLE. They used to come last, and on a
        //    maximised window the process list grew until it pushed them off the bottom. Four
        //    attempts to compute a row count that "just fits" all produced a confident number and a
        //    clipped window - the last one SILENTLY, because GetPositionFromCharIndex clamps to the
        //    visible area, so the overflow test could never fire however far the text overran.
        //
        //    Ordering by importance retires the whole problem. The table is open-ended and the
        //    findings are not, so anything cut off is now the tail of an already-sorted list rather
        //    than the diagnosis. Nothing has to be predicted correctly.
        ReportSections.AppendHeader(sb, snapshot);
        ReportSections.AppendNotes(sb, snapshot);
        ReportSections.AppendHealth(sb, snapshot);

        if (snapshot.Power is { } power) ReportSections.AppendPower(sb, power);
        if (storage is not null) RenderStorage(sb, storage);

        ReportSections.AppendByProgram(sb, snapshot);
        ReportSections.AppendSuspects(sb, suspects, ancestry);
        ReportSections.AppendTopProcesses(sb, snapshot);
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
