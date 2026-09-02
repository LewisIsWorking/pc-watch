namespace PcWatch;

/// <summary>One folder and everything beneath it.</summary>
public sealed record FolderSize(string Path, long Bytes)
{
    public double Gb => Bytes / 1024d / 1024 / 1024;
}

/// <summary>A completed scan: what was found, and when.</summary>
/// <remarks>
/// ⚠️ <see cref="TakenAt"/> is not decoration. A full scan takes minutes, so results are cached and
/// reused across launches - which means a size shown here can be days old. A stale measurement
/// presented without its age is indistinguishable from a fresh one, and the whole point of this app
/// is that every number states what it is.
/// </remarks>
public sealed record DiskScan(IReadOnlyList<FolderSize> Folders, DateTime TakenAt, bool Complete);

/// <summary>
/// Finds the folders using the most space, without blocking the UI or lying about totals.
/// </summary>
/// <remarks>
/// 2026-09-02. Written after a 1,861 GB drive reached 97% full and nothing on the machine could say
/// where it had gone.
///
/// ⛔ REPARSE POINTS ARE SKIPPED, and that is the whole correctness story. Windows is full of
///    junctions: C:\Users\All Users points at C:\ProgramData, and every user profile contains
///    self-referential compatibility links like "Application Data". Following them double-counts
///    silently and can recurse until the stack dies. The result is not an error - it is a confident,
///    plausible, badly wrong number, which is the worst kind.
///
/// ⚠️ Access-denied is expected and ignored, not reported as a failure: a normal user cannot read
///    every folder on C:, so a scan that refused to finish without full rights would never finish.
///    Totals are therefore a FLOOR, not an exact figure, and the UI says so.
/// </remarks>
public sealed class DiskScanner
{
    private readonly object _gate = new();
    private DiskScan? _last;
    private CancellationTokenSource? _running;

    /// <summary>Folders shallower than this are reported; deeper ones roll up into their parent.</summary>
    public int Depth { get; init; } = 2;

    /// <summary>Ignore anything smaller than this, so the list is not padded with noise.</summary>
    public long MinimumBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Most recent completed scan, or null if none has finished yet.</summary>
    public DiskScan? Last
    {
        get { lock (_gate) { return _last; } }
    }

    public bool IsScanning => _running is { IsCancellationRequested: false };

    /// <summary>Raised on a background thread when a scan finishes.</summary>
    public event Action<DiskScan>? Completed;

    /// <summary>
    /// Start a scan if one is not already running. Returns immediately.
    /// </summary>
    public void Start(string root = "C:\\")
    {
        if (IsScanning) return;

        var cancellation = new CancellationTokenSource();
        _running = cancellation;

        _ = Task.Run(() =>
        {
            try
            {
                var found = new List<FolderSize>();
                Walk(root, 0, found, cancellation.Token);

                var scan = new DiskScan(
                    [.. found.Where(f => f.Bytes >= MinimumBytes).OrderByDescending(f => f.Bytes).Take(25)],
                    DateTime.Now,
                    !cancellation.IsCancellationRequested);

                lock (_gate) { _last = scan; }
                Completed?.Invoke(scan);
            }
            catch
            {
                // A scan that fails must leave the previous result intact rather than blanking it.
            }
            finally
            {
                _running = null;
                cancellation.Dispose();
            }
        }, cancellation.Token);
    }

    public void Cancel() => _running?.Cancel();

    /// <summary>
    /// Total bytes under <paramref name="path"/>, recording folders down to <see cref="Depth"/>.
    /// </summary>
    private long Walk(string path, int depth, List<FolderSize> found, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested) return 0;

        long total = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(path))
            {
                try { total += new FileInfo(file).Length; } catch { /* vanished or denied */ }
            }

            foreach (string directory in Directory.EnumerateDirectories(path))
            {
                if (cancellation.IsCancellationRequested) break;

                // ⛔ The junction guard. Without it, C:\Users\All Users re-walks all of ProgramData
                //    and the profile compatibility links recurse into themselves.
                try
                {
                    if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) continue;
                }
                catch { continue; }

                total += Walk(directory, depth + 1, found, cancellation);
            }
        }
        catch
        {
            // Denied, offline, or removed mid-walk. Expected on a live system; the total becomes a
            // floor rather than an exact figure, which the UI states.
        }

        if (depth is > 0 and <= 2) found.Add(new FolderSize(path, total));
        return total;
    }
}
