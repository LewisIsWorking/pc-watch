using Microsoft.Win32;

namespace PcWatch;

/// <summary>
/// The slow-moving machine facts: CPU name, memory, clock ratio, disk space.
/// </summary>
/// <remarks>
/// 2026-09-02. Split out of CpuSampler at the 200-line limit. The seam is real: this answers "what
/// is this computer", which changes rarely or never, while CpuSampler answers "what is it doing
/// right now", which changes every second. They cache on completely different timescales.
/// </remarks>
public sealed class MachineProbe
{
    private readonly string _cpuName = ReadCpuName();
    private (double? FreeGb, double? TotalGb) _drive;
    private DateTime _driveStamp = DateTime.MinValue;

    public string CpuName => _cpuName;

    /// <summary>Clock ratio, memory and uptime. Cheap enough to run every tick, unlike WMI.</summary>
    public MachineStats Read()
    {
        UptimeFacts uptime = SystemUptime.Get();
        try
        {
            var (usedGb, totalGb) = Native.GetMemory();
            return new MachineStats(
                Native.GetClockPercentOfBase(),
                Math.Round(usedGb, 1),
                Math.Round(totalGb, 1),
                uptime,
                _cpuName);
        }
        catch
        {
            // A failed stats read must not take the CPU figure down with it. That comes from a
            // different source entirely, and it is the one the window was opened for.
            return new MachineStats(null, 0, 0, uptime, _cpuName);
        }
    }

    /// <summary>
    /// Free and total space on the Windows drive. Cached: it barely moves and the call touches disk.
    /// </summary>
    public (double? FreeGb, double? TotalGb) SystemDrive()
    {
        if (_driveStamp != DateTime.MinValue && DateTime.Now - _driveStamp < TimeSpan.FromSeconds(30))
        {
            return _drive;
        }

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            const double gb = 1024d * 1024 * 1024;
            _drive = (drive.AvailableFreeSpace / gb, drive.TotalSize / gb);
        }
        catch
        {
            _drive = (null, null);
        }

        _driveStamp = DateTime.Now;
        return _drive;
    }

    /// <summary>
    /// Processor model from the registry, where the OS records it at boot.
    /// </summary>
    /// <remarks>
    /// Read once. It cannot change while the process lives, and the alternative (Win32_Processor)
    /// costs ~1.5 s to answer a question with a constant answer.
    /// </remarks>
    private static string ReadCpuName()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "CPU";
        }
        catch
        {
            return "CPU";
        }
    }
}
