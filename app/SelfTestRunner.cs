using System.Runtime.InteropServices;

namespace PcWatch;

/// <summary>
/// Console plumbing and pass/fail bookkeeping for <c>--self-test</c>.
/// </summary>
/// <remarks>
/// 2026-08-31. Split out of SelfTest at the 200-line limit. Keeping the reporting separate from the
/// cases means a new case is a single line in one place and cannot accidentally change how failures
/// are counted - which is the one thing in a test harness that must not be easy to get wrong.
/// </remarks>
public sealed partial class SelfTestRunner
{
    private int _failures;

    public void Section(string title) => Console.WriteLine($"\n{title}");

    public void Note(string message) => Console.WriteLine($"        {message}");

    /// <summary>
    /// Run one check. A throw is a failure, and the message is printed rather than swallowed.
    /// </summary>
    /// <remarks>
    /// ⚠️ Every case runs even after an earlier one fails. Stopping at the first failure hides how
    /// many things broke, and "one test failed" and "eleven tests failed" call for different actions.
    /// </remarks>
    public void Check(string what, Action body)
    {
        try
        {
            body();
            Console.WriteLine($"  [PASS] {what}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"  [FAIL] {what} -> {ex.Message}");
        }
    }

    /// <summary>Print the summary and return the process exit code.</summary>
    public int Summarise()
    {
        Console.WriteLine();
        if (_failures > 0)
        {
            Console.WriteLine($"{_failures} CHECK(S) FAILED");
            return 1;
        }
        Console.WriteLine("ALL CHECKS PASSED");
        return 0;
    }

    // A WinExe has no console attached, so --self-test output would go nowhere at all. Attach to
    // the launching console when there is one, and allocate a fresh one when there is not.
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetConsoleWindow();

    public static void EnsureConsole()
    {
        if (GetConsoleWindow() != IntPtr.Zero) return;
        if (!AttachConsole(-1)) AllocConsole();
    }
}
