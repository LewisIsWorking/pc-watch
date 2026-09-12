using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Loading the window icon, including the fallback no shipped build ever takes.
/// </summary>
/// <remarks>
/// 2026-09-10. The fallback runs when someone copies PcWatch.exe somewhere on its own. It was
/// unreachable while the loader could only look beside the executable, where the icon always is.
/// </remarks>
[TestFixture]
public sealed class AppIconTests
{
    private string _dir = string.Empty;

    [SetUp]
    public void MakeAnEmptyFolder()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"pcwatch-icon-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void RemoveIt()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public void The_real_icon_is_loaded_when_it_sits_beside_the_executable()
    {
        // The build copies PcWatch.ico to the output folder, so the test binary's folder has it.
        Icon icon = AppIcon.Load();

        // ⚠️ Not `using`: if this ever fell back, disposing would destroy the SHARED
        //    SystemIcons.Application instance and break every later test that touches it.
        try
        {
            icon.Should().NotBeSameAs(SystemIcons.Application, "the shipped icon must actually be used");
        }
        finally
        {
            if (!ReferenceEquals(icon, SystemIcons.Application)) icon.Dispose();
        }
    }

    [Test]
    public void A_MISSING_ICON_FALLS_BACK_INSTEAD_OF_FAILING_TO_START()
    {
        // ⛔ A cosmetic file must never be the reason the app does not open.
        Icon icon = AppIcon.Load(_dir);

        icon.Should().BeSameAs(SystemIcons.Application);
    }

    [Test]
    public void A_CORRUPT_icon_file_also_falls_back()
    {
        // new Icon(path) throws on bytes that are not an icon. That is the catch block's whole job.
        File.WriteAllText(Path.Combine(_dir, AppIcon.FileName), "this is not an icon");

        Func<Icon> load = () => AppIcon.Load(_dir);

        load.Should().NotThrow();
        load().Should().BeSameAs(SystemIcons.Application);
    }

    [Test]
    public void A_folder_that_does_not_exist_falls_back_rather_than_throwing()
    {
        Func<Icon> load = () => AppIcon.Load(Path.Combine(_dir, "no-such-folder"));

        load.Should().NotThrow();
    }
}
