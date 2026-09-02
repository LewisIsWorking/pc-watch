namespace PcWatch;

/// <summary>
/// "Who launched this?" - the question that decides whether a heavy process may be killed.
/// </summary>
/// <remarks>
/// 2026-08-31. WHY THIS EXISTS. The analyzer flagged qemu-system-x86_64 at 16% and advised closing
/// it. Tracing the parents gave:
///     emulator &lt;- bash &lt;- bash &lt;- bash &lt;- claude.exe (pid 84528) &lt;- pwsh &lt;- WindowsTerminal
/// Another agent session had launched it 90 minutes earlier and adb showed it connected. Acting on
/// that advice would have killed live work. A load figure cannot separate a runaway leftover from
/// something in active use; the ancestry can.
/// </remarks>
public sealed class ProcessAncestry
{
    private Dictionary<int, ProcessTableEntry>? _table;
    private DateTime _stamp = DateTime.MinValue;

    /// <summary>Rebuilding the table costs a full process enumeration, so it is cached.</summary>
    public TimeSpan CacheFor { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Shells and launchers that say nothing about who wanted the work done.
    /// </summary>
    private static readonly HashSet<string> PassThrough = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash", "sh", "zsh", "cmd", "conhost", "wscript", "cscript",
        "pwsh", "powershell", "winpty-agent", "git", "env",
    };

    /// <summary>
    /// Session-level things that represent a person or an agent deciding to do work.
    /// </summary>
    /// <remarks>
    /// ⚠️ Skipping shells is not enough. For the emulator the nearest non-shell ancestor is
    /// `emulator` itself, which answers "what launched qemu" (obvious) rather than "who wanted it"
    /// (actionable). So a named owner ANYWHERE in the chain wins over the nearest non-shell.
    /// </remarks>
    private static readonly HashSet<string> Owners = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude", "code", "devenv", "rider64", "rider", "webstorm64", "idea64", "pycharm64",
        "node", "npm", "dotnet", "msbuild", "docker", "explorer", "services", "taskeng",
        "windowsterminal", "wt",
    };

    private Dictionary<int, ProcessTableEntry> Table
    {
        get
        {
            if (_table is null || DateTime.Now - _stamp > CacheFor)
            {
                _table = ProcessTable.Snapshot();
                _stamp = DateTime.Now;
            }
            return _table;
        }
    }

    /// <summary>Discard the cache so the next lookup re-reads the machine.</summary>
    public void Invalidate() => _table = null;

    /// <summary>
    /// Ancestors of a process, nearest first. Empty when the pid or its parents are unknown.
    /// </summary>
    /// <remarks>
    /// ⚠️ Pids are reused. A "parent" whose start time is LATER than its child's cannot be the real
    /// parent, so the walk stops there rather than inventing a lineage and confidently naming an
    /// unrelated owner. The check applies only when both times are known - see ProcessTable.
    /// </remarks>
    public IReadOnlyList<ProcessTableEntry> ChainFor(int processId, int maxDepth = 8)
    {
        var table = Table;
        var chain = new List<ProcessTableEntry>();
        if (!table.TryGetValue(processId, out ProcessTableEntry? child)) return chain;

        var seen = new HashSet<int> { processId };
        DateTime? childStarted = child.Started;
        int currentId = child.ParentId;

        for (int depth = 0; depth < maxDepth; depth++)
        {
            if (currentId <= 0 || !table.TryGetValue(currentId, out ProcessTableEntry? parent)) break;
            if (!seen.Add(currentId)) break;                                   // cycle guard
            if (childStarted is { } c && parent.Started is { } p && p > c) break;  // recycled pid

            chain.Add(parent);
            childStarted = parent.Started;
            currentId = parent.ParentId;
        }

        return chain;
    }

    /// <summary>
    /// One line naming whoever is meaningfully responsible, or null if nothing can be resolved.
    /// </summary>
    public string? OwnerLabelFor(int processId)
    {
        IReadOnlyList<ProcessTableEntry> chain = ChainFor(processId);
        if (chain.Count == 0) return null;

        ProcessTableEntry owner =
            chain.FirstOrDefault(e => Owners.Contains(e.Name))
            ?? chain.FirstOrDefault(e => !PassThrough.Contains(e.Name))
            ?? chain[^1];

        string trail = string.Join(" <- ", chain.Take(4).Select(e => e.Name));
        return $"launched by {owner.Name} (pid {owner.Id})   [{trail}]";
    }
}
