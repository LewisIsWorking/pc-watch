using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Asking once per version, and honouring the opt-out before anything is sent.
/// </summary>
/// <remarks>
/// ⚠️ MessageBox.Show is never reached by these tests. A modal dialog does not fail a test, it HANGS
///    it for ever, so the decision either side of the dialog was extracted into Apply and the
///    dialog itself is the only untested line in the file. That is a deliberate boundary, not an
///    oversight.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class UpdatePromptTests : SettingsRedirectFixture
{


    private static AvailableUpdate Update(string version = "9.9.9", string? notes = "some notes") =>
        new(version, "https://example.invalid/release", "https://example.invalid/download", notes ?? "");

    // ── The opt-out ─────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Opting_out_means_NO_REQUEST_IS_EVER_SENT()
    {
        // ⭐ THE POINT OF THE OPT-OUT. Checking and then discarding the answer has ALREADY told
        //   GitHub the app is running, which is the exact thing being opted out of. Asserting "no
        //   dialog appeared" would pass for a version that still phoned home.
        var http = FakeHttp.Json(FakeHttp.Release(tag: "v99.0.0"));
        using var form = new Form();

        await UpdatePrompt.CheckAsync(form, new UpdateChecker(http), new Settings { CheckForUpdates = false });

        http.Requests.Should().BeEmpty("the opt-out is honoured BEFORE the request, not after");
    }

    [Test]
    public async Task Opting_in_does_send_a_request()
    {
        var http = FakeHttp.Json(FakeHttp.Release(tag: "v0.0.1"));
        using var form = new Form();

        await UpdatePrompt.CheckAsync(form, new UpdateChecker(http), new Settings { CheckForUpdates = true });

        http.Requests.Should().ContainSingle("premise: with the opt-out off, a check really happens");
    }

    // ── When nothing should be shown ────────────────────────────────────────────────────────────

    [Test]
    public async Task No_newer_release_shows_nothing()
    {
        var http = FakeHttp.Json(FakeHttp.Release(tag: "v0.0.1"));
        using var form = new Form();

        Func<Task> check = () => UpdatePrompt.CheckAsync(form, new UpdateChecker(http), new Settings());

        await check.Should().NotThrowAsync();
    }

    [Test]
    public async Task A_failed_check_shows_nothing()
    {
        var http = FakeHttp.Throwing(new HttpRequestException("offline"));
        using var form = new Form();

        Func<Task> check = () => UpdatePrompt.CheckAsync(form, new UpdateChecker(http), new Settings());

        await check.Should().NotThrowAsync("a monitoring tool must not pop a dialog about its own update check");
    }

    [Test]
    public async Task A_version_already_declined_is_not_offered_again()
    {
        // ⚠️ ONCE PER VERSION, NOT ONCE PER LAUNCH. A prompt that reappears every start gets
        //    dismissed reflexively, and then the one that matters is dismissed too.
        var http = FakeHttp.Json(FakeHttp.Release(tag: "v99.0.0"));
        using var form = new Form();
        var settings = new Settings { SkipVersion = "99.0.0" };

        await UpdatePrompt.CheckAsync(form, new UpdateChecker(http), settings);

        // It still checked; it simply had nothing to say.
        http.Requests.Should().ContainSingle();
        settings.SkipVersion.Should().Be("99.0.0", "declining is remembered, not re-asked");
    }

    [Test]
    public async Task Nothing_is_shown_when_the_window_has_no_handle_yet()
    {
        // BeginInvoke on a form with no handle throws. The check runs during startup, so this is a
        // real window rather than a theoretical one.
        var http = FakeHttp.Json(FakeHttp.Release(tag: "v99.0.0"));
        using var form = new Form();
        form.IsHandleCreated.Should().BeFalse("premise: nothing has realised the window");

        Func<Task> check = () => UpdatePrompt.CheckAsync(form, new UpdateChecker(http), new Settings());

        await check.Should().NotThrowAsync();
    }

    // ── Acting on the answer ────────────────────────────────────────────────────────────────────

    [Test]
    public void Accepting_opens_the_release_page()
    {
        var opened = new List<string>();
        var settings = new Settings();

        UpdatePrompt.Apply(DialogResult.Yes, Update(), settings, opened.Add);

        opened.Should().Equal("https://example.invalid/release");
    }

    [Test]
    public void Accepting_does_NOT_record_the_version_as_skipped()
    {
        // ⛔ If it did, a user who opens the download page and then does not install would never be
        //    told about that release again.
        var settings = new Settings();

        UpdatePrompt.Apply(DialogResult.Yes, Update("9.9.9"), settings, _ => { });

        settings.SkipVersion.Should().BeNull("accepting is not declining");
    }

    [Test]
    public void Declining_records_the_version_and_opens_nothing()
    {
        var opened = new List<string>();
        var settings = new Settings();

        UpdatePrompt.Apply(DialogResult.No, Update("9.9.9"), settings, opened.Add);

        settings.SkipVersion.Should().Be("9.9.9");
        opened.Should().BeEmpty();
    }

    [Test]
    public void Declining_persists_the_choice_to_disk()
    {
        // The promise is that it stays quiet ACROSS LAUNCHES, which only holds if it reaches disk.
        UpdatePrompt.Apply(DialogResult.No, Update("9.9.9"), new Settings(), _ => { });

        SettingsStore.Load().SkipVersion.Should().Be("9.9.9");
    }

    [Test]
    public void Closing_the_dialog_counts_as_declining()
    {
        // Alt+F4 or the X returns Cancel. Treating that as acceptance would open a browser the user
        // did not ask for.
        var opened = new List<string>();
        var settings = new Settings();

        UpdatePrompt.Apply(DialogResult.Cancel, Update("9.9.9"), settings, opened.Add);

        opened.Should().BeEmpty();
        settings.SkipVersion.Should().Be("9.9.9");
    }
}
