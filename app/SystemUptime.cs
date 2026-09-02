using System.Diagnostics.Eventing.Reader;
using Microsoft.Win32;

namespace PcWatch;

/// <summary>How long the machine has been on, and how long since it last cold-booted.</summary>
public sealed record UptimeFacts(
    TimeSpan? OnFor,
    DateTime? OnSince,
    TimeSpan SinceKernelBoot,
    bool FastStartupEnabled)
{
    /// <summary>The figure a person means by "how long has my PC been on".</summary>
    public TimeSpan Best => OnFor ?? SinceKernelBoot;

    /// <summary>
    /// True when the kernel counter is materially older than the real power-on, which is the
    /// signature of Fast Startup and the reason the two must be reported separately.
    /// </summary>
    public bool CountersDisagree =>
        OnFor is { } on && (SinceKernelBoot - on) > TimeSpan.FromHours(6);
}

/// <summary>
/// Works out when the machine actually came up.
/// </summary>
/// <remarks>
/// ⛔ 2026-08-31, THE BUG THIS FIXES. The app reported "up 18.8 days" for a PC that had been on for
///    about a day and a half, and the user knew it was wrong. Measured:
///
///      GetTickCount64                18.8 days
///      WMI LastBootUpTime            18.8 days  (13 Aug)
///      HiberbootEnabled              1          (Fast Startup ON)
///      Kernel-Boot event 27          30 Aug 11:16:56, "boot type 0x1" = hiberboot
///      explorer.exe start            30 Aug 11:16:59
///
///    With Fast Startup, "shut down" hibernates the kernel session instead of stopping it, so
///    NEITHER counter resets. They are not broken - they answer "how long since a full boot", which
///    is a different question from the one being asked, and they agree with each other perfectly
///    while both being useless for it. Two sources agreeing is not evidence they are right.
///
///    The event log is the only authoritative record, so that is what this reads.
/// </remarks>
public static class SystemUptime
{
    // Kernel-Boot 27 fires on every boot including a Fast Startup resume; Kernel-Power 107 and
    // Power-Troubleshooter 1 fire on resume from sleep or hibernate. The most recent of them is
    // the moment the machine last became usable, which is what "on for" should measure.
    private const string Query = """
        *[System[
            (Provider[@Name='Microsoft-Windows-Kernel-Boot']           and EventID=27) or
            (Provider[@Name='Microsoft-Windows-Kernel-Power']          and (EventID=107 or EventID=507)) or
            (Provider[@Name='Microsoft-Windows-Power-Troubleshooter']  and EventID=1)
        ]]
        """;

    private static UptimeFacts? _cached;
    private static DateTime _stamp = DateTime.MinValue;

    /// <summary>
    /// Cached for five minutes. The event log query is far too slow for a one-second tick, and the
    /// answer only changes when the machine sleeps or reboots.
    /// </summary>
    public static UptimeFacts Get()
    {
        if (_cached is not null && DateTime.Now - _stamp < TimeSpan.FromMinutes(5)) return _cached;

        // Explicitly GetTickCount64, not Environment.TickCount64: .NET 11 Preview 7 redefined the
        // latter to exclude sleep, which changed this number by 4.12 days on this machine. See
        // Native.TimeSinceBootIncludingSleep.
        TimeSpan kernel = Native.TimeSinceBootIncludingSleep();
        DateTime? onSince = MostRecentBootOrResume();

        _cached = new UptimeFacts(
            onSince is { } when ? DateTime.Now - when : null,
            onSince,
            kernel,
            FastStartupEnabled());
        _stamp = DateTime.Now;
        return _cached;
    }

    private static DateTime? MostRecentBootOrResume()
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName, Query)
            {
                ReverseDirection = true,   // newest first, so the first hit is the answer
            };

            using var reader = new EventLogReader(query);
            using EventRecord? record = reader.ReadEvent();
            return record?.TimeCreated;
        }
        catch
        {
            // Reading the System log can be denied by policy. Returning null is honest: the caller
            // falls back to the kernel counter and says which figure it is showing.
            return null;
        }
    }

    private static bool FastStartupEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            return (key?.GetValue("HiberbootEnabled") as int?) == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The line explaining why another tool says a much larger number, or null when they agree.
    /// </summary>
    public static string? DisagreementNote(UptimeFacts facts)
    {
        if (!facts.CountersDisagree) return null;

        string kernel = $"{facts.SinceKernelBoot.TotalDays:N1} days";
        string on = ReportRenderer.Age(facts.Best);
        string cause = facts.FastStartupEnabled
            ? "Fast Startup is ON, so shutting down hibernates the kernel session rather than "
              + "stopping it and neither counter resets."
            : "The machine has resumed from sleep or hibernation since that boot.";

        return $"This PC has been on for {on}, but its kernel boot counter reads {kernel}. {cause} "
             + "Task Manager and most other tools show the larger figure.";
    }
}
