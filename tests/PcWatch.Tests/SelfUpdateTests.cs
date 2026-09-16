using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// The whole in-place update, end to end, against files in a temp folder.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-16. THE PROMISE UNDER TEST: no failure leaves no working copy. The updater was held back
///    for exactly that reason - "a self-update that fails halfway leaves no working copy at all" - so
///    every failure case here asserts the ORIGINAL file is still there, byte for byte, and that
///    restart was never called.
/// </remarks>
[TestFixture]
public sealed class SelfUpdateTests
{
    private UpdateFilesForTests _files = null!;

    [SetUp]
    public void MakeFolder() => _files = new UpdateFilesForTests();

    [TearDown]
    public void RemoveFolder() => _files.Dispose();

    private async Task<(Exception? Error, List<string> Restarts)> Install(byte[] served, string? sha256)
    {
        var restarts = new List<string>();
        string current = _files.CurrentExe();
        try
        {
            await SelfUpdate.InstallAsync(
                UpdateFilesForTests.Update(sha256), current, _files.PathOf("work"),
                new UpdateDownloader(FakeHttp.Bytes(served)), restarts.Add);
            return (null, restarts);
        }
        catch (Exception ex)
        {
            return (ex, restarts);
        }
    }

    [Test]
    public async Task A_VERIFIED_UPDATE_REPLACES_THE_EXE_KEEPS_THE_OLD_ONE_AND_RESTARTS()
    {
        byte[] zip = UpdateFilesForTests.ReleaseZip("NEW VERSION");

        var (error, restarts) = await Install(zip, UpdateFilesForTests.Sha256Of(zip));

        error.Should().BeNull();
        File.ReadAllText(_files.PathOf("PcWatch.exe")).Should().Be("NEW VERSION");
        File.ReadAllText(_files.PathOf("PcWatch.exe.old")).Should().Be("OLD VERSION",
            "the old copy stays until the NEW version has launched and can delete it");
        restarts.Should().Equal(_files.PathOf("PcWatch.exe"));
    }

    [Test]
    public async Task The_downloaded_archive_is_removed_once_its_exe_is_in_place()
    {
        byte[] zip = UpdateFilesForTests.ReleaseZip();

        await Install(zip, UpdateFilesForTests.Sha256Of(zip));

        File.Exists(_files.PathOf(Path.Combine("work", "PcWatch-9.9.9-win-x64.zip"))).Should().BeFalse(
            "a 47 MB archive left in temp after every update is a slow leak");
    }

    [Test]
    public async Task A_CHECKSUM_MISMATCH_CHANGES_NOTHING_AND_NEVER_RESTARTS()
    {
        // ⛔ The core guarantee. Verification happens before the running exe is touched.
        byte[] zip = UpdateFilesForTests.ReleaseZip("TAMPERED");

        var (error, restarts) = await Install(zip, UpdateFilesForTests.Sha256Of([1, 2, 3]));

        error.Should().BeOfType<UpdateFailedException>()
            .Which.LeftDiskChanged.Should().BeFalse();
        File.ReadAllText(_files.PathOf("PcWatch.exe")).Should().Be("OLD VERSION");
        File.Exists(_files.PathOf("PcWatch.exe.old")).Should().BeFalse();
        restarts.Should().BeEmpty();
    }

    [Test]
    public async Task An_archive_without_PcWatch_exe_changes_nothing()
    {
        byte[] zip = UpdateFilesForTests.ReleaseZip(exeEntryName: "SomethingElse.exe");

        var (error, restarts) = await Install(zip, UpdateFilesForTests.Sha256Of(zip));

        error.Should().BeOfType<UpdateFailedException>().Which.Message.Should().Contain("PcWatch.exe");
        File.ReadAllText(_files.PathOf("PcWatch.exe")).Should().Be("OLD VERSION");
        restarts.Should().BeEmpty();
    }

    [Test]
    public async Task No_published_checksum_means_no_install_and_NO_DOWNLOAD()
    {
        var http = FakeHttp.Bytes(UpdateFilesForTests.ReleaseZip());
        string current = _files.CurrentExe();

        Func<Task> install = () => SelfUpdate.InstallAsync(
            UpdateFilesForTests.Update(sha256: null), current, _files.PathOf("work"),
            new UpdateDownloader(http), _ => { });

        await install.Should().ThrowAsync<UpdateFailedException>();
        http.Requests.Should().BeEmpty("with nothing to verify against there is no reason to fetch 47 MB");
        File.ReadAllText(current).Should().Be("OLD VERSION");
    }

    // ── When in-app install is allowed at all ──────────────────────────────────────────────────

    [TestCase(true, true, true, TestName = "published single file, digest present -> install")]
    [TestCase(true, false, false, TestName = "published single file, no digest -> page")]
    [TestCase(false, true, false, TestName = "dev or test build, digest present -> page")]
    public void Install_needs_BOTH_a_published_build_and_a_digest(bool singleFile, bool hasDigest, bool expected)
    {
        AvailableUpdate update = UpdateFilesForTests.Update(hasDigest ? new string('a', 64) : null);

        SelfUpdate.CanInstall(update, singleFile).Should().Be(expected);
    }

    [Test]
    public void THIS_TEST_RUN_CAN_NEVER_SELF_UPDATE()
    {
        // ⛔ The test host is not the published exe. If this ever returned true, some later test path
        //    could rename the runner itself.
        SelfUpdate.CanInstall(UpdateFilesForTests.Update(new string('a', 64))).Should().BeFalse();
    }

    [Test]
    public void A_folder_holding_PcWatch_dll_is_a_dev_build_not_the_published_exe()
    {
        string exe = _files.CurrentExe();
        SelfUpdate.IsPublishedSingleFile(_files.Root, exe).Should().BeTrue("premise: only the exe is there");

        File.WriteAllText(_files.PathOf("PcWatch.dll"), "");

        SelfUpdate.IsPublishedSingleFile(_files.Root, exe).Should().BeFalse();
    }

    [TestCase(null, TestName = "no process path")]
    [TestCase(@"C:\Program Files\dotnet\dotnet.exe", TestName = "running under dotnet.exe")]
    [TestCase(@"C:\somewhere\PcWatch.dll", TestName = "not an exe")]
    public void Anything_but_a_real_exe_is_refused(string? processPath)
    {
        SelfUpdate.IsPublishedSingleFile(_files.Root, processPath).Should().BeFalse();
    }
}
