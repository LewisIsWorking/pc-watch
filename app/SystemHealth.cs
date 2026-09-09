namespace PcWatch;

/// <summary>
/// What an indicator is telling you about.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-02. These must not be averaged together. PERFORMANCE answers "how is it running right
///    now"; CAPACITY answers "what will bite you later". A drive at 3% free is a genuine problem and
///    a genuine non-emergency: measured on this machine at the time, the page file held 0.2 GB, so
///    nothing was actually paging. Letting it set the headline verdict made a storage warning read
///    as a performance emergency, and the user quite reasonably asked whether the PC was dying.
/// </remarks>
public enum IndicatorKind { Performance, Capacity }

/// <summary>One graded aspect of how the machine is running.</summary>
/// <param name="HeadlineWord">
/// Overrides the generic severity word when this indicator is the worst one.
/// </param>
/// <remarks>
/// ⛔ 2026-09-09. HeadlineWord exists because the severity words describe LOAD, and not every High
///    reading is about load. The GPU grades High on TEMPERATURE, so a card at 88 C while only 40%
///    busy was announced as "FLAT OUT" - which is not merely imprecise, it points at the wrong
///    remedy. Flat out means "stop giving it work"; running hot means "check your fans". Any
///    indicator whose worst state is not about load must say its own word.
/// </remarks>
public sealed record HealthIndicator(
    string Name, string Value, Severity Severity, string Verdict,
    IndicatorKind Kind = IndicatorKind.Performance, string? HeadlineWord = null);

/// <summary>
/// "How well is this PC running" - as several named indicators, never a single mystery score.
/// </summary>
/// <remarks>
/// 2026-09-02. ⛔ DELIBERATELY NOT A SINGLE 0-100 NUMBER. A composite score has no named basis: two
/// machines scoring 72 can be unwell in completely different ways, and nobody can tell what to do
/// about a 72. Worse, the weighting that produced it is invisible, so the number cannot be argued
/// with. Each indicator here says what was measured, what it read, and what that means - the same
/// discipline that made the CPU percentage trustworthy in the first place.
///
/// The overall word is derived from the WORST indicator rather than an average, because a machine
/// with one saturated resource is not "mostly fine".
/// </remarks>
public static class SystemHealth
{
    public static IReadOnlyList<HealthIndicator> Assess(Snapshot snapshot)
    {
        var list = new List<HealthIndicator>();

        if (snapshot.TotalCpuPercent is { } cpu)
        {
            list.Add(new HealthIndicator("CPU", $"{cpu:N0}%",
                cpu >= 90 ? Severity.High : cpu >= 60 ? Severity.Medium : Severity.Low,
                cpu >= 90 ? "saturated - everything else is queueing behind it"
                : cpu >= 60 ? "busy, but with headroom left"
                : "plenty of headroom"));
        }

        double ramPercent = snapshot.Machine.RamPercent;
        if (snapshot.Machine.RamTotalGb > 0)
        {
            list.Add(new HealthIndicator("Memory",
                $"{snapshot.Machine.RamUsedGb:N1} / {snapshot.Machine.RamTotalGb:N1} GB ({ramPercent:N0}%)",
                ramPercent >= 90 ? Severity.High : ramPercent >= 75 ? Severity.Medium : Severity.Low,
                ramPercent >= 90 ? "Windows is trimming working sets to disk - expect stutter"
                : ramPercent >= 75 ? "filling up, but not yet paging hard"
                : "comfortable"));
        }

        if (snapshot.Gpu is { } gpu)
        {
            // ⛔ The GPU's High state is about HEAT, not load, so it carries its own headline word.
            //    "FLAT OUT" on a card that is 40% busy and 88 C sends the reader to close programs
            //    when the actual problem is airflow. Different cause, different fix, different word.
            bool hot = gpu.TemperatureC >= 84;
            list.Add(new HealthIndicator("GPU",
                $"{gpu.UtilisationPercent}%, {gpu.Watts:N0} W, {gpu.TemperatureC} C",
                hot ? Severity.High : gpu.UtilisationPercent >= 90 ? Severity.Medium : Severity.Low,
                hot ? "running hot - it will be throttling"
                : gpu.UtilisationPercent >= 90 ? "fully loaded"
                : "idle or light",
                HeadlineWord: hot ? "RUNNING HOT" : null));
        }

        if (snapshot.SystemDriveFreeGb is { } freeGb && snapshot.SystemDriveTotalGb is { } totalGb && totalGb > 0)
        {
            double freePercent = 100 * freeGb / totalGb;
            list.Add(new HealthIndicator("Disk", $"{freeGb:N0} GB free of {totalGb:N0} GB ({freePercent:N0}%)",
                freePercent < 5 ? Severity.High : freePercent < 12 ? Severity.Medium : Severity.Low,
                freePercent < 5 ? "critically low - Windows needs room for the page file"
                : freePercent < 12 ? "getting tight"
                : "fine",
                IndicatorKind.Capacity));
        }

        return list;
    }

    /// <summary>
    /// One word for the whole machine, taken from the WORST indicator.
    /// </summary>
    /// <remarks>
    /// Not an average. A box with 100% CPU and everything else idle is not "good with a caveat" -
    /// it is unusable, and averaging would hide exactly the reading worth acting on.
    /// </remarks>
    public static (string Word, Severity Severity, string? Driver) Overall(
        IReadOnlyList<HealthIndicator> indicators)
    {
        // ⚠️ PERFORMANCE indicators only. Capacity problems are real but they are not what "how is
        //    this machine running" asks, and letting a 3%-free disk say STRUGGLING while the CPU sat
        //    at 54% and nothing was paging was simply wrong. Capacity is reported by Warnings().
        var performance = indicators.Where(i => i.Kind == IndicatorKind.Performance).ToList();
        if (performance.Count == 0) return ("measuring", Severity.Low, null);

        Severity worst = performance.Max(i => i.Severity);
        // ⚠️ 2026-09-02: "STRUGGLING" was the High word, and it implies a FAULT. A machine running 24
        //    node processes and 11 agent sessions at 100% CPU is not faulty - it is doing exactly
        //    what it was told, at capacity. The wrong word sent the user looking for a hardware
        //    problem that did not exist. These words describe LOAD; the per-indicator lines below
        //    say what the consequence is ("everything else is queueing behind it").
        string word = worst switch
        {
            Severity.High => "FLAT OUT",
            Severity.Medium => "WORKING HARD",
            _ => "HEALTHY",
        };

        // ⛔ 2026-09-09, REPORTED BY LEWIS: "my PC is sitting at like 49% but it still says FLAT
        //    OUT". Both readings were correct and the pairing was not. The verdict is the WORST
        //    performance indicator, which was Memory, but "FLAT OUT" is CPU-shaped language sitting
        //    directly under a line reading "CPU 49%", so it reads as the app contradicting itself.
        //
        //    A verdict that names no subject gets attached to the nearest number. So the driver is
        //    named whenever there is something to name, and the reader can see at a glance that the
        //    CPU is not the thing that is flat out.
        //
        // ⚠️ Named only when the news is bad. "HEALTHY - CPU" would imply the other indicators are
        //    NOT healthy, which is the opposite of what a Low worst-case means.
        HealthIndicator leader = performance.First(i => i.Severity == worst);
        string? driver = worst == Severity.Low ? null : leader.Name;

        // ⛔ 2026-09-09. An indicator whose worst state is not about LOAD supplies its own word. The
        //    GPU grades High on temperature, and "FLAT OUT" there points at the wrong remedy: it
        //    says stop giving it work, when what is needed is airflow.
        if (driver is not null && leader.HeadlineWord is { } own) word = own;

        return (word, worst, driver);
    }

    /// <summary>
    /// Capacity problems: real, but about later rather than now.
    /// </summary>
    /// <remarks>
    /// Kept out of the headline verdict and surfaced separately, so that "your drive is nearly full"
    /// is neither hidden nor mistaken for "your PC is on fire".
    /// </remarks>
    public static IReadOnlyList<HealthIndicator> Warnings(IReadOnlyList<HealthIndicator> indicators) =>
        [.. indicators.Where(i => i.Kind == IndicatorKind.Capacity && i.Severity != Severity.Low)];
}
