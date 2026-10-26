namespace Cofre.App.Services;

/// <summary>
/// Only one tray process per Windows session. Starting the exe again just asks the running one to open its popup,
/// so a desktop shortcut works as a "show my vault" button.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Cofre.SingleInstance";
    private const string ShowEventName = @"Local\Cofre.ShowPopup";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly CancellationTokenSource _cts = new();

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    public event Action? ShowRequested;

    /// <summary>Returns null (after poking the running instance) when another instance already owns the session.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        if (!createdNew)
        {
            showEvent.Set();
            showEvent.Dispose();
            mutex.Dispose();
            return null;
        }

        var instance = new SingleInstance(mutex, showEvent);
        instance.StartListening();
        return instance;
    }

    private void StartListening()
    {
        var thread = new Thread(() =>
        {
            var handles = new[] { _showEvent, _cts.Token.WaitHandle };
            while (WaitHandle.WaitAny(handles) == 0)
                ShowRequested?.Invoke();
        })
        {
            IsBackground = true,
            Name = "Cofre.SingleInstance",
        };
        thread.Start();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _showEvent.Dispose();
        _cts.Dispose();
    }
}
