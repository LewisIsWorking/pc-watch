using System.Diagnostics;

namespace PcWatch;

/// <summary>
/// Ends a process, and refuses when ending it would take Windows down with it.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-02. THE DENYLIST IS THE POINT, not the killing. Terminating csrss, wininit, winlogon,
///    services, smss or lsass BUGCHECKS the machine immediately - Windows treats them as critical
///    and a blue screen is the DESIGNED response, not a bug. svchost hosts dozens of services and
///    killing the wrong instance takes networking or audio with it.
///
///    A "kill" button next to a list that includes those processes is a loaded gun pointed at the
///    user's unsaved work. The list shows them because they are genuinely long-lived and hiding them
///    would be dishonest; the button refuses them and says why.
///
/// ⚠️ Being long-lived is not a fault. This exists to make a deliberate decision easy, never to
///    imply that everything old should go.
/// </remarks>
public static class ProcessKiller
{
    /// <summary>Killing any of these bugchecks Windows or breaks the session beyond recovery.</summary>
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "MemCompression",
        "csrss", "wininit", "winlogon", "services", "smss", "lsass", "svchost",
        "fontdrvhost", "dwm", "LsaIso", "SecurityHealthService",
    };

    /// <summary>Safe to end, but the user should know what happens next.</summary>
    private static readonly Dictionary<string, string> Warnings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["explorer"] = "This is the Windows shell. The taskbar and desktop will disappear and "
                     + "Windows normally restarts it within a few seconds. Open File Explorer "
                     + "windows will close.",
        ["qemu-system-x86_64"] = "This is a virtual machine or Android emulator. Anything running "
                               + "inside it is lost immediately, with no shutdown.",
        ["vmmem"] = "This is the WSL or Hyper-V memory process. Linux distributions and containers "
                  + "running in it stop at once.",
        ["vmmemWSL"] = "This is the WSL memory process. Running Linux distributions stop at once.",
    };

    /// <summary>Whether this process may be ended, and the reason when it may not.</summary>
    public static (bool Allowed, string? Reason) CanKill(string name) =>
        Critical.Contains(name)
            ? (false, $"{name} is a critical Windows process. Ending it would bugcheck the machine, "
                    + "so PC Watch will not do it.")
            : (true, Warnings.TryGetValue(name, out string? warning) ? warning : null);

    /// <summary>
    /// End a process by id, verifying the name still matches.
    /// </summary>
    /// <remarks>
    /// ⚠️ The name is re-checked against the live process before killing. Pids are recycled fast on
    /// Windows, and the row the user clicked was rendered up to a second earlier - long enough for
    /// that pid to belong to something entirely different. Killing by a stale pid is how a monitor
    /// ends the wrong program and no one can work out why.
    /// </remarks>
    public static (bool Killed, string Message) Kill(int processId, string expectedName)
    {
        var (allowed, reason) = CanKill(expectedName);
        if (!allowed) return (false, reason!);

        try
        {
            using Process process = Process.GetProcessById(processId);

            if (!string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"pid {processId} is now '{process.ProcessName}', not '{expectedName}'. "
                             + "The pid was reused, so nothing was ended.");
            }

            process.Kill(entireProcessTree: true);
            return (true, $"Ended {expectedName} (pid {processId}) and its child processes.");
        }
        catch (ArgumentException)
        {
            return (false, $"pid {processId} has already exited.");
        }
        catch (InvalidOperationException)
        {
            return (false, $"pid {processId} has already exited.");
        }
        catch (Exception ex)
        {
            // Access denied is the common one: an elevated or another user's process cannot be
            // ended from here. Saying so beats a silent no-op.
            return (false, $"Could not end {expectedName} (pid {processId}): {ex.Message}");
        }
    }
}
