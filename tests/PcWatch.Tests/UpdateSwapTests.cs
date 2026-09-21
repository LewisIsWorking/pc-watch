using System.Net;
using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Extracting the new exe, swapping it in, and every way that can fail safely.
/// </summary>
[TestFixture]
public sealed class UpdateSwapTests
{
    private UpdateFilesForTests _files = null!;

    [SetUp]
    public void MakeFolder() => _files = new UpdateFilesForTests();

    [TearDown]
    public void RemoveFolder() => _files.Dispose();

    // ── Swap ────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void A_REPLACEMENT_THAT_CANNOT_BE_MOVED_IN_PUTS_THE_ORIGINAL_BACK()
    {
        // ⛔ The rollback. The running exe has already been renamed aside when the second move fails,
        //    so without this there would be no PcWatch.exe at all.
        string current = _files.CurrentExe();

        Action swap = () => UpdateSwap.Swap(current, _files.PathOf("does-not-exist.exe"));

        swap.Should().Throw<UpdateFailedException>().Which.LeftDiskChanged.Should().BeFalse();
        File.ReadAllText(current).Should().Be("OLD VERSION");
        File.Exists(current + ".old").Should().BeFalse("the rename was undone, not left half-done");
    }

    [Test]
    public void IF_EVEN_THE_ROLLBACK_FAILS_THE_ERROR_SAYS_THE_DISK_WAS_CHANGED()
    {
        // ⛔ The one path that leaves the running exe moved aside. It cannot be produced with real files
        //    from a single thread, so the moves are scripted: aside succeeds, both later moves fail.
        int calls = 0;
        void Move(string from, string to)
        {
            if (++calls > 1) throw new IOException($"move {calls} failed");
        }

        Action swap = () => UpdateSwap.Swap(_files.PathOf("PcWatch.exe"), _files.PathOf("new.exe"), Move);

        swap.Should().Throw<UpdateFailedException>()
            .Which.Should().Match<UpdateFailedException>(e => e.LeftDiskChanged && e.Message.Contains("PcWatch.exe.old"));
        calls.Should().Be(3, "aside, replace, and the attempted restore");
    }

    [Test]
    public void AN_EXE_HELD_OPEN_THROUGHOUT_CHANGES_NOTHING()
    {
        // ⚠️ 2026-09-21, CORRECTED. This was named AN_UNWRITABLE_INSTALL and asserted "not writable",
        //    but holding the file open is a SHARING VIOLATION, not a permissions problem - the same
        //    conflation the first real end-to-end update exposed. It now asserts the diagnosis that is
        //    true. A genuinely unwritable folder is covered by ACCESS_DENIED_IS_NOT_RETRIED.
        string current = _files.CurrentExe();
        string replacement = _files.PathOf("new.exe");
        File.WriteAllText(replacement, "NEW VERSION");

        using (new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Action swap = () => UpdateSwap.Swap(current, replacement);
            swap.Should().Throw<UpdateFailedException>().WithMessage("*held open by another program*");
        }

        File.ReadAllText(current).Should().Be("OLD VERSION");
        File.Exists(replacement).Should().BeTrue("nothing was moved");
    }

    [Test]
    public void A_stale_old_copy_from_an_earlier_update_does_not_block_the_next_one()
    {
        string current = _files.CurrentExe();
        File.WriteAllText(current + ".old", "ANCIENT");
        string replacement = _files.PathOf("new.exe");
        File.WriteAllText(replacement, "NEW VERSION");

        UpdateSwap.Swap(current, replacement);

        File.ReadAllText(current).Should().Be("NEW VERSION");
        File.ReadAllText(current + ".old").Should().Be("OLD VERSION");
    }

    [Test]
    public void Cleanup_removes_the_old_copy_and_is_harmless_when_there_is_none()
    {
        string current = _files.CurrentExe();
        File.WriteAllText(current + ".old", "OLD");

        UpdateCleanup.CleanupAfterUpdate(current);
        File.Exists(current + ".old").Should().BeFalse();

        Action again = () => UpdateCleanup.CleanupAfterUpdate(current);
        again.Should().NotThrow();
    }

    // ── Extract ─────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void The_exe_is_extracted_from_a_release_shaped_zip()
    {
        string zip = _files.PathOf("release.zip");
        File.WriteAllBytes(zip, UpdateFilesForTests.ReleaseZip("NEW VERSION"));

        string exe = UpdateSwap.ExtractExecutable(zip, _files.Root);

        File.ReadAllText(exe).Should().Be("NEW VERSION");
    }

    [TestCase("../PcWatch.exe", TestName = "parent-directory traversal")]
    [TestCase("nested/PcWatch.exe", TestName = "not at the root")]
    public void ONLY_PcWatch_exe_AT_THE_ROOT_IS_ACCEPTED(string entryName)
    {
        // ⛔ An entry named "../PcWatch.exe" extracts wherever it says. Matching the exact root name,
        //    to a fixed target path, is what keeps a hostile or broken archive inside the work folder.
        string zip = _files.PathOf("release.zip");
        File.WriteAllBytes(zip, UpdateFilesForTests.ReleaseZip(exeEntryName: entryName));

        Action extract = () => UpdateSwap.ExtractExecutable(zip, _files.Root);

        extract.Should().Throw<UpdateFailedException>();
        File.Exists(Path.Combine(Path.GetDirectoryName(_files.Root)!, "PcWatch.exe")).Should().BeFalse();
    }

    [Test]
    public void A_corrupt_archive_is_reported_rather_than_thrown_raw()
    {
        string zip = _files.PathOf("release.zip");
        File.WriteAllText(zip, "this is not a zip");

        Action extract = () => UpdateSwap.ExtractExecutable(zip, _files.Root);

        extract.Should().Throw<UpdateFailedException>().WithMessage("*unpacked*");
    }

    [Test]
    public void An_exe_asset_is_used_as_it_is()
    {
        string exe = _files.PathOf("PcWatch-9.9.9.exe");
        File.WriteAllText(exe, "NEW");

        UpdateSwap.ExtractExecutable(exe, _files.Root).Should().Be(exe);
    }

    // ── Waiting for the old process ─────────────────────────────────────────────────────────────

    [Test]
    public void Waiting_for_a_process_that_has_already_gone_returns_at_once()
    {
        UpdateCleanup.WaitForExit(int.MaxValue, TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    [Test]
    public void Waiting_for_a_real_process_returns_when_it_exits()
    {
        using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;

        UpdateCleanup.WaitForExit(child.Id, TimeSpan.FromSeconds(20)).Should().BeTrue();
    }
}
