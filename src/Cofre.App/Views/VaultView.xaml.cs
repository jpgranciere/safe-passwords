using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Cofre.App.Services;
using Cofre.App.ViewModels;
using Cofre.Core.Models;
using Cofre.Core.Vault;

namespace Cofre.App.Views;

internal partial class VaultView : UserControl, IPopupView
{
    private readonly PopupWindow _popup;
    private readonly IReadOnlyList<VaultEntry> _all;

    public VaultView(PopupWindow popup)
    {
        _popup = popup;
        InitializeComponent();
        _all = App.Services.Vault.Entries;
        Rebuild();
    }

    public void OnShown() => Search.Focus();

    private void Rebuild()
    {
        var query = Search.Text;
        var matching = _all
            .Where(e => EntryMatcher.Matches(e, query))
            .OrderByDescending(e => e.Favorite)
            .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var rows = new List<ListRow>();
        var suggested = string.IsNullOrWhiteSpace(query)
            ? EntryMatcher.SuggestFor(matching, _popup.TargetTitle)
            : [];

        if (suggested.Count > 0)
        {
            rows.Add(new HeaderRow("SUGERIDAS PARA ESTA JANELA"));
            rows.AddRange(suggested.Select(e => new EntryRow(e)));

            var others = matching.Except(suggested).ToList();
            if (others.Count > 0)
            {
                rows.Add(new HeaderRow("TODAS AS SENHAS"));
                rows.AddRange(others.Select(e => new EntryRow(e)));
            }
        }
        else
        {
            rows.AddRange(matching.Select(e => new EntryRow(e)));
        }

        List.ItemsSource = rows;
        List.SelectedItem = rows.OfType<EntryRow>().FirstOrDefault();

        EmptyVault.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoResults.Visibility = _all.Count > 0 && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter when List.SelectedItem is EntryRow row:
                e.Handled = true;
                if (Keyboard.Modifiers == ModifierKeys.Control) CopyPassword(row.Entry);
                else AutoFill(row.Entry);
                break;
            case Key.Escape when Search.Text.Length > 0:
                Search.Text = "";
                e.Handled = true;
                break;
        }
    }

    private void MoveSelection(int step)
    {
        var rows = List.Items.OfType<EntryRow>().ToList();
        if (rows.Count == 0) return;

        var index = List.SelectedItem is EntryRow current ? rows.IndexOf(current) + step : 0;
        index = Math.Clamp(index, 0, rows.Count - 1);
        List.SelectedItem = rows[index];
        List.ScrollIntoView(rows[index]);
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<ButtonBase>(source) is not null) return;
        if (List.SelectedItem is EntryRow row) AutoFill(row.Entry);
    }

    private static VaultEntry? EntryOf(object sender) =>
        (sender as FrameworkElement)?.DataContext is EntryRow row ? row.Entry : null;

    private void OnCopyUser(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is not { } entry) return;
        if (entry.Username.Length == 0)
        {
            _popup.ShowToast("Esta entrada não tem usuário", isError: true);
            return;
        }

        App.Services.Clipboard.CopySensitive(entry.Username, App.Services.Settings.Current.ClipboardClearSeconds);
        _popup.ShowToast("Usuário copiado");
    }

    private void OnCopyPassword(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } entry) CopyPassword(entry);
    }

    private void OnAutoFill(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } entry) AutoFill(entry);
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } entry) _popup.Navigate(new EntryEditorView(_popup, entry.Clone(), isNew: false));
    }

    private void CopyPassword(VaultEntry entry)
    {
        var seconds = App.Services.Settings.Current.ClipboardClearSeconds;
        App.Services.Clipboard.CopySensitive(entry.Password, seconds);
        _popup.ShowToast(seconds > 0 ? $"Senha copiada · limpa em {seconds} s" : "Senha copiada");
    }

    /// <summary>Hides the popup and types "user, Tab, password, Enter" into the window the user came from.</summary>
    private async void AutoFill(VaultEntry entry)
    {
        var services = App.Services;
        var target = _popup.TargetWindow;
        _popup.HidePopup();

        var result = await services.AutoType.TypeAsync(target, entry.Username, entry.Password, services.Settings.Current.AutoSubmit);
        if (result == AutoTypeResult.Typed) return;

        // Couldn't type anywhere: leave the password on the clipboard so the user can still paste it.
        services.Clipboard.CopySensitive(entry.Password, services.Settings.Current.ClipboardClearSeconds);
        services.Tray.ShowMessage("Cofre",
            "Não encontrei onde digitar. Clique no campo de login antes de abrir o Cofre. A senha foi copiada: use Ctrl+V.");
    }

    private void OnNew(object sender, RoutedEventArgs e) =>
        _popup.Navigate(new EntryEditorView(_popup, new VaultEntry(), isNew: true));

    private void OnGenerator(object sender, RoutedEventArgs e) =>
        _popup.Navigate(new GeneratorView(_popup, onUse: null, onBack: () => _popup.Navigate(new VaultView(_popup))));

    private void OnSettings(object sender, RoutedEventArgs e) => _popup.Navigate(new SettingsView(_popup));

    private void OnLock(object sender, RoutedEventArgs e) => App.Services.Vault.Lock();

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var node = start; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T match) return match;
        }

        return null;
    }
}
