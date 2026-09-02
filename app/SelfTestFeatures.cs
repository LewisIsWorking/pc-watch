using static PcWatch.SelfTestFixtures;

namespace PcWatch;

/// <summary>
/// Self-test cases for the parts added in 1.1: power, health, killing and updates.
/// </summary>
/// <remarks>
/// 2026-09-02. The kill denylist gets the most attention here, because it is the only feature in
/// this app that can destroy something. Everything else reports; that one acts.
/// </remarks>
public static class SelfTestFeatures
{
    public static void Run(SelfTestRunner r)
    {
        r.Section("KILLING: THE DENYLIST IS THE FEATURE");
        foreach (string critical in new[] { "csrss", "wininit", "winlogon", "services", "smss", "lsass", "System", "svchost" })
        {
            r.Check($"{critical} is refused", () =>
            {
                var (allowed, reason) = ProcessKiller.CanKill(critical);
                if (allowed) throw new Exception("would have been killed - this bugchecks Windows");
                if (string.IsNullOrWhiteSpace(reason)) throw new Exception("refused with no reason given");
            });
        }
        r.Check("case does not defeat the denylist (CSRSS)", () =>
        {
            if (ProcessKiller.CanKill("CSRSS").Allowed) throw new Exception("case-sensitive denylist");
        });
        r.Check("an ordinary process is allowed", () =>
        {
            if (!ProcessKiller.CanKill("notepad").Allowed) throw new Exception("notepad refused");
        });
        r.Check("explorer is allowed but carries a warning", () =>
        {
            var (allowed, warning) = ProcessKiller.CanKill("explorer");
            if (!allowed) throw new Exception("explorer refused");
            if (string.IsNullOrWhiteSpace(warning)) throw new Exception("no warning given");
        });
        r.Check("a stale pid is not killed by name mismatch", () =>
        {
            // Our own pid, claimed to be something else: the guard must refuse rather than kill us.
            var (killed, message) = ProcessKiller.Kill(Environment.ProcessId, "definitely-not-this");
            if (killed) throw new Exception("killed the wrong process");
            if (!message.Contains("reused", StringComparison.OrdinalIgnoreCase)) throw new Exception(message);
        });

        r.Section("POWER: MEASURED AND ESTIMATED MUST STAY APART");
        r.Check("the CPU figure always carries its basis", () =>
        {
            PowerReport report = PowerEstimate.Build("AMD Ryzen 9 5900X 12-Core Processor", 50, null);
            if (report.CpuWatts is null) throw new Exception("no estimate produced");
            if (string.IsNullOrWhiteSpace(report.CpuBasis)) throw new Exception("no basis recorded");
            if (!report.CpuBasis.Contains("estimated")) throw new Exception($"basis not marked as an estimate: {report.CpuBasis}");
        });
        r.Check("power rises with load and stays inside the package limit", () =>
        {
            const string cpu = "AMD Ryzen 9 5900X 12-Core Processor";
            double idle = PowerEstimate.ForLoad(cpu, 0).Watts;
            double half = PowerEstimate.ForLoad(cpu, 50).Watts;
            double full = PowerEstimate.ForLoad(cpu, 100).Watts;

            if (!(idle < half && half < full)) throw new Exception($"not monotonic: {idle}/{half}/{full}");
            if (full > 142) throw new Exception($"exceeds the 142 W package limit: {full}");
            r.Note($"5900X estimate: idle {idle:N0} W, 50% {half:N0} W, 100% {full:N0} W");
        });
        r.Check("an unknown CPU still produces a labelled estimate", () =>
        {
            var (watts, basis) = PowerEstimate.ForLoad("Some Unknown CPU", 50);
            if (watts <= 0) throw new Exception("no estimate");
            if (!basis.Contains("generic")) throw new Exception($"fallback not disclosed: {basis}");
        });
        r.Check("with no GPU reading there is no system total", () =>
        {
            if (PowerEstimate.Build("AMD Ryzen 9 5900X", 50, null).EstimatedSystemWatts is not null)
            {
                throw new Exception("invented a system total from a missing GPU reading");
            }
        });

        r.Section("HEALTH: THE WORST INDICATOR, NOT THE AVERAGE");
        r.Check("100% CPU with everything else idle is not 'healthy'", () =>
        {
            var indicators = SystemHealth.Assess(Sample(100));
            var (word, severity) = SystemHealth.Overall(indicators);
            if (severity != Severity.High) throw new Exception($"graded {severity} ({word})");
        });
        r.Check("a quiet machine reads healthy", () =>
        {
            var (word, _) = SystemHealth.Overall(SystemHealth.Assess(Sample(5)));
            if (word != "HEALTHY") throw new Exception(word);
        });
        r.Check("memory at 95% is High even when CPU is idle", () =>
        {
            var indicators = SystemHealth.Assess(Sample(5, ramUsed: 61, ramTotal: 64));
            if (SystemHealth.Overall(indicators).Severity != Severity.High) throw new Exception("missed memory pressure");
        });

        r.Section("VERSIONS AND WINDOW PLACEMENT");
        r.Check("1.10.0 is NEWER than 1.9.0 (string compare would say otherwise)", () =>
        {
            if (AppVersion.Compare("1.10.0", "1.9.0") <= 0) throw new Exception("compared as strings");
            if (string.CompareOrdinal("1.10.0", "1.9.0") >= 0) throw new Exception("premise wrong");
        });
        r.Check("a leading v on a release tag is tolerated", () =>
        {
            if (AppVersion.Compare("v2.0.0", "1.9.9") <= 0) throw new Exception("v prefix broke it");
        });
        r.Check("the same version is not an update", () =>
        {
            if (AppVersion.Compare(AppVersion.Number, AppVersion.Number) != 0) throw new Exception("not equal to itself");
        });
        r.Check("an off-screen saved position is rejected", () =>
        {
            if (SettingsStore.IsOnScreen(new Rectangle(-30000, -30000, 900, 700)))
            {
                throw new Exception("would restore to a window nobody can reach");
            }
        });
        r.Check("a position on a real screen is accepted", () =>
        {
            Rectangle work = Screen.PrimaryScreen!.WorkingArea;
            if (!SettingsStore.IsOnScreen(new Rectangle(work.X + 40, work.Y + 40, 900, 700)))
            {
                throw new Exception("rejected a valid placement");
            }
        });
        r.Check("a degenerate saved size is rejected", () =>
        {
            if (SettingsStore.IsOnScreen(new Rectangle(0, 0, 0, 0))) throw new Exception("accepted a zero-size window");
        });

        r.Section("GPU (skips cleanly when there is none)");
        r.Check("GPU telemetry never throws, with or without an NVIDIA card", () =>
        {
            using var gpu = new GpuTelemetry();
            GpuReading? reading = gpu.Read();
            r.Note(reading is null
                ? "no NVIDIA GPU detected - the app runs without it"
                : $"{reading.Name}: {reading.Watts:N0} W of {reading.WattLimit:N0} W, "
                  + $"{reading.UtilisationPercent}%, {reading.TemperatureC} C");
        });
    }
}
