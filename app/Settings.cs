using System.Text.Json;

namespace PcWatch;

/// <summary>Everything remembered between runs.</summary>
public sealed class Settings
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; } = true;
    public bool HasPlacement { get; set; }

    /// <summary>Skip update prompts for this version. Set when the user dismisses one.</summary>
    public string? SkipVersion { get; set; }
}

/// <summary>
/// Loads and saves <see cref="Settings"/> in %APPDATA%\PcWatch\settings.json.
/// </summary>
/// <remarks>
/// 2026-09-02. Written so the window reopens where it was left, including on a second monitor.
///
/// ⚠️ A saved position is NOT automatically a valid one. Monitors get unplugged, resolutions change,
/// and a laptop docked yesterday is undocked today. Restoring blindly puts the window somewhere the
/// user cannot see or drag it back from, which looks exactly like the app failing to start. So the
/// bounds are validated against the CURRENT screens before use, and rejected if they do not
/// meaningfully intersect one.
/// </remarks>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PcWatch", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) ?? new Settings();
        }
        catch
        {
            // A corrupt or unreadable settings file must never stop the app starting. Defaults are
            // always usable, which is not true of a half-parsed placement.
            return new Settings();
        }
    }

    public static void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // Losing a window position is not worth an error dialog on exit.
        }
    }

    /// <summary>
    /// Is a saved rectangle still usable on the monitors attached right now?
    /// </summary>
    /// <remarks>
    /// Requires a real overlap with a screen's working area, not merely a corner on it: a window
    /// whose title bar sits off-screen cannot be moved with the mouse, so "technically visible" is
    /// not the test that matters. The threshold is a title-bar-sized patch of window.
    /// </remarks>
    public static bool IsOnScreen(Rectangle bounds)
    {
        if (bounds.Width < 200 || bounds.Height < 150) return false;

        foreach (Screen screen in Screen.AllScreens)
        {
            Rectangle overlap = Rectangle.Intersect(screen.WorkingArea, bounds);
            if (overlap.Width >= 200 && overlap.Height >= 60) return true;
        }
        return false;
    }
}
