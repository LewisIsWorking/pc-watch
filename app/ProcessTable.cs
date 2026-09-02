using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PcWatch;

/// <summary>One row of the process table: identity, parent and start time.</summary>
public sealed record ProcessTableEntry(int Id, int ParentId, string Name, DateTime? Started);

/// <summary>
/// A snapshot of every visible process with its PARENT, which Process.GetProcesses cannot give.
/// </summary>
/// <remarks>
/// 2026-08-31. Parent ids come from a Toolhelp snapshot rather than NtQueryInformationProcess
/// because Toolhelp needs no handle to any process, so it does not silently omit the elevated ones
/// it cannot open. A gap there would be invisible and would break exactly the lookups that matter.
/// Start times still need a managed Process, and are best effort: see the note in Snapshot().
/// </remarks>
public static partial class ProcessTable
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    private const uint Th32CsSnapProcess = 0x00000002;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    /// <summary>
    /// Every process, keyed by id.
    /// </summary>
    /// <remarks>
    /// ⚠️ Started is null wherever the OS refuses it (System, Idle, and anything running as another
    /// user without rights). That is not an error, but it does disable the recycled-pid check for
    /// that link - see <see cref="ProcessAncestry"/>. Documented rather than papered over, because a
    /// guard that quietly stops applying is worse than one that is known to be partial.
    /// </remarks>
    public static Dictionary<int, ProcessTableEntry> Snapshot()
    {
        var parents = ReadParentIds();
        var table = new Dictionary<int, ProcessTableEntry>(parents.Count);

        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                int id;
                string name;
                try
                {
                    id = process.Id;
                    name = process.ProcessName;
                }
                catch { continue; }

                DateTime? started = null;
                try { started = process.StartTime; } catch { /* refused: see remarks */ }

                int parentId = parents.TryGetValue(id, out int p) ? p : 0;
                table[id] = new ProcessTableEntry(id, parentId, name, started);
            }
        }

        // Anything Toolhelp saw but the managed enumeration missed still belongs in the map, or a
        // chain can dead-end on a process that demonstrably exists.
        foreach (var pair in parents)
        {
            if (table.ContainsKey(pair.Key)) continue;
            table[pair.Key] = new ProcessTableEntry(pair.Key, pair.Value, "?", null);
        }

        return table;
    }

    private static unsafe Dictionary<int, int> ReadParentIds()
    {
        var parents = new Dictionary<int, int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(Th32CsSnapProcess, 0);
        if (snapshot == InvalidHandle || snapshot == IntPtr.Zero) return parents;

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)sizeof(ProcessEntry32) };
            if (!Process32FirstW(snapshot, ref entry)) return parents;

            do
            {
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
            }
            while (Process32NextW(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return parents;
    }
}
