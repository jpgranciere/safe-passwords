using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Cofre.Core.Models;
using Cofre.Core.Security;
using Cofre.Core.Vault;
using Cofre.App.ViewModels;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace Cofre.App.Views;

internal partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _copyTimer = new() { Interval = TimeSpan.FromSeconds(2) };
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
        _copyTimer.Tick += (_, _) => HideCopyFeedback();
        Deactivated += (_, _) => HideCopyFeedback();
        StateChanged += (_, _) => HideCopyFeedback();
        PreviewMouseDown += (_, _) => App.Services.IdleLock.Touch();
        PreviewKeyDown += (_, _) => App.Services.IdleLock.Touch();
        foreach (var input in new[] { TitleInput, UserInput, UrlInput, FolderInput, TagsInput, NotesInput })
            input.TextChanged += (_, _) => { if (!_refreshing) _dirty = true; };
        FavoriteInput.Click += (_, _) => _dirty = true;
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
        PreviewKeyDown += OnWindowKeyDown;
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
            var listRows = rows.Select(e => new EntryRow(e)).ToArray();
            Entries.ItemsSource = listRows;
            Entries.SelectedItem = listRows.FirstOrDefault(e => e.Id == selectedId);
            Counts.Text = rows.Length == all.Count
                ? $"{all.Count} {(all.Count == 1 ? "conta salva" : "contas salvas")} no seu cofre"
                : $"{rows.Length} de {all.Count} contas nesta visualização";
            Empty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!_dirty && !_new && !_editing)
            {
                var selected = rows.FirstOrDefault(e => e.Id == selectedId);
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
        HideCopyFeedback();
        DetailsPane.BeginAnimation(OpacityProperty, null);
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
        var animate = _draft?.Id != entry.Id;
        HideCopyFeedback();
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
        var context = string.IsNullOrWhiteSpace(entry.Folder) ? "Salvo neste cofre" : entry.Folder;
        SummaryMeta.Text = entry.Favorite ? $"Favorito · {context}" : context;
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
        RevealIcon.Symbol = SymbolRegular.Eye24;
        RevealButton.ToolTip = "Exibir senha";
        RevealButton.IsEnabled = entry.Password.Length > 0;
        _editing = isNew;
        Status.Text = ""; _dirty = false;
        _refreshing = wasRefreshing;
        UpdateTotp();
        UpdateMode();
        DetailScroll.ScrollToTop();
        if (animate) DetailsPane.BeginAnimation(OpacityProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(130)));
    }

    private void UpdateMode()
    {
        var selected = _draft is not null;
        DetailsPane.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        DetailsColumn.Width = new GridLength(selected ? 430 : 0);
        Resources["AccountMetadataVisibility"] = selected ? Visibility.Collapsed : Visibility.Visible;
        CollectionTip.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
        DetailsCaption.Text = _editing ? (_new ? "ADICIONAR CONTA" : "EDITAR CONTA") : "DETALHES DA CONTA";
        EditButton.Visibility = selected && !_editing ? Visibility.Visible : Visibility.Collapsed;
        ReadPanel.Visibility = selected && !_editing ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = selected && _editing ? Visibility.Visible : Visibility.Collapsed;
        EditorActions.Visibility = selected && _editing ? Visibility.Visible : Visibility.Collapsed;
        NoSelection.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearSelection()
    {
        var wasRefreshing = _refreshing;
        _refreshing = true;
        try
        {
            _draft = null;
            _dirty = false;
            Entries.SelectedItem = null;
            ClearEditor();
        }
        finally { _refreshing = wasRefreshing; }
    }

    private void OnCloseDetails(object sender, RoutedEventArgs e)
    {
        if (!CanDiscard()) return;
        ClearSelection();
        Entries.Focus();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _draft is null) return;
        e.Handled = true;
        OnCloseDetails(this, e);
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
        RevealIcon.Symbol = SymbolRegular.Eye24;
        RevealButton.ToolTip = "Exibir senha";
        UpdateMode(); DetailScroll.ScrollToTop(); TitleInput.Focus();
    }

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !App.Services.Vault.IsUnlocked) return;
        _passwordVisible = !_passwordVisible;
        SummaryPassword.Text = _passwordVisible ? _draft.Password : "••••••••••••";
        RevealIcon.Symbol = _passwordVisible ? SymbolRegular.EyeOff24 : SymbolRegular.Eye24;
        RevealButton.ToolTip = _passwordVisible ? "Ocultar senha" : "Exibir senha";
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

    private bool CanDiscard() => !_dirty || AppDialogWindow.Confirm(this,
        "Descartar alterações?", "Você fez alterações nesta conta que ainda não foram salvas. Se descartar, elas serão perdidas.",
        "Descartar alterações", "Continuar editando", destructive: true);

    private void OnSelect(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || Entries.SelectedItem is not EntryRow row) return;
        if (!CanDiscard())
        {
            _refreshing = true;
            Entries.SelectedItem = Entries.Items.OfType<EntryRow>().FirstOrDefault(x => x.Id == _draft?.Id);
            _refreshing = false;
            return;
        }
        LoadEntry(row.Entry, false);
    }

    private void OnSearch(object sender, TextChangedEventArgs e) { if (!_refreshing && Entries is not null) Refresh(); }
    private void OnFilter(object sender, RoutedEventArgs e)
    {
        SelectSection((string)((FrameworkElement)sender).Tag);
    }
    internal void SelectSection(string section)
    {
        if (!CanDiscard()) { UpdateNavigation(); return; }
        ClearSelection();
        _filter = section; _folder = null;
        Heading.Text = _filter switch { "favorites" => "Favoritos", "totp" => "Autenticador", "security" => "Segurança", _ => "Minhas contas" };
        Refresh();
    }
    private void OnFolder(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || Folders.SelectedItem is not string folder) return;
        if (!CanDiscard()) { Refresh(); return; }
        ClearSelection();
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
        if (_draft is null || _new || !AppDialogWindow.Confirm(this, "Excluir esta conta?",
                "A conta será removida do seu cofre. Esta ação não pode ser desfeita.", "Excluir conta", "Manter conta",
                destructive: true, context: _draft.Title) || _draft is null || !App.Services.Vault.IsUnlocked) return;
        try { App.Services.Vault.Delete(_draft.Id); _draft = null; _dirty = false; _editing = false; Refresh(); Status.Text = "Conta excluída."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status.Text = "Não foi possível excluir a entrada."; }
    }
    private void Copy(string value, string label, FrameworkElement? target)
    {
        if (!App.Services.Vault.IsUnlocked) return;
        if (value.Length == 0) { Status.Text = "Não há conteúdo para copiar."; return; }
        try
        {
            App.Services.Clipboard.CopySensitive(value, App.Services.Settings.Current.ClipboardClearSeconds);
            Status.Text = label + ".";
            if (target is not null) ShowCopyFeedback(target, label);
        }
        catch (System.Runtime.InteropServices.COMException) { Status.Text = "A área de transferência está ocupada. Tente novamente."; }
    }
    internal void ShowCopyFeedback(FrameworkElement target, string label)
    {
        CopyFeedback.PlacementTarget = target;
        CopyFeedbackText.Text = label;
        CopyFeedback.IsOpen = true;
        _copyTimer.Stop(); _copyTimer.Start();
    }
    private void HideCopyFeedback()
    {
        _copyTimer.Stop();
        CopyFeedback.IsOpen = false;
        CopyFeedback.PlacementTarget = null;
        CopyFeedbackText.Text = "";
    }
    private void OnCopyPassword(object sender, RoutedEventArgs e) => Copy(_editing ? PasswordInput.Password : _draft?.Password ?? "", "Senha copiada", sender as FrameworkElement);
    private void OnCopyUser(object sender, RoutedEventArgs e) => Copy(_editing ? UserInput.Text : _draft?.Username ?? "", "Usuário copiado", sender as FrameworkElement);
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
        try { Copy(Totp.Generate(Totp.Parse(TotpInput.Password), DateTimeOffset.UtcNow), "Código copiado", sender as FrameworkElement); }
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
            if (!_editing && !_dirty) ClearSelection();
            _passwordVisible = false;
            SummaryPassword.Text = _draft?.Password.Length > 0 ? "••••••••••••" : "";
            RevealIcon.Symbol = SymbolRegular.Eye24;
            RevealButton.ToolTip = "Exibir senha";
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
        HideCopyFeedback();
        App.Services.Vault.Unlocked -= ScheduleRefresh; App.Services.Vault.Changed -= ScheduleRefresh;
        App.Services.Vault.Locked -= OnVaultLocked;
        Close();
    }

}
