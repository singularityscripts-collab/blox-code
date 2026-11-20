namespace BloxCode.Core;

/// <summary>
/// One Roblox install or launch at a time, across every Blox Code process. Two at once would both
/// find the version missing and the second would delete the folder the first is still unpacking.
/// A named mutex rather than a semaphore: when a launch crashes or is killed, Windows hands the
/// mutex on (abandoned) instead of leaving it taken forever, so waiting has no time limit, only
/// the Cancel button. A mutex belongs to the thread that took it, and a launch awaits across
/// threads, so a thread of its own takes it and gives it back.
/// </summary>
public sealed class LaunchGate : IDisposable
{
    private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _leave = new();

    /// <summary>True once this launch holds the gate; false if <c>ct</c> was cancelled while waiting.</summary>
    public Task<bool> Entered => _entered.Task;

    public LaunchGate(string name, CancellationToken ct)
    {
        new Thread(() =>
        {
            bool owned = false;
            try
            {
                using var mutex = new Mutex(false, name);
                try { owned = WaitHandle.WaitAny(new[] { mutex, ct.WaitHandle }) == 0; }
                catch (AbandonedMutexException ex) when (ex.MutexIndex == 0)
                {
                    owned = true;
                    Logger.Info("LaunchGate", "The previous launch ended without finishing; carrying on");
                }
                _entered.TrySetResult(owned);
                if (owned)
                {
                    _leave.Wait();
                    mutex.ReleaseMutex();
                }
            }
            catch (Exception ex)
            {
                // Never strand the launch: without the gate it still runs, as it did before.
                Logger.Warn("LaunchGate", ex.Message);
                _entered.TrySetResult(!ct.IsCancellationRequested);
            }
        }) { IsBackground = true, Name = "LaunchGate" }.Start();
    }

    /// <summary>Lets the next launch in; safe to call more than once.</summary>
    public void Dispose() => _leave.Set();
}
