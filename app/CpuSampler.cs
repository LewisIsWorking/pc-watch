using System.Diagnostics;

namespace PcWatch;

/// <summary>
/// Turns two points in time into "what is this machine doing".
/// </summary>
/// <remarks>
/// 2026-08-31. Everything here is DELTA based. A single reading of a process's TotalProcessorTime
/// says what it has burned since it started, which is a different question from what it is burning
/// now, and confusing the two blames a process that idled all week for the last five seconds.
/// </remarks>
public sealed class CpuSampler : IDisposable
{
    private readonly int _cores = Environment.ProcessorCount;
    private readonly GpuTelemetry _gpu = new();
    private readonly MachineProbe _machine = new();
    private readonly Dictionary<int, double> _lastCpuSeconds = new();
    private (long Idle, long Total)? _lastTicks;
    private DateTime? _lastStamp;

    /// <summary>
    /// How many processes to report. Settable so the window can ask for as many rows as it can
    /// actually show - a maximised window has room for far more than a restored one, and a fixed
    /// count either wastes most of the screen or overflows a small window.
    /// </summary>
    public int TopCount { get; set; } = 14;

    /// <summary>
    /// Below this share of the machine a process is not worth a row.
    /// </summary>
    /// <remarks>
    /// 2026-08-31: lowered from 0.4 to 0.1 once TopCount became adaptive. On an idle machine the
    /// 0.4 floor left a maximised window showing ten rows that explained only HALF the measured
    /// load, with the rest scattered below the cutoff - the list was short because of the filter,
    /// not because there was nothing to see. TopCount now does the limiting, so the floor only has
    /// to exclude genuine noise.
    /// </remarks>
    public double MinimumPercent { get; init; } = 0.1;

    public Snapshot Sample()
    {
        DateTime now = DateTime.Now;
        var ticks = Native.GetCpuTicks();

        double? totalCpu = null;
        if (_lastTicks is { } previous)
        {
            long totalDelta = ticks.Total - previous.Total;
            long idleDelta = ticks.Idle - previous.Idle;
            if (totalDelta > 0)
            {
                totalCpu = Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
            }
        }

        double elapsed = _lastStamp is { } stamp ? (now - stamp).TotalSeconds : 0;
        var all = new List<ProcessLoad>();
        var current = new Dictionary<int, double>();

        foreach (Process process in Process.GetProcesses())
        {
            // ⛔ EVERY Process must be disposed. Reading TotalProcessorTime opens a native handle,
            //    and the finalizer returns it slower than a one-second loop consumes them. Measured
            //    in the PowerShell original: +24 handles per 40 s across ~400 processes a tick,
            //    reaching the 10000-handle quota in about four hours.
            using (process)
            {
                double seconds;
                int id;
                try
                {
                    id = process.Id;
                    seconds = process.TotalProcessorTime.TotalSeconds;
                }
                catch
                {
                    continue;   // access denied, or it exited between enumeration and the read
                }

                current[id] = seconds;

                double percent = 0;
                if (elapsed > 0 && _lastCpuSeconds.TryGetValue(id, out double before))
                {
                    double burned = seconds - before;
                    // ⚠️ Divide by core count. The raw figure is per-core and runs to 100 x cores.
                    if (burned > 0) percent = Math.Round(100 * burned / elapsed / _cores, 1);
                }

                DateTime? started = null;
                try { started = process.StartTime; } catch { /* system processes refuse */ }

                long memory;
                string name;
                try
                {
                    memory = process.WorkingSet64;
                    name = process.ProcessName;
                }
                catch { continue; }

                // ⚠️ Built for EVERY process, including idle ones. The CPU floor is applied further
                //    down, because the long-lived list is about age and memory rather than load: a
                //    forgotten app holding 2 GB at 0% CPU is exactly what it exists to surface, and
                //    filtering on CPU first would hide it.
                all.Add(new ProcessLoad(name, id, percent, memory, started, seconds / 3600));
            }
        }

        _lastTicks = ticks;
        _lastStamp = now;
        _lastCpuSeconds.Clear();
        foreach (var pair in current) _lastCpuSeconds[pair.Key] = pair.Value;

        var rows = all.Where(r => r.Percent >= MinimumPercent).ToList();
        var top = rows.OrderByDescending(r => r.Percent).Take(TopCount).ToList();

        // Long-lived: age first, then whatever is holding the most memory. Sorted by memory rather
        // than by age because "which of these old things is costing me something" is the question a
        // kill button answers; the oldest process on any Windows box is one nobody should touch.
        var longLived = all
            .Where(p => p.IsLongLived)
            .OrderByDescending(p => p.MemoryMb)
            .Take(40)
            .ToList();

        // Grouped BEFORE the Take, so the totals cover every process measured rather than only the
        // ones that fit on screen. Grouping the truncated list would under-report the very case
        // this exists for: many small processes sharing one name.
        var groups = rows
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProcessGroup(g.Key, g.Count(), Math.Round(g.Sum(r => r.Percent), 1), g.Sum(r => r.MemoryMb)))
            .Where(g => g.Count > 1 && g.Percent >= 1)
            .OrderByDescending(g => g.Percent)
            .Take(8)
            .ToList();

        MachineStats machine = _machine.Read();
        GpuReading? gpu = _gpu.Read();
        var (freeGb, totalGb) = _machine.SystemDrive();

        return new Snapshot(
            totalCpu, _cores, top, machine, now, groups, longLived, gpu,
            PowerEstimate.Build(machine.CpuName, totalCpu, gpu),
            freeGb, totalGb);
    }


    public void Dispose() => _gpu.Dispose();
}
