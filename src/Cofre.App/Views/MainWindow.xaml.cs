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
    private EntryKind? _kindFilter;
    private EntryKind _editorKind;

    private const string Mask = "••••••••••••";

    // Passwords and tokens/keys live in separate sections: the first group is "logins", the second "secrets".
    private bool IsSecretGroup => _filter is "secrets" or "expiring";
    private EntryKind EditorKind => KindInput.SelectedIndex >= 0 ? (EntryKind)KindInput.SelectedIndex : EntryKind.Login;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    private static string ValueOf(VaultEntry entry) => entry.Kind.IsSecret() ? entry.Secret : entry.Password;
    private static string NounOf(EntryKind kind) => kind.IsSecret() ? "segredo" : "conta";

    private static string MaskedValue(VaultEntry entry) => ValueOf(entry).Length > 0 ? Mask
        : entry.Kind.IsSecret() ? "Nenhum valor cadastrado" : "Sem senha cadastrada";

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
        foreach (var input in new[] { TitleInput, UserInput, UrlInput, FolderInput, TagsInput, NotesInput, SecretInput })
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
            UpdateGroupUi();
            LockedOverlay.Visibility = unlocked ? Visibility.Collapsed : Visibility.Visible;
            Workspace.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
            if (!unlocked)
            {
                _draft = null;
                Entries.ItemsSource = null;
                Folders.ItemsSource = null;
                ClearEditor();
                Counts.Text = AuditSummary.Text = Status.Text = FindingText.Text = ExpiryBadge.Text = "";
                ExpiryBadge.Visibility = Visibility.Collapsed;
                _dirty = false;
                return;
            }
            var all = App.Services.Vault.Entries;
            var findings = SecurityAudit.Analyze(all);
            var today = Today;
            var expiring = all.Count(e => SecretExpiry.NeedsAttention(e, today));
            AuditSummary.Text = $"{findings.Count(f => f.Weak)} senhas fracas\n{findings.Count(f => f.Reused)} entradas com senha repetida\n{findings.Count(f => f.MissingTotp)} sem TOTP cadastrado\n{expiring} tokens ou chaves vencidos ou perto de vencer";
            AuditCard.Visibility = _filter == "security" ? Visibility.Visible : Visibility.Collapsed;
            ExpiryBadge.Text = expiring.ToString();
            ExpiryBadge.Visibility = expiring > 0 ? Visibility.Visible : Visibility.Collapsed;
            var secretGroup = IsSecretGroup;
            var movable = SecretDetector.Find(all).Count;
            MoveButton.Content = $"Mover de Senhas ({movable})";
            MoveButton.Visibility = secretGroup && movable > 0 ? Visibility.Visible : Visibility.Collapsed;
            var scope = all.Where(e => e.Kind.IsSecret() == secretGroup).ToArray();
            Folders.ItemsSource = scope.Select(e => e.Folder).Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f).ToArray();
            var needsAttention = findings.Where(f => f.NeedsAttention).Select(f => f.EntryId).ToHashSet();
            var matching = scope.Where(e => EntryMatcher.Matches(e, Search.Text)
                && (_folder is null || string.Equals(e.Folder, _folder, StringComparison.OrdinalIgnoreCase))
                && (_filter != "favorites" || e.Favorite)
                && (_filter != "totp" || e.Totp.Length > 0)
                && (_filter != "security" || needsAttention.Contains(e.Id))
                && (_filter != "expiring" || e.Expires is not null)
                && (_kindFilter is null || e.Kind == _kindFilter));
            var rows = (_filter == "expiring"
                ? matching.OrderBy(e => e.Expires).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
                : matching.OrderByDescending(e => e.Favorite).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)).ToArray();
            var selectedId = _draft?.Id;
            var listRows = rows.Select(e => new EntryRow(e)).ToArray();
            Entries.ItemsSource = listRows;
            Entries.SelectedItem = listRows.FirstOrDefault(e => e.Id == selectedId);
            var noun = secretGroup ? ("segredo salvo", "segredos salvos") : ("conta salva", "contas salvas");
            Counts.Text = rows.Length == scope.Length
                ? $"{scope.Length} {(scope.Length == 1 ? noun.Item1 : noun.Item2)} no seu cofre"
                : $"{rows.Length} de {scope.Length} {(secretGroup ? "segredos" : "contas")} nesta visualização";
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

    /// <summary>Titles, labels and filters that differ between the passwords group and the tokens-and-keys group.</summary>
    private void UpdateGroupUi()
    {
        var secrets = IsSecretGroup;
        Heading.Text = _folder ?? _filter switch
        {
            "favorites" => "Favoritos", "totp" => "Autenticador", "security" => "Segurança",
            "secrets" => "Tokens e chaves", "expiring" => "Vencimentos", _ => "Minhas contas",
        };
        NewButton.Content = secrets ? "+ Adicionar segredo" : "+ Adicionar conta";
        KindFilter.Visibility = secrets ? Visibility.Visible : Visibility.Collapsed;
        Search.PlaceholderText = secrets ? "Buscar tokens, chaves, pastas ou tags" : "Buscar contas, pastas ou tags";
        NameHeader.Text = secrets ? "SEGREDO" : "CONTA";
        SiteHeader.Text = secrets ? "TIPO" : "SITE";
        UpdatedHeader.Text = secrets ? "VALIDADE" : "ATUALIZADA EM";
        EmptyTitle.Text = secrets ? "Nenhum segredo por aqui" : "Nenhuma conta por aqui";
        EmptyHint.Text = secrets ? "Adicione um token, chave SSH ou chave de recuperação, ou ajuste a busca." : "Adicione uma conta ou ajuste a busca.";
        TipText.Text = secrets ? "Selecione um item para ver os detalhes. Os valores ficam ocultos até você pedir para exibi-los."
            : "Selecione uma conta para ver os detalhes. Suas senhas ficam ocultas até você pedir para exibi-las.";
    }

    /// <summary>Shows only the editor fields that belong to <paramref name="kind"/>.</summary>
    private void ApplyEditorKind(EntryKind kind)
    {
        var secret = kind.IsSecret();
        _editorKind = kind;
        LoginFields.Visibility = secret ? Visibility.Collapsed : Visibility.Visible;
        TotpFields.Visibility = secret ? Visibility.Collapsed : Visibility.Visible;
        SecretFields.Visibility = secret ? Visibility.Visible : Visibility.Collapsed;
        TitleFieldLabel.Text = secret ? "Nome" : "Nome da conta";
        UserFieldLabel.Text = secret ? "Conta ou identificação (opcional)" : "Usuário / e-mail";
        UrlFieldLabel.Text = secret ? "Serviço ou endereço (opcional)" : "Endereço do site";
        SecretFieldLabel.Text = kind.ValueLabel();
        SecretHint.Text = kind switch
        {
            EntryKind.SshKey => "Cole a chave privada completa, com as linhas BEGIN e END. O valor aparece enquanto você edita.",
            EntryKind.RecoveryKey => "Códigos ou frases de recuperação. O valor aparece enquanto você edita.",
            _ => "O valor aparece enquanto você edita e fica oculto nos detalhes.",
        };
        if (_draft is not null) EditorHeading.Text = (_new ? "Adicionar " : "Editar ") + NounOf(kind);
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LoginFields is null || KindInput.SelectedIndex < 0) return;
        var previous = _editorKind;
        var kind = EditorKind;
        if (kind == previous) return;
        if (!_refreshing)
        {
            _dirty = true;
            // Carry the value across so switching a type by mistake does not lose what was typed.
            if (previous == EntryKind.Login && kind.IsSecret() && SecretInput.Text.Length == 0)
            { SecretInput.Text = PasswordInput.Password; PasswordInput.Password = ""; }
            else if (previous.IsSecret() && kind == EntryKind.Login && PasswordInput.Password.Length == 0 && !SecretInput.Text.Contains('\n'))
            { PasswordInput.Password = SecretInput.Text; SecretInput.Text = ""; }
        }
        ApplyEditorKind(kind);
        UpdateMode();
    }

    private void OnExpiryChanged(object? sender, SelectionChangedEventArgs e) { if (!_refreshing) _dirty = true; }

    private void OnKindFilter(object sender, SelectionChangedEventArgs e)
    {
        if (KindFilter.SelectedItem is not ComboBoxItem item) return;
        _kindFilter = item.Tag is string tag ? Enum.Parse<EntryKind>(tag) : null;
        if (!_refreshing && Entries is not null) Refresh();
    }

    private void ClearEditor()
    {
        HideCopyFeedback();
        DetailsPane.BeginAnimation(OpacityProperty, null);
        var wasRefreshing = _refreshing;
        _refreshing = true;
        KindInput.SelectedIndex = 0; ApplyEditorKind(EntryKind.Login);
        SecretInput.Clear(); ExpiryInput.SelectedDate = null;
        _refreshing = wasRefreshing;
        ExpiryPanel.Visibility = Visibility.Collapsed;
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
        KindInput.SelectedIndex = (int)entry.Kind; ApplyEditorKind(entry.Kind);
        SecretInput.Text = entry.Secret; ExpiryInput.SelectedDate = entry.Expires?.ToDateTime(TimeOnly.MinValue);
        TitleInput.Text = entry.Title; UserInput.Text = entry.Username;
        PasswordInput.Password = entry.Password; UrlInput.Text = entry.Url;
        FolderInput.Text = entry.Folder; TagsInput.Text = entry.Tags; NotesInput.Text = entry.Notes;
        TotpInput.Password = entry.Totp; FavoriteInput.IsChecked = entry.Favorite;
        EditorHeading.Text = (isNew ? "Adicionar " : "Editar ") + NounOf(entry.Kind);
        DeleteButton.IsEnabled = !isNew; Editor.IsEnabled = true;
        DeleteButton.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        var finding = SecurityAudit.Analyze(App.Services.Vault.Entries).FirstOrDefault(f => f.EntryId == entry.Id);
        FindingText.Text = finding is { NeedsAttention: true } ? finding.Summary : "";
        SummaryTitle.Text = entry.Title;
        var context = string.IsNullOrWhiteSpace(entry.Folder) ? "Salvo neste cofre" : entry.Folder;
        var secret = entry.Kind.IsSecret();
        if (secret) context = $"{entry.Kind.Label()} · {context}";
        SummaryMeta.Text = entry.Favorite ? $"Favorito · {context}" : context;
        UserLabel.Text = secret ? "CONTA / IDENTIFICAÇÃO" : "USUÁRIO / E-MAIL";
        SiteLabel.Text = secret ? "SERVIÇO / ENDEREÇO" : "SITE";
        SecretLabel.Text = secret ? entry.Kind.ValueLabel().ToUpperInvariant() : "SENHA";
        CopySecretText.Text = secret ? "Copiar valor" : "Copiar senha";
        SummaryPassword.FontSize = secret ? 14 : 21;
        ExpiryPanel.Visibility = secret ? Visibility.Visible : Visibility.Collapsed;
        SummaryExpiry.Text = entry.Expires is not { } due ? "Sem validade definida"
            : SecretExpiry.NeedsAttention(entry, Today) ? $"{SecretExpiry.Describe(entry, Today)} · {due:dd/MM/yyyy}"
            : SecretExpiry.Describe(entry, Today);
        SummaryExpiry.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            SecretExpiry.NeedsAttention(entry, Today) ? "MainDangerBrush" : "MainInkBrush");
        SummaryUser.Text = string.IsNullOrWhiteSpace(entry.Username) ? "Não informado" : entry.Username;
        SummarySite.Text = string.IsNullOrWhiteSpace(entry.Url) ? (secret ? "Não informado" : "Endereço não informado") : entry.Url;
        SummaryOrganization.Text = string.Join(" · ", new[] { entry.Folder, entry.Tags }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (SummaryOrganization.Text.Length == 0) SummaryOrganization.Text = "Sem pasta ou tags";
        SummaryNotes.Text = string.IsNullOrWhiteSpace(entry.Notes) ? "Nenhuma nota adicionada." : entry.Notes;
        SummaryStrength.Text = secret ? "" : StrengthText.Text;
        SummaryFinding.Text = secret ? ExpiryAdvice(entry) : FindingText.Text;
        OpenSiteButton.IsEnabled = TrySite(entry.Url, out _);
        _passwordVisible = false;
        SummaryPassword.Text = MaskedValue(entry);
        RevealIcon.Symbol = SymbolRegular.Eye24;
        RevealButton.ToolTip = secret ? "Exibir valor" : "Exibir senha";
        RevealButton.IsEnabled = ValueOf(entry).Length > 0;
        _editing = isNew;
        Status.Text = ""; _dirty = false;
        _refreshing = wasRefreshing;
        UpdateTotp();
        UpdateMode();
        DetailScroll.ScrollToTop();
        if (animate) DetailsPane.BeginAnimation(OpacityProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(130)));
    }

    private static string ExpiryAdvice(VaultEntry entry) => SecretExpiry.StateOf(entry, Today) switch
    {
        ExpiryState.Expired => "Este segredo expirou. Gere um novo no serviço e atualize aqui.",
        ExpiryState.Soon => "Está perto de expirar. Renove antes que pare de funcionar.",
        _ => "",
    };

    private void UpdateMode()
    {
        var selected = _draft is not null;
        DetailsPane.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        DetailsColumn.Width = new GridLength(selected ? 430 : 0);
        Resources["AccountMetadataVisibility"] = selected ? Visibility.Collapsed : Visibility.Visible;
        CollectionTip.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
        var isSecret = selected && (_editing ? EditorKind : _draft!.Kind).IsSecret();
        DetailsCaption.Text = _editing ? $"{(_new ? "ADICIONAR" : "EDITAR")} {(isSecret ? "SEGREDO" : "CONTA")}"
            : isSecret ? "DETALHES DO SEGREDO" : "DETALHES DA CONTA";
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
        SecretsNav.IsChecked = _filter == "secrets" && _folder is null;
        ExpiringNav.IsChecked = _filter == "expiring";
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !App.Services.Vault.IsUnlocked) return;
        _editing = true;
        _passwordVisible = false;
        SummaryPassword.Text = MaskedValue(_draft);
        RevealIcon.Symbol = SymbolRegular.Eye24;
        RevealButton.ToolTip = _draft.Kind.IsSecret() ? "Exibir valor" : "Exibir senha";
        UpdateMode(); DetailScroll.ScrollToTop(); TitleInput.Focus();
    }

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !App.Services.Vault.IsUnlocked) return;
        _passwordVisible = !_passwordVisible;
        var word = _draft.Kind.IsSecret() ? "valor" : "senha";
        SummaryPassword.Text = _passwordVisible ? ValueOf(_draft) : MaskedValue(_draft);
        RevealIcon.Symbol = _passwordVisible ? SymbolRegular.EyeOff24 : SymbolRegular.Eye24;
        RevealButton.ToolTip = _passwordVisible ? $"Ocultar {word}" : $"Exibir {word}";
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
        if (!IsSecretGroup) _kindFilter = null;
        Refresh();
    }
    private void OnFolder(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || Folders.SelectedItem is not string folder) return;
        if (!CanDiscard()) { Refresh(); return; }
        ClearSelection();
        _folder = folder; _filter = IsSecretGroup ? "secrets" : "all"; Refresh();
    }
    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (!CanDiscard()) return;
        _refreshing = true; Entries.SelectedItem = null; _refreshing = false;
        var kind = !IsSecretGroup ? EntryKind.Login : _kindFilter ?? EntryKind.ApiToken;
        LoadEntry(new VaultEntry { Folder = _folder ?? "", Kind = kind }, true); _dirty = true; TitleInput.Focus();
    }
    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_draft is null || !App.Services.Vault.IsUnlocked) return;
        if (string.IsNullOrWhiteSpace(TitleInput.Text)) { Status.Text = "Informe um título."; return; }
        var kind = EditorKind;
        var secretValue = EntryKinds.NormalizeSecret(SecretInput.Text);
        if (kind.IsSecret() && secretValue.Length == 0) { Status.Text = "Informe o valor do segredo."; return; }
        try
        {
            if (!kind.IsSecret() && !string.IsNullOrWhiteSpace(TotpInput.Password)) Totp.Parse(TotpInput.Password);
            var entry = _draft.Clone();
            entry.Kind = kind;
            entry.Title = TitleInput.Text.Trim(); entry.Username = UserInput.Text.Trim();
            entry.Url = UrlInput.Text.Trim();
            entry.Folder = FolderInput.Text.Trim(); entry.Tags = TagsInput.Text.Trim();
            entry.Notes = NotesInput.Text; entry.Favorite = FavoriteInput.IsChecked == true;
            if (kind.IsSecret())
            {
                entry.Secret = secretValue;
                entry.Expires = ExpiryInput.SelectedDate is { } due ? DateOnly.FromDateTime(due) : null;
            }
            else { entry.Password = PasswordInput.Password; entry.Totp = TotpInput.Password.Trim(); }
            _draft = App.Services.Vault.Upsert(entry); _dirty = false; _new = false; _editing = false;
            Refresh(); Status.Text = kind.IsSecret() ? "Segredo salvo no cofre." : "Conta salva no cofre.";
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or IOException or UnauthorizedAccessException)
        { Status.Text = ex is FormatException or OverflowException ? "Chave ou configuração TOTP inválida." : "Não foi possível salvar a entrada."; }
    }
    /// <summary>
    /// Offers the logins that look like tokens or keys. Sure matches come pre-checked, name-only guesses do not,
    /// and nothing moves until the user confirms. A copy of the vault file is kept first.
    /// </summary>
    private void OnMoveSecrets(object sender, RoutedEventArgs e)
    {
        if (!App.Services.Vault.IsUnlocked || !CanDiscard()) return;
        var candidates = SecretDetector.Find(App.Services.Vault.Entries);
        if (candidates.Count == 0) { Refresh(); return; }

        var boxes = new List<(System.Windows.Controls.CheckBox Box, SecretCandidate Candidate)>();
        var list = new StackPanel();
        foreach (var candidate in candidates.OrderByDescending(c => c.Confident).ThenBy(c => c.Entry.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            var text = new StackPanel();
            text.Children.Add(new System.Windows.Controls.TextBlock { Text = candidate.Entry.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var hint = new System.Windows.Controls.TextBlock { Text = $"{candidate.Kind.Label()} · {candidate.Reason}", FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
            hint.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "MainMutedBrush");
            text.Children.Add(hint);
            var box = new System.Windows.Controls.CheckBox { Content = text, IsChecked = candidate.Confident, Margin = new Thickness(0, 0, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetName(box, candidate.Entry.Title);
            boxes.Add((box, candidate));
            list.Children.Add(box);
        }
        var scroll = new ScrollViewer { Content = list, MaxHeight = 250, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 10, 0) };

        if (!AppDialogWindow.Confirm(this, "Mover para Tokens e chaves?",
                "Estes itens de Senhas parecem tokens ou chaves. Marque os que devem mudar de seção: a senha vira o valor do segredo e o resto é mantido. Os desmarcados foram sugeridos só pelo nome.",
                "Mover selecionados", "Agora não", extra: scroll) || !App.Services.Vault.IsUnlocked) return;

        var chosen = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Candidate).ToArray();
        if (chosen.Length == 0) return;
        try
        {
            App.Services.Vault.BackupTo(App.Services.Vault.Path + ".antes-de-mover-tokens");
            foreach (var candidate in chosen)
            {
                // Re-read: the list could have changed while the dialog was open.
                var current = App.Services.Vault.Entries.FirstOrDefault(x => x.Id == candidate.Entry.Id);
                if (current is null || current.Kind != EntryKind.Login) continue;
                App.Services.Vault.Upsert(SecretDetector.ToSecret(current, candidate.Kind));
            }
            Refresh();
            Status.Text = chosen.Length == 1 ? "1 item movido para Tokens e chaves." : $"{chosen.Length} itens movidos para Tokens e chaves.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Refresh(); Status.Text = "Não foi possível concluir a mudança. Verifique o que foi movido."; }
    }

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        if (!CanDiscard()) return;
        _dirty = false; _new = false; _editing = false; Refresh();
    }
    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_draft is null || _new || !AppDialogWindow.Confirm(this, _draft.Kind.IsSecret() ? "Excluir este segredo?" : "Excluir esta conta?",
                (_draft.Kind.IsSecret() ? "O segredo" : "A conta") + " será removido do seu cofre. Esta ação não pode ser desfeita.",
                _draft.Kind.IsSecret() ? "Excluir segredo" : "Excluir conta", _draft.Kind.IsSecret() ? "Manter segredo" : "Manter conta",
                destructive: true, context: _draft.Title) || _draft is null || !App.Services.Vault.IsUnlocked) return;
        try { App.Services.Vault.Delete(_draft.Id); _draft = null; _dirty = false; _editing = false; Refresh(); Status.Text = "Item excluído."; }
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
    private void OnCopyPassword(object sender, RoutedEventArgs e)
    {
        var secret = (_editing ? EditorKind : _draft?.Kind ?? EntryKind.Login).IsSecret();
        var value = _editing ? (secret ? SecretInput.Text : PasswordInput.Password) : _draft is null ? "" : ValueOf(_draft);
        Copy(value, secret ? "Valor copiado" : "Senha copiada", sender as FrameworkElement);
    }
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
            SummaryPassword.Text = _draft is not null && ValueOf(_draft).Length > 0 ? Mask : "";
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
