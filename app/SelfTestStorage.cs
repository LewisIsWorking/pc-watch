using System.Text;

namespace PcWatch;

/// <summary>
/// Proves the disk scanner counts a real tree correctly and does not follow junctions.
/// </summary>
/// <remarks>
/// 2026-09-02. Built against a REAL temporary directory tree of known size rather than a mock,
/// because the things that break a disk scanner are all filesystem behaviours - reparse points,
/// denied folders, files vanishing mid-walk - and a mock has none of them.
/// </remarks>
public static class SelfTestStorage
{
    public static void Run(SelfTestRunner r)
    {
        r.Section("DISK SCAN: SIZE IS ONLY RIGHT IF JUNCTIONS ARE SKIPPED");

        string root = Path.Combine(Path.GetTempPath(), "PcWatchScanTest_" + Environment.ProcessId);
        try
        {
            // A known tree: 6 MB in one branch, 2 MB in another.
            Directory.CreateDirectory(Path.Combine(root, "big"));
            Directory.CreateDirectory(Path.Combine(root, "small", "nested"));
            WriteBytes(Path.Combine(root, "big", "a.bin"), 4 * 1024 * 1024);
            WriteBytes(Path.Combine(root, "big", "b.bin"), 2 * 1024 * 1024);
            WriteBytes(Path.Combine(root, "small", "nested", "c.bin"), 2 * 1024 * 1024);

            var scanner = new DiskScanner { MinimumBytes = 1, Depth = 2 };

            r.Check("a known tree is measured correctly", () =>
            {
                DiskScan scan = ScanAndWait(scanner, root);
                FolderSize big = Find(scan, "big");
                if (Math.Abs(big.Bytes - 6L * 1024 * 1024) > 65536)
                {
                    throw new Exception($"'big' measured {big.Bytes} bytes, expected 6 MB");
                }
                r.Note($"big = {big.Bytes / 1024 / 1024} MB, small = {Find(scan, "small").Bytes / 1024 / 1024} MB");
            });

            // ⛔ THE JUNCTION TRAP. C:\Users\All Users points at C:\ProgramData and profile
            //    compatibility links point at themselves. Following one double-counts silently and
            //    can recurse forever - a confident, plausible, badly wrong total.
            string junction = Path.Combine(root, "small", "link-to-big");
            bool madeJunction = TryCreateJunction(junction, Path.Combine(root, "big"));

            if (!madeJunction)
            {
                r.Note("could not create a junction here - skipping the reparse-point case");
            }
            else
            {
                r.Check("a junction is NOT followed, so nothing is double-counted", () =>
                {
                    DiskScan scan = ScanAndWait(scanner, root);
                    FolderSize small = Find(scan, "small");
                    if (small.Bytes > 3L * 1024 * 1024)
                    {
                        throw new Exception(
                            $"'small' measured {small.Bytes / 1024 / 1024} MB - it followed the junction "
                            + "into 'big' and counted those 6 MB twice");
                    }
                    r.Note($"small = {small.Bytes / 1024 / 1024} MB with a junction present (correct: 2 MB)");
                });
            }

            r.Check("a missing root yields no results and does not throw", () =>
            {
                var empty = new DiskScanner { MinimumBytes = 1 };
                DiskScan scan = ScanAndWait(empty, Path.Combine(root, "does-not-exist"));
                if (scan.Folders.Count != 0) throw new Exception("invented folders for a missing path");
            });

            r.Check("the report states WHEN the scan was taken", () =>
            {
                var sb = new StringBuilder();
                ReportRenderer.RenderStorage(sb, scanner);
                string text = sb.ToString();
                if (!text.Contains("measured")) throw new Exception($"no age stated: {text}");
                if (!text.Contains("floor")) throw new Exception("did not disclose that totals are a floor");
            });

            r.Check("an unscanned drive says so rather than showing nothing", () =>
            {
                var sb = new StringBuilder();
                ReportRenderer.RenderStorage(sb, new DiskScanner());
                if (!sb.ToString().Contains("not scanned yet")) throw new Exception("silently blank");
            });
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    private static DiskScan ScanAndWait(DiskScanner scanner, string root)
    {
        scanner.Start(root);
        for (int i = 0; i < 200 && scanner.IsScanning; i++) Thread.Sleep(25);
        return scanner.Last ?? new DiskScan([], DateTime.Now, true);
    }

    private static FolderSize Find(DiskScan scan, string leaf) =>
        scan.Folders.FirstOrDefault(f => Path.GetFileName(f.Path) == leaf)
        ?? throw new Exception($"'{leaf}' missing from the scan");

    private static void WriteBytes(string path, int count) =>
        File.WriteAllBytes(path, new byte[count]);

    /// <summary>Create a directory junction, or return false where the OS will not allow it.</summary>
    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            // Creating links can need Developer Mode or elevation. Skipping is honest; pretending
            // the case passed would be worse than not running it.
            return false;
        }
    }
}
