namespace PcWatch;

/// <summary>
/// One palette for the whole app, referenced by meaning rather than by colour.
/// </summary>
/// <remarks>
/// 2026-08-31. Severity is chosen at the call site as High/Medium/Low, never as a literal colour, so
/// restyling the app is a change here and nowhere else - and so a "red" reading cannot come to mean
/// two different things in two different controls.
/// </remarks>
public static class Theme
{
    public static readonly Color Window = Color.FromArgb(24, 24, 28);
    public static readonly Color Panel = Color.FromArgb(30, 30, 36);
    public static readonly Color Grid = Color.FromArgb(48, 48, 58);
    public static readonly Color Body = Color.FromArgb(222, 222, 230);
    public static readonly Color Dim = Color.FromArgb(140, 140, 152);
    public static readonly Color Heading = Color.FromArgb(120, 190, 255);

    public static readonly Color High = Color.FromArgb(235, 90, 90);
    public static readonly Color Medium = Color.FromArgb(245, 185, 80);
    public static readonly Color Low = Color.FromArgb(110, 205, 130);
    public static readonly Color Unknown = Color.FromArgb(120, 120, 130);

    /// <summary>Colour for a load level. Null means NOT MEASURED, which is not the same as zero.</summary>
    public static Color ForLoad(double? percent) => percent switch
    {
        null => Unknown,
        < 50 => Low,
        < 80 => Medium,
        _ => High,
    };

    public static Color ForSeverity(Severity severity) => severity switch
    {
        Severity.High => High,
        Severity.Medium => Medium,
        _ => Low,
    };
}
