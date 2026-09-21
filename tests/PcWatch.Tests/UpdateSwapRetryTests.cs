using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Moving the running exe aside while another program briefly holds it open.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-21. Found by the first real end-to-end update against the live v1.2.0 release: a process
///    holding PcWatch.exe open at the moment of the swap made the update fail, and the message blamed
///    folder permissions. The re-run with nothing holding the file then updated cleanly in 12.6 s.
/// </remarks>
[TestFixture]
public sealed class UpdateSwapRetryTests
{
    private UpdateFilesForTests _files = null!;

    [SetUp]
    public void MakeFolder() => _files = new UpdateFilesForTests();

    [TearDown]
    public void RemoveFolder() => _files.Dispose();

    private static IOException SharingViolation() =>
        new("The process cannot access the file because it is being used by another process.",
            unchecked((int)0x80070020));

    [Test]
    public void A_BRIEFLY_HELD_EXE_IS_WAITED_OUT_AND_THE_SWAP_SUCCEEDS()
    {
        // The first move (aside) fails twice with a sharing violation, then succeeds.
        int asideCalls = 0, waits = 0;
        void Move(string from, string to)
        {
            if (to.EndsWith(".old", StringComparison.Ordinal) && ++asideCalls <= 2) throw SharingViolation();
        }

        UpdateSwap.Swap(_files.PathOf("PcWatch.exe"), _files.PathOf("new.exe"), Move, _ => waits++);

        asideCalls.Should().Be(3);
        waits.Should().Be(2, "one pause after each failed attempt, none after success");
    }

    [Test]
    public void A_FILE_HELD_FOR_TOO_LONG_FAILS_WITH_THE_RIGHT_DIAGNOSIS_AND_CHANGES_NOTHING()
    {
        int asideCalls = 0;
        void Move(string from, string to)
        {
            if (to.EndsWith(".old", StringComparison.Ordinal)) { asideCalls++; throw SharingViolation(); }
            throw new InvalidOperationException("the replacement must never be moved in");
        }

        Action swap = () => UpdateSwap.Swap(_files.PathOf("PcWatch.exe"), _files.PathOf("new.exe"), Move, _ => { });

        swap.Should().Throw<UpdateFailedException>()
            .Which.Message.Should().Contain("held open by another program")
            .And.NotContain("not writable", "the folder is writable; blaming it sends the user the wrong way");
        asideCalls.Should().Be(UpdateSwap.AsideAttempts);
    }

    [Test]
    public void ACCESS_DENIED_IS_NOT_RETRIED()
    {
        // A folder the user cannot write to stays that way. Waiting three seconds would only delay
        // the same answer.
        int asideCalls = 0, waits = 0;
        void Move(string from, string to) { asideCalls++; throw new UnauthorizedAccessException("Access is denied."); }

        Action swap = () => UpdateSwap.Swap(_files.PathOf("PcWatch.exe"), _files.PathOf("new.exe"), Move, _ => waits++);

        swap.Should().Throw<UpdateFailedException>().Which.Message.Should().Contain("not writable");
        asideCalls.Should().Be(1);
        waits.Should().Be(0);
    }

    [TestCase(unchecked((int)0x80070020), true, TestName = "sharing violation (32) is transient")]
    [TestCase(unchecked((int)0x80070021), true, TestName = "lock violation (33) is transient")]
    [TestCase(unchecked((int)0x80070005), false, TestName = "access denied (5) is not")]
    [TestCase(unchecked((int)0x80070002), false, TestName = "file not found (2) is not")]
    public void Only_sharing_and_lock_violations_count_as_transient(int hresult, bool expected)
    {
        UpdateSwap.IsSharingViolation(new IOException("x", hresult)).Should().Be(expected);
    }

    [Test]
    public async Task A_REAL_FILE_HELD_OPEN_THEN_RELEASED_IS_SWAPPED()
    {
        // ⭐ The end-to-end condition with real files and real waiting: something opens the exe without
        //   FILE_SHARE_DELETE, as a scanner does, and lets go after 0.6 s.
        string current = _files.CurrentExe();
        string replacement = _files.PathOf("new.exe");
        File.WriteAllText(replacement, "NEW VERSION");

        var holder = new FileStream(current, FileMode.Open, FileAccess.Read, FileShare.Read);
        Task release = Task.Delay(600).ContinueWith(_ => holder.Dispose());

        UpdateSwap.Swap(current, replacement);
        await release;

        File.ReadAllText(current).Should().Be("NEW VERSION");
        File.ReadAllText(current + ".old").Should().Be("OLD VERSION");
    }
}
