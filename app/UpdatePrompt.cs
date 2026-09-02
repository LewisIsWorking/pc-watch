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
        AvailableUpdate? update = await checker.CheckAsync();
        if (update is null || update.Version == settings.SkipVersion) return;

        // CheckAsync resumed on a thread-pool thread; UI work has to go back to the message loop.
        if (!owner.IsHandleCreated) return;
        owner.BeginInvoke(() => Show(update, settings));
    }

    private static void Show(AvailableUpdate update, Settings settings)
    {
        DialogResult choice = MessageBox.Show(
            $"PC Watch {update.Version} is available. You are running {AppVersion.Number}.\n\n"
            + $"{Truncate(update.Notes, 400)}\n\nOpen the download page?",
            "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

        if (choice == DialogResult.Yes)
        {
            try
            {
                Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true });
            }
            catch
            {
                // No default browser is not this app's problem to solve.
            }
            return;
        }

        settings.SkipVersion = update.Version;
        SettingsStore.Save(settings);
    }

    private static string Truncate(string text, int max) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty
        : text.Length <= max ? text
        : text[..max] + "...";
}
