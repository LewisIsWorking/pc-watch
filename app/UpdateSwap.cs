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
    public static void Swap(string current, string replacement) => Swap(current, replacement, File.Move);

    /// <summary>The swap, with the move operation passed in.</summary>
    /// <remarks>
    /// The seam exists for one case: the second move fails AND putting the original back fails too.
    /// That double failure cannot be produced with real files from a single thread, and it is the only
    /// path that leaves the disk changed, so it is the one whose message most needs a test.
    /// </remarks>
    internal static void Swap(string current, string replacement, Action<string, string> move)
    {
        string old = current + OldSuffix;

        // A leftover from an earlier update would make the rename below fail. It is not running:
        // only the file WITHOUT the suffix is ever launched.
        try { File.Delete(old); } catch { /* if it is somehow locked, the rename reports it */ }

        try
        {
            move(current, old);
        }
        catch (Exception ex)
        {
            // Nothing has changed yet. The usual cause is an install folder this user cannot write to.
            throw new UpdateFailedException($"PC Watch's folder is not writable: {ex.Message}", ex);
        }

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

    /// <summary>Delete the copy an earlier update left behind. Harmless when there is none.</summary>
    public static void CleanupAfterUpdate(string current)
    {
        try { File.Delete(current + OldSuffix); } catch { /* still locked: try again next launch */ }
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
