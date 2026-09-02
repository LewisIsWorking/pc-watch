using static PcWatch.SelfTestFixtures;

namespace PcWatch;

/// <summary>
/// <c>PcWatch.exe --self-test</c> - feeds every rule the bug it was written for, then the near-miss.
/// </summary>
/// <remarks>
/// 2026-08-31. Ported from the PowerShell suite this app replaces, so retiring those scripts did not
/// retire their evidence. A heuristic nobody has fed a known-bad input to has only ever been
/// observed agreeing with itself.
///
/// The SECOND half of each pair is the important one. The first draft of the analyzer flagged
/// vmmemWSL at 0.9% as HIGH while the machine's real problem sat at 14%: a false alarm trains you to
/// ignore the panel, which is worse than saying nothing at all.
/// </remarks>
public static class SelfTest
{
    public static int Run()
    {
        SelfTestRunner.EnsureConsole();
        var r = new SelfTestRunner();

        r.Section("THE BUG THAT PROMPTED THE SEVERITY RULE");
        r.Check("vmmemWSL at 0.9% is not reported at all",
            () => AssertSilent(Sample(20, Proc("vmmemWSL", 45616, 0.9)), "vmmemWSL"));
        r.Check("the same class of process at 14% IS reported, as High",
            () => AssertFires(Sample(30, Proc("qemu-system-x86_64", 15264, 14)), "qemu-system", Severity.High));
        r.Check("at 4% it is reported only as Low",
            () => AssertFires(Sample(20, Proc("vmmem", 100, 4)), "vmmem", Severity.Low));

        r.Section("EACH RULE FIRES ON ITS OWN BUG");
        r.Check("headless browser at 36.7%",
            () => AssertFires(Sample(50, Proc("chrome-headless-shell", 26028, 36.7)), "headless", Severity.High));
        r.Check("explorer at 8.5%, which should idle near zero",
            () => AssertFires(Sample(20, Proc("explorer", 45804, 8.5)), "explorer", Severity.Medium));
        r.Check("an unrecognised process at 22% hits the catch-all",
            () => AssertFires(Sample(30, Proc("SomeThing", 999, 22)), "SomeThing", Severity.High));
        r.Check("being on for 18.4 days",
            () => AssertFires(Sample(10, uptimeDays: 18.4), "On for", Severity.Low));
        r.Check("memory at 92%",
            () => AssertFires(Sample(10, ramUsed: 59, ramTotal: 64), "Memory", Severity.High));

        r.Section("DOUBLE-REPORTING AND THE SUBSTRING TRAP");
        r.Check("a VM at 30% is reported once, not also by the catch-all", () =>
        {
            int hits = SuspectAnalyzer.Analyze(Sample(40, Proc("qemu-system-x86_64", 15264, 30)))
                                      .Count(f => f.ProcessId == 15264);
            if (hits != 1) throw new Exception($"reported {hits} times");
        });
        r.Check("pid 152 is not swallowed by a claimed pid 15264",
            () => AssertFires(Sample(60, Proc("qemu-system-x86_64", 15264, 30), Proc("other", 152, 20)),
                              @"\(pid 152\)", Severity.High));

        r.Section("THE ALL-CLEAR MUST BE EARNED");
        r.Check("a quiet machine says nothing is wrong",
            () => AssertFires(Sample(12), "Nothing obviously wrong", Severity.Low));
        r.Check("88% spread thin does NOT report all-clear",
            () => AssertSilent(Sample(88, Proc("a", 1, 3)), "Nothing obviously wrong"));
        r.Check("88% with no single culprit says exactly that",
            () => AssertFires(Sample(88), "no single process to blame", Severity.Medium));

        r.Section("THE FREQUENCY NOTE");
        r.Check("clock 98% against load 49% produces the explanation", () =>
        {
            if (SuspectAnalyzer.FrequencyNote(Sample(49, clock: 98)) is null) throw new Exception("no note");
        });
        r.Check("clock 98% against load 95% stays silent", () =>
        {
            if (SuspectAnalyzer.FrequencyNote(Sample(95, clock: 98)) is not null) throw new Exception("note shown anyway");
        });
        r.Check("a missing clock reading does not throw",
            () => _ = SuspectAnalyzer.FrequencyNote(Sample(50, clock: null)));

        SelfTestFeatures.Run(r);
        SelfTestStorage.Run(r);
        SelfTestLive.RunUptimeChecks(r);
        SelfTestLive.RunMachineChecks(r);
        return r.Summarise();
    }
}
