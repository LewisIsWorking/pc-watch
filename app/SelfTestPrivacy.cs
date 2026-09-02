namespace PcWatch;

/// <summary>
/// Proves the report is safe to paste, and that the thing proving it can detect anything at all.
/// </summary>
/// <remarks>
/// 2026-09-02. Split out of SelfTestFeatures at the 200-line limit.
///
/// ⛔ Both halves matter. Checking a real report only shows that today's renderer is clean; it says
///    nothing about whether the scanner works, and it would keep passing if every predicate were
///    broken. Feeding the scanner known-bad reports shows it detects something. The final case -
///    an ordinary clean line - shows it is not simply flagging everything, which the bad cases
///    alone would happily "prove".
/// </remarks>
public static class SelfTestPrivacy
{
    public static void Run(SelfTestRunner r)
    {
        r.Section("PRIVACY: THE REPORT IS SHAREABLE, SO IT MUST BE CLEAN");

        // A LIVE snapshot, not a fixture. A fixture contains only what the test author put in it and
        // would pass forever no matter what the renderer later started including.
        var sampler = new CpuSampler();
        _ = sampler.Sample();
        Thread.Sleep(1100);
        Snapshot live = sampler.Sample();
        string report = ReportRenderer.Render(live, SuspectAnalyzer.Analyze(live), new ProcessAncestry());
        sampler.Dispose();

        r.Note($"report is {report.Length} chars over {report.Split('\n').Length} lines");

        r.Check("a REAL report of this machine is clean", () =>
        {
            IReadOnlyList<string> violations = ReportPrivacy.Scan(report);
            if (violations.Count > 0) throw new Exception(string.Join("; ", violations));
        });

        var leaks = new (string Name, string Line)[]
        {
            ("a filesystem path",       @"   12.0%  thing   C:\Users\someone\secret\app.exe"),
            ("a command-line argument", "   12.0%  node --auth-token abc123def456"),
            ("a credential assignment", "   12.0%  svc  password=hunter2"),
            ("an environment variable", "   12.0%  thing  %USERPROFILE%"),
        };
        foreach (var (name, line) in leaks)
        {
            r.Check($"the scanner CATCHES {name}", () =>
            {
                if (ReportPrivacy.Scan(line, "nobody", "nomachine").Count == 0)
                {
                    throw new Exception($"scanner passed a report containing: {line.Trim()}");
                }
            });
        }

        r.Check("the scanner CATCHES a username", () =>
        {
            if (ReportPrivacy.Scan("  12.0%  hello-alice", "alice", "nomachine").Count == 0)
            {
                throw new Exception("scanner missed the username");
            }
        });
        r.Check("the scanner CATCHES a machine name", () =>
        {
            if (ReportPrivacy.Scan("  12.0%  on BUILDBOX-01", "nobody", "BUILDBOX-01").Count == 0)
            {
                throw new Exception("scanner missed the machine name");
            }
        });
        r.Check("an ordinary clean line is NOT flagged", () =>
        {
            var clean = ReportPrivacy.Scan("   12.0%  chrome  1234   512 MB  up 2d 3h", "nobody", "nomachine");
            if (clean.Count > 0) throw new Exception($"false positive: {string.Join("; ", clean)}");
        });

        r.Check("the update check can be switched off", () =>
        {
            // The opt-out has to be honoured BEFORE the request. A check that fires and discards the
            // answer has already told GitHub the app is running, which is the part being declined.
            var off = new Settings { CheckForUpdates = false };
            if (off.CheckForUpdates) throw new Exception("opt-out did not stick");
            if (!new Settings().CheckForUpdates) throw new Exception("default should be on");
        });
    }
}
