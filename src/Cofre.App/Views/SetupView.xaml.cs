using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Cofre.Core.Vault;

namespace Cofre.App.Views;

internal partial class SetupView : UserControl, IPopupView
{
    private readonly PopupWindow _popup;

    public SetupView(PopupWindow popup)
    {
        _popup = popup;
        InitializeComponent();
        PasswordInput.PreviewKeyDown += OnEnter;
        ConfirmInput.PreviewKeyDown += OnEnter;
    }

    public void OnShown() => PasswordInput.Focus();

    private void OnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (sender == PasswordInput) ConfirmInput.Focus();
        else OnCreate(sender, e);
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        Strength?.Update(PasswordInput.Password);
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private async void OnCreate(object sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Password;

        if (password.Length < VaultService.MinMasterPasswordLength)
        {
            ShowError($"A senha mestra precisa ter pelo menos {VaultService.MinMasterPasswordLength} caracteres.");
            return;
        }

        if (password != ConfirmInput.Password)
        {
            ShowError("As senhas não são iguais.");
            return;
        }

        CreateButton.IsEnabled = false;
        CreateButton.Content = "Criando…";
        try
        {
            // Argon2 takes a moment on purpose; keep the UI responsive meanwhile.
            await Task.Run(() => App.Services.Vault.Create(password));
            _popup.ShowToast("Cofre criado");
            _popup.Navigate(new VaultView(_popup));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError("Não foi possível criar o cofre: " + ex.Message);
            CreateButton.IsEnabled = true;
            CreateButton.Content = "Criar cofre";
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
