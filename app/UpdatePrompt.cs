using System.Diagnostics;

namespace PcWatch;

/// <summary>
/// Asks once per version whether to open the download page for a newer release.
/// </summary>
/// <remarks>
/// 2026-09-02. Split out of MainForm at the 200-line limit.
///
/// ⚠️ Once per VERSION, not once per launch. A prompt that reappears every time the app starts gets
/// dismissed reflexively, and then the one that matters is dismissed too. Declining records the
/// version in settings and the app stays quiet until a newer one appears.
///
/// It notifies rather than self-updating: replacing a running binary that is holding a file lock is
/// how an updater leaves a machine with no working copy, and this app is most wanted precisely when
/// the machine is already misbehaving.
/// </remarks>
public static class UpdatePrompt
{
    public static async Task CheckAsync(Form owner, UpdateChecker checker, Settings settings)
    {
        // Opt-out honoured BEFORE the request, not after: a check that fires and then discards the
        // answer has already told GitHub the app is running, which is the part being opted out of.
        if (!settings.CheckForUpdates) return;

        AvailableUpdate? update = await checker.CheckAsync();
        if (update is null || update.Version == settings.SkipVersion) return;

        // CheckAsync resumed on a thread-pool thread; UI work has to go back to the message loop.
        if (!owner.IsHandleCreated) return;
        owner.BeginInvoke(() => Show(update, settings));
    }

    /// <summary>
    /// Check because the user ASKED, from the tray menu.
    /// </summary>
    /// <remarks>
    /// ⛔ 2026-09-10. THREE DELIBERATE DIFFERENCES from the startup check, each the opposite of what
    ///    copying CheckAsync would have produced:
    ///
    ///      1. THE OPT-OUT DOES NOT APPLY. CheckForUpdates=false means "do not phone home on your
    ///         own". Clicking a button labelled Check for updates IS that consent, and refusing
    ///         silently would leave a dead-looking menu item and no way to find out why.
    ///      2. SkipVersion DOES NOT APPLY. Dismissing 9.9.9 once must not make it invisible to
    ///         someone explicitly asking what the latest version is.
    ///      3. ⭐ A RESULT IS ALWAYS SHOWN, including "up to date" and including failure. The startup
    ///         check is silent by design; a MANUAL check that is silent is indistinguishable from a
    ///         broken button, and the user's next move is to click it again.
    /// </remarks>
    public static async Task CheckNowAsync(Form owner, UpdateChecker checker, Settings settings)
    {
        AvailableUpdate? update = await checker.CheckAsync();

        if (!owner.IsHandleCreated) return;
        owner.BeginInvoke(() => ShowManualResult(update, checker.LastError, settings));
    }

    /// <summary>
    /// What a manual check says, given what came back.
    /// </summary>
    /// <remarks>
    /// Separated from the dialog so the WORDING can be asserted. This is the only feedback the
    /// button ever gives, so "up to date" and "could not check" must not be confusable.
    /// </remarks>
    internal static (string Message, bool IsOffer) ManualOutcome(AvailableUpdate? update, string? error)
    {
        if (update is not null) return (BuildMessage(update), true);

        // ⚠️ AN ERROR IS NOT "UP TO DATE". Reporting a failed check as "you have the latest" is the
        //    worst outcome available here: a confident answer produced by not knowing.
        return error is not null
            ? ($"Could not check for updates.\n\n{error}", false)
            : ($"PC Watch {AppVersion.Number} is the latest version.", false);
    }

    private static void ShowManualResult(AvailableUpdate? update, string? error, Settings settings)
    {
        var (message, isOffer) = ManualOutcome(update, error);

        if (!isOffer)
        {
            MessageBox.Show(message, "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult choice = MessageBox.Show(
            message, "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        Apply(choice, update!, settings, OpenInBrowser);
    }

    private static void Show(AvailableUpdate update, Settings settings)
    {
        // ⚠️ THE ONLY UNTESTABLE LINE IN THIS FILE, and deliberately the only one. MessageBox.Show
        //    blocks on a modal dialog, so a test that reached it would hang the suite for ever
        //    rather than fail. Everything decided either side of it lives in Apply below.
        DialogResult choice = MessageBox.Show(
            BuildMessage(update), "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

        Apply(choice, update, settings, OpenInBrowser);
    }

    /// <summary>What the dialog says. Separated so its wording can be asserted.</summary>
    internal static string BuildMessage(AvailableUpdate update) =>
        $"PC Watch {update.Version} is available. You are running {AppVersion.Number}.\n\n"
        + $"{Truncate(update.Notes, 400)}\n\nOpen the download page?";

    /// <summary>
    /// Act on the user's answer: open the page, or go quiet until a newer version appears.
    /// </summary>
    /// <remarks>
    /// ⭐ 2026-09-06. Extracted from Show so it can be tested at all. This is where the promise in
    ///   the class remarks is actually kept - declining records the version so the prompt does not
    ///   reappear on every launch - and that promise had no test, because reaching it required
    ///   answering a modal dialog.
    ///
    /// ⚠️ ONLY a decline records the version. Accepting must NOT, or a user who opens the download
    ///    page and then does not install would never be told about that release again.
    /// </remarks>
    internal static void Apply(
        DialogResult choice, AvailableUpdate update, Settings settings, Action<string> open)
    {
        if (choice == DialogResult.Yes)
        {
            open(update.Url);
            return;
        }

        settings.SkipVersion = update.Version;
        SettingsStore.Save(settings);
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No default browser is not this app's problem to solve.
        }
    }

    internal static string Truncate(string text, int max) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty
        : text.Length <= max ? text
        : text[..max] + "...";
}
