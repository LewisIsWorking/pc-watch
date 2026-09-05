using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Loading and saving what is remembered between runs.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-05. Untested until now, and it could not have been: SettingsStore.Path was get-only, so
///    exercising Save() would have OVERWRITTEN THE USER'S REAL SETTINGS. Running the test suite
///    would have destroyed the saved window position it exists to protect.
///
/// ⚠️ Every test here points Path at a temp file and puts the real one back afterwards. If this
///    fixture ever fails to restore it, the next run of the app writes to a temp directory instead,
///    so the teardown is not optional politeness.
/// </remarks>
[TestFixture]
public sealed class SettingsStoreTests
{
    private string _realPath = string.Empty;
    private string _temp = string.Empty;

    [SetUp]
    public void PointAtATempFile()
    {
        _realPath = SettingsStore.Path;
        _temp = Path.Combine(Path.GetTempPath(), $"pcwatch-tests-{Guid.NewGuid():N}", "settings.json");
        SettingsStore.Path = _temp;
    }

    [TearDown]
    public void PutTheRealPathBack()
    {
        SettingsStore.Path = _realPath;
        string? dir = Path.GetDirectoryName(_temp);
        if (dir is not null && Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Test]
    public void A_missing_file_loads_defaults_rather_than_failing()
    {
        File.Exists(_temp).Should().BeFalse("premise: nothing has been written yet");

        Settings loaded = SettingsStore.Load();

        loaded.Should().NotBeNull();
        loaded.HasPlacement.Should().BeFalse();
        loaded.Maximized.Should().BeTrue("the documented default is a maximised window");
        loaded.CheckForUpdates.Should().BeTrue();
    }

    [Test]
    public void A_saved_placement_round_trips()
    {
        var original = new Settings
        {
            X = 1920, Y = 100, Width = 1280, Height = 800,
            Maximized = false, HasPlacement = true,
            SkipVersion = "9.9.9", CheckForUpdates = false,
        };

        SettingsStore.Save(original);
        Settings loaded = SettingsStore.Load();

        loaded.Should().BeEquivalentTo(original, "everything remembered must survive a restart");
    }

    [Test]
    public void Saving_creates_the_directory_if_it_is_missing()
    {
        Directory.Exists(Path.GetDirectoryName(_temp)!).Should().BeFalse("premise");

        SettingsStore.Save(new Settings { HasPlacement = true, Width = 100 });

        File.Exists(_temp).Should().BeTrue("a first run has no PcWatch folder yet");
    }

    [Test]
    public void A_CORRUPT_file_loads_defaults_instead_of_stopping_the_app()
    {
        // ⛔ The behaviour that matters. A half-written or hand-edited file must never prevent the
        //    app starting: defaults are always usable, a half-parsed placement is not.
        Directory.CreateDirectory(Path.GetDirectoryName(_temp)!);
        File.WriteAllText(_temp, "{ this is not json at all");

        Settings loaded = SettingsStore.Load();

        loaded.Should().NotBeNull();
        loaded.Maximized.Should().BeTrue("it fell back to defaults");
    }

    [Test]
    public void An_EMPTY_file_loads_defaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_temp)!);
        File.WriteAllText(_temp, string.Empty);

        SettingsStore.Load().Should().NotBeNull();
    }

    [Test]
    public void A_json_null_loads_defaults_rather_than_returning_null()
    {
        // Deserialize returns null for the literal "null", which would NullReferenceException at
        // every use site if the ?? new Settings() were ever removed.
        Directory.CreateDirectory(Path.GetDirectoryName(_temp)!);
        File.WriteAllText(_temp, "null");

        SettingsStore.Load().Should().NotBeNull();
    }

    [Test]
    public void Saving_to_an_impossible_path_does_not_throw()
    {
        // Losing a window position is not worth an error dialog while the app is closing.
        SettingsStore.Path = Path.Combine("Z:", "no-such-drive", "settings.json");

        Action save = () => SettingsStore.Save(new Settings());

        save.Should().NotThrow("a failed save must never interrupt shutdown");
    }

    // ── Is a saved rectangle still usable? ──────────────────────────────────────────────────────

    [TestCase(199, 600, TestName = "too narrow to be worth restoring")]
    [TestCase(800, 149, TestName = "too short to be worth restoring")]
    [TestCase(0, 0, TestName = "degenerate")]
    public void A_window_smaller_than_a_usable_size_is_rejected(int width, int height)
    {
        SettingsStore.IsOnScreen(new Rectangle(0, 0, width, height)).Should().BeFalse();
    }

    [Test]
    public void A_rectangle_far_off_every_monitor_is_rejected()
    {
        // ⚠️ THE FAILURE THIS PREVENTS. A window restored where no monitor is has no reachable title
        //    bar, so it cannot be dragged back. That is indistinguishable from the app failing to
        //    launch, and the user's next move is to launch it again, which the single-instance mutex
        //    turns into nothing happening at all.
        SettingsStore.IsOnScreen(new Rectangle(-40000, -40000, 1200, 800)).Should().BeFalse();
    }

    [Test]
    public void A_rectangle_on_the_primary_screen_is_accepted()
    {
        Screen? primary = Screen.PrimaryScreen;
        Assume.That(primary, Is.Not.Null, "needs a display; skipped on a headless agent");

        Rectangle work = primary!.WorkingArea;
        var onScreen = new Rectangle(work.X + 20, work.Y + 20,
            Math.Min(800, work.Width - 40), Math.Min(600, work.Height - 40));

        SettingsStore.IsOnScreen(onScreen).Should().BeTrue();
    }
}
