using System.Windows;
using System.Windows.Threading;
using Cofre.Core.Vault;
using Microsoft.Win32;

namespace Cofre.App.Services;

/// <summary>Locks the vault after inactivity, when the Windows session locks and when the PC goes to sleep.</summary>
internal sealed class IdleLockService : IDisposable
{
    private readonly VaultService _vault;
    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    public IdleLockService(VaultService vault, SettingsStore settings)
    {
        _vault = vault;
        _settings = settings;

        _timer.Tick += (_, _) => CheckIdle();
        _timer.Start();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    /// <summary>Call on any user interaction with the popup.</summary>
    public void Touch() => _lastActivityUtc = DateTime.UtcNow;

    private void CheckIdle()
    {
        var minutes = _settings.Current.AutoLockMinutes;
        if (minutes <= 0 || !_vault.IsUnlocked) return;

        if (DateTime.UtcNow - _lastActivityUtc >= TimeSpan.FromMinutes(minutes))
            _vault.Lock();
    }

    // SystemEvents are raised on a dedicated thread, so hop back to the UI thread before touching the vault.
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            LockOnUiThread();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) LockOnUiThread();
    }

    private void LockOnUiThread() => Application.Current?.Dispatcher.BeginInvoke(() => _vault.Lock());

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
