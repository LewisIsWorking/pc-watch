using System.Net;
using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Checking for updates because the user clicked "Check for updates".
/// </summary>
/// <remarks>
/// ⛔ 2026-09-10. A manual check deliberately differs from the startup check in three ways, and each
///    is the opposite of what copying the startup check would give:
///
///      1. The opt-out does NOT apply - clicking the button IS the consent.
///      2. A previously declined version is NOT hidden - the user is asking what the latest is.
///      3. A result is ALWAYS shown - a silent manual check is indistinguishable from a dead button.
///
/// ⚠️ MessageBox.Show is never reached: every form here has no window handle, so CheckNowAsync
///    returns after the request and before the dialog. That boundary is what makes the network half
///    testable at all, and ManualOutcome carries the half that decides what the dialog would say.
/// </remarks>
[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class UpdatePromptManualTests : SettingsRedirectFixture
{
    private static AvailableUpdate Update(string version = "9.9.9") =>
        new(version, "https://example.invalid/release", "https://example.invalid/download", "notes");

    // ── What it says ────────────────────────────────────────────────────────────────────────────

    [Test]
    public void A_newer_release_is_OFFERED_with_the_usual_message()
    {
        var (message, isOffer) = UpdatePrompt.ManualOutcome(Update("9.9.9"), error: null);

        isOffer.Should().BeTrue("there is something to accept or decline");
        message.Should().Be(UpdatePrompt.BuildMessage(Update("9.9.9")),
            "the manual path must not grow a second, drifting copy of the wording");
    }

    [Test]
    public void Nothing_newer_says_UP_TO_DATE_and_names_the_running_version()
    {
        var (message, isOffer) = UpdatePrompt.ManualOutcome(update: null, error: null);

        isOffer.Should().BeFalse();
        message.Should().Contain("latest version").And.Contain(AppVersion.Number,
            "'you are up to date' is only reassuring if it says what you are on");
    }

    [Test]
    public void A_FAILED_CHECK_NEVER_CLAIMS_TO_BE_UP_TO_DATE()
    {
        // ⛔ THE ONE THAT MATTERS. update is null when the check fails AND when there is nothing new,
        //    so a naive "null means up to date" turns every network failure into a confident false
        //    reassurance - an answer produced by not knowing.
        var (message, isOffer) = UpdatePrompt.ManualOutcome(update: null, error: "no such host");

        isOffer.Should().BeFalse();
        message.Should().Contain("Could not check").And.Contain("no such host");
        message.Should().NotContain("latest version", "a failure is not a clean bill of health");
    }

    [Test]
    public void An_offer_wins_even_if_an_error_string_is_left_over()
    {
        // LastError can belong to an EARLIER failed check. A later success must not be reported as
        // a failure just because the stale message is still sitting there.
        var (_, isOffer) = UpdatePrompt.ManualOutcome(Update("9.9.9"), error: "stale earlier failure");

        isOffer.Should().BeTrue();
    }

    // ── What it does ────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task A_MANUAL_CHECK_IGNORES_THE_AUTOMATIC_OPT_OUT()
    {
        // ⭐ CheckForUpdates=false means "do not phone home on your own". Refusing an explicit click
        //   would leave a menu item that does nothing and no way to discover why.
        var http = FakeHttp.Json(FakeHttp.Release(tag: "v99.0.0"));
        using var form = new Form();

        await UpdatePrompt.CheckNowAsync(form, new UpdateChecker(http), new Settings { CheckForUpdates = false });

        http.Requests.Should().ContainSingle("the user asked, so the request is made");
    }

    [Test]
    public async Task The_automatic_check_STILL_honours_the_opt_out()
    {
        // The other half: adding a manual path must not have loosened the automatic one.
        var http = FakeHttp.Json(FakeHttp.Release(tag: "v99.0.0"));
        using var form = new Form();

        await UpdatePrompt.CheckAsync(form, new UpdateChecker(http), new Settings { CheckForUpdates = false });

        http.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task A_manual_check_does_not_record_anything_as_declined()
    {
        // Merely asking must not write SkipVersion, or the next automatic check would stay silent
        // about a release the user never actually turned down.
        var http = FakeHttp.Json(FakeHttp.Release(tag: "v99.0.0"));
        var settings = new Settings();
        using var form = new Form();

        await UpdatePrompt.CheckNowAsync(form, new UpdateChecker(http), settings);

        settings.SkipVersion.Should().BeNull();
        File.Exists(SettingsPath).Should().BeFalse("nothing was decided, so nothing is saved");
    }

    [Test]
    public async Task A_failing_manual_check_does_not_throw_at_the_menu()
    {
        var http = FakeHttp.Returning(HttpStatusCode.Forbidden);
        using var form = new Form();

        Func<Task> check = () => UpdatePrompt.CheckNowAsync(form, new UpdateChecker(http), new Settings());

        await check.Should().NotThrowAsync("a tray menu click must never crash the app");
    }
}
