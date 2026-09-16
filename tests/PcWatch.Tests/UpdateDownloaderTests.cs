using System.Net;
using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Downloading a release asset and proving it is the one GitHub published.
/// </summary>
[TestFixture]
public sealed class UpdateDownloaderTests
{
    private UpdateFilesForTests _files = null!;

    [SetUp]
    public void MakeFolder() => _files = new UpdateFilesForTests();

    [TearDown]
    public void RemoveFolder() => _files.Dispose();

    [Test]
    public async Task The_real_downloader_refuses_an_undigested_release_before_touching_the_network()
    {
        // The production constructor, with the no-digest refusal proving no request is attempted.
        Func<Task> download = () => new UpdateDownloader()
            .DownloadVerifiedAsync(UpdateFilesForTests.Update(sha256: null), _files.Root);

        await download.Should().ThrowAsync<UpdateFailedException>().WithMessage("*checksum*");
    }

    [Test]
    public async Task A_matching_download_is_returned_with_its_exact_bytes()
    {
        byte[] zip = UpdateFilesForTests.ReleaseZip();

        string path = await new UpdateDownloader(FakeHttp.Bytes(zip))
            .DownloadVerifiedAsync(UpdateFilesForTests.Update(UpdateFilesForTests.Sha256Of(zip)), _files.Root);

        File.ReadAllBytes(path).Should().Equal(zip);
    }

    [Test]
    public async Task The_published_digest_is_compared_case_insensitively()
    {
        byte[] zip = UpdateFilesForTests.ReleaseZip();

        Func<Task> download = () => new UpdateDownloader(FakeHttp.Bytes(zip)).DownloadVerifiedAsync(
            UpdateFilesForTests.Update(UpdateFilesForTests.Sha256Of(zip).ToUpperInvariant()), _files.Root);

        await download.Should().NotThrowAsync("hex case is presentation, not content");
    }

    [Test]
    public async Task A_MISMATCHED_DOWNLOAD_IS_DELETED_NOT_LEFT_LYING_AROUND()
    {
        // ⚠️ A file that failed verification but still sits in temp is one something else might run.
        byte[] zip = UpdateFilesForTests.ReleaseZip();

        Func<Task> download = () => new UpdateDownloader(FakeHttp.Bytes(zip)).DownloadVerifiedAsync(
            UpdateFilesForTests.Update(new string('0', 64)), _files.Root);

        await download.Should().ThrowAsync<UpdateFailedException>().WithMessage("*checksum*");
        Directory.GetFiles(_files.Root).Should().BeEmpty();
    }

    [Test]
    public async Task A_failed_download_reports_the_status_and_leaves_no_file()
    {
        Func<Task> download = () => new UpdateDownloader(FakeHttp.Returning(HttpStatusCode.NotFound))
            .DownloadVerifiedAsync(UpdateFilesForTests.Update(new string('a', 64)), _files.Root);

        await download.Should().ThrowAsync<UpdateFailedException>().WithMessage("*404*");
        Directory.GetFiles(_files.Root).Should().BeEmpty();
    }

    [Test]
    public async Task A_network_failure_is_wrapped_in_a_message_fit_to_show()
    {
        Func<Task> download = () => new UpdateDownloader(FakeHttp.Throwing(new HttpRequestException("no such host")))
            .DownloadVerifiedAsync(UpdateFilesForTests.Update(new string('a', 64)), _files.Root);

        await download.Should().ThrowAsync<UpdateFailedException>().WithMessage("*no such host*");
    }

    [Test]
    public async Task The_real_v1_1_0_digest_format_is_what_the_hash_produces()
    {
        // GitHub publishes lower-case hex. Measured 2026-09-16 against the real v1.1.0 asset, whose
        // bytes hashed to exactly the digest the API returned.
        byte[] bytes = "known content"u8.ToArray();
        string path = _files.PathOf("known.bin");
        await File.WriteAllBytesAsync(path, bytes);

        string hash = await UpdateDownloader.HashAsync(path);

        hash.Should().MatchRegex("^[0-9a-f]{64}$").And.Be(UpdateFilesForTests.Sha256Of(bytes));
    }
}
