using System.Diagnostics;

namespace PcWatch;

/// <summary>
/// Installs a newer release in place and restarts into it.
/// </summary>
/// <remarks>
/// ⭐ 2026-09-16. Added because "Check for updates" could only open the download page, when what was
///   asked for was a button that updates PC Watch to the latest version.
///
/// ⛔ ORDER IS THE SAFETY. Download, VERIFY, extract, swap, restart - and nothing on disk beside the
///    running exe is touched until the SHA-256 has matched. Every step that can fail comes before the
///    swap, and the swap itself rolls back, so there is no failure that leaves no working copy.
///
/// ⚠️ Refused, falling back to the download page, in two cases:
///      - the release publishes no digest: nothing independent to verify the bytes against;
///      - this is not the published single-file exe (a dev build, `dotnet run`, the test host):
///        replacing "PcWatch.exe" there would replace a build output, or nothing, or dotnet itself.
/// </remarks>
public static class SelfUpdate
{
    /// <summary>Passed to the new copy: the id of the process it is replacing.</summary>
    internal const string ReplacingFlag = "--replacing";

    /// <summary>Whether this update can be installed by the running app.</summary>
    public static bool CanInstall(AvailableUpdate update) =>
        CanInstall(update, IsPublishedSingleFile(AppContext.BaseDirectory, Environment.ProcessPath));

    internal static bool CanInstall(AvailableUpdate update, bool publishedSingleFile) =>
        publishedSingleFile && update.Sha256 is not null;

    /// <summary>
    /// True only for the published single-file PcWatch.exe.
    /// </summary>
    /// <remarks>
    /// Detected by what is NOT beside it. A normal build has PcWatch.dll next to the exe; the
    /// single-file publish bundles it inside, so its folder holds only PcWatch.exe and the icon.
    /// Deliberately not Assembly.Location, which raises IL3000 in exactly the build that matters.
    /// </remarks>
    internal static bool IsPublishedSingleFile(string baseDirectory, string? processPath) =>
        processPath is not null
        && processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
        && !File.Exists(Path.Combine(baseDirectory, "PcWatch.dll"));

    /// <summary>
    /// Download, verify, swap in, then hand off to <paramref name="restart"/> with the exe path.
    /// </summary>
    /// <exception cref="UpdateFailedException">Any step failed; <paramref name="currentExe"/> is unchanged.</exception>
    public static async Task InstallAsync(
        AvailableUpdate update, string currentExe, string workDir,
        UpdateDownloader downloader, Action<string> restart, CancellationToken cancellation = default)
    {
        string downloaded = await downloader.DownloadVerifiedAsync(update, workDir, cancellation);
        string replacement = UpdateSwap.ExtractExecutable(downloaded, workDir);
        UpdateSwap.Swap(currentExe, replacement);

        // The ~47 MB archive is no longer needed once its exe is in place. (When the asset WAS the
        // exe it has just been moved, so there is nothing left at this path to delete.)
        if (!string.Equals(downloaded, replacement, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(downloaded); } catch { /* temp folder; Windows cleans it eventually */ }
        }

        // Only now is the new version on disk. Restart is last because it cannot be undone: the old
        // process is about to exit.
        restart(currentExe);
    }

    /// <summary>Start the new copy, telling it which process to wait for.</summary>
    public static void Restart(string exe) => Process.Start(RestartInfo(exe, Environment.ProcessId));

    /// <summary>
    /// How the new copy is started. Separated so the hand-off contract can be tested without launching.
    /// </summary>
    /// <remarks>
    /// ⚠️ The arguments written here are parsed back by FinishReplacing in a DIFFERENT process. If the
    ///    two ever disagree the new copy stops waiting, and the single-instance mutex hands control
    ///    straight back to the old version. The round trip is asserted rather than assumed.
    /// </remarks>
    internal static ProcessStartInfo RestartInfo(string exe, int replacingProcessId) =>
        new(exe, $"{ReplacingFlag} {replacingProcessId}")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
        };

    /// <summary>
    /// At startup: if this copy replaced another, wait for it, then remove what it left behind.
    /// </summary>
    internal static void FinishReplacing(string[] args) =>
        FinishReplacing(args, AppContext.BaseDirectory, Environment.ProcessPath);

    /// <summary>The startup hand-off, with the paths passed in so it can run against a temp folder.</summary>
    /// <remarks>
    /// Verified against the real published RC 1 exe on 2026-09-16: started with --replacing pointing
    /// at a process that lived 26 s, it showed no window and left PcWatch.exe.old in place for the
    /// whole 26 s, deleted it 0.1 s after that process exited, and opened its window 0.9 s later.
    /// </remarks>
    internal static void FinishReplacing(string[] args, string baseDirectory, string? processPath) =>
        FinishReplacing(args, baseDirectory, processPath, Thread.Sleep);

    internal static void FinishReplacing(string[] args, string baseDirectory, string? processPath, Action<TimeSpan> wait)
    {
        if (!IsPublishedSingleFile(baseDirectory, processPath)) return;

        bool replacing = int.TryParse(Program.ArgumentValue(args, ReplacingFlag), out int oldProcess);
        if (replacing)
        {
            UpdateCleanup.WaitForExit(oldProcess, TimeSpan.FromSeconds(30));
        }

        // Every launch, not only after an update: a copy that could not delete it last time (still
        // locked) gets another chance, and a missing file costs nothing.
        //
        // ⚠️ 2026-09-21: RETRIED ONLY STRAIGHT AFTER AN UPDATE, up to ~3 s. That is the moment the old
        //    exe is most likely still being released. On an ordinary launch one attempt is enough, and
        //    a file some other program holds for a long time must never delay normal startup.
        UpdateCleanup.CleanupAfterUpdate(processPath!, replacing ? 10 : 1, TimeSpan.FromMilliseconds(300), wait);
    }
}
