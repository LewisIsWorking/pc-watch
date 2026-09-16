namespace PcWatch;

/// <summary>
/// What the user sees while an accepted update installs, and after it fails.
/// </summary>
/// <remarks>
/// 2026-09-16. The UI half of SelfUpdate, kept apart so SelfUpdate stays free of dialogs and can be
/// driven end to end by a test against temp files.
///
/// ⭐ A download of ~47 MB can take a while, and a click that then shows nothing reads as a broken
///   button. The window title says what is happening for the whole install. It is set once at startup
///   and never touched by the once-a-second tick, so it is not overwritten mid-download.
///
/// ⚠️ Accept only WIRES the real window, browser, dialog and process launch into RunAsync and Restart,
///    which hold every decision and take those as parameters. A test reaching MessageBox.Show would
///    hang the suite rather than fail, and one reaching the browser would open a real one.
/// </remarks>
public static class UpdateInstallFlow
{
    /// <summary>Install if that is safe here; otherwise open the download page as before.</summary>
    public static void Accept(Form owner, AvailableUpdate update)
    {
        string workDir = Path.Combine(Path.GetTempPath(), "PcWatch-update", update.Version);

        _ = RunAsync(
            update,
            SelfUpdate.CanInstall(update),
            // Off the UI thread: hashing 47 MB and a slow download must not freeze the window.
            install: () => Task.Run(() => SelfUpdate.InstallAsync(
                update, Environment.ProcessPath!, workDir, new UpdateDownloader(),
                restart: exe => Restart(exe, SelfUpdate.Restart, () => owner.BeginInvoke(Application.Exit)))),
            openPage: UpdatePrompt.OpenInBrowser,
            showMessage: message => MessageBox.Show(
                message, "Update not completed", MessageBoxButtons.OK, MessageBoxIcon.Warning),
            title: text => owner.Text = text,
            originalTitle: owner.Text);
    }

    /// <summary>Every decision in accepting an update, with the outside world passed in.</summary>
    internal static async Task RunAsync(
        AvailableUpdate update, bool canInstall, Func<Task> install, Action<string> openPage,
        Action<string> showMessage, Action<string> title, string originalTitle)
    {
        if (!canInstall)
        {
            openPage(update.Url);
            return;
        }

        title($"PC Watch - installing {update.Version}...");
        try
        {
            await install();
        }
        catch (Exception ex)
        {
            // Back on the UI thread in the real app: the await resumes on the WinForms context.
            title(originalTitle);
            var (message, open) = Failure(update, ex);
            showMessage(message);
            if (open) openPage(update.Url);
        }
    }

    /// <summary>Start the new copy, then exit - or, if it cannot be started, say so without exiting.</summary>
    /// <remarks>
    /// ⛔ EXIT ONLY AFTER A SUCCESSFUL START. Exiting first and then failing to start would close the app
    ///    with the update installed and nothing running, and no message anyone could see.
    /// </remarks>
    internal static void Restart(string exe, Action<string> start, Action exit)
    {
        try
        {
            start(exe);
        }
        catch (Exception ex)
        {
            throw new RestartFailedException(ex);
        }

        // Called from the install task; in the real app this hops back to the message loop.
        exit();
    }

    /// <summary>
    /// What to tell the user, and whether sending them to the download page would help.
    /// </summary>
    /// <remarks>
    /// ⛔ 2026-09-16. A FAILED RESTART IS NOT A FAILED UPDATE. By the time restart runs, the new
    ///    version is already on disk. Saying "not installed" and opening the download page would send
    ///    the user to fetch a version they already have - so that case says to start PC Watch again.
    /// </remarks>
    internal static (string Message, bool OpenPage) Failure(AvailableUpdate update, Exception error) =>
        error switch
        {
            RestartFailedException restart =>
                ($"PC Watch {update.Version} was installed, but could not restart itself "
                 + $"({restart.InnerException?.Message}).\n\nClose PC Watch and start it again to use it.", false),
            UpdateFailedException { LeftDiskChanged: true } broken =>
                ($"PC Watch {update.Version} was not installed, and the original file was moved aside: "
                 + $"{broken.Message}.\n\nRename it back to PcWatch.exe to restore it.", false),
            UpdateFailedException failed =>
                ($"PC Watch {update.Version} was not installed: {failed.Message}.\n\n"
                 + "Nothing was changed. The download page will open instead.", true),
            _ =>
                ($"PC Watch {update.Version} was not installed because of an unexpected error: "
                 + $"{error.Message}\n\nThe download page will open instead.", true),
        };

    /// <summary>The update is on disk; only the relaunch failed.</summary>
    internal sealed class RestartFailedException(Exception inner)
        : Exception("the new version could not be started", inner);
}
