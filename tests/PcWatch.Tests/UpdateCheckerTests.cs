using System.Net;
using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Every branch of the update check, especially the ones that fail.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-04. This file was at 0% coverage. It is the most dangerous kind of untested code:
///    failure is SILENT BY DESIGN, so a broken check is indistinguishable from "you are up to date"
///    and nobody would ever report it. The app would simply stop offering updates, for ever, quietly.
///
/// ⚠️ THE VERSION COMPARISON IS THE POINT. A string compare puts "1.10.0" BEFORE "1.9.0", so updates
///    would silently stop at the tenth minor release - a bug that cannot manifest for months and
///    then looks like the server's fault. It is pinned explicitly below.
/// </remarks>
[TestFixture]
public sealed class UpdateCheckerTests
{
    private static string Newer => "99.0.0";
    private static string Older => "0.0.1";

    [Test]
    public async Task A_newer_release_is_offered()
    {
        var http = FakeHttp.Json(FakeHttp.Release(tag: $"v{Newer}", body: "the notes"));
        var checker = new UpdateChecker(http);

        AvailableUpdate? update = await checker.CheckAsync();

        update.Should().NotBeNull();
        update!.Version.Should().Be(Newer, "the leading v is stripped for display");
        update.Notes.Should().Be("the notes");
        checker.LastError.Should().BeNull();
        checker.LastChecked.Should().NotBeNull();
    }

    [Test]
    public async Task An_older_release_is_not_offered()
    {
        var checker = new UpdateChecker(FakeHttp.Json(FakeHttp.Release(tag: $"v{Older}")));

        (await checker.CheckAsync()).Should().BeNull();
        checker.LastError.Should().BeNull("a successful check that finds nothing is not an error");
    }

    [Test]
    public async Task The_running_version_itself_is_not_offered()
    {
        var checker = new UpdateChecker(FakeHttp.Json(FakeHttp.Release(tag: AppVersion.Number)));

        (await checker.CheckAsync()).Should().BeNull("equal is not newer");
        checker.LastError.Should().BeNull();
    }

    [Test]
    public async Task Version_10_beats_version_9_despite_sorting_before_it_as_a_string()
    {
        // ⛔ The regression this guards: "1.10.0" < "1.9.0" alphabetically. A string comparison here
        //    would return null and updates would stop at the tenth minor release.
        string.Compare("1.10.0", "1.9.0", StringComparison.Ordinal)
            .Should().BeNegative("premise: a STRING compare really does get this wrong");

        var checker = new UpdateChecker(FakeHttp.Json(FakeHttp.Release(tag: "v1.10.0")));
        AppVersion.Compare("1.10.0", "1.9.0").Should().BePositive("but the version compare is right");

        // And the checker must use the version compare, not the string one.
        (await checker.CheckAsync()).Should().NotBeNull();
    }

    [Test]
    public async Task A_non_success_status_reports_the_code_and_offers_nothing()
    {
        var checker = new UpdateChecker(FakeHttp.Returning(HttpStatusCode.Forbidden));

        (await checker.CheckAsync()).Should().BeNull();
        checker.LastError.Should().Be("GitHub returned 403",
            "403 is what a missing User-Agent looks like, and it must not read as 'no updates'");
        checker.LastChecked.Should().NotBeNull("a rejected check still happened");
    }

    [Test]
    public async Task A_release_with_no_tag_is_reported_rather_than_guessed_at()
    {
        var checker = new UpdateChecker(FakeHttp.Json(FakeHttp.Release(tag: null)));

        (await checker.CheckAsync()).Should().BeNull();
        checker.LastError.Should().Be("release had no tag");
    }

    [Test]
    public async Task A_blank_tag_is_treated_as_no_tag()
    {
        var checker = new UpdateChecker(FakeHttp.Json(FakeHttp.Release(tag: "   ")));

        (await checker.CheckAsync()).Should().BeNull();
        checker.LastError.Should().Be("release had no tag");
    }

    [Test]
    public async Task Malformed_json_is_caught_and_never_thrown_at_the_caller()
    {
        var checker = new UpdateChecker(FakeHttp.Json("{ this is not json"));

        (await checker.CheckAsync()).Should().BeNull();
        checker.LastError.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task A_network_failure_is_swallowed_and_recorded()
    {
        var checker = new UpdateChecker(FakeHttp.Throwing(new HttpRequestException("no such host")));

        (await checker.CheckAsync()).Should().BeNull("a monitoring tool must not pop a dialog");
        checker.LastError.Should().Contain("no such host");
        checker.LastChecked.Should().NotBeNull();
    }

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
