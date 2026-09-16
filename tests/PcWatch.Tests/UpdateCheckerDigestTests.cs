using System.Text.Json;
using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Reading GitHub's published SHA-256, which decides whether the app may install an update itself.
/// </summary>
[TestFixture]
public sealed class UpdateCheckerDigestTests
{
    private const string Real110Digest = "bf292de06b0790ba9e076065c0e34b2d5abf577d584d23ccb24b0787d32569cc";

    private static async Task<AvailableUpdate?> Check(string json) =>
        await new UpdateChecker(FakeHttp.Json(json)).CheckAsync();

    [Test]
    public async Task The_digest_of_the_chosen_asset_is_read()
    {
        // The real v1.1.0 digest, measured 2026-09-16 to match the downloaded bytes exactly.
        AvailableUpdate? update = await Check(FakeHttp.ReleaseWithAsset(
            "v99.0.0", "PcWatch-99.0.0-win-x64.zip", "https://example.invalid/a.zip", $"sha256:{Real110Digest}"));

        update!.Sha256.Should().Be(Real110Digest);
        update.DownloadUrl.Should().Be("https://example.invalid/a.zip");
    }

    [Test]
    public async Task A_release_with_no_digest_offers_no_in_app_install()
    {
        AvailableUpdate? update = await Check(FakeHttp.ReleaseWithAsset(
            "v99.0.0", "PcWatch.zip", "https://example.invalid/a.zip", digest: null));

        update!.Sha256.Should().BeNull("with nothing to verify against, only the download page is safe");
    }

    [Test]
    public async Task With_no_downloadable_asset_there_is_no_digest_either()
    {
        // The download url falls back to the release PAGE. A digest attached to a web page would be
        // checked against HTML and could never match.
        AvailableUpdate? update = await Check(FakeHttp.Release(tag: "v99.0.0"));

        update!.DownloadUrl.Should().Be(update.Url);
        update.Sha256.Should().BeNull();
    }

    [TestCase($"sha256:{Real110Digest}", Real110Digest, TestName = "the real format")]
    [TestCase("SHA256:BF292DE06B0790BA9E076065C0E34B2D5ABF577D584D23CCB24B0787D32569CC", Real110Digest,
        TestName = "upper case is normalised")]
    [TestCase("sha512:abcdef", null, TestName = "another algorithm is not mistaken for sha256")]
    [TestCase("sha256:not-hex-at-all", null, TestName = "not hex")]
    [TestCase("sha256:abc123", null, TestName = "too short to be a sha256")]
    [TestCase(Real110Digest, null, TestName = "missing the algorithm prefix")]
    public void Only_a_well_formed_sha256_digest_is_accepted(string digest, string? expected)
    {
        // ⚠️ A malformed digest accepted as real would fail verification on EVERY download and read as
        //    a corrupted file each time, rather than as what it is: a release with no usable checksum.
        using JsonDocument doc = JsonDocument.Parse($"{{\"digest\":\"{digest}\"}}");

        UpdateChecker.ParseSha256(doc.RootElement).Should().Be(expected);
    }

    [Test]
    public void A_null_digest_field_is_treated_as_absent()
    {
        using JsonDocument doc = JsonDocument.Parse("{\"digest\":null}");

        UpdateChecker.ParseSha256(doc.RootElement).Should().BeNull();
    }
}
