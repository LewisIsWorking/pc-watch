using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Deleting the old exe after an update, when Windows has not quite let go of it.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-21. In the real v1.2.0 -> v1.2.1 update the single cleanup attempt collided with the old
///    exe still being released, and 108 MB stayed behind until the next launch. The file is held here
///    the way that release happens: open without FILE_SHARE_DELETE, let go after a moment.
/// </remarks>
[TestFixture]
public sealed class UpdateCleanupTests
{
    private UpdateFilesForTests _files = null!;

    [SetUp]
    public void MakeFolder() => _files = new UpdateFilesForTests();

    [TearDown]
    public void RemoveFolder() => _files.Dispose();

    private (string Exe, string Old) Leftover()
    {
        string exe = _files.CurrentExe();
        File.WriteAllText(exe + ".old", "previous version");
        return (exe, exe + ".old");
    }

    [Test]
    public async Task STRAIGHT_AFTER_AN_UPDATE_A_BRIEFLY_HELD_OLD_EXE_IS_STILL_REMOVED()
    {
        var (exe, old) = Leftover();
        var holder = new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read);
        Task release = Task.Delay(600).ContinueWith(_ => holder.Dispose());

        // int.MaxValue is never a live process, so the wait for the old copy returns at once.
        SelfUpdate.FinishReplacing([SelfUpdate.ReplacingFlag, int.MaxValue.ToString()], _files.Root, exe);
        await release;

        File.Exists(old).Should().BeFalse("the retries outlast a lock that is released within moments");
    }

    [Test]
    public void AN_ORDINARY_LAUNCH_TRIES_ONCE_AND_NEVER_WAITS()
    {
        // ⚠️ Something holding the leftover for a long time must not slow down normal startup.
        var (exe, old) = Leftover();
        int waits = 0;

        using (new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            SelfUpdate.FinishReplacing([], _files.Root, exe, _ => waits++);
        }

        waits.Should().Be(0);
        File.Exists(old).Should().BeTrue("one attempt failed; the next launch will try again");
    }

    [Test]
    public void After_an_update_a_lock_that_never_lifts_gives_up_after_its_attempts()
    {
        var (exe, old) = Leftover();
        int waits = 0;

        using (new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            SelfUpdate.FinishReplacing([SelfUpdate.ReplacingFlag, int.MaxValue.ToString()], _files.Root, exe, _ => waits++);
        }

        waits.Should().Be(9, "ten attempts, a pause between each");
        File.Exists(old).Should().BeTrue("it gives up rather than blocking startup indefinitely");
    }

    [Test]
    public void Nothing_to_delete_is_success_at_once()
    {
        string exe = _files.CurrentExe();
        int waits = 0;

        UpdateCleanup.CleanupAfterUpdate(exe, 10, TimeSpan.FromMilliseconds(300), _ => waits++).Should().BeTrue();
        waits.Should().Be(0);
    }
}
