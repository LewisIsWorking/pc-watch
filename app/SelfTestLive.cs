using static PcWatch.SelfTestFixtures;

namespace PcWatch;

/// <summary>
/// Self-test cases that touch the real machine, plus the Fast Startup uptime regression.
/// </summary>
/// <remarks>
/// 2026-08-31. Split out of SelfTest at the 200-line limit. Kept together because these are the
/// cases whose inputs cannot be fabricated: they read this machine, so a failure here means the
/// world changed rather than the logic did, and that is worth being able to run on its own.
/// </remarks>
public static class SelfTestLive
{
    /// <summary>
    /// The bug: 18.8 days reported for a machine that had been on for a day and a half.
    /// </summary>
    /// <remarks>
    /// ⛔ Fast Startup hibernates the kernel session on shutdown, so GetTickCount64 and WMI
    ///    LastBootUpTime both survive it. They agreed with each other exactly, and were both wrong
    ///    for the question being asked. Two sources agreeing is not evidence that either is right.
    /// </remarks>
    public static void RunUptimeChecks(SelfTestRunner r)
    {
        r.Section("UPTIME MUST NOT BELIEVE THE KERNEL COUNTER");

        // Real power-on 1.5 days ago, kernel counter still reading 18.8 - exactly what Fast Startup
        // produces, and exactly the reading that was correctly rejected as wrong.
        var fastStartup = new UptimeFacts(
            TimeSpan.FromDays(1.5), DateTime.Now.AddDays(-1.5), TimeSpan.FromDays(18.8), true);

        r.Check("'on for' prefers the real power-on over the kernel counter", () =>
        {
            if (Math.Abs(fastStartup.Best.TotalDays - 1.5) > 0.01)
            {
                throw new Exception($"reported {fastStartup.Best.TotalDays:N1} days, expected 1.5");
            }
        });
        r.Check("the disagreement is detected and explained", () =>
        {
            if (!fastStartup.CountersDisagree) throw new Exception("disagreement not detected");
            string note = SystemUptime.DisagreementNote(fastStartup) ?? throw new Exception("no note");
            if (!note.Contains("Fast Startup")) throw new Exception($"note does not name the cause: {note}");
        });
        r.Check("the 7-day rule does NOT fire on a 1.5-day machine with an 18.8-day counter",
            () => AssertSilent(
                new Snapshot(20, 24, [], new MachineStats(60, 10, 64, fastStartup, "Test"), DateTime.Now, [], []),
                "On for"));
        r.Check("agreeing counters produce no note", () =>
        {
            if (SystemUptime.DisagreementNote(Uptime(3)) is not null) throw new Exception("note shown anyway");
        });
        r.Check("an unreadable event log falls back to the kernel counter", () =>
        {
            var noEventLog = new UptimeFacts(null, null, TimeSpan.FromDays(4), true);
            if (Math.Abs(noEventLog.Best.TotalDays - 4) > 0.01) throw new Exception("bad fallback");
            if (noEventLog.CountersDisagree) throw new Exception("claimed disagreement with nothing to compare");
        });
        r.Check("the live machine resolves a real power-on time", () =>
        {
            UptimeFacts live = SystemUptime.Get();
            r.Note($"on for {ReportRenderer.Age(live.Best)}"
                 + (live.OnSince is { } s ? $" (since {s:ddd d MMM HH:mm})" : " (kernel counter - log unreadable)")
                 + $", kernel counter {live.SinceKernelBoot.TotalDays:N1} days, fast startup {live.FastStartupEnabled}");

            if (live.OnSince is null) throw new Exception("could not read a boot/resume event");
            if (live.Best > live.SinceKernelBoot + TimeSpan.FromMinutes(5))
            {
                throw new Exception("on-for exceeds time since kernel boot, which is impossible");
            }
        });
    }

    public static void RunMachineChecks(SelfTestRunner r)
    {
        r.Section("AGAINST THE LIVE MACHINE");

        r.Check("GetSystemTimes advances between two reads", () =>
        {
            var first = Native.GetCpuTicks();
            Thread.Sleep(300);
            if (Native.GetCpuTicks().Total <= first.Total) throw new Exception("tick total did not advance");
        });
        r.Check("memory reads back a sane total", () =>
        {
            var (used, total) = Native.GetMemory();
            if (total <= 0 || used <= 0 || used > total) throw new Exception($"used {used}, total {total}");
        });
        r.Check("this process resolves to a real owner", () =>
        {
            string label = new ProcessAncestry().OwnerLabelFor(Environment.ProcessId)
                           ?? throw new Exception("no owner label for self");
            r.Note(label);
        });
        r.Check("an unknown pid yields no owner and does not throw", () =>
        {
            if (new ProcessAncestry().OwnerLabelFor(999999) is not null) throw new Exception("expected null");
        });
        r.Check("the first sample reports null rather than a fake zero", () =>
        {
            if (new CpuSampler().Sample().TotalCpuPercent is not null) throw new Exception("expected null");
        });
        r.Check("no process exceeds 100% (core-count normalisation applied)", () =>
        {
            var sampler = new CpuSampler();
            _ = sampler.Sample();
            Thread.Sleep(1200);
            Snapshot s = sampler.Sample();
            foreach (ProcessLoad p in s.TopProcesses)
            {
                if (p.Percent > 100) throw new Exception($"{p.Name} reported {p.Percent}% - per-core leak");
            }
            r.Note($"CPU {s.TotalCpuPercent:N1}%, top {s.TopProcesses.Count} processes");
        });
        r.Check("300 tray icons do not leak GDI handles", () =>
        {
            using var renderer = new TrayIconRenderer();
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            self.Refresh();
            int before = self.HandleCount;
            for (int i = 0; i < 300; i++) _ = renderer.Render(i % 100);
            self.Refresh();
            int growth = self.HandleCount - before;
            r.Note($"handle growth over 300 renders: {growth}");
            if (growth > 100) throw new Exception($"leaked {growth} handles - DestroyIcon not reached");
        });
    }
}
