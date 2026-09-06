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
