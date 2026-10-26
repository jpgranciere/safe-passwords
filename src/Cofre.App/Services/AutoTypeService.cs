using System.Runtime.InteropServices;

namespace Cofre.App.Services;

public enum AutoTypeResult { Typed, NoTargetWindow, FocusFailed }

/// <summary>Types credentials into whatever window had focus before the popup, using synthetic Unicode key events.</summary>
internal sealed class AutoTypeService
{
    private static readonly int InputSize = Marshal.SizeOf<NativeMethods.INPUT>();

    public async Task<AutoTypeResult> TypeAsync(IntPtr target, string username, string password, bool submit)
    {
        if (target == IntPtr.Zero || !NativeMethods.IsWindow(target) || NativeMethods.IsShellOrOwn(target))
            return AutoTypeResult.NoTargetWindow;

        NativeMethods.SetForegroundWindow(target);
        if (!await WaitForForegroundAsync(target, timeoutMs: 800))
            return AutoTypeResult.FocusFailed;

        // Give the target a moment to restore its own focus (browsers refocus the page asynchronously),
        // and make sure the hotkey modifiers are physically released or they'd turn the text into shortcuts.
        await Task.Delay(120);
        await WaitForModifiersReleasedAsync(timeoutMs: 1500);

        if (username.Length > 0)
        {
            await TypeTextAsync(username);
            await PressAsync(NativeMethods.VK_TAB);
            await Task.Delay(60);
        }

        await TypeTextAsync(password);
        if (submit) await PressAsync(NativeMethods.VK_RETURN);

        return AutoTypeResult.Typed;
    }

    private static async Task<bool> WaitForForegroundAsync(IntPtr target, int timeoutMs)
    {
        for (var waited = 0; waited < timeoutMs; waited += 25)
        {
            if (NativeMethods.GetForegroundWindow() == target) return true;
            await Task.Delay(25);
        }

        return NativeMethods.GetForegroundWindow() == target;
    }

    private static async Task WaitForModifiersReleasedAsync(int timeoutMs)
    {
        int[] keys = [NativeMethods.VK_CONTROL, NativeMethods.VK_MENU, NativeMethods.VK_SHIFT, NativeMethods.VK_LWIN, NativeMethods.VK_RWIN];
        for (var waited = 0; waited < timeoutMs; waited += 25)
        {
            if (keys.All(k => (NativeMethods.GetAsyncKeyState(k) & 0x8000) == 0)) return;
            await Task.Delay(25);
        }
    }

    private static async Task TypeTextAsync(string text)
    {
        foreach (var ch in text)
        {
            Send(Unicode(ch, keyUp: false), Unicode(ch, keyUp: true));
            await Task.Delay(4);
        }
    }

    private static async Task PressAsync(ushort virtualKey)
    {
        Send(Virtual(virtualKey, keyUp: false), Virtual(virtualKey, keyUp: true));
        await Task.Delay(20);
    }

    private static void Send(params NativeMethods.INPUT[] inputs) =>
        NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);

    private static NativeMethods.INPUT Unicode(char ch, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = NativeMethods.KEYEVENTF_UNICODE | (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0),
            },
        },
    };

    private static NativeMethods.INPUT Virtual(ushort vk, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = vk, dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0 },
        },
    };
}
