namespace PcWatch;

/// <summary>
/// One process's share of the machine, for a single sampling interval.
/// </summary>
/// <remarks>
/// 2026-08-31. <see cref="Percent"/> is the share of the WHOLE machine, already divided by logical
/// core count. The raw Windows counter is per-core and runs to 100 x cores: on this 24-core box one
/// process reported 880%, which is 36.7% of the machine. Anything that surfaces this number must
/// keep that basis, because a percentage without a stated basis is read as the flattering one.
/// </remarks>
public sealed record ProcessLoad(
    string Name,
    int Id,
    double Percent,
    long MemoryBytes,
    DateTime? Started,
    double LifetimeHours)
{
    public double MemoryMb => MemoryBytes / 1024d / 1024d;

    /// <summary>How long since it started, or null when Windows refuses the start time.</summary>
    public TimeSpan? Age => Started is { } s ? DateTime.Now - s : null;

    /// <summary>
    /// Old enough that "is this still meant to be running?" is a fair question.
    /// </summary>
    /// <remarks>
    /// A day is the threshold because it survives a normal working session: anything still here
    /// tomorrow was probably not closed deliberately. Being long-lived is NOT by itself a fault -
    /// Explorer and the shell are always old - so this only ever marks a candidate for review.
    /// </remarks>
    public bool IsLongLived => Age is { TotalDays: >= 1 };
}

/// <summary>
/// Slow-moving machine facts, refreshed on a longer cadence than CPU.
/// </summary>
/// <remarks>
/// <see cref="ClockPercentOfBase"/> is NOT load. It is current clock divided by base clock, and it
/// is carried purely so the UI can explain the number other taskbar tools display. Measured
/// 2026-08-31: it sat at 98.6 across five samples while real load moved 90 to 100%, then load
/// halved to 50% and it still read 98.
/// </remarks>
public sealed record MachineStats(
    double? ClockPercentOfBase,
    double RamUsedGb,
    double RamTotalGb,
    UptimeFacts Uptime,
    string CpuName)
{
    public double RamPercent => RamTotalGb > 0 ? 100 * RamUsedGb / RamTotalGb : 0;
}

/// <summary>
/// One complete reading of the machine.
/// </summary>
/// <remarks>
/// <see cref="TotalCpuPercent"/> is null on the very first sample: there is no previous point to
/// subtract from. Substituting zero there would paint a reassuring green icon over an unknown state.
/// </remarks>
/// <summary>
/// Several processes sharing one executable name, added together.
/// </summary>
/// <remarks>
/// 2026-09-02. Added because a machine at 100% CPU listed nothing above 12%: the load was thirty
/// `dotnet` and `claude` processes at one or two percent each. Per-process rows cannot show that,
/// and "no single process to blame" tells you a fact without telling you where to look. Grouping by
/// name turns an unreadable list into "dotnet x22, 31%".
/// </remarks>
public sealed record ProcessGroup(string Name, int Count, double Percent, double MemoryMb);

/// <summary>
/// How the busy time divides between your programs and the kernel.
/// </summary>
/// <remarks>
/// ⭐ 2026-09-07. Added because a bare total is nearly useless: "100%" says there is no headroom
///   without saying what kind of work filled it, and the two have completely different remedies.
///
///   USER time is your programs computing. If it is high, the process table names the culprit and
///   closing something helps.
///
///   KERNEL time is the operating system working on their behalf: file and network I/O, drivers,
///   antivirus scanning, virtualisation overhead. It is still ATTRIBUTED to the process that caused
///   it (Process.TotalProcessorTime is user + privileged), so the table still names who, but the
///   remedy is different - you change what the program is doing, not whether it runs.
///
/// ⚠️ Measured on this machine at saturation: user 52%, kernel 48%. Half a machine in kernel time is
///    a fact worth surfacing, and no amount of staring at a process list reveals it.
/// </remarks>
public sealed record CpuSplit(double UserPercent, double KernelPercent)
{
    /// <summary>True when the kernel is doing at least as much work as the programs.</summary>
    /// <remarks>
    /// Worth calling out rather than leaving the reader to compare two numbers. An even split is
    /// unusual on a desktop and points at I/O, security software or a hypervisor.
    /// </remarks>
    public bool KernelHeavy => KernelPercent >= UserPercent && KernelPercent > 0;
}

public sealed record Snapshot(
    double? TotalCpuPercent,
    int LogicalCores,
    IReadOnlyList<ProcessLoad> TopProcesses,
    MachineStats Machine,
    DateTime TakenAt,
    IReadOnlyList<ProcessGroup> Groups,
    IReadOnlyList<ProcessLoad> LongLived,
    GpuReading? Gpu = null,
    PowerReport? Power = null,
    double? SystemDriveFreeGb = null,
    double? SystemDriveTotalGb = null,
    CpuSplit? Cpu = null)
{
    /// <summary>Share of the measured load that <see cref="TopProcesses"/> actually explains.</summary>
    /// <remarks>
    /// Measured 2026-09-02 at 91% CPU, top twelve summing to 47%: the list named a sixth of the problem
    /// while reading as a complete account. A list with no coverage figure cannot be told apart from
    /// a full one.
    /// </remarks>
    public double? ExplainedPercent
    {
        get
        {
            if (TotalCpuPercent is not > 0) return null;
            return 100 * TopProcesses.Sum(p => p.Percent) / TotalCpuPercent.Value;
        }
    }
}

public enum Severity { Low, Medium, High }

/// <summary>
/// A finding: something that looks wrong, why, and which process it concerns.
/// </summary>
/// <remarks>
/// <see cref="ProcessId"/> exists so the UI can resolve WHO LAUNCHED IT before repeating advice
/// about closing it. A 16% emulator that another agent session is actively driving and an abandoned
/// one look identical from the load figure alone; only the ancestry separates them.
/// </remarks>
public sealed record Suspect(Severity Severity, int? ProcessId, string Title, string Detail);
