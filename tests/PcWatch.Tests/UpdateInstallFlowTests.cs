using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// What the user is told when an accepted update does not complete.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-16. Three failures that must not share a message: nothing changed, installed but not
///    restarted, and the one where the running exe was moved aside and could not be put back. Mixing
///    them up either sends the user to download a version they already have, or tells them their
///    install is untouched when it is not.
/// </remarks>
[TestFixture]
public sealed class UpdateInstallFlowTests
{
    private static readonly AvailableUpdate Update = UpdateFilesForTests.Update(new string('a', 64));

    [Test]
    public void An_ordinary_failure_says_nothing_changed_and_opens_the_page()
    {
        var (message, openPage) = UpdateInstallFlow.Failure(Update, new UpdateFailedException("the download returned 404"));

        message.Should().Contain("was not installed").And.Contain("404").And.Contain("Nothing was changed");
        openPage.Should().BeTrue("the page is still a way to get the update");
    }

    [Test]
    public void A_FAILED_RESTART_SAYS_IT_WAS_INSTALLED_AND_DOES_NOT_OPEN_THE_PAGE()
    {
        // ⛔ The new version is already on disk. Sending the user to download it again is wrong.
        var restart = new UpdateInstallFlow.RestartFailedException(new InvalidOperationException("access denied"));

        var (message, openPage) = UpdateInstallFlow.Failure(Update, restart);

        message.Should().Contain("was installed").And.Contain("start it again").And.Contain("access denied");
        message.Should().NotContain("was not installed");
        openPage.Should().BeFalse();
    }

    [Test]
    public void A_FAILED_ROLLBACK_NEVER_CLAIMS_NOTHING_CHANGED()
    {
        // ⛔ The single case where the disk WAS changed and not restored. "Nothing was changed" here
        //    would be the most dangerous sentence this updater could show.
        var broken = new UpdateFailedException(
            @"the update failed and the original could not be restored - it is at C:\x\PcWatch.exe.old",
            leftDiskChanged: true);

        var (message, openPage) = UpdateInstallFlow.Failure(Update, broken);

        message.Should().NotContain("Nothing was changed");
        message.Should().Contain("Rename it back to PcWatch.exe").And.Contain("PcWatch.exe.old");
        openPage.Should().BeFalse("the fix is a rename, not a download");
    }

    // ── The accept flow ─────────────────────────────────────────────────────────────────────────

    private sealed class World
    {
        public List<string> Pages { get; } = [];
        public List<string> Messages { get; } = [];
        public List<string> Titles { get; } = [];
        public int Installs { get; set; }
    }

    private static async Task<World> Run(bool canInstall, Exception? installFails = null)
    {
        var world = new World();
        await UpdateInstallFlow.RunAsync(
            Update, canInstall,
            install: () => { world.Installs++; return installFails is null ? Task.CompletedTask : Task.FromException(installFails); },
            openPage: world.Pages.Add, showMessage: world.Messages.Add, title: world.Titles.Add,
            originalTitle: "PC Watch");
        return world;
    }

    [Test]
    public async Task When_install_is_not_possible_the_page_opens_and_NOTHING_is_installed()
    {
        World world = await Run(canInstall: false);

        world.Installs.Should().Be(0);
        world.Pages.Should().Equal("https://example.invalid/release");
        world.Titles.Should().BeEmpty("nothing is happening, so the window must not claim otherwise");
    }

    [Test]
    public async Task A_successful_install_shows_progress_and_no_message()
    {
        World world = await Run(canInstall: true);

        world.Installs.Should().Be(1);
        world.Titles.Should().ContainSingle().Which.Should().Contain("installing 9.9.9");
        world.Messages.Should().BeEmpty();
        world.Pages.Should().BeEmpty("the app is restarting into the new version");
    }

    [Test]
    public async Task A_FAILED_INSTALL_PUTS_THE_TITLE_BACK_EXPLAINS_AND_OFFERS_THE_PAGE()
    {
        World world = await Run(canInstall: true, new UpdateFailedException("the download returned 404"));

        world.Titles.Should().Equal("PC Watch - installing 9.9.9...", "PC Watch");
        world.Messages.Should().ContainSingle().Which.Should().Contain("404");
        world.Pages.Should().Equal("https://example.invalid/release");
    }

    [Test]
    public async Task A_failed_restart_explains_but_does_not_send_the_user_to_download_again()
    {
        World world = await Run(canInstall: true,
            new UpdateInstallFlow.RestartFailedException(new InvalidOperationException("denied")));

        world.Messages.Should().ContainSingle().Which.Should().Contain("was installed");
        world.Pages.Should().BeEmpty();
    }

    [Test]
    public void RESTART_EXITS_ONLY_AFTER_THE_NEW_COPY_HAS_STARTED()
    {
        var order = new List<string>();

        UpdateInstallFlow.Restart("PcWatch.exe", _ => order.Add("start"), () => order.Add("exit"));

        order.Should().Equal("start", "exit");
    }

    [Test]
    public void A_START_THAT_FAILS_NEVER_EXITS_THE_APP()
    {
        // ⛔ Exiting anyway would close PC Watch with the update installed and nothing running.
        bool exited = false;

        Action restart = () => UpdateInstallFlow.Restart(
            "PcWatch.exe", _ => throw new InvalidOperationException("denied"), () => exited = true);

        restart.Should().Throw<UpdateInstallFlow.RestartFailedException>()
            .WithInnerException<InvalidOperationException>();
        exited.Should().BeFalse();
    }

    [Test]
    public void An_unexpected_error_is_still_reported_with_its_text()
    {
        var (message, openPage) = UpdateInstallFlow.Failure(Update, new IOException("disk full"));

        message.Should().Contain("unexpected error").And.Contain("disk full");
        message.Should().NotContain("Nothing was changed", "an unexpected error proves nothing about the disk");
        openPage.Should().BeTrue();
    }
}
