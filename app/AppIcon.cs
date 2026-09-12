namespace PcWatch;

/// <summary>
/// The window icon, with a fallback that can never stop the app starting.
/// </summary>
/// <remarks>
/// 2026-09-10. Extracted from MainForm, which had reached 202 lines against the 200-line limit. The
/// seam is real rather than arbitrary: loading an icon from disk has nothing to do with sampling or
/// rendering, and as a private method of a Form its fallback could not be reached by any test.
///
/// ⚠️ The directory is a parameter for that reason. The shipped build always has PcWatch.ico beside
///    the executable, so the fallback branch was unreachable from the one directory the old code
///    looked in - the branch that runs when a user copies the exe somewhere on its own.
/// </remarks>
internal static class AppIcon
{
    internal const string FileName = "PcWatch.ico";

    /// <summary>Load the app icon from the running executable's folder.</summary>
    public static Icon Load() => Load(AppContext.BaseDirectory);

    /// <summary>Load the app icon from <paramref name="directory"/>, or a stock icon if it cannot.</summary>
    internal static Icon Load(string directory)
    {
        try
        {
            string path = Path.Combine(directory, FileName);
            if (File.Exists(path)) return new Icon(path);
        }
        catch
        {
            // Fall through: a missing icon is cosmetic, not a reason to fail to start.
        }
        return SystemIcons.Application;
    }
}
