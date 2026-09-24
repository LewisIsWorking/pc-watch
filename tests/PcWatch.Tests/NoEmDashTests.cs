using AwesomeAssertions;
using NUnit.Framework;
namespace PcWatch.Tests;

/// <summary>
/// Zero em dashes anywhere in the repository: code, comments, docs, scripts.
/// </summary>
/// <remarks>
/// Lewis, 2026-09-21: "There should be NO em dashes across any repo!" Cleared here on 2026-09-24 (36, all in
/// README.md and SECURITY.md) and guarded so the count stays at zero. Escapes count too, because they print
/// one: the patterns below are built from pieces so this file does not trip its own check.
/// </remarks>
[TestFixture]
public sealed class NoEmDashTests
{
    private static readonly string[] Forbidden =
    [
        ((char)0x2014).ToString(),
        "\\" + "u2014",
        "&" + "mdash;",
        "\\N{" + "EM DASH}",
    ];

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".md", ".ps1", ".psm1", ".csproj", ".props", ".targets", ".json", ".xml", ".yml", ".yaml",
        ".txt", ".config", ".editorconfig", ".gitignore", ".gitattributes", ".sln", ".resx", ".manifest",
    };

    // Build output and tool output are not the repo's text.
    private static readonly string[] SkippedFolders = ["bin", "obj", ".git", ".vs", "coverage-output", "TestResults"];

    [Test]
    public void No_file_in_the_repository_contains_an_em_dash()
    {
        var root = RepoRoot();
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => SkippedFolders.Contains(part, StringComparer.OrdinalIgnoreCase)))
                continue;
            if (!TextExtensions.Contains(Path.GetExtension(path)) && !TextExtensions.Contains(Path.GetFileName(path)))
                continue;

            scanned++;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (Forbidden.Any(f => lines[i].Contains(f, StringComparison.Ordinal)))
                    offenders.Add($"{relative}:{i + 1}");
            }
        }

        // A scan that found no files would pass vacuously; README.md alone proves it looked.
        scanned.Should().BeGreaterThan(20, "the scan must actually have read the repository");
        offenders.Should().BeEmpty("write \" - \", a comma, a colon or a full stop instead of an em dash");
    }

    [Test]
    public void The_scan_would_see_an_em_dash_if_one_were_there()
    {
        // Control: proves the check can fail, so the zero above means zero rather than "never matched".
        var line = "a " + (char)0x2014 + " b";
        Forbidden.Any(f => line.Contains(f, StringComparison.Ordinal)).Should().BeTrue();
    }

    /// <summary>Walks up from the test binaries to the folder holding .git (a folder, or a file in a worktree).</summary>
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return dir.FullName;
        }
        throw new InvalidOperationException("No .git found above " + AppContext.BaseDirectory);
    }
}
