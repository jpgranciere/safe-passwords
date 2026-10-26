using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Cofre.Core.Security;

namespace Cofre.App.Views;

internal partial class UnlockView : UserControl, IPopupView
{
    private readonly PopupWindow _popup;
    private int _failures;

    public UnlockView(PopupWindow popup)
    {
        _popup = popup;
        InitializeComponent();
        HintText.Text = $"Atalho global: {App.Services.Settings.Current.HotkeyText}";
        PasswordInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            OnUnlock(this, e);
        };
    }

    public void OnShown() => PasswordInput.Focus();

    private async void OnUnlock(object sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Password;
        if (password.Length == 0 || !UnlockButton.IsEnabled) return;

        UnlockButton.IsEnabled = false;
        UnlockButton.Content = "Verificando…";
        ErrorText.Visibility = Visibility.Collapsed;

        try
        {
            await Task.Run(() => App.Services.Vault.Unlock(password));
            _popup.Navigate(new VaultView(_popup));
            return;
        }
        catch (InvalidMasterPasswordException)
        {
            _failures++;
            ShowError("Senha mestra incorreta.");
            PasswordInput.Password = "";
            PlayShake();
            // A small growing delay on top of Argon2's own cost makes guessing at the keyboard pointless.
            await Task.Delay(Math.Min(_failures, 5) * 400);
        }
        catch (Exception ex) when (ex is VaultFormatException or IOException or UnauthorizedAccessException)
        {
            ShowError(ex.Message);
        }

        UnlockButton.IsEnabled = true;
        UnlockButton.Content = "Desbloquear";
        PasswordInput.Focus();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void PlayShake()
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(320) };
        double[] offsets = [0, -9, 9, -6, 6, -3, 0];
        for (var i = 0; i < offsets.Length; i++)
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(offsets[i], KeyTime.FromPercent((double)i / (offsets.Length - 1))));
        Shake.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animation);
    }
}
