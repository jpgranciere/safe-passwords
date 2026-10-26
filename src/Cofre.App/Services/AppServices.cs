using Cofre.Core.Vault;

namespace Cofre.App.Services;

/// <summary>The handful of long-lived services, created once at startup and shared by the views.</summary>
internal sealed class AppServices
{
    public required VaultService Vault { get; init; }
    public required SettingsStore Settings { get; init; }
    public required ClipboardService Clipboard { get; init; }
    public required AutoTypeService AutoType { get; init; }
    public required StartupService Startup { get; init; }
    public required IdleLockService IdleLock { get; init; }
    public required HotkeyService Hotkeys { get; init; }
    public required ForegroundTracker Foreground { get; init; }
    public required TrayService Tray { get; init; }
}
