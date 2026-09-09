using System.Diagnostics;

namespace PcWatch;

/// <summary>
/// The notification-area right-click menu.
/// </summary>
/// <remarks>
/// 2026-08-31. Deliberately contains nothing destructive. The app can tell you that a process looks
/// like a runaway, but it cannot tell whether the agent session that owns it is mid-task, so ending
/// processes stays a decision made in Task Manager with the ownership line in view.
/// </remarks>
public static class TrayMenu
{
    /// <param name="launch">
    /// How to open an external tool. Defaults to really starting it.
    /// </param>
    /// <remarks>
    /// ⛔ 2026-09-06. The launcher is a parameter because a test that CLICKS these items would
    ///    otherwise really open Task Manager and Resource Monitor on the developer's desktop, every
    ///    single run. That is not a hypothetical: it is what happens the moment you assert the menu
    ///    wires its handlers up.
    ///
    /// ⭐ It also lets a test assert WHICH tool each item asks for. A test that only checks "a
    ///   handler ran" passes just as happily when both items open the same program.
    /// </remarks>
    public static ContextMenuStrip Build(
        Action show, Func<string> reportText, Action scanStorage, Action exit,
        Action<string>? launch = null, Action<string>? copyText = null)
    {
        Action<string> open = launch ?? Launch;
        Action<string> copy = copyText ?? Clipboard.SetText;
        var menu = new ContextMenuStrip();

        menu.Items.Add("Show PC Watch", null, (_, _) => show());
        menu.Items.Add("Copy report", null, (_, _) => CopyReport(reportText, copy));
        menu.Items.Add(new ToolStripSeparator());

        // On demand, never on a timer. Walking a 2 TB drive takes minutes and hammers the disk;
        // doing that automatically would make the monitor a cause of the slowness it reports.
        menu.Items.Add("Scan disk usage", null, (_, _) => scanStorage());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Task Manager", null, (_, _) => open("taskmgr.exe"));
        menu.Items.Add("Resource Monitor", null, (_, _) => open("resmon.exe"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        return menu;
    }

    /// <summary>
    /// Put the current report on the clipboard, unless there is nothing worth putting there.
    /// </summary>
    /// <remarks>
    /// 2026-09-09. Extracted, and the clipboard call injected, for the same reason Launch is
    /// internal: the behaviour is worth pinning and the real thing is not safe to call in a test.
    ///
    /// ⚠️ Clipboard.SetText THROWS on empty or null, so the blank guard is not politeness - it is
    ///    what stops "Copy report" raising an exception during the seconds before the first sample
    ///    lands, which is exactly when a new user is most likely to be poking at the tray menu.
    /// </remarks>
    internal static void CopyReport(Func<string> reportText, Action<string> setText)
    {
        string text = reportText();
        if (!string.IsNullOrWhiteSpace(text)) setText(text);
    }

    /// <summary>Really open an external tool, swallowing any failure.</summary>
    /// <remarks>Internal rather than private so the failure path can be tested directly.</remarks>
    internal static void Launch(string exe)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }
        catch
        {
            // These are conveniences. Failing to open one must not take the app down.
        }
    }
}
