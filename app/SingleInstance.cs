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
        var thread = new Thread(() =>
        {
            WaitHandle[] handles = [_activateSignal, _cancellation.Token.WaitHandle];
            while (!_cancellation.IsCancellationRequested)
            {
                if (WaitHandle.WaitAny(handles) == 0) ActivationRequested?.Invoke();
            }
        })
        {
            IsBackground = true,
            Name = "PcWatch.ActivationListener",
        };
        thread.Start();
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
        }
        _mutex.Dispose();
        _activateSignal.Dispose();
        _cancellation.Dispose();
    }
}
