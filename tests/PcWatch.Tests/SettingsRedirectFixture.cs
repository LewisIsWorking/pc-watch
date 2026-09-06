using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Base fixture for anything that touches the settings file.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-06. SettingsStore.Path is a process-wide static, and several things under test read or
///    WRITE it: MainForm loads on construction and saves on dispose, UpdatePrompt records a declined
///    version, Program persists the update opt-out, WindowPlacement saves the window position.
///
///    Without this redirect every one of those fixtures would move Lewis's real saved window
///    position and update preferences as a side effect of running the suite. That is not a
///    hypothetical: it is what those classes are FOR.
///
/// ⚠️ The teardown is not politeness. If the real path is not restored, the next thing to run in
///    this process writes the user's settings into a temp directory that has just been deleted, and
///    the loss is silent - SettingsStore.Save swallows its own failures by design.
///
/// Four fixtures used to carry an identical copy of this. Shared so a change is made once.
/// </remarks>
public abstract class SettingsRedirectFixture
{
    private string _realPath = string.Empty;

    /// <summary>The temp settings file for the current test. Unique per test.</summary>
    protected string SettingsPath { get; private set; } = string.Empty;

    [SetUp]
    public void RedirectSettingsToATempFile()
    {
        _realPath = SettingsStore.Path;
        SettingsPath = Path.Combine(
            Path.GetTempPath(), $"pcwatch-tests-{Guid.NewGuid():N}", "settings.json");
        SettingsStore.Path = SettingsPath;
    }

    [TearDown]
    public void RestoreTheRealSettingsPath()
    {
        SettingsStore.Path = _realPath;

        string? dir = Path.GetDirectoryName(SettingsPath);
        if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
