using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Cofre.App.Services;
using Wpf.Ui.Controls;
using WinForms = System.Windows.Forms;

namespace Cofre.App.Views;

/// <summary>Implemented by the screens hosted in the popup; called once the screen is on display.</summary>
internal interface IPopupView
{
    void OnShown();
}

/// <summary>
/// A compact panel that appears next to the tray, hides itself when it loses
/// focus and swaps between screens (setup, unlock, list, editor...).
/// </summary>
internal partial class PopupWindow : FluentWindow
{
    private const double EdgeMargin = 12;

    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromMilliseconds(2200) };
    private int _autoHideSuppressions;
    private long _lastAutoHideTick;
    private bool _allowClose;

    public PopupWindow()
    {
        InitializeComponent();

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
        };

        PreviewMouseDown += (_, _) => App.Services.IdleLock.Touch();
        PreviewKeyDown += (_, _) => App.Services.IdleLock.Touch();
    }

    /// <summary>The window the user was working in before opening the popup (where auto-type will write).</summary>
    public IntPtr TargetWindow { get; private set; }

    public string? TargetTitle { get; private set; }

    /// <summary>True right after the popup hid itself because focus moved; used to ignore the tray click that caused it.</summary>
    public bool WasJustAutoHidden => Environment.TickCount64 - _lastAutoHideTick < 350;

    public void ShowAt(IntPtr target)
    {
        TargetWindow = target;
        TargetTitle = target == IntPtr.Zero ? null : NativeMethods.GetTitle(target);

        App.Services.IdleLock.Touch();
        if (!CanResumeCurrentView()) RouteToStart();
        PositionOnScreen(target);

        if (!IsVisible) Show();
        Activate();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    public void HidePopup()
    {
        if (IsVisible) Hide();
    }

    /// <summary>
    /// The popup hides itself whenever focus moves (e.g. to copy credentials from another window). Forms in progress
    /// are kept as they were so nothing has to be typed again; the list and the unlock screen are rebuilt every time.
    /// Locking the vault replaces the view, so a half-filled password never outlives the unlocked session.
    /// </summary>
    private bool CanResumeCurrentView()
    {
        var vault = App.Services.Vault;
        return Host.Content switch
        {
            SetupView => !vault.Exists,
            EntryEditorView or GeneratorView or SettingsView or ChangePasswordView => vault.IsUnlocked,
            _ => false,
        };
    }

    /// <summary>Setup if there is no vault yet, unlock if it is locked, otherwise the list.</summary>
    public void RouteToStart()
    {
        var vault = App.Services.Vault;
        if (!vault.Exists) Navigate(new SetupView(this));
        else if (!vault.IsUnlocked) Navigate(new UnlockView(this));
        else Navigate(new VaultView(this));
    }

    public void Navigate(UserControl view)
    {
        Host.Content = view;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });

        if (view is IPopupView popupView)
            Dispatcher.BeginInvoke(popupView.OnShown, DispatcherPriority.Input);
    }

    public void ShowToast(string message, bool isError = false)
    {
        ToastText.Text = message;
        ToastIcon.Symbol = isError ? SymbolRegular.ErrorCircle24 : SymbolRegular.CheckmarkCircle24;
        ToastIcon.Foreground = new SolidColorBrush(isError ? Color.FromRgb(0xD8, 0xA0, 0x8B) : Color.FromRgb(0xBA, 0xC9, 0x9A));

        ToastBorder.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>Keep the popup open while a system dialog (save file...) takes the focus.</summary>
    public IDisposable SuppressAutoHide()
    {
        _autoHideSuppressions++;
        return new Releaser(this);
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_autoHideSuppressions > 0 || !IsVisible) return;

        _lastAutoHideTick = Environment.TickCount64;
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            HidePopup();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true; // closing (Alt+F4) only hides; the tray menu's "Sair" really exits
            Hide();
        }

        base.OnClosing(e);
    }

    /// <summary>Bottom-right corner of the work area (right above the tray) of the monitor the user is working on.</summary>
    private void PositionOnScreen(IntPtr target)
    {
        var screen = target != IntPtr.Zero
            ? WinForms.Screen.FromHandle(target)
            : WinForms.Screen.FromPoint(WinForms.Cursor.Position);
        var area = screen.WorkingArea; // physical pixels

        var dpi = VisualTreeHelper.GetDpi(this);
        var margin = EdgeMargin * dpi.DpiScaleX;
        Left = (area.Right - Width * dpi.DpiScaleX - margin) / dpi.DpiScaleX;
        Top = (area.Bottom - Height * dpi.DpiScaleY - margin) / dpi.DpiScaleY;
    }

    private sealed class Releaser(PopupWindow owner) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released) return;
            _released = true;
            owner._autoHideSuppressions--;
        }
    }
}
