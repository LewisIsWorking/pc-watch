namespace PcWatch;

/// <summary>
/// Entry point. Also hosts <c>--self-test</c>, which is how the heuristics stay proven.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (WantsSelfTest(args))
        {
            return SelfTest.Run();
        }

        using var instance = new SingleInstance("PcWatch");
        if (!instance.IsFirstInstance)
        {
            // Clicking a pinned taskbar button must SHOW the running app, not start a rival copy.
            instance.SignalExistingInstance();
            return 0;
        }

        ApplicationConfiguration.Initialize();

#if NET11_0_OR_GREATER
        // 2026-09-01. .NET 11 Preview 7 added an application-level visual styles switch. Guarded by
        // the framework symbol so the same source still compiles against net10.0-windows, which is
        // what the shipping build uses - the preview is an experiment, not a migration.
        //
        // ⛔ 2026-09-12, CORRECTED: that last sentence stopped being true on 2026-09-02, when the
        //    csproj made net11.0-windows the default and the release SELF-CONTAINED on .NET 11. The
        //    shipping build takes THIS branch. net10.0-windows is now the fallback, used only where no
        //    .NET 11 SDK is installed.
        //
        // ⭐ Still present in .NET 11 RC 1 (11.0.100-rc.1.26425.128): preview-only APIs are often
        //   renamed before RC, and this one builds with 0 errors and 0 warnings. Verified 2026-09-12.
        Application.SetDefaultVisualStylesMode(VisualStylesMode.Net11);
#endif

        // --no-update-check disables the one outbound request this app makes, permanently: it is
        // written to settings rather than applied to this run only, so it does not have to be
        // remembered on every launch.
        if (WantsNoUpdateCheck(args))
        {
            DisableUpdateChecks();
        }

        using var form = new MainForm();

        // --monitor left|right|primary|<1-based index>. A one-off override: the placement is saved
        // on exit, so it only has to be passed once and the window reopens there from then on.
        string? monitor = ArgumentValue(args, "--monitor");
        if (monitor is not null)
        {
            form.Shown += (_, _) => form.MoveToMonitor(monitor);
        }

        instance.ActivationRequested += () =>
        {
            // Fired on the listener thread; UI work has to hop back to the message loop.
            if (form.IsHandleCreated) form.BeginInvoke(form.RestoreFromTray);
        };

        Application.Run(form);
        return 0;
    }

    // ── The command line, as testable decisions ────────────────────────────────────────────────
    //
    // 2026-09-06. Main is a composition root: it ends in Application.Run, which blocks for the life
    // of the app, so it cannot be called from a test at all. Extracting the DECISIONS it makes
    // leaves Main as wiring and puts the parsing - which has real edge cases and no UI - where it
    // can be exercised.

    /// <summary>Run the in-app self test instead of the window.</summary>
    internal static bool WantsSelfTest(string[] args) => HasFlag(args, "--self-test");

    /// <summary>
    /// Turn off the one outbound request this app makes.
    /// </summary>
    /// <remarks>
    /// ⚠️ PERMANENT, not for this run. It is written to settings so it does not have to be
    ///    remembered on every launch, which is the difference between an opt-out and a chore.
    /// </remarks>
    internal static bool WantsNoUpdateCheck(string[] args) => HasFlag(args, "--no-update-check");

    /// <summary>Persist the update opt-out.</summary>
    internal static void DisableUpdateChecks()
    {
        Settings settings = SettingsStore.Load();
        settings.CheckForUpdates = false;
        SettingsStore.Save(settings);
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Value of "--name value" or "--name=value", or null when absent.</summary>
    internal static string? ArgumentValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
            {
                return args[i][(name.Length + 1)..];
            }
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
