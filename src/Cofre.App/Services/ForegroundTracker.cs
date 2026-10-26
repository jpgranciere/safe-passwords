namespace Cofre.App.Services;

/// <summary>
/// Remembers the last "real" window the user was in. Clicking the tray icon makes the taskbar the foreground
/// window, so without this the popup would not know where to type or which entries to suggest.
/// </summary>
internal sealed class ForegroundTracker : IDisposable
{
    private readonly NativeMethods.WinEventProc _callback; // kept in a field so the GC can't collect it
    private readonly IntPtr _hook;

    public ForegroundTracker()
    {
        _callback = OnForegroundChanged;
        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

        Remember(NativeMethods.GetForegroundWindow());
    }

    public IntPtr LastExternalWindow { get; private set; }

    /// <summary>The remembered window if it still exists, otherwise zero.</summary>
    public IntPtr CurrentTarget => LastExternalWindow != IntPtr.Zero && NativeMethods.IsWindow(LastExternalWindow)
        ? LastExternalWindow
        : IntPtr.Zero;

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) =>
        Remember(hwnd);

    private void Remember(IntPtr hwnd)
    {
        if (!NativeMethods.IsShellOrOwn(hwnd)) LastExternalWindow = hwnd;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_hook);
    }
}
