using System.Reflection;
using System.Runtime.InteropServices;

namespace PcWatch;

/// <summary>
/// The running version, and which runtime it was built against.
/// </summary>
/// <remarks>
/// 2026-09-02. Shown in the window and in the tray tooltip because an app that updates itself must
/// be able to tell you which copy you are looking at. "Did the update apply?" is unanswerable
/// otherwise, and a silent no-op update is indistinguishable from a successful one.
/// </remarks>
public static class AppVersion
{
    /// <summary>Semantic version, e.g. "1.1.0". Comes from &lt;Version&gt; in the csproj.</summary>
    public static string Number { get; } = ReadInformationalVersion();

    /// <summary>Runtime this build is running on, e.g. ".NET 11.0.0-preview.7".</summary>
    public static string Runtime { get; } = RuntimeInformation.FrameworkDescription;

    /// <summary>What the UI shows: "v1.1.0 on .NET 11.0.0-preview.7".</summary>
    public static string Display { get; } = $"v{Number} on {Runtime}";

    private static string ReadInformationalVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends "+<commit sha>" to the informational version. Useful in a log, noise
            // in a title bar, and it would break a plain string comparison against a release tag.
            int plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// Compare two dotted versions. Negative when <paramref name="a"/> is older.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not a string comparison. "1.10.0" sorts BEFORE "1.9.0" alphabetically, so a string compare
    /// would silently stop offering updates at the tenth minor release - a bug that cannot appear
    /// until long after anyone would think to test for it. Leading "v" is tolerated because release
    /// tags carry it and assembly versions do not.
    /// </remarks>
    public static int Compare(string a, string b)
    {
        static Version Parse(string text)
        {
            string trimmed = text.TrimStart('v', 'V').Split('-', '+')[0];
            return Version.TryParse(trimmed, out Version? parsed) ? parsed : new Version(0, 0, 0);
        }

        return Parse(a).CompareTo(Parse(b));
    }
}
