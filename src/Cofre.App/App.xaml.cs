using System.Windows;
using System.Windows.Threading;
using Cofre.App.Services;
using Cofre.App.Views;
using Cofre.Core.Vault;

namespace Cofre.App;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private PopupWindow? _popup;
    private Views.MainWindow? _main;
    private BrowserBridge? _browser;

    internal static AppServices Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var showAtStart = false;
        string? renderDir = null;
#if DEBUG
        string renderPassword = "";
#endif
        for (var i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--data-dir" && i + 1 < e.Args.Length) AppPaths.Override(e.Args[++i]);
            else if (e.Args[i] == "--show") showAtStart = true;
#if DEBUG
            else if (e.Args[i] == "--render" && i + 1 < e.Args.Length) renderDir = e.Args[++i];
            else if (e.Args[i] == "--password" && i + 1 < e.Args.Length) renderPassword = e.Args[++i];
#endif
        }

        if (renderDir is null)
        {
            _singleInstance = SingleInstance.TryAcquire();
            if (_singleInstance is null)
            {
                Shutdown();
                return;
            }
        }

        DispatcherUnhandledException += OnUnhandledException;

        var settings = new SettingsStore();
        settings.Load();
        ThemeService.Apply(settings.Current.Theme);

        var vault = new VaultService(AppPaths.VaultFile);
        var tray = new TrayService();
        var hotkeys = new HotkeyService();

        Services = new AppServices
        {
            Vault = vault,
            Settings = settings,
            Clipboard = new ClipboardService(),
            AutoType = new AutoTypeService(),
            Startup = new StartupService(),
            IdleLock = new IdleLockService(vault, settings),
            Hotkeys = hotkeys,
            Foreground = new ForegroundTracker(),
            Tray = tray,
        };

        _popup = new PopupWindow();

#if DEBUG
        if (renderDir is not null)
        {
            try { Debug.ViewRenderer.RenderAll(_popup, renderDir, renderPassword); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); Shutdown(1); return; }
            Shutdown();
            return;
        }
#endif

        ThemeService.Apply(settings.Current.Theme, _popup);

        // Unlocked is raised from the worker thread that ran Argon2, Locked from wherever Lock() was called.
        vault.Unlocked += () => Dispatcher.BeginInvoke(() =>
        {
            tray.SetUnlocked(true);
            Services.IdleLock.Touch();
        });
        vault.Locked += () => Dispatcher.BeginInvoke(() =>
        {
            tray.SetUnlocked(false);
            Services.Clipboard.ClearIfOwned();
            _popup.RouteToStart(); // also drops the views that hold decrypted entries
        });

        tray.ToggleRequested += TogglePopup;
        tray.LockRequested += () => vault.Lock();
        tray.ExitRequested += ExitApp;
        _singleInstance!.ShowRequested += () => Dispatcher.BeginInvoke(OpenMainWindow);

        hotkeys.Pressed += TogglePopup;
        if (!hotkeys.Register(settings.Current.HotkeyModifiers, settings.Current.HotkeyVirtualKey))
        {
            tray.ShowMessage("Atalho indisponível",
                $"{settings.Current.HotkeyText} já está em uso por outro programa. Escolha outro em Configurações.");
        }

        // First run: there's no vault yet, so open straight into the setup screen.
        if (showAtStart || !vault.Exists)
            Dispatcher.BeginInvoke(ShowPopup, DispatcherPriority.ApplicationIdle);

        _main = new Views.MainWindow();
        tray.MainRequested += OpenMainWindow;
        _browser = new BrowserBridge();
        _browser.Start();
        OpenMainWindow();
    }

    internal void OpenMainWindow()
    {
        if (_main is null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    internal void OpenQuickPanel() => ShowPopup();

    private void ShowPopup() => _popup?.ShowAt(Services.Foreground.CurrentTarget);

    private void TogglePopup()
    {
        if (_popup is null) return;

        if (_popup.IsVisible) _popup.HidePopup();
        else if (!_popup.WasJustAutoHidden) ShowPopup(); // the click that dismissed it must not reopen it
    }

    private void ExitApp()
    {
        Services.Clipboard.ClearIfOwned();
        Services.Vault.Lock();
        Shutdown();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A tray app that dies silently is worse than one that reports; keep running and tell the user.
        e.Handled = true;
        Services?.Tray.ShowMessage("Cofre", "Ocorreu um erro inesperado: " + e.Exception.Message);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _browser?.Dispose();
        _main?.ForceClose();
        if (Services is not null)
        {
            Services.IdleLock.Dispose();
            Services.Hotkeys.Dispose();
            Services.Foreground.Dispose();
            Services.Tray.Dispose();
            Services.Vault.Dispose();
        }

        _popup?.ForceClose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
