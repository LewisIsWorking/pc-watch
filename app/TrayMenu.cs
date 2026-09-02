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
    public static ContextMenuStrip Build(Action show, Func<string> reportText, Action exit)
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Show PC Watch", null, (_, _) => show());
        menu.Items.Add("Copy report", null, (_, _) =>
        {
            string text = reportText();
            if (!string.IsNullOrWhiteSpace(text)) Clipboard.SetText(text);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Task Manager", null, (_, _) => Launch("taskmgr.exe"));
        menu.Items.Add("Resource Monitor", null, (_, _) => Launch("resmon.exe"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        return menu;
    }

    private static void Launch(string exe)
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
