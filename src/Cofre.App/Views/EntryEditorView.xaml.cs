using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Cofre.Core.Models;
using Cofre.Core.Security;

namespace Cofre.App.Views;

internal partial class EntryEditorView : UserControl, IPopupView
{
    private readonly PopupWindow _popup;
    private readonly VaultEntry _entry;
    private readonly bool _isNew;
    private readonly DispatcherTimer _deleteArmTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _deleteArmed;
    private bool _favorite;

    /// <param name="entry">A working copy; never the instance held by the vault.</param>
    public EntryEditorView(PopupWindow popup, VaultEntry entry, bool isNew)
    {
        _popup = popup;
        _entry = entry;
        _isNew = isNew;
        InitializeComponent();

        TitleText.Text = isNew ? "Nova senha" : "Editar senha";
        TitleInput.Text = entry.Title;
        UsernameInput.Text = entry.Username;
        PasswordInput.Password = entry.Password;
        UrlInput.Text = entry.Url;
        NotesInput.Text = entry.Notes;
        SetFavorite(entry.Favorite);
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        Strength.Update(entry.Password);

        _deleteArmTimer.Tick += (_, _) => DisarmDelete();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public void OnShown()
    {
        if (_isNew) TitleInput.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            OnCancel(this, e);
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OnSave(this, e);
        }
    }

    private void OnAnyChanged(object sender, RoutedEventArgs e)
    {
        if (TitleError is not null) TitleError.Visibility = Visibility.Collapsed;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e) => Strength?.Update(PasswordInput.Password);

    private void OnQuickGenerate(object sender, RoutedEventArgs e)
    {
        PasswordInput.Password = PasswordGenerator.Generate(GeneratorView.LastOptions);
        _popup.ShowToast("Senha gerada");
    }

    /// <summary>Opens the full generator and comes back to this editor with everything typed so far.</summary>
    private void OnOpenGenerator(object sender, RoutedEventArgs e)
    {
        var draft = ReadFields();
        _popup.Navigate(new GeneratorView(
            _popup,
            onUse: password =>
            {
                draft.Password = password;
                _popup.Navigate(new EntryEditorView(_popup, draft, _isNew));
            },
            onBack: () => _popup.Navigate(new EntryEditorView(_popup, draft, _isNew))));
    }

    private VaultEntry ReadFields()
    {
        var copy = _entry.Clone();
        copy.Title = TitleInput.Text.Trim();
        copy.Username = UsernameInput.Text.Trim();
        copy.Password = PasswordInput.Password;
        copy.Url = UrlInput.Text.Trim();
        copy.Notes = NotesInput.Text;
        copy.Favorite = _favorite;
        return copy;
    }

    private void OnToggleFavorite(object sender, RoutedEventArgs e) => SetFavorite(!_favorite);

    private void SetFavorite(bool favorite)
    {
        _favorite = favorite;
        FavoriteIcon.Filled = favorite;
        FavoriteIcon.Foreground = favorite
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24))
            : (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var edited = ReadFields();
        if (edited.Title.Length == 0)
        {
            TitleError.Visibility = Visibility.Visible;
            TitleInput.Focus();
            return;
        }

        try
        {
            App.Services.Vault.Upsert(edited);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _popup.ShowToast("Não foi possível salvar: " + ex.Message, isError: true);
            return;
        }

        _popup.ShowToast("Salvo no cofre");
        _popup.Navigate(new VaultView(_popup));
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _popup.Navigate(new VaultView(_popup));

    /// <summary>Two clicks to delete: the first arms the button for three seconds.</summary>
    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (!_deleteArmed)
        {
            _deleteArmed = true;
            DeleteButton.Content = "Confirmar exclusão";
            _deleteArmTimer.Start();
            return;
        }

        App.Services.Vault.Delete(_entry.Id);
        _popup.ShowToast("Entrada excluída");
        _popup.Navigate(new VaultView(_popup));
    }

    private void DisarmDelete()
    {
        _deleteArmTimer.Stop();
        _deleteArmed = false;
        DeleteButton.Content = "Excluir";
    }
}
