using System.Text.Json;
using System.Windows.Input;

namespace Cofre.App.Services;

internal sealed class AppSettings
{
    /// <summary>Idle minutes before the vault locks itself. 0 = never.</summary>
    public int AutoLockMinutes { get; set; } = 5;

    /// <summary>Seconds before a copied password is wiped from the clipboard. 0 = never.</summary>
    public int ClipboardClearSeconds { get; set; } = 20;

    /// <summary>Press Enter after typing the password into the target window.</summary>
    public bool AutoSubmit { get; set; } = true;

    public uint HotkeyModifiers { get; set; } = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT;

    public uint HotkeyVirtualKey { get; set; } = 0x50; // P

    /// <summary>System | Dark | Light</summary>
    public string Theme { get; set; } = "System";

    public string HotkeyText => FormatHotkey(HotkeyModifiers, HotkeyVirtualKey);

    public static string FormatHotkey(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey((int)virtualKey).ToString());
        return string.Join(" + ", parts);
    }
}

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Current = new AppSettings(); // a broken settings file must never keep the app from starting
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(Current, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are a convenience; failing to persist them is not worth interrupting the user.
        }
    }
}
