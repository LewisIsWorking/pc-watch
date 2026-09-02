namespace PcWatch;

/// <summary>
/// Entry point. Also hosts <c>--self-test</c>, which is how the heuristics stay proven.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
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
        Application.SetDefaultVisualStylesMode(VisualStylesMode.Net11);
#endif

        // --no-update-check disables the one outbound request this app makes, permanently: it is
        // written to settings rather than applied to this run only, so it does not have to be
        // remembered on every launch.
        if (args.Any(a => a.Equals("--no-update-check", StringComparison.OrdinalIgnoreCase)))
        {
            Settings settings = SettingsStore.Load();
            settings.CheckForUpdates = false;
            SettingsStore.Save(settings);
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

    /// <summary>Value of "--name value" or "--name=value", or null when absent.</summary>
    private static string? ArgumentValue(string[] args, string name)
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
