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
        Rectangle? worth = PlacementToSave(form.WindowState, form.Bounds, form.RestoreBounds);
        if (worth is not Rectangle bounds) return;

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

        int? chosen = SelectMonitorIndex(monitor, ordered.Length);
        Screen? target = chosen is int index ? ordered[index] : ResolvePrimary(monitor);
        if (target is null) return false;

        Rectangle work = target.WorkingArea;
        form.StartPosition = FormStartPosition.Manual;

        // Set Normal bounds FIRST so the restore size is sensible, then maximise onto that screen.
        // Maximising a window that still believes it lives on another monitor lands it back there.
        FormWindowState wanted = form.WindowState;
        form.WindowState = FormWindowState.Normal;
        form.Bounds = CentreWithin(work, form.Size);

        if (wanted == FormWindowState.Maximized) form.WindowState = FormWindowState.Maximized;
        return true;
    }

    private static Screen? ResolvePrimary(string monitor) =>
        monitor.Equals("primary", StringComparison.OrdinalIgnoreCase) ? Screen.PrimaryScreen : null;

    // ── The decisions, as pure functions ────────────────────────────────────────────────────────
    //
    // 2026-09-05. Extracted because this whole file sat at 0%: every branch needed real monitors
    // and a real window, so the arithmetic that decides WHERE your window reopens was never once
    // exercised by a test. The remarks at the top of this file describe the failure it guards
    // against - a window restored onto a monitor that is no longer attached, which is
    // indistinguishable from the app failing to launch.

    /// <summary>
    /// Which screen a name refers to, as an index into the X-ordered list. Null means "not an index".
    /// </summary>
    /// <remarks>
    /// ⚠️ Screens are ordered by X coordinate, not by enumeration order: "right monitor" is a
    ///    SPATIAL statement, and Screen.AllScreens order is an installation detail that does not
    ///    track physical arrangement. "primary" is deliberately NOT resolved here, because it is the
    ///    one name that means a specific display rather than a position.
    /// </remarks>
    public static int? SelectMonitorIndex(string monitor, int screenCount)
    {
        if (screenCount <= 0) return null;

        switch (monitor.ToLowerInvariant())
        {
            case "left":
            case "first":
                return 0;
            case "right":
            case "last":
                return screenCount - 1;
            default:
                // 1-BASED, because a person saying "monitor 2" means the second one.
                return int.TryParse(monitor, out int index) && index >= 1 && index <= screenCount
                    ? index - 1
                    : null;
        }
    }

    /// <summary>Centre a window in a working area, shrinking it if it does not fit.</summary>
    public static Rectangle CentreWithin(Rectangle work, Size window)
    {
        int width = Math.Min(window.Width, work.Width);
        int height = Math.Min(window.Height, work.Height);
        return new Rectangle(
            work.X + (work.Width - width) / 2,
            work.Y + (work.Height - height) / 2,
            width,
            height);
    }

    /// <summary>The rectangle worth persisting, or null when there is nothing sensible to save.</summary>
    /// <remarks>
    /// ⚠️ RestoreBounds, never Bounds, unless the window is Normal. While maximised, Bounds is the
    ///    whole screen; saving that makes un-maximising snap to full screen FOR EVER afterwards,
    ///    and the user can never get their small window back.
    /// </remarks>
    public static Rectangle? PlacementToSave(
        FormWindowState state, Rectangle bounds, Rectangle restoreBounds)
    {
        Rectangle chosen = state == FormWindowState.Normal ? bounds : restoreBounds;
        return chosen.Width <= 0 || chosen.Height <= 0 ? null : chosen;
    }
}
