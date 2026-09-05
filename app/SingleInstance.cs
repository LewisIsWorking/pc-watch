namespace PcWatch;

/// <summary>
/// One running copy, and clicking the pinned icon again SHOWS it rather than starting a second.
/// </summary>
/// <remarks>
/// 2026-08-31. This is what makes a pinned taskbar button behave like an application instead of a
/// launcher. Without it, every click on the pin starts another process, each with its own tray icon
/// and its own sampling cost, and none of them aware of the others.
///
/// ⛔ AbandonedMutexException means the wait SUCCEEDED and the previous owner died holding the mutex.
///    It is NOT a refusal. Letting it propagate meant that killing or crashing one instance made the
///    NEXT launch die too - and in the PowerShell original that happened ten seconds in, after
///    startup work had completed, so the process appeared in the task list first and looked healthy.
///    A crash must never be able to lock the user out of the app.
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateSignal;
    private readonly CancellationTokenSource _cancellation = new();
    private Thread? _listener;

    public bool IsFirstInstance { get; }

    /// <summary>Raised on a background thread when another launch asks this instance to show itself.</summary>
    public event Action? ActivationRequested;

    public SingleInstance(string name)
    {
        _mutex = new Mutex(false, $@"Local\{name}_Mutex_{Environment.UserName}");
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset,
                                              $@"Local\{name}_Activate_{Environment.UserName}");

        bool acquired;
        try
        {
            acquired = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;   // the previous owner died; ownership is now ours
        }

        IsFirstInstance = acquired;
        if (acquired) StartListening();
    }

    /// <summary>Ask the already-running instance to show its window. Called by the second launch.</summary>
    public void SignalExistingInstance() => _activateSignal.Set();

    private void StartListening()
    {
        // ⛔ 2026-09-05: THE TOKEN AND HANDLES ARE CAPTURED HERE, ON THE CALLING THREAD.
        //
        //    They used to be read INSIDE the thread body, which raced with Dispose. Construct an
        //    instance and dispose it before the new thread is scheduled, and the first thing that
        //    thread did was read _cancellation.Token on a DISPOSED source and throw
        //    ObjectDisposedException. An unhandled exception on a background thread TAKES THE WHOLE
        //    PROCESS DOWN, so the app died with a stack trace pointing at a line that merely
        //    prepared a wait.
        //
        //    It hid because the FIRST instance normally lives for the lifetime of the app. But the
        //    second instance is constructed and disposed within milliseconds on EVERY click of the
        //    pinned icon, which is the single most common thing a user does with this app.
        //
        //    Found by SingleInstanceTests on 2026-09-04, which crashed the whole test host.
        CancellationToken token = _cancellation.Token;
        WaitHandle[] handles = [_activateSignal, token.WaitHandle];

        _listener = new Thread(() =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (WaitHandle.WaitAny(handles) == 0) ActivationRequested?.Invoke();
                }
            }
            catch (ObjectDisposedException)
            {
                // Torn down while waiting. Shutting down is not a failure, and throwing here would
                // kill the process rather than the thread.
            }
        })
        {
            IsBackground = true,
            Name = "PcWatch.ActivationListener",
        };
        _listener.Start();
    }

    public void Dispose()
    {
        _cancellation.Cancel();

        // ⚠️ Wait for the listener to leave BEFORE disposing anything it waits on. Cancelling only
        //    asks; without the join, the handles below can be destroyed while the thread is still
        //    inside WaitAny. Bounded so a wedged thread cannot hang an application exit.
        _listener?.Join(TimeSpan.FromSeconds(2));

        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
        }
        _mutex.Dispose();
        _activateSignal.Dispose();
        _cancellation.Dispose();
    }
}
