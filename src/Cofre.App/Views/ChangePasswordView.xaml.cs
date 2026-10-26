using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Cofre.Core.Security;
using Cofre.Core.Vault;

namespace Cofre.App.Views;

internal partial class ChangePasswordView : UserControl, IPopupView
{
    private readonly PopupWindow _popup;

    public ChangePasswordView(PopupWindow popup)
    {
        _popup = popup;
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            OnBack(this, e);
        };
    }

    public void OnShown() => CurrentInput.Focus();

    private void OnNewChanged(object sender, RoutedEventArgs e) => Strength?.Update(NewInput.Password);

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var current = CurrentInput.Password;
        var next = NewInput.Password;

        if (next.Length < VaultService.MinMasterPasswordLength)
        {
            ShowError($"A nova senha precisa ter pelo menos {VaultService.MinMasterPasswordLength} caracteres.");
            return;
        }

        if (next != ConfirmInput.Password)
        {
            ShowError("As senhas novas não são iguais.");
            return;
        }

        SaveButton.IsEnabled = false;
        SaveButton.Content = "Reencriptando…";
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            await Task.Run(() => App.Services.Vault.ChangeMasterPassword(current, next));
            _popup.ShowToast("Senha mestra alterada");
            _popup.Navigate(new SettingsView(_popup));
            return;
        }
        catch (InvalidMasterPasswordException)
        {
            ShowError("A senha mestra atual está incorreta.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError("Não foi possível alterar: " + ex.Message);
        }

        SaveButton.IsEnabled = true;
        SaveButton.Content = "Alterar senha mestra";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnBack(object sender, RoutedEventArgs e) => _popup.Navigate(new SettingsView(_popup));
}
