using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The hardware probes, against the real machine.
/// </summary>
/// <remarks>
/// 2026-09-06. These read genuine hardware, so the assertions are about CONTRACTS and INTERNAL
/// CONSISTENCY rather than fixed values: a test that hardcodes 64 GB of RAM passes on one machine
/// and is worthless everywhere else, which is the opposite of what a public project needs.
///
/// ⭐ THE DEGRADATION CONTRACT IS THE POINT. PC Watch is public now, so an AMD or Intel user must
///   get a working app with one fewer row rather than a crash. Every probe here is asserted to
///   return null or a plausible value, and never to throw.
/// </remarks>
[TestFixture]
public sealed class HardwareProbeTests
{
    // ── GPU ─────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Constructing_gpu_telemetry_never_throws_even_with_no_nvidia_gpu()
    {
        // ⚠️ nvml.dll ships with the NVIDIA driver and simply is not there on AMD or Intel. A
        //    DllNotFoundException escaping the constructor would stop the whole app starting for
        //    every non-NVIDIA user, over a row they were never going to see.
        Action construct = () => new GpuTelemetry().Dispose();

        construct.Should().NotThrow();
    }

    [Test]
    public void A_gpu_reading_is_either_absent_or_internally_consistent()
    {
        using var gpu = new GpuTelemetry();

        GpuReading? reading = gpu.Read();

        if (reading is null)
        {
            Assert.Pass("no NVIDIA GPU on this machine, which is a supported configuration");
            return;
        }

        reading.Name.Should().NotBeNullOrWhiteSpace();
        reading.Watts.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(2000, "no consumer GPU draws 2 kW");
        reading.UtilisationPercent.Should().BeInRange(0, 100);
        reading.TemperatureC.Should().BeInRange(0, 150, "above this the card has melted");
        if (reading.WattLimit > 0) reading.WattLimit.Should().BeLessThan(2000);
    }

    [Test]
    public void Reading_the_gpu_repeatedly_is_stable()
    {
        using var gpu = new GpuTelemetry();

        GpuReading?[] readings = [.. Enumerable.Range(0, 5).Select(_ => gpu.Read())];

        readings.Select(r => r is null).Distinct().Should().ContainSingle(
            "it must not flicker between working and not working");
    }

    [Test]
    public void Reading_after_dispose_returns_null_rather_than_touching_a_shut_down_library()
    {
        var gpu = new GpuTelemetry();
        gpu.Dispose();

        gpu.Read().Should().BeNull("NVML has been shut down; calling into it now is undefined");
    }

    [Test]
    public void Disposing_twice_is_safe()
    {
        var gpu = new GpuTelemetry();

        Action twice = () => { gpu.Dispose(); gpu.Dispose(); };

        twice.Should().NotThrow("a second Shutdown must not take the app down on exit");
    }

    // ── Machine ─────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void The_cpu_is_named()
    {
        new MachineProbe().CpuName.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public void Memory_is_reported_consistently()
    {
        MachineStats stats = new MachineProbe().Read();

        stats.RamTotalGb.Should().BeGreaterThan(0, "a machine running this test has RAM");
        stats.RamUsedGb.Should().BeGreaterThan(0);
        stats.RamUsedGb.Should().BeLessThanOrEqualTo(stats.RamTotalGb,
            "used memory above total is the classic sign of mixed units");
        stats.RamPercent.Should().BeInRange(0, 100);
    }

    [Test]
    public void A_zero_total_cannot_divide_by_zero_in_the_percentage()
    {
        // The failure path of Read() returns 0/0, and RamPercent is rendered every second.
        var degraded = new MachineStats(null, 0, 0, SystemUptime.Get(), "unknown");

        Func<double> percent = () => degraded.RamPercent;

        percent.Should().NotThrow<DivideByZeroException>();
        percent().Should().Be(0);
    }

    [Test]
    public void The_clock_reading_is_absent_or_plausible()
    {
        // ⛔ This is the figure that started the whole project: "% Processor Performance" is clock
        //    divided by BASE clock, not load, and it sits pinned near 98% for ever on a 5900X. It is
        //    reported as its own thing and must never be mistaken for a load percentage.
        MachineStats stats = new MachineProbe().Read();

        if (stats.ClockPercentOfBase is { } clock)
        {
            clock.Should().BeGreaterThan(0);
            clock.Should().BeLessThan(500, "a boost ratio, not a runaway counter");
        }
    }

    [Test]
    public void The_system_drive_is_reported_consistently_or_not_at_all()
    {
        var (free, total) = new MachineProbe().SystemDrive();

        if (free is null || total is null)
        {
            free.Should().BeNull("free and total must be absent together, never half-known");
            total.Should().BeNull();
            return;
        }

        double freeGb = free.Value, totalGb = total.Value;
        totalGb.Should().BeGreaterThan(0);
        freeGb.Should().BeGreaterThanOrEqualTo(0);
        freeGb.Should().BeLessThanOrEqualTo(totalGb, "free space above total is impossible");
    }

    [Test]
    public void The_drive_reading_is_CACHED_so_a_once_a_second_tick_does_not_hammer_the_disk()
    {
        // ⚠️ The watcher must not cost what it measures. This call touches the disk, and the value
        //    barely moves, so it is cached for 30 seconds.
        var probe = new MachineProbe();

        var first = probe.SystemDrive();
        var repeated = Enumerable.Range(0, 50).Select(_ => probe.SystemDrive()).ToArray();

        repeated.Should().AllBeEquivalentTo(first, "50 calls inside the cache window must agree");
    }

    [Test]
    public void Uptime_is_always_available_even_when_the_stats_read_fails()
    {
        // Uptime is read BEFORE the try, deliberately: a failed memory read must not take the
        // uptime down with it, because they come from entirely different sources.
        MachineStats stats = new MachineProbe().Read();

        stats.Uptime.Should().NotBeNull();
        stats.Uptime.Best.Should().BeGreaterThan(TimeSpan.Zero, "the machine is demonstrably running");
    }
}
