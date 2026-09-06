using System.Net;
using AwesomeAssertions;
using NUnit.Framework;
namespace PcWatch.Tests;

/// <summary>
/// Continued from UpdateCheckerTests, split at the repo's 200-line limit.
/// </summary>
[TestFixture]
public sealed class UpdateCheckerAssetTests
{
    private static string Newer => "99.0.0";
    private static string Older => "0.0.1";

    [Test]
    public async Task The_windows_asset_is_preferred_over_the_release_page()
    {
        var http = FakeHttp.Json(FakeHttp.Release(
            tag: $"v{Newer}",
            assets: [("notes.txt", "https://example.invalid/notes.txt"),
                     ("PcWatch-win-x64.zip", "https://example.invalid/PcWatch.zip")]));

        AvailableUpdate? update = await new UpdateChecker(http).CheckAsync();

        update!.DownloadUrl.Should().Be("https://example.invalid/PcWatch.zip",
            "the .txt must be skipped and the .zip chosen");
    }

    [Test]
    public async Task An_exe_asset_is_accepted_too()
    {
        var http = FakeHttp.Json(FakeHttp.Release(
            tag: $"v{Newer}", assets: [("Setup.exe", "https://example.invalid/Setup.exe")]));

        (await new UpdateChecker(http).CheckAsync())!.DownloadUrl
            .Should().Be("https://example.invalid/Setup.exe");
    }

    [Test]
    public async Task With_no_usable_asset_the_release_page_is_the_download_link()
    {
        var http = FakeHttp.Json(FakeHttp.Release(
            tag: $"v{Newer}", htmlUrl: "https://example.invalid/page",
            assets: [("checksums.txt", "https://example.invalid/checksums.txt")]));

        (await new UpdateChecker(http).CheckAsync())!.DownloadUrl
            .Should().Be("https://example.invalid/page", "the user still needs somewhere to go");
    }

    [Test]
    public async Task An_assets_array_missing_download_urls_falls_back_to_the_page()
    {
        var http = FakeHttp.Json(FakeHttp.ReleaseWithUrllessAsset($"v{Newer}"));

        (await new UpdateChecker(http).CheckAsync())!.DownloadUrl
            .Should().Be("https://example.invalid/page");
    }

    [Test]
    public async Task A_release_with_no_assets_key_at_all_falls_back_to_the_page()
    {
        var http = FakeHttp.Json(FakeHttp.Release(
            tag: $"v{Newer}", htmlUrl: "https://example.invalid/page"));

        (await new UpdateChecker(http).CheckAsync())!.DownloadUrl
            .Should().Be("https://example.invalid/page");
    }

    [Test]
    public async Task Missing_html_url_and_body_degrade_to_empty_rather_than_throwing()
    {
        var http = FakeHttp.Json(FakeHttp.Release(tag: $"v{Newer}", htmlUrl: null, body: null));

        AvailableUpdate? update = await new UpdateChecker(http).CheckAsync();

        update.Should().NotBeNull();
        update!.Url.Should().BeEmpty();
        update.Notes.Should().BeEmpty();
    }

    [Test]
    public async Task The_check_asks_github_for_the_latest_release()
    {
        // ⚠️ Asserting the DESTINATION, not merely that a request happened. A test that only counts
        //    requests passes just as happily when the URL is wrong.
        var http = FakeHttp.Json(FakeHttp.Release());
        await new UpdateChecker(http).CheckAsync();

        http.Requests.Should().ContainSingle();
        http.Requests[0].RequestUri!.ToString()
            .Should().Be("https://api.github.com/repos/LewisIsWorking/pc-watch/releases/latest");
    }

    [Test]
    public async Task The_user_agent_github_demands_is_actually_sent()
    {
        var http = FakeHttp.Json(FakeHttp.Release());
        await new UpdateChecker(http).CheckAsync();

        http.Requests[0].Headers.UserAgent.ToString()
            .Should().StartWith("PcWatch/", "without this GitHub answers 403, which reads as 'no updates'");
    }

    [Test]
    public void Status_line_before_any_check_says_so()
    {
        new UpdateChecker(FakeHttp.Json("{}")).StatusLine().Should().Be("update check: not run yet");
    }

    [Test]
    public async Task Status_line_after_a_clean_check_says_up_to_date()
    {
        var checker = new UpdateChecker(FakeHttp.Json(FakeHttp.Release(tag: $"v{Older}")));
        await checker.CheckAsync();

        checker.StatusLine().Should().Contain("up to date");
    }

    [Test]
    public async Task Status_line_after_a_failure_names_the_failure()
    {
        var checker = new UpdateChecker(FakeHttp.Returning(HttpStatusCode.ServiceUnavailable));
        await checker.CheckAsync();

        checker.StatusLine().Should().Contain("failed").And.Contain("503",
            "'checked and you are current' and 'could not check' must not look the same");
    }
}
