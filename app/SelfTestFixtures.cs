using System.Text.RegularExpressions;

namespace PcWatch;

/// <summary>
/// Synthetic snapshots and the two assertions every self-test case is built from.
/// </summary>
/// <remarks>
/// 2026-08-31. Split out of SelfTest at the 200-line limit and shared with SelfTestLive.
///
/// Both assertions exist because a rule has two ways to be wrong and only one of them is obvious.
/// AssertFires catches a rule that stopped matching. AssertSilent catches a rule that matches too
/// much - the failure that produced a HIGH alarm about vmmemWSL using 0.9% of the machine, and the
/// one that no amount of "does it detect the problem?" testing would ever have found.
/// </remarks>
public static class SelfTestFixtures
{
    public static ProcessLoad Proc(string name, int id, double percent) =>
        new(name, id, percent, 100L * 1024 * 1024, DateTime.Now.AddHours(-2), 1);

    public static Snapshot Sample(double totalCpu, params ProcessLoad[] processes) =>
        Sample(totalCpu, 2, 10, 64, 60, processes);

    public static Snapshot Sample(
        double totalCpu, double uptimeDays = 2, double ramUsed = 10, double ramTotal = 64,
        double? clock = 60, params ProcessLoad[] processes) =>
        new(totalCpu, 24, processes,
            new MachineStats(clock, ramUsed, ramTotal, Uptime(uptimeDays), "Test CPU"),
            DateTime.Now, [], []);

    /// <summary>A snapshot with specific disk figures, for the capacity-versus-performance cases.</summary>
    public static Snapshot SampleWithDisk(double totalCpu, double freeGb, double totalGb) =>
        new(totalCpu, 24, [],
            new MachineStats(60, 10, 64, Uptime(2), "Test CPU"),
            DateTime.Now, [], [], null, null, freeGb, totalGb);

    /// <summary>An uptime where the real power-on and the kernel counter agree.</summary>
    public static UptimeFacts Uptime(double days) =>
        new(TimeSpan.FromDays(days), DateTime.Now.AddDays(-days), TimeSpan.FromDays(days), false);

    /// <summary>Assert that some finding matches <paramref name="pattern"/>, at a given severity.</summary>
    public static void AssertFires(Snapshot snapshot, string pattern, Severity expected)
    {
        IReadOnlyList<Suspect> found = SuspectAnalyzer.Analyze(snapshot);
        Suspect hit = found.FirstOrDefault(s => Regex.IsMatch(s.Title, pattern))
            ?? throw new Exception(
                $"nothing matched /{pattern}/; got: {string.Join(" | ", found.Select(s => s.Title))}");

        if (hit.Severity != expected)
        {
            throw new Exception($"severity was {hit.Severity}, expected {expected}");
        }
    }

    /// <summary>Assert that NO finding matches - the half that catches an over-eager rule.</summary>
    public static void AssertSilent(Snapshot snapshot, string pattern)
    {
        var hit = SuspectAnalyzer.Analyze(snapshot).Where(s => Regex.IsMatch(s.Title, pattern)).ToList();
        if (hit.Count > 0)
        {
            throw new Exception($"expected silence, got: {string.Join(" | ", hit.Select(s => s.Title))}");
        }
    }
}
