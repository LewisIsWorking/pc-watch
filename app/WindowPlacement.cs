namespace PcWatch;

/// <summary>
/// Reopens the window where it was last closed, including on a second monitor.
/// </summary>
/// <remarks>
/// 2026-09-02. Split out of MainForm at the 200-line limit.
///
/// ⚠️ A saved rectangle is validated against the monitors attached RIGHT NOW. Screens get unplugged,
/// resolutions change, and a laptop docked yesterday is undocked today. Restoring blindly puts the
/// window somewhere with no reachable title bar, which is indistinguishable from the app failing to
/// launch - and the user's next move is to launch it again, which the single-instance mutex turns
/// into nothing happening at all.
/// </remarks>
public static class WindowPlacement
{
    /// <summary>Apply a saved placement, or fall back to maximised on the primary screen.</summary>
    public static void Restore(Form form, Settings settings)
    {
        var saved = new Rectangle(settings.X, settings.Y, settings.Width, settings.Height);

        if (settings.HasPlacement && SettingsStore.IsOnScreen(saved))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = saved;
            form.WindowState = settings.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
        }
        else
        {
            form.WindowState = FormWindowState.Maximized;
        }
    }

    /// <summary>Record where the window is, so the next launch reopens there.</summary>
    public static void Save(Form form, Settings settings)
    {
        // RestoreBounds, not Bounds: while maximised, Bounds is the whole screen, and saving that
        // would make un-maximising snap to full screen for ever afterwards.
        Rectangle bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        settings.X = bounds.X;
        settings.Y = bounds.Y;
        settings.Width = bounds.Width;
        settings.Height = bounds.Height;
        settings.Maximized = form.WindowState == FormWindowState.Maximized;
        settings.HasPlacement = true;
        SettingsStore.Save(settings);
    }

    /// <summary>
    /// Move a window to a named monitor: "left", "right", "primary", or a 1-based index.
    /// </summary>
    /// <remarks>
    /// Screens are ordered by their X coordinate rather than by the order Windows enumerates them,
    /// because "right monitor" is a spatial statement and <see cref="Screen.AllScreens"/> order is
    /// an installation detail that does not track physical arrangement.
    /// </remarks>
    public static bool MoveTo(Form form, string monitor)
    {
        Screen[] ordered = [.. Screen.AllScreens.OrderBy(s => s.Bounds.X)];
        if (ordered.Length == 0) return false;

        Screen? target = monitor.ToLowerInvariant() switch
        {
            "left" or "first" => ordered[0],
            "right" or "last" => ordered[^1],
            "primary" => Screen.PrimaryScreen,
            _ => int.TryParse(monitor, out int index) && index >= 1 && index <= ordered.Length
                ? ordered[index - 1]
                : null,
        };
        if (target is null) return false;

        Rectangle work = target.WorkingArea;
        form.StartPosition = FormStartPosition.Manual;

        // Set Normal bounds FIRST so the restore size is sensible, then maximise onto that screen.
        // Maximising a window that still believes it lives on another monitor lands it back there.
        FormWindowState wanted = form.WindowState;
        form.WindowState = FormWindowState.Normal;
        form.Bounds = new Rectangle(
            work.X + (work.Width - Math.Min(form.Width, work.Width)) / 2,
            work.Y + (work.Height - Math.Min(form.Height, work.Height)) / 2,
            Math.Min(form.Width, work.Width),
            Math.Min(form.Height, work.Height));

        if (wanted == FormWindowState.Maximized) form.WindowState = FormWindowState.Maximized;
        return true;
    }
}
