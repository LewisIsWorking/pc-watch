using System.IO.Compression;

namespace PcWatch;

/// <summary>
/// Puts a verified new executable where the running one is, without ever leaving no working copy.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-16. WINDOWS WILL NOT OVERWRITE A RUNNING EXE, BUT IT WILL RENAME ONE. The running
///    PcWatch.exe is renamed to PcWatch.exe.old, the new file is moved into its place, and the old one
///    is deleted by the NEXT launch once nothing holds it open. If the second step fails the first is
///    undone, so every failure path ends with a PcWatch.exe that starts.
/// </remarks>
public static class UpdateSwap
{
    internal const string ExecutableName = "PcWatch.exe";
    internal const string OldSuffix = ".old";

    /// <summary>
    /// The new executable from a downloaded asset: the asset itself if it is an exe, else extracted.
    /// </summary>
    /// <remarks>
    /// ⛔ ONE ENTRY, BY EXACT NAME, TO A FIXED PATH - never ExtractToDirectory. A zip entry named
    ///    "..\..\something.exe" extracts wherever it says, which is how an archive writes outside the
    ///    folder it was opened into. Only PcWatch.exe at the root is accepted.
    /// </remarks>
    public static string ExtractExecutable(string downloaded, string workDir)
    {
        if (downloaded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return downloaded;

        string target = Path.Combine(workDir, "PcWatch.new.exe");
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(downloaded);
            ZipArchiveEntry? entry = zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, ExecutableName, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                throw new UpdateFailedException($"the download does not contain {ExecutableName}");
            }
            entry.ExtractToFile(target, overwrite: true);
        }
        catch (UpdateFailedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UpdateFailedException($"the download could not be unpacked: {ex.Message}", ex);
        }

        return target;
    }

    /// <summary>
    /// Replace <paramref name="current"/> with <paramref name="replacement"/>, keeping the old copy
    /// beside it until the next launch.
    /// </summary>
    /// <exception cref="UpdateFailedException">The swap failed; <paramref name="current"/> is unchanged.</exception>
    public static void Swap(string current, string replacement) =>
        Swap(current, replacement, File.Move, Thread.Sleep);

    /// <summary>The swap, with the move operation passed in and no real waiting between retries.</summary>
    /// <remarks>
    /// The seam exists for one case: the second move fails AND putting the original back fails too.
    /// That double failure cannot be produced with real files from a single thread, and it is the only
    /// path that leaves the disk changed, so it is the one whose message most needs a test.
    /// </remarks>
    internal static void Swap(string current, string replacement, Action<string, string> move) =>
        Swap(current, replacement, move, _ => { });

    /// <summary>How many times to try moving the running exe aside while something else holds it open.</summary>
    internal const int AsideAttempts = 10;

    /// <summary>The pause between those attempts: 10 x 300 ms rides out a scan of about three seconds.</summary>
    internal static readonly TimeSpan AsideRetryDelay = TimeSpan.FromMilliseconds(300);

    internal static void Swap(string current, string replacement, Action<string, string> move, Action<TimeSpan> wait)
    {
        string old = current + OldSuffix;

        // A leftover from an earlier update would make the rename below fail. It is not running:
        // only the file WITHOUT the suffix is ever launched.
        try { File.Delete(old); } catch { /* if it is somehow locked, the rename reports it */ }

        MoveAside(current, old, move, wait);

        try
        {
            move(replacement, current);
        }
        catch (Exception ex)
        {
            // ⛔ THE STEP THAT MATTERS. Put the original back, or there is no PcWatch.exe at all.
            try { move(old, current); }
            catch (Exception restore)
            {
                throw new UpdateFailedException(
                    $"the update failed and the original could not be restored - it is at {old}",
                    restore, leftDiskChanged: true);
            }
            throw new UpdateFailedException($"the new version could not be put in place: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Rename the running exe aside, retrying while another program briefly holds it open.
    /// </summary>
    /// <remarks>
    /// ⛔ 2026-09-21, FOUND BY THE FIRST REAL END-TO-END UPDATE. The swap failed with "the process
    ///    cannot access the file because it is being used by another process" - and reported it as
    ///    "PC Watch's folder is not writable", which is the wrong diagnosis and sends the user to check
    ///    folder permissions. The holder that time was the test harness hashing the exe; on a real
    ///    machine it is antivirus, backup software, or OneDrive when the exe sits in a synced folder
    ///    such as the Desktop. All of them let go within moments.
    ///
    ///    Retrying is safe HERE and only here: until this rename succeeds, nothing has changed.
    ///    Only sharing and lock violations are retried. Access denied means the folder really is not
    ///    writable, and waiting will not change that, so it still fails at once.
    /// </remarks>
    private static void MoveAside(string current, string old, Action<string, string> move, Action<TimeSpan> wait)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                move(current, old);
                return;
            }
            catch (Exception ex) when (IsSharingViolation(ex) && attempt < AsideAttempts)
            {
                wait(AsideRetryDelay);
            }
            catch (Exception ex) when (IsSharingViolation(ex))
            {
                throw new UpdateFailedException(
                    "PcWatch.exe was held open by another program (antivirus, backup or sync software) "
                    + $"for longer than {AsideAttempts * AsideRetryDelay.TotalSeconds:N0} s: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                // Nothing has changed yet. The usual cause is an install folder this user cannot write to.
                throw new UpdateFailedException($"PC Watch's folder is not writable: {ex.Message}", ex);
            }
        }
    }

    /// <summary>ERROR_SHARING_VIOLATION (32) or ERROR_LOCK_VIOLATION (33): transient, worth waiting out.</summary>
    internal static bool IsSharingViolation(Exception ex) =>
        ex is IOException io && (io.HResult & 0xFFFF) is 32 or 33;
}
