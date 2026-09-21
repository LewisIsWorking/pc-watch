namespace PcWatch;

/// <summary>
/// The startup half of an update: wait for the replaced process to go, then remove what it left.
/// </summary>
/// <remarks>
/// 2026-09-21. Split from UpdateSwap when it reached 210 lines against the 200-line limit. The seam is
/// real: UpdateSwap runs in the OLD process and puts the new exe in place; everything here runs in the
/// NEW process at startup, after the old one has handed over.
/// </remarks>
public static class UpdateCleanup
{
    /// <summary>Delete the copy an earlier update left behind. Harmless when there is none.</summary>
    public static void CleanupAfterUpdate(string current) => CleanupAfterUpdate(current, 1, TimeSpan.Zero, _ => { });

    /// <summary>
    /// Delete the leftover copy, trying up to <paramref name="attempts"/> times. True once it is gone.
    /// </summary>
    /// <remarks>
    /// ⛔ 2026-09-21, SEEN IN THE REAL v1.2.0 -> v1.2.1 UPDATE. The new copy waited for the old process
    ///    to exit and then deleted PcWatch.exe.old - once. That one attempt landed while Windows was
    ///    still letting go of the exe the old process had been running from, failed, was swallowed by
    ///    design, and 108 MB sat there until the next launch removed it (1.6 s into that launch, which
    ///    proved the lock had been transient). A single attempt at the one moment most likely to
    ///    collide is the wrong shape; a few seconds of retries straight after an update is right.
    /// </remarks>
    internal static bool CleanupAfterUpdate(string current, int attempts, TimeSpan delay, Action<TimeSpan> wait)
    {
        string old = current + UpdateSwap.OldSuffix;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Delete(old);   // no exception when it is already absent
                return true;
            }
            catch
            {
                // Still locked. Retry while attempts remain; after that, the next launch tries again.
                if (attempt >= attempts) return false;
                wait(delay);
            }
        }
    }

    /// <summary>
    /// Wait for the process being replaced to exit, so this one can become the single instance.
    /// </summary>
    /// <remarks>
    /// ⚠️ Without this the new copy starts while the old one still holds the single-instance mutex,
    ///    concludes it is a second launch, signals the old window to show itself, and exits - so the
    ///    update "restarts" into the OLD version still running from memory.
    /// </remarks>
    public static bool WaitForExit(int processId, TimeSpan timeout)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
            return process.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            return true; // already gone
        }
        catch (InvalidOperationException)
        {
            return true; // exited between the lookup and the wait
        }
    }
}
