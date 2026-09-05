using AwesomeAssertions;
using NUnit.Framework;

namespace PcWatch.Tests;

/// <summary>
/// One running copy, and a crash must never lock the user out of the next launch.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-04. This file was at 0% coverage, and it contains the single nastiest bug in the app's
///    history: an AbandonedMutexException means the wait SUCCEEDED and the previous owner died
///    holding the mutex. Treating it as a refusal meant that crashing or killing one instance made
///    the NEXT launch die too.
///
///    In the PowerShell original it died about ten seconds in, AFTER startup work had finished, so
///    the process appeared in the task list and looked healthy before vanishing. The user's instinct
///    is to launch it again, which the mutex turns into nothing happening at all.
///
/// ⚠️ Every test uses a UNIQUE name. These are NAMED KERNEL OBJECTS, machine-wide for the user, so a
///    shared name would make tests interfere with each other AND with a real running PC Watch.
/// </remarks>
[TestFixture]
public sealed class SingleInstanceTests
{
    private static string UniqueName() => $"PcWatchTest_{Guid.NewGuid():N}";

    /// <summary>
    /// Build a SingleInstance on a DIFFERENT thread, which is what a second launch really is.
    /// </summary>
    /// <remarks>
    /// ⛔ 2026-09-04. A MUTEX IS THREAD-AFFINE AND REENTRANT. WaitOne(0) SUCCEEDS when the calling
    ///    thread already owns it, so two instances constructed on the same test thread BOTH report
    ///    "I am first" and the test fails against perfectly correct code.
    ///
    ///    The second instance is a separate PROCESS in reality, so it is necessarily a different
    ///    thread. Constructing it on one here is not a trick to make a test pass: it is the only
    ///    arrangement that models what actually happens when the pinned icon is clicked twice.
    /// </remarks>
    private static SingleInstance SecondLaunch(string name)
    {
        SingleInstance? built = null;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try { built = new SingleInstance(name); }
            catch (Exception ex) { failure = ex; }
        });
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10)).Should().BeTrue("the second launch must not hang");

        if (failure is not null) throw failure;
        return built!;
    }

    [Test]
    public void The_first_instance_knows_it_is_first()
    {
        using var first = new SingleInstance(UniqueName());

        first.IsFirstInstance.Should().BeTrue();
    }

    [Test]
    public void A_second_instance_of_the_same_name_knows_it_is_not_first()
    {
        string name = UniqueName();
        using var first = new SingleInstance(name);
        using SingleInstance second = SecondLaunch(name);

        first.IsFirstInstance.Should().BeTrue();
        second.IsFirstInstance.Should().BeFalse("this is what stops the pin launching a second copy");
    }

    [Test]
    public void Different_names_do_not_block_each_other()
    {
        using var one = new SingleInstance(UniqueName());
        using var two = new SingleInstance(UniqueName());

        one.IsFirstInstance.Should().BeTrue();
        two.IsFirstInstance.Should().BeTrue();
    }

    [Test]
    public void After_the_first_instance_is_disposed_a_new_one_becomes_first_again()
    {
        string name = UniqueName();
        using (var first = new SingleInstance(name))
        {
            first.IsFirstInstance.Should().BeTrue();
        }

        using var replacement = new SingleInstance(name);
        replacement.IsFirstInstance.Should().BeTrue("a clean exit must release the lock");
    }

    [Test]
    public void An_ABANDONED_mutex_is_treated_as_ACQUIRED_not_as_a_refusal()
    {
        // ⛔ THE REGRESSION. A thread takes ownership and dies without releasing, which is exactly
        //    what a crash or a Task Manager kill looks like to the kernel. The next launch MUST
        //    still start. Before the fix it threw AbandonedMutexException and the app was unusable
        //    until a reboot cleared the handle.
        string name = UniqueName();
        string mutexName = $@"Local\{name}_Mutex_{Environment.UserName}";

        var owned = new ManualResetEventSlim(false);
        var abandoner = new Thread(() =>
        {
            // Ownership of a Mutex belongs to the THREAD that took it. Letting this thread end
            // without releasing is what abandons it.
            var mutex = new Mutex(false, mutexName);
            mutex.WaitOne(0);
            owned.Set();
        });
        abandoner.Start();
        owned.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue("the helper thread must have taken the mutex");
        abandoner.Join(TimeSpan.FromSeconds(5)).Should().BeTrue("and then died still holding it");

        Action launch = () =>
        {
            using var next = new SingleInstance(name);
            next.IsFirstInstance.Should().BeTrue("an abandoned mutex means the wait SUCCEEDED");
        };

        launch.Should().NotThrow<AbandonedMutexException>(
            "a crash must never be able to lock the user out of the app");
    }

    [Test]
    public void Signalling_asks_the_running_instance_to_show_itself()
    {
        string name = UniqueName();
        using var running = new SingleInstance(name);

        var raised = new ManualResetEventSlim(false);
        running.ActivationRequested += () => raised.Set();

        using (SingleInstance launcher = SecondLaunch(name))
        {
            launcher.IsFirstInstance.Should().BeFalse();
            launcher.SignalExistingInstance();
        }

        raised.Wait(TimeSpan.FromSeconds(5))
            .Should().BeTrue("clicking the pin again must SHOW the window, not start a second copy");
    }

    [Test]
    public void A_second_instance_with_no_subscriber_signals_harmlessly()
    {
        // The event is null when nothing has subscribed yet, which is a real window during startup.
        string name = UniqueName();
        using var running = new SingleInstance(name);
        using SingleInstance launcher = SecondLaunch(name);

        Action signal = launcher.SignalExistingInstance;

        signal.Should().NotThrow();
    }

    [Test]
    public void Disposing_a_non_first_instance_does_not_throw()
    {
        // It never owned the mutex, so ReleaseMutex would throw ApplicationException. That is caught
        // deliberately, and this pins it: the second launch disposes on its way out every single time.
        string name = UniqueName();
        using var first = new SingleInstance(name);
        SingleInstance second = SecondLaunch(name);

        Action dispose = second.Dispose;

        dispose.Should().NotThrow();
    }
}
