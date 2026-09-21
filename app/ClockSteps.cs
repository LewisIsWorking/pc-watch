using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;

namespace PcWatch;

/// <summary>
/// Forward jumps of the system clock, which make "on for" legitimately exceed the kernel tick count.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-17, MEASURED ON A VMWARE VM (the dev machine). The self-test declared it "impossible" for the
///    wall-clock time since boot to exceed the kernel's own count by more than 5 minutes. Then it did,
///    by 12.5 minutes:
///
///      Kernel-Boot event 27 / WMI LastBootUpTime     17:04:59
///      boot implied by GetTickCount64                17:17:30   (12.5 min later)
///      sleep time (tick vs unbiased counter)         none
///      Kernel-General 1, 00:57:58                    clock stepped FORWARD 751.3 s (12.52 min)
///
///    The VM stalled, so its clock fell behind while the tick count, correctly, did not advance for
///    time it was not running. Time sync then stepped the wall clock forward. Both counters were
///    right. The "impossible" was not.
///
///    ⚠️ 2026-09-21, CORRECTED: this first said the stall happened BECAUSE nine other sessions' dotnet
///       builds were running. That was seen alongside it, never shown to cause it - and the same
///       week that VM's crashes were traced to the host SSD being full, with virtual disk writes
///       taking 7-15 s, which stalls a guest just as well. The cause of this stall is not
///       established. Nothing below depends on it: any stall that time sync corrects looks the same.
///
///    A physical PC does the same when NTP corrects a slow hardware clock after boot.
///
/// ⚠️ NOT A BIGGER MARGIN. The check exists to catch reading an OLDER boot event than the real one,
///    which shows up as hours or days. Any fixed margin generous enough for the next long VM stall
///    would also hide that bug. So the forward steps Windows actually LOGGED are counted instead: an
///    excess they explain is fine, an excess they do not is still a failure.
/// </remarks>
public static class ClockSteps
{
    /// <summary>
    /// Total forward adjustment of the system clock since <paramref name="since"/>, or null when the
    /// event log cannot be read.
    /// </summary>
    public static TimeSpan? ForwardSince(DateTime since)
    {
        try
        {
            string utc = since.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            var query = new EventLogQuery("System", PathType.LogName,
                $"*[System[Provider[@Name='Microsoft-Windows-Kernel-General'] and EventID=1 "
                + $"and TimeCreated[@SystemTime>='{utc}']]]");

            var steps = new List<(DateTime Old, DateTime New)>();
            using var reader = new EventLogReader(query);
            for (EventRecord? record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    if (Parse(record.ToXml()) is { } step) steps.Add(step);
                }
            }
            return SumForward(steps);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>OldTime and NewTime from a Kernel-General 1 event, or null if either is missing.</summary>
    internal static (DateTime Old, DateTime New)? Parse(string eventXml)
    {
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        var data = XDocument.Parse(eventXml).Descendants(ns + "Data")
            .ToDictionary(d => (string?)d.Attribute("Name") ?? "", d => d.Value);

        return data.TryGetValue("OldTime", out string? oldText)
               && data.TryGetValue("NewTime", out string? newText)
               && DateTime.TryParse(oldText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime old)
               && DateTime.TryParse(newText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime @new)
            ? (old, @new)
            : null;
    }

    /// <summary>
    /// Sum only the steps that moved the clock FORWARD.
    /// </summary>
    /// <remarks>
    /// A backward step makes the wall clock distance to boot SHORTER, so it can never explain on-for
    /// running ahead of the kernel. Netting it off would let a later backward correction cancel out
    /// the forward one that actually explains the gap.
    /// </remarks>
    internal static TimeSpan SumForward(IEnumerable<(DateTime Old, DateTime New)> steps) =>
        steps.Select(s => s.New - s.Old).Where(d => d > TimeSpan.Zero)
             .Aggregate(TimeSpan.Zero, (total, d) => total + d);

    /// <summary>
    /// Whether on-for running ahead of the kernel count is accounted for by logged clock steps.
    /// </summary>
    internal static bool OnForIsExplained(TimeSpan onFor, TimeSpan kernel, TimeSpan forwardSteps, TimeSpan slack) =>
        onFor <= kernel + forwardSteps + slack;
}
