using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using Cofre.App.Services;
using Wpf.Ui.Controls;

namespace Cofre.App.Views;

/// <summary>Application-owned confirmation, with a dimmed owner and a safe default action.</summary>
internal partial class AppDialogWindow : FluentWindow
{
    private bool _cancelPending;

    private AppDialogWindow(Window owner, string title, string message, string confirm, string cancel, bool destructive, string? context, bool bringToFront, UIElement? extra)
    {
        InitializeComponent();
        Owner = owner;
        ShowActivated = owner.ShowActivated;
        Topmost = bringToFront && owner.ShowActivated;
        ShowInTaskbar = bringToFront && owner.ShowActivated;
        Title = bringToFront ? title + " — Cofre" : title;
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        ConfirmAction.Content = confirm;
        CancelAction.Content = cancel;
        if (extra is not null) { ExtraHost.Content = extra; ExtraHost.Visibility = Visibility.Visible; }
        if (!string.IsNullOrWhiteSpace(context)) { ContextText.Text = context; ContextBox.Visibility = Visibility.Visible; }
        if (destructive)
        {
            DialogIcon.Symbol = SymbolRegular.Warning24;
            DialogIcon.SetResourceReference(ForegroundProperty, "MainDangerBrush");
            ConfirmAction.Style = (Style)FindResource("DangerButton");
        }
        PreviewMouseDown += (_, _) => App.Services.IdleLock.Touch();
        PreviewKeyDown += (_, _) => App.Services.IdleLock.Touch();
        Loaded += (_, _) =>
        {
            if (_cancelPending) { Cancel(); return; }
            if (bringToFront && owner.ShowActivated)
            {
                Activate();
                NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
            }
            CancelAction.Focus();
        };
    }

    internal static bool Confirm(Window owner, string title, string message, string confirm, string cancel = "Cancelar",
        bool destructive = false, string? context = null, CancellationToken cancellationToken = default, bool bringToFront = false, UIElement? extra = null)
    {
        var vault = App.Services.Vault;
        if (!vault.IsUnlocked || cancellationToken.IsCancellationRequested
            || owner.OwnedWindows.OfType<AppDialogWindow>().Any()) return false;
        var dialog = new AppDialogWindow(owner, title, message, confirm, cancel, destructive, context, bringToFront, extra);
        var root = owner.Content as Panel;
        var backdrop = new Border { Name = "DialogBackdrop", Background = new SolidColorBrush(Color.FromArgb(145, 0, 0, 0)), Focusable = false };
        Panel.SetZIndex(backdrop, 100);
        Grid.SetRowSpan(backdrop, 100); Grid.SetColumnSpan(backdrop, 100);
        var previousFocus = System.Windows.Input.Keyboard.FocusedElement;
        void CancelForLock() => dialog.Dispatcher.BeginInvoke(dialog.Cancel);
        vault.Locked += CancelForLock;
        using var registration = cancellationToken.Register(() => dialog.Dispatcher.BeginInvoke(dialog.Cancel));
        root?.Children.Add(backdrop);
        try { return dialog.ShowDialog() == true && vault.IsUnlocked && !cancellationToken.IsCancellationRequested; }
        finally
        {
            vault.Locked -= CancelForLock;
            root?.Children.Remove(backdrop);
            if (previousFocus is UIElement element && element.IsVisible && element.IsEnabled) element.Focus();
        }
    }

    private void Cancel()
    {
        _cancelPending = true;
        if (IsVisible) DialogResult = false;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Cancel();
    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;
}
