using System.Text.RegularExpressions;

namespace PcWatch;

/// <summary>
/// Turns a process list into a diagnosis: what looks wrong, and why.
/// </summary>
/// <remarks>
/// 2026-08-31. A list sorted by CPU is not a diagnosis. The top entry is often something that is
/// SUPPOSED to be busy (a compile, a game) while the real problem is a process quietly burning 9%
/// for a day and a half. These rules encode the difference between "busy" and "wrong", and each was
/// written against something actually measured on this machine.
///
/// ⚠️ Every rule is gated on CURRENT load, not merely on a process existing. The first draft flagged
/// any VM process regardless, and promptly reported vmmemWSL at 0.9% as HIGH while the machine's
/// actual problem sat at 14%. A panel that shouts about a hundredth of the CPU is a panel you stop
/// reading, so severity tracks the number rather than the category.
/// </remarks>
public static partial class SuspectAnalyzer
{
    [GeneratedRegex(@"^(qemu-system|vmmem|VBoxHeadless|vmware-vmx)", RegexOptions.IgnoreCase)]
    private static partial Regex VirtualMachine();

    [GeneratedRegex("headless", RegexOptions.IgnoreCase)]
    private static partial Regex HeadlessBrowser();

    private static Severity ByLoad(double percent) =>
        percent >= 10 ? Severity.High : percent >= 5 ? Severity.Medium : Severity.Low;

    public static IReadOnlyList<Suspect> Analyze(Snapshot snapshot)
    {
        var found = new List<Suspect>();
        var claimed = new HashSet<int>();

        void Add(Severity severity, int? processId, string title, string detail)
        {
            found.Add(new Suspect(severity, processId, title, detail));
            if (processId is { } id) claimed.Add(id);
        }

        foreach (ProcessLoad p in snapshot.TopProcesses.Where(p => VirtualMachine().IsMatch(p.Name) && p.Percent >= 3))
        {
            Add(ByLoad(p.Percent), p.Id,
                $"{p.Name} (pid {p.Id}) - virtual machine, {p.Percent}% now",
                $"Has consumed {p.LifetimeHours:N1} CPU-hours and holds {p.MemoryMb:N0} MB. Emulators "
                + "outlive the IDE that started them, but read the owner line above before closing it: "
                + "a live one looks identical to an abandoned one from the load figure alone.");
        }

        foreach (ProcessLoad p in snapshot.TopProcesses.Where(p => HeadlessBrowser().IsMatch(p.Name) && p.Percent >= 2))
        {
            Add(ByLoad(p.Percent), p.Id,
                $"{p.Name} (pid {p.Id}) - headless browser, {p.Percent}%",
                "This is Playwright or Puppeteer, not something you opened. Headless Chrome falls back "
                + "to SwiftShader software rendering and will saturate several cores. Safe to kill ONLY "
                + "if no test run is in progress: if the owner line above says node or npm, it is live.");
        }

        // explorer.exe idles at ~0%. Sustained load there is nearly always an injected shell mod
        // (Windhawk, StartAllBack, ExplorerPatcher) or a thumbnail/indexing storm.
        foreach (ProcessLoad p in snapshot.TopProcesses.Where(p =>
                     p.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase) && p.Percent >= 3))
        {
            Add(ByLoad(p.Percent), p.Id,
                $"explorer (pid {p.Id}) - {p.Percent}%, should be near zero",
                "The Windows shell idles at 0%. Sustained load here usually means an injected taskbar "
                + "mod redrawing on a timer, or a folder of media being thumbnailed. Restarting "
                + "explorer costs nothing and tells you which.");
        }

        // Anything heavy the rules above did not already explain. Matching on the pid SET, never on
        // rendered title text: a substring search for "pid 152" also matches "pid 15264".
        foreach (ProcessLoad p in snapshot.TopProcesses.Where(p => p.Percent >= 15 && !claimed.Contains(p.Id)))
        {
            Add(Severity.High, p.Id,
                $"{p.Name} (pid {p.Id}) - {p.Percent}% of the whole machine",
                $"{p.LifetimeHours:N1} CPU-hours lifetime, {p.MemoryMb:N0} MB. Expected, or a runaway?");
        }

        // ⚠️ Measured against the REAL power-on, not the kernel counter. Fast Startup keeps the
        //    kernel counter running across a shutdown, so this rule used to fire at "18.8 days" on
        //    a machine that had been on for a day and a half - an alarm about a fiction.
        if (snapshot.Machine.Uptime.Best.TotalDays >= 7)
        {
            Add(Severity.Low, null,
                $"On for {snapshot.Machine.Uptime.Best.TotalDays:N1} days",
                "Leaked handles, orphaned child processes and fragmented memory all accumulate. Long "
                + "uptime does not cause slowness by itself, but it is why the leftovers above had "
                + "time to pile up.");
        }

        if (snapshot.Machine.RamTotalGb > 0 && snapshot.Machine.RamPercent >= 90)
        {
            Add(Severity.High, null,
                $"Memory {snapshot.Machine.RamUsedGb:N1} / {snapshot.Machine.RamTotalGb:N1} GB",
                "Above ~90% Windows starts trimming working sets to the page file. The symptom is "
                + "stutter rather than a high CPU number, so this can be the cause even when CPU looks fine.");
        }

        // ⚠️ Load with no single owner. Without this, a 90%-busy machine could report "nothing
        //    obviously wrong" purely because the work was spread across thirty processes that each
        //    stayed under every threshold.
        if (found.Count == 0 && snapshot.TotalCpuPercent >= 60)
        {
            Add(Severity.Medium, null,
                $"CPU is {snapshot.TotalCpuPercent:N0}% with no single process to blame",
                "The load is spread thinly rather than concentrated. That usually means many instances "
                + "of one thing (browser tabs, build workers, agent sessions) or a driver in the "
                + "kernel. Group the list by name before hunting individual rows.");
        }

        if (found.Count == 0 && snapshot.TotalCpuPercent is not null)
        {
            Add(Severity.Low, null, "Nothing obviously wrong",
                $"CPU is {snapshot.TotalCpuPercent:N0}% and no single process is behaving unusually. If "
                + "the machine still feels slow, the cause is more likely disk or memory pressure than CPU.");
        }

        return found;
    }

    /// <summary>
    /// The standing note about frequency-ratio counters, or null when there is nothing to explain.
    /// </summary>
    /// <remarks>
    /// 2026-08-31: exists because a Windhawk taskbar mod read 98% while Task Manager read 67%.
    /// Five consecutive samples had real load moving 90 to 100% while the clock ratio sat at 98.6
    /// every time; load then halved to 50% and it still read 98.
    /// </remarks>
    public static string? FrequencyNote(Snapshot snapshot)
    {
        if (snapshot.Machine.ClockPercentOfBase is not { } clock || snapshot.TotalCpuPercent is not { } load)
        {
            return null;
        }
        if (clock < 90 || clock - load < 25) return null;

        return $"Clock is at {clock:N0}% of base while real load is {load:N0}%. If another taskbar tool "
             + $"shows you roughly {clock:N0}%, it is displaying CLOCK SPEED, not utilisation.";
    }
}
