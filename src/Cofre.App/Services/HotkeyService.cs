using System.Windows.Interop;

namespace Cofre.App.Services;

/// <summary>System-wide hotkey through a message-only window, so no visible window is needed.</summary>
internal sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0xC0FE;

    private readonly HwndSource _source;
    private bool _registered;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("CofreHotkeyWindow")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event Action? Pressed;

    /// <summary>Returns false when another program already owns the combination.</summary>
    public bool Register(uint modifiers, uint virtualKey)
    {
        Unregister();
        _registered = NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
