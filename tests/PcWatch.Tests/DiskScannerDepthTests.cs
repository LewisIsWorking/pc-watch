using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// That the configured scan depth is the depth actually used.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-10, THE BUG THIS PINS. Walk tested `depth is > 0 and <= 2`, a hardcoded literal, while
///    Depth was a public init property documented as the cutoff and Walk's own summary promised
///    "recording folders down to Depth". So `new DiskScanner { Depth = 4 }` compiled, read as
///    configured, and silently scanned to 2.
///
/// ⭐ WHAT MADE IT INVISIBLE: the literal happened to equal the default. Every existing caller got
///   the right answer for the wrong reason, so no test, no user and no reading of the output could
///   have revealed it. Only setting Depth to something OTHER than 2 shows the difference, which is
///   exactly what these tests do.
///
/// ⚠️ Scans a temp tree this fixture creates and deletes. MinimumBytes is set to 0 because the real
///    default is 2 GB and no test should write that.
/// </remarks>
[TestFixture]
public sealed class DiskScannerDepthTests
{
    private string _root = string.Empty;

    [SetUp]
    public void BuildAFourDeepTree()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pcwatch-depth-{Guid.NewGuid():N}");
        string deepest = Path.Combine(_root, "a", "b", "c");
        Directory.CreateDirectory(deepest);
        File.WriteAllText(Path.Combine(deepest, "payload.txt"), new string('x', 4096));
    }

    [TearDown]
    public void RemoveTheTree()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Run a scan to completion and return the folders it recorded.</summary>
    private IReadOnlyList<FolderSize> Scan(int depth)
    {
        var scanner = new DiskScanner { Depth = depth, MinimumBytes = 0 };
        using var done = new ManualResetEventSlim(false);
        DiskScan? result = null;

        scanner.Completed += s => { result = s; done.Set(); };
        scanner.Start(_root);

        done.Wait(TimeSpan.FromSeconds(20)).Should().BeTrue("the scan must finish");
        result.Should().NotBeNull();
        return result!.Folders;
    }

    /// <summary>How deep below the temp root a recorded folder sits.</summary>
    private int DepthOf(FolderSize folder) =>
        folder.Path.TrimEnd(Path.DirectorySeparatorChar)[_root.TrimEnd(Path.DirectorySeparatorChar).Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length;

    [Test]
    public void A_DEPTH_OF_ONE_RECORDS_ONLY_THE_TOP_FOLDER()
    {
        IReadOnlyList<FolderSize> folders = Scan(depth: 1);

        folders.Should().NotBeEmpty("the tree has content");
        folders.Select(DepthOf).Should().AllSatisfy(d => d.Should().Be(1));
    }

    [Test]
    public void A_DEPTH_OF_THREE_REACHES_THREE_LEVELS_DOWN()
    {
        // ⛔ THE ACTUAL REGRESSION. Before the fix this returned depth 1 and 2 only, because the
        //    hardcoded literal stopped at 2 no matter what Depth said.
        IReadOnlyList<FolderSize> folders = Scan(depth: 3);

        folders.Select(DepthOf).Should().Contain(3, "Depth = 3 must actually reach level three");
        folders.Select(DepthOf).Max().Should().Be(3, "and must not go past it either");
    }

    [Test]
    public void CHANGING_DEPTH_CHANGES_THE_RESULT()
    {
        // The single assertion that could not have passed before the fix, whatever the default was.
        int shallow = Scan(depth: 1).Count;
        int deep = Scan(depth: 3).Count;

        deep.Should().BeGreaterThan(shallow,
            "if Depth were ignored these two scans would be identical");
    }

    [Test]
    public void The_default_depth_is_still_two()
    {
        // ⚠️ The fix must not change behaviour for anyone who never set Depth. The old literal was
        //    2, so 2 is what the default must remain.
        new DiskScanner().Depth.Should().Be(2);

        var scanner = new DiskScanner { MinimumBytes = 0 };
        using var done = new ManualResetEventSlim(false);
        DiskScan? result = null;
        scanner.Completed += s => { result = s; done.Set(); };
        scanner.Start(_root);
        done.Wait(TimeSpan.FromSeconds(20)).Should().BeTrue();

        result!.Folders.Select(DepthOf).Max().Should().Be(2);
    }

    [Test]
    public void The_root_itself_is_never_listed_as_a_folder()
    {
        // Depth 0 is the scan root. Listing it would put "C:\ is using 1.8 TB" at the top of a list
        // whose entire purpose is to say WHICH folder is using the space.
        Scan(depth: 3).Select(DepthOf).Should().NotContain(0);
    }
}
