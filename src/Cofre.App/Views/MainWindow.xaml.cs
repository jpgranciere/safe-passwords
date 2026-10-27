using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cofre.Core.Models;
using Cofre.Core.Security;
using Cofre.Core.Vault;

namespace Cofre.App.Views;

internal partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private VaultEntry? _draft;
    private string _filter = "all";
    private string? _folder;
    private bool _refreshing;
    private bool _forceClose;
    private bool _dirty;
    private bool _new;
    private bool _editing;
    private bool _passwordVisible;

    public MainWindow()
    {
        InitializeComponent();
        App.Services.Vault.Unlocked += ScheduleRefresh;
        App.Services.Vault.Changed += ScheduleRefresh;
        App.Services.Vault.Locked += OnVaultLocked;
        _clock.Tick += (_, _) => UpdateTotp();
        _clock.Start();
        PreviewMouseDown += (_, _) => App.Services.IdleLock.Touch();
        PreviewKeyDown += (_, _) => App.Services.IdleLock.Touch();
        foreach (var input in new[] { TitleInput, UserInput, UrlInput, FolderInput, TagsInput, NotesInput })
            input.TextChanged += (_, _) => { if (!_refreshing) _dirty = true; };
        FavoriteInput.Click += (_, _) => _dirty = true;
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
        Refresh();
    }

    private void ScheduleRefresh() => Dispatcher.BeginInvoke(() => Refresh());
    private void OnVaultLocked()
    {
        // Drop all decrypted UI references immediately when locking on the UI thread.
        if (Dispatcher.CheckAccess()) Refresh();
        else Dispatcher.Invoke(Refresh);
    }

    private void Refresh()
    {
        _refreshing = true;
        try
        {
            var unlocked = App.Services.Vault.IsUnlocked;
            UpdateNavigation();
            LockedOverlay.Visibility = unlocked ? Visibility.Collapsed : Visibility.Visible;
            Workspace.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
            if (!unlocked)
            {
                _draft = null;
                Entries.ItemsSource = null;
                Folders.ItemsSource = null;
                ClearEditor();
                Counts.Text = AuditSummary.Text = Status.Text = FindingText.Text = "";
                _dirty = false;
                return;
            }
            var all = App.Services.Vault.Entries;
            var findings = SecurityAudit.Analyze(all);
            Counts.Text = $"{all.Count} entradas · {all.Count(e => e.Favorite)} favoritas · {all.Count(e => e.Totp.Length > 0)} autenticadores";
            AuditSummary.Text = $"{findings.Count(f => f.Weak)} senhas fracas\n{findings.Count(f => f.Reused)} entradas com senha repetida\n{findings.Count(f => f.MissingTotp)} sem TOTP cadastrado";
            AuditCard.Visibility = _filter == "security" ? Visibility.Visible : Visibility.Collapsed;
            Folders.ItemsSource = all.Select(e => e.Folder).Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToArray();
            var needsAttention = findings.Where(f => f.NeedsAttention).Select(f => f.EntryId).ToHashSet();
            var rows = all.Where(e => EntryMatcher.Matches(e, Search.Text)
                && (_folder is null || string.Equals(e.Folder, _folder, StringComparison.OrdinalIgnoreCase))
                && (_filter != "favorites" || e.Favorite)
                && (_filter != "totp" || e.Totp.Length > 0)
                && (_filter != "security" || needsAttention.Contains(e.Id)))
                .OrderByDescending(e => e.Favorite).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
            var selectedId = _draft?.Id;
            Entries.ItemsSource = rows;
            Entries.SelectedItem = rows.FirstOrDefault(e => e.Id == selectedId);
            Empty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!_dirty && !_new && !_editing)
            {
                var selected = rows.FirstOrDefault(e => e.Id == selectedId) ?? rows.FirstOrDefault();
                Entries.SelectedItem = selected;
                if (selected is null) { _draft = null; ClearEditor(); }
                else LoadEntry(selected, false);
            }
            Editor.IsEnabled = _draft is not null;
            UpdateMode();
        }
        finally { _refreshing = false; }
    }

    private void ClearEditor()
    {
        TitleInput.Clear(); UserInput.Clear(); PasswordInput.Password = ""; UrlInput.Clear();
        FolderInput.Clear(); TagsInput.Clear(); NotesInput.Clear(); TotpInput.Password = "";
        FavoriteInput.IsChecked = false;
        TotpCode.Text = TotpTime.Text = StrengthText.Text = FindingText.Text = "";
        TotpCard.Visibility = Visibility.Collapsed;
        Editor.IsEnabled = false;
        _new = false;
        _editing = false;
        _passwordVisible = false;
        SummaryTitle.Text = SummaryMeta.Text = SummaryUser.Text = SummaryPassword.Text = SummaryStrength.Text = "";
        SummarySite.Text = SummaryOrganization.Text = SummaryNotes.Text = SummaryFinding.Text = "";
        UpdateMode();
    }

    private void LoadEntry(VaultEntry entry, bool isNew)
    {
        var wasRefreshing = _refreshing;
        _refreshing = true;
        _draft = entry.Clone(); _new = isNew;
        TitleInput.Text = entry.Title; UserInput.Text = entry.Username;
        PasswordInput.Password = entry.Password; UrlInput.Text = entry.Url;
        FolderInput.Text = entry.Folder; TagsInput.Text = entry.Tags; NotesInput.Text = entry.Notes;
        TotpInput.Password = entry.Totp; FavoriteInput.IsChecked = entry.Favorite;
        EditorHeading.Text = isNew ? "Adicionar conta" : "Editar conta";
        DeleteButton.IsEnabled = !isNew; Editor.IsEnabled = true;
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        var finding = SecurityAudit.Analyze(App.Services.Vault.Entries).FirstOrDefault(f => f.EntryId == entry.Id);
        FindingText.Text = finding is { NeedsAttention: true } ? finding.Summary : "";
        SummaryTitle.Text = entry.Title;
        SummaryMeta.Text = entry.Favorite ? "Favorito · Conta pessoal" : "Conta pessoal";
        SummaryUser.Text = string.IsNullOrWhiteSpace(entry.Username) ? "Não informado" : entry.Username;
        SummarySite.Text = string.IsNullOrWhiteSpace(entry.Url) ? "Endereço não informado" : entry.Url;
        SummaryOrganization.Text = string.Join(" · ", new[] { entry.Folder, entry.Tags }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (SummaryOrganization.Text.Length == 0) SummaryOrganization.Text = "Sem pasta ou tags";
        SummaryNotes.Text = string.IsNullOrWhiteSpace(entry.Notes) ? "Nenhuma nota adicionada." : entry.Notes;
        SummaryStrength.Text = StrengthText.Text;
        SummaryFinding.Text = FindingText.Text;
        OpenSiteButton.IsEnabled = TrySite(entry.Url, out _);
        _passwordVisible = false;
        SummaryPassword.Text = entry.Password.Length == 0 ? "Sem senha cadastrada" : "••••••••••••";
        RevealButton.Content = "Exibir";
        RevealButton.IsEnabled = entry.Password.Length > 0;
        _editing = isNew;
        Status.Text = ""; _dirty = false;
        _refreshing = wasRefreshing;
        UpdateTotp();
        UpdateMode();
        DetailScroll.ScrollToTop();
    }

    private void UpdateMode()
    {
        var selected = _draft is not null;
        ReadPanel.Visibility = selected && !_editing ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = selected && _editing ? Visibility.Visible : Visibility.Collapsed;
        EditorActions.Visibility = selected && _editing ? Visibility.Visible : Visibility.Collapsed;
        NoSelection.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateNavigation()
    {
        AllNav.IsChecked = _filter == "all" && _folder is null;
        FavoritesNav.IsChecked = _filter == "favorites";
        TotpNav.IsChecked = _filter == "totp";
        SecurityNav.IsChecked = _filter == "security";
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !App.Services.Vault.IsUnlocked) return;
        _editing = true;
        _passwordVisible = false;
        SummaryPassword.Text = _draft.Password.Length == 0 ? "Sem senha cadastrada" : "••••••••••••";
        RevealButton.Content = "Exibir";
        UpdateMode(); DetailScroll.ScrollToTop(); TitleInput.Focus();
    }

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !App.Services.Vault.IsUnlocked) return;
        _passwordVisible = !_passwordVisible;
        SummaryPassword.Text = _passwordVisible ? _draft.Password : "••••••••••••";
        RevealButton.Content = _passwordVisible ? "Ocultar" : "Exibir";
    }

    private static bool TrySite(string value, out Uri? uri) => Uri.TryCreate(value, UriKind.Absolute, out uri)
        && uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0;

    private void OnOpenSite(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !App.Services.Vault.IsUnlocked || !TrySite(_draft.Url, out var uri)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Status.Text = "Não foi possível abrir o navegador."; }
    }

    private bool CanDiscard() => !_dirty || MessageBox.Show(this,
        "Descartar as alterações ainda não salvas?", "Alterações pendentes", MessageBoxButton.YesNo,
        MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void OnSelect(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || Entries.SelectedItem is not VaultEntry entry) return;
        if (!CanDiscard())
        {
            _refreshing = true;
            Entries.SelectedItem = Entries.Items.OfType<VaultEntry>().FirstOrDefault(x => x.Id == _draft?.Id);
            _refreshing = false;
            return;
        }
        LoadEntry(entry, false);
    }

    private void OnSearch(object sender, TextChangedEventArgs e) { if (!_refreshing && Entries is not null) Refresh(); }
    private void OnFilter(object sender, RoutedEventArgs e)
    {
        SelectSection((string)((FrameworkElement)sender).Tag);
    }
    internal void SelectSection(string section)
    {
        if (!CanDiscard()) { UpdateNavigation(); return; }
        _editing = _dirty = _new = false;
        _filter = section; _folder = null;
        Heading.Text = _filter switch { "favorites" => "Favoritos", "totp" => "Autenticador", "security" => "Segurança", _ => "Minhas contas" };
        Refresh();
    }
    private void OnFolder(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || Folders.SelectedItem is not string folder) return;
        if (!CanDiscard()) { Refresh(); return; }
        _editing = _dirty = _new = false;
        _folder = folder; _filter = "all"; Heading.Text = folder; Refresh();
    }
    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (!CanDiscard()) return;
        _refreshing = true; Entries.SelectedItem = null; _refreshing = false;
        LoadEntry(new VaultEntry { Folder = _folder ?? "" }, true); _dirty = true; TitleInput.Focus();
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !App.Services.Vault.IsUnlocked) return;
        if (string.IsNullOrWhiteSpace(TitleInput.Text)) { Status.Text = "Informe um título."; return; }
        try
        {
            if (!string.IsNullOrWhiteSpace(TotpInput.Password)) Totp.Parse(TotpInput.Password);
            var entry = _draft.Clone();
            entry.Title = TitleInput.Text.Trim(); entry.Username = UserInput.Text.Trim();
            entry.Password = PasswordInput.Password; entry.Url = UrlInput.Text.Trim();
            entry.Folder = FolderInput.Text.Trim(); entry.Tags = TagsInput.Text.Trim();
            entry.Notes = NotesInput.Text; entry.Totp = TotpInput.Password.Trim(); entry.Favorite = FavoriteInput.IsChecked == true;
            _draft = App.Services.Vault.Upsert(entry); _dirty = false; _new = false; _editing = false;
            Refresh(); Status.Text = "Conta salva no cofre.";
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or IOException or UnauthorizedAccessException)
        { Status.Text = ex is FormatException or OverflowException ? "Chave ou configuração TOTP inválida." : "Não foi possível salvar a entrada."; }
    }
    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        if (!CanDiscard()) return;
        _dirty = false; _new = false; _editing = false; Refresh();
    }
    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_draft is null || _new || MessageBox.Show(this, "Excluir esta entrada permanentemente?", "Excluir entrada",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { App.Services.Vault.Delete(_draft.Id); _draft = null; _dirty = false; _editing = false; Refresh(); Status.Text = "Conta excluída."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = "Não foi possível excluir a entrada."; }
    }
    private void Copy(string value, string label)
    {
        if (!App.Services.Vault.IsUnlocked || value.Length == 0) return;
        App.Services.Clipboard.CopySensitive(value, App.Services.Settings.Current.ClipboardClearSeconds);
        Status.Text = label + " copiado.";
    }
    private void OnCopyPassword(object sender, RoutedEventArgs e) => Copy(_editing ? PasswordInput.Password : _draft?.Password ?? "", "Senha");
    private void OnCopyUser(object sender, RoutedEventArgs e) => Copy(_editing ? UserInput.Text : _draft?.Username ?? "", "Usuário");
    private void OnGenerate(object sender, RoutedEventArgs e) => PasswordInput.Password = PasswordGenerator.Generate(GeneratorView.LastOptions);
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_refreshing) _dirty = true;
        if (StrengthText is null) return;
        StrengthText.Text = PasswordStrength.Evaluate(PasswordInput.Password).Level switch
        { StrengthLevel.VeryWeak => "Muito fraca", StrengthLevel.Weak => "Fraca", StrengthLevel.Fair => "Razoável", StrengthLevel.Strong => "Forte", _ => "Muito forte" };
    }
    private void OnTotpChanged(object sender, RoutedEventArgs e) { if (!_refreshing) _dirty = true; UpdateTotp(); }
    private void UpdateTotp()
    {
        if (TotpCode is null || !App.Services.Vault.IsUnlocked || !IsVisible) return;
        TotpCode.Text = TotpTime.Text = "";
        TotpCard.Visibility = string.IsNullOrWhiteSpace(TotpInput.Password) ? Visibility.Collapsed : Visibility.Visible;
        if (string.IsNullOrWhiteSpace(TotpInput.Password)) return;
        try
        {
            var config = Totp.Parse(TotpInput.Password); var now = DateTimeOffset.UtcNow;
            TotpCode.Text = Totp.Generate(config, now);
            TotpTime.Text = $"Renova em {Totp.RemainingSeconds(config, now)} s";
        }
        catch (Exception ex) when (ex is FormatException or OverflowException) { TotpTime.Text = "Chave TOTP inválida."; }
    }
    private void OnCopyTotp(object sender, RoutedEventArgs e)
    {
        try { Copy(Totp.Generate(Totp.Parse(TotpInput.Password), DateTimeOffset.UtcNow), "Código TOTP"); }
        catch (Exception ex) when (ex is FormatException or OverflowException) { Status.Text = "Informe uma chave TOTP válida."; }
    }
    private void OnQuick(object sender, RoutedEventArgs e) => ((App)Application.Current).OpenQuickPanel();
    private void OnLock(object sender, RoutedEventArgs e) => App.Services.Vault.Lock();
    private void OnExtension(object sender, RoutedEventArgs e)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(System.IO.Path.Combine(folder.FullName, "extension", "manifest.json"))) folder = folder.Parent;
        Status.Text = folder is null ? "Consulte README.md para instalar a extensão no Chrome ou Edge."
            : $"Chrome/Edge: ative o modo de desenvolvedor e use Carregar sem compactação nesta pasta:\n{System.IO.Path.Combine(folder.FullName, "extension")}";
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            _passwordVisible = false;
            SummaryPassword.Text = _draft?.Password.Length > 0 ? "••••••••••••" : "";
            RevealButton.Content = "Exibir";
            Hide();
            if (!App.Services.Settings.Current.TrayCloseNoticeShown)
            {
                App.Services.Tray.ShowMessage("O Cofre continua na bandeja", "Para reabrir, use o atalho Cofre ou o ícone perto do relógio. Para encerrar, escolha Sair nesse ícone.");
                App.Services.Settings.Current.TrayCloseNoticeShown = true;
                App.Services.Settings.Save();
            }
        }
        base.OnClosing(e);
    }
    public void ForceClose()
    {
        _clock.Stop(); _forceClose = true;
        App.Services.Vault.Unlocked -= ScheduleRefresh; App.Services.Vault.Changed -= ScheduleRefresh;
        App.Services.Vault.Locked -= OnVaultLocked;
        Close();
    }

}
