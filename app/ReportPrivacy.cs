using System.Text.RegularExpressions;

namespace PcWatch;

/// <summary>
/// Checks that a rendered report contains nothing private, before anyone pastes it anywhere.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-02. "Copy report" exists so people can paste this into a bug report or a chat, which
///    makes everything in it PUBLISHED BY DESIGN. The danger is not what the report shows today but
///    what a later change quietly adds.
///
///    The specific hazard is process COMMAND LINES. Task Manager shows them, they look like an
///    obvious upgrade for a process list, and they routinely contain "--token=", "--password=" and
///    connection strings. PC Watch reads names only, from a Toolhelp snapshot - an API that cannot
///    return a command line even by accident. This is what notices if that ever changes.
///
///    Separated from the self-test so it can be fed a KNOWN-BAD report as well as a real one. A
///    scanner that has only ever been run against clean input has not been shown to detect anything.
/// </remarks>
public static partial class ReportPrivacy
{
    [GeneratedRegex(@"[A-Za-z]:\\[^\s""]{3,}")]
    private static partial Regex FilesystemPath();

    [GeneratedRegex(@"--[a-z][a-z-]{2,}[= ]\S")]
    private static partial Regex CommandLineArgument();

    [GeneratedRegex(@"(?i)(token|password|passwd|secret|api[_-]?key)\s*[:=]\s*\S")]
    private static partial Regex CredentialAssignment();

    [GeneratedRegex(@"%[A-Z_]+%|\$env:")]
    private static partial Regex EnvironmentVariable();

    /// <summary>
    /// Everything private found in <paramref name="report"/>. Empty means clean.
    /// </summary>
    /// <param name="report">The rendered report text.</param>
    /// <param name="userName">Username to look for. Defaults to the current user.</param>
    /// <param name="machineName">Machine name to look for. Defaults to this machine.</param>
    /// <remarks>
    /// The identifiers are parameters rather than constants so the scanner can be tested against a
    /// report that contains someone else's name, and so it protects whoever is actually running it
    /// rather than one hardcoded person.
    /// </remarks>
    public static IReadOnlyList<string> Scan(string report, string? userName = null, string? machineName = null)
    {
        userName ??= Environment.UserName;
        machineName ??= Environment.MachineName;

        var violations = new List<string>();

        if (FilesystemPath().Match(report) is { Success: true } path)
        {
            violations.Add($"filesystem path: {path.Value}");
        }
        if (!string.IsNullOrWhiteSpace(userName) && report.Contains(userName, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"Windows username: {userName}");
        }
        if (!string.IsNullOrWhiteSpace(machineName) && report.Contains(machineName, StringComparison.OrdinalIgnoreCase))
        {
            violations.Add($"machine name: {machineName}");
        }
        if (CommandLineArgument().Match(report) is { Success: true } arg)
        {
            violations.Add($"command-line argument: {arg.Value}");
        }
        if (CredentialAssignment().Match(report) is { Success: true } credential)
        {
            violations.Add($"credential assignment: {credential.Value}");
        }
        if (EnvironmentVariable().Match(report) is { Success: true } variable)
        {
            violations.Add($"environment variable: {variable.Value}");
        }

        return violations;
    }
}
