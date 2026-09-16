using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The hand-off between the old process and the copy that replaces it.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-16. The two halves run in DIFFERENT PROCESSES: RestartInfo writes the arguments and
///    FinishReplacing parses them. Nothing checks at compile time that they agree, and if they drift
///    apart the new copy stops waiting, finds the old one still holding the single-instance mutex,
///    and hands control straight back to the OLD version. So the round trip is asserted here.
///
///    The real thing was also run once against the published RC 1 exe (see FinishReplacing).
/// </remarks>
[TestFixture]
public sealed class SelfUpdateHandoffTests
{
    private UpdateFilesForTests _files = null!;

    [SetUp]
    public void MakeFolder() => _files = new UpdateFilesForTests();

    [TearDown]
    public void RemoveFolder() => _files.Dispose();

    [Test]
    public void THE_PROCESS_ID_WRITTEN_BY_RESTART_IS_THE_ONE_FINISH_REPLACING_READS()
    {
        var info = SelfUpdate.RestartInfo(_files.PathOf("PcWatch.exe"), replacingProcessId: 4242);

        string[] args = info.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Program.ArgumentValue(args, SelfUpdate.ReplacingFlag).Should().Be("4242");
    }

    [Test]
    public void The_new_copy_starts_in_its_own_folder_without_the_shell()
    {
        string exe = _files.PathOf("PcWatch.exe");

        var info = SelfUpdate.RestartInfo(exe, 1);

        info.FileName.Should().Be(exe, "the replaced file, not whatever PcWatch.exe the PATH finds first");
        info.WorkingDirectory.Should().Be(_files.Root);
        info.UseShellExecute.Should().BeFalse();
    }

    [Test]
    public void A_PUBLISHED_COPY_CLEANS_UP_THE_OLD_EXE_ONCE_THE_OLD_PROCESS_HAS_GONE()
    {
        string exe = _files.CurrentExe();
        File.WriteAllText(exe + ".old", "previous version");

        // int.MaxValue is never a live process id, so the wait returns at once.
        SelfUpdate.FinishReplacing([SelfUpdate.ReplacingFlag, int.MaxValue.ToString()], _files.Root, exe);

        File.Exists(exe + ".old").Should().BeFalse();
    }

    [Test]
    public void A_normal_launch_also_clears_a_leftover_old_copy()
    {
        // An earlier cleanup can fail while the old file is still locked; the next launch retries.
        string exe = _files.CurrentExe();
        File.WriteAllText(exe + ".old", "previous version");

        SelfUpdate.FinishReplacing([], _files.Root, exe);

        File.Exists(exe + ".old").Should().BeFalse();
    }

    [Test]
    public void A_DEV_BUILD_NEVER_DELETES_ANYTHING_AT_STARTUP()
    {
        // ⛔ PcWatch.dll beside the exe means a build output, not an install. Deleting "*.exe.old" there
        //    is not this app's business, and on a developer's machine it could be anything.
        string exe = _files.CurrentExe();
        File.WriteAllText(exe + ".old", "not ours to delete");
        File.WriteAllText(_files.PathOf("PcWatch.dll"), "");

        SelfUpdate.FinishReplacing([], _files.Root, exe);

        File.Exists(exe + ".old").Should().BeTrue();
    }

    [TestCase("--replacing", TestName = "flag with no value")]
    [TestCase("--replacing=not-a-number", TestName = "value that is not a process id")]
    public void A_malformed_replacing_argument_does_not_stop_startup(string arg)
    {
        string exe = _files.CurrentExe();

        Action finish = () => SelfUpdate.FinishReplacing([arg], _files.Root, exe);

        finish.Should().NotThrow("a bad argument must never be the reason the app does not open");
    }
}
