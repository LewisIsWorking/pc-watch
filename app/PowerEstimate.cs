namespace PcWatch;

/// <summary>Power draw: what was measured, and what was only estimated.</summary>
/// <remarks>
/// ⚠️ <see cref="CpuWatts"/> is an ESTIMATE and <see cref="GpuWatts"/> is a MEASUREMENT. They are
/// separate properties, and every renderer must keep the distinction visible. Averaging them into
/// one "system watts" figure would launder a guess into a reading.
/// </remarks>
public sealed record PowerReport(double? CpuWatts, double? GpuWatts, double? GpuLimitWatts, string CpuBasis)
{
    /// <summary>Rough whole-box draw. Null unless both parts are known.</summary>
    public double? EstimatedSystemWatts =>
        CpuWatts is { } cpu && GpuWatts is { } gpu ? cpu + gpu + OtherComponentsWatts : null;

    /// <summary>
    /// Everything that is neither CPU nor GPU: board, RAM, drives, fans, PSU losses.
    /// </summary>
    /// <remarks>
    /// A flat allowance, not a measurement. On a desktop with several drives and a bank of fans this
    /// is 50-80 W and does not vary much with load, so a constant is closer to the truth than
    /// pretending it is zero. Named rather than folded silently into the CPU figure.
    /// </remarks>
    public const double OtherComponentsWatts = 60;
}

/// <summary>
/// Estimates CPU package power from load, because nothing else can read it without a driver.
/// </summary>
/// <remarks>
/// 2026-09-02. AMD does not expose package power to unprivileged code. LibreHardwareMonitor reads it
/// by installing a kernel driver, which needs administrator rights an agent does not have and a user
/// may not want. So this interpolates between an idle floor and the chip's sustained package limit.
///
/// ⛔ IT IS A MODEL, NOT A SENSOR. It cannot see per-core boost behaviour, undervolting, or a power
///    limit the user has changed in BIOS, and it will be wrong by tens of watts on a heavily tuned
///    machine. The basis string travels with the number so the UI can say where it came from - a
///    plausible unlabelled figure is indistinguishable from a measured one, which is precisely how
///    this app's original CPU-percentage confusion started.
/// </remarks>
public static class PowerEstimate
{
    private sealed record Profile(string Match, double IdleWatts, double MaxWatts, string Label);

    // Sustained package power (PPT for Ryzen, PL1/PL2 for Intel), not marketing TDP. Idle figures
    // are package idle for a desktop part with several cores parked.
    private static readonly Profile[] Profiles =
    [
        new("Ryzen 9 79", 35, 230, "Ryzen 9 7000 PPT 230 W"),
        new("Ryzen 9 59", 25, 142, "Ryzen 9 5000 PPT 142 W"),
        new("Ryzen 9",    30, 180, "Ryzen 9 generic"),
        new("Ryzen 7",    22, 142, "Ryzen 7 generic"),
        new("Ryzen 5",    20, 88,  "Ryzen 5 generic"),
        new("Core i9",    25, 250, "Core i9 generic"),
        new("Core i7",    20, 190, "Core i7 generic"),
        new("Core i5",    15, 150, "Core i5 generic"),
    ];

    private const double FallbackIdle = 20;
    private const double FallbackMax = 125;

    /// <summary>
    /// Estimated package watts for a load percentage, plus a plain description of the assumption.
    /// </summary>
    /// <remarks>
    /// Power against load is closer to quadratic than linear on a modern part, because voltage rises
    /// with frequency and power goes as roughly V squared. A straight line badly over-estimates the
    /// middle of the range, so this uses load^1.6 - still a model, but a less wrong one.
    /// </remarks>
    public static (double Watts, string Basis) ForLoad(string cpuName, double loadPercent)
    {
        Profile? profile = Profiles.FirstOrDefault(
            p => cpuName.Contains(p.Match, StringComparison.OrdinalIgnoreCase));

        double idle = profile?.IdleWatts ?? FallbackIdle;
        double max = profile?.MaxWatts ?? FallbackMax;
        string label = profile?.Label ?? $"generic desktop CPU, {FallbackIdle}-{FallbackMax} W";

        double fraction = Math.Pow(Math.Clamp(loadPercent, 0, 100) / 100.0, 1.6);
        double watts = idle + (max - idle) * fraction;

        return (Math.Round(watts, 0), $"estimated from load, {label}");
    }

    /// <summary>Build the full report from a CPU load figure and an optional GPU reading.</summary>
    public static PowerReport Build(string cpuName, double? loadPercent, GpuReading? gpu)
    {
        if (loadPercent is not { } load)
        {
            return new PowerReport(null, gpu?.Watts, gpu?.WattLimit, "not yet measured");
        }

        var (watts, basis) = ForLoad(cpuName, load);
        return new PowerReport(watts, gpu?.Watts, gpu?.WattLimit, basis);
    }
}
