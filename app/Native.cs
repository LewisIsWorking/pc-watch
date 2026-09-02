using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PcWatch;

/// <summary>
/// The Win32 calls PcWatch cannot do without. No WMI anywhere: see the timings below.
/// </summary>
/// <remarks>
/// 2026-08-31, measured on this box (Ryzen 9 5900X, 24 logical):
///
///   CPU LOAD. GetSystemTimes is the only source fast enough for a one-second tick.
///     Get-Counter '\Processor Information(_Total)\% Processor Time'    2127 ms
///     CIM Win32_PerfFormattedData_Counters_ProcessorInformation        1475 ms
///     GetSystemTimes                                                    under 1 ms
///   It is also the source Task Manager is built on, so the figure agrees with Task Manager
///   instead of becoming a fourth rival number.
///
///   CLOCK AND MEMORY. WMI (Win32_Processor / Win32_OperatingSystem) costs ~1.5 s per query, which
///   forced the PowerShell original onto a slow 10-second cadence for those. CallNtPowerInformation
///   and GlobalMemoryStatusEx answer the same questions in microseconds with no dependency, so they
///   can simply run every tick.
///
///   DestroyIcon. Icon.FromHandle does NOT take ownership of the HICON that Bitmap.GetHicon()
///   created. Disposing the managed Icon frees the wrapper and leaks the unmanaged handle. At one
///   fresh icon per second that is 3600 leaked GDI handles an hour against a 10000-handle process
///   quota, so an unfixed build dies in under three hours, which is long enough to look correct.
/// </remarks>
internal static partial class Native
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [LibraryImport("kernel32.dll")]
    private static partial ulong GetTickCount64();

    [LibraryImport("powrprof.dll")]
    private static partial uint CallNtPowerInformation(
        int informationLevel, IntPtr inputBuffer, uint inputSize, IntPtr outputBuffer, uint outputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    private const int ProcessorInformationLevel = 11;

    /// <summary>
    /// Raw kernel CPU tick totals since boot, in 100 ns units.
    /// </summary>
    /// <remarks>
    /// ⚠️ kernelTime ALREADY INCLUDES idleTime. Busy work is (kernel + user) - idle over a total of
    /// (kernel + user). Treating kernel as exclusive of idle yields a number that looks plausible at
    /// every load level, which is the worst kind of wrong: nothing ever looks off enough to check.
    /// Verified against the perf counter over matched 12-second windows, bias 1.1 / 0.0 / 0.1 points.
    /// </remarks>
    public static (long Idle, long Total) GetCpuTicks()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetSystemTimes failed");
        }
        return (idle, kernel + user);
    }

    /// <summary>
    /// Time since boot INCLUDING time spent asleep.
    /// </summary>
    /// <remarks>
    /// ⛔ 2026-09-02. This used to be Environment.TickCount64, and that is not a stable definition.
    ///    Measured on this machine, seconds apart, running the same three lines of code:
    ///
    ///      .NET 10.0.11                    19.83 days   (== GetTickCount64)
    ///      .NET 11.0.0-preview.7           15.71 days   (== QueryUnbiasedInterruptTime)
    ///      difference                       4.12 days   (time the PC has spent asleep)
    ///
    ///    .NET 11 Preview 7 switched Environment.TickCount64 to the UNBIASED interrupt time, which
    ///    excludes sleep. Both numbers are defensible; they answer different questions. Since this
    ///    value exists only to explain the larger figure Task Manager shows - and Task Manager uses
    ///    the sleep-INCLUSIVE one - the API is now named explicitly so the meaning cannot drift with
    ///    the runtime the app happens to be built against.
    ///
    ///    Found by accident: the .NET 11 build of this app printed 15.7 days for a counter the .NET
    ///    10 build had reported as 18.8 the day before. A counter that goes backwards is the only
    ///    reason anyone looked.
    /// </remarks>
    public static TimeSpan TimeSinceBootIncludingSleep() => TimeSpan.FromMilliseconds(GetTickCount64());

    /// <summary>Physical memory in gigabytes: used and total.</summary>
    public static (double UsedGb, double TotalGb) GetMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0);

        const double gb = 1024d * 1024 * 1024;
        double total = status.TotalPhys / gb;
        return (total - status.AvailPhys / gb, total);
    }

    /// <summary>
    /// Current clock as a percentage of base, averaged across cores, or null if unavailable.
    /// </summary>
    /// <remarks>
    /// ⛔ THIS IS NOT LOAD, and nothing may present it as such. It is the quantity a Windhawk taskbar
    /// mod was displaying as "CPU 98%" while Task Manager read 67%. Measured 2026-08-31: it held at
    /// 98.6 across five samples while real load moved 90 to 100%, then load halved to 50% and it
    /// still read 98. On a chip parked near its base clock it reads ~98% forever, idle or not. It is
    /// carried solely so the UI can explain where that other number comes from.
    /// </remarks>
    public static double? GetClockPercentOfBase()
    {
        int count = Environment.ProcessorCount;
        int size = Marshal.SizeOf<ProcessorPowerInformation>();
        IntPtr buffer = Marshal.AllocHGlobal(size * count);
        try
        {
            if (CallNtPowerInformation(ProcessorInformationLevel, IntPtr.Zero, 0, buffer, (uint)(size * count)) != 0)
            {
                return null;
            }

            double currentSum = 0, baseSum = 0;
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<ProcessorPowerInformation>(buffer + i * size);
                currentSum += info.CurrentMhz;
                baseSum += info.MaxMhz;
            }
            return baseSum > 0 ? Math.Round(100 * currentSum / baseSum, 0) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Free an HICON produced by <see cref="System.Drawing.Bitmap.GetHicon"/>.</summary>
    public static void ReleaseIconHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero) DestroyIcon(handle);
    }
}
