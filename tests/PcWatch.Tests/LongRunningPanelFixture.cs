using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// Shared setup for the long-running panel's fixtures.
/// </summary>
/// <remarks>
/// ⚠️ THE WINDOW HANDLE IS LOAD-BEARING. Setting ListViewItem.Selected on a ListView with no created
///    handle silently does nothing: the assignment is accepted, SelectedItems stays empty, and
///    SelectedIndexChanged never fires. Eight tests once failed against perfectly correct code for
///    exactly this reason. Realising the control tree - without ever showing it - is what makes
///    selection behave the way it does for a real user.
/// </remarks>
public abstract class LongRunningPanelFixture
{
    private Form _host = null!;

    protected LongRunningPanel Panel { get; private set; } = null!;

    protected ListView List => Panel.Controls.OfType<ListView>().Single();

    protected Button KillButton => Panel.Controls.OfType<Button>().Single();

    [SetUp]
    public void BuildARealisedPanel()
    {
        _host = new Form { ShowInTaskbar = false, Size = new Size(700, 500) };
        Panel = new LongRunningPanel(new ProcessAncestry()) { Dock = DockStyle.Fill };
        _host.Controls.Add(Panel);
        _host.CreateControl();
        Panel.CreateControl();
        _ = List.Handle;
    }

    [TearDown]
    public void DisposeThePanel()
    {
        Panel.Dispose();
        _host.Dispose();
    }

    /// <summary>A plausible long-lived process. Defaults to something safe to end.</summary>
    protected static ProcessLoad Process(
        string name = "dotnet", int id = 4321, double memoryMb = 512, double ageHours = 30) =>
        new(name, id, 1.5, (long)(memoryMb * 1024 * 1024),
            DateTime.Now.AddHours(-ageHours), ageHours);
}
