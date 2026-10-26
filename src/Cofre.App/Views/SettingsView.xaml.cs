using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Cofre.App.Services;
using Microsoft.Win32;

namespace Cofre.App.Views;

internal partial class SettingsView : UserControl, IPopupView
{
    private static readonly (string Label, int Value)[] AutoLockChoices =
        [("1 minuto", 1), ("2 minutos", 2), ("5 minutos", 5), ("10 minutos", 10), ("15 minutos", 15), ("30 minutos", 30), ("Nunca", 0)];

    private static readonly (string Label, int Value)[] ClipboardChoices =
        [("10 segundos", 10), ("20 segundos", 20), ("30 segundos", 30), ("60 segundos", 60), ("Nunca", 0)];

    private static readonly (string Label, string Value)[] ThemeChoices =
        [("Sistema", "System"), ("Escuro", "Dark"), ("Claro", "Light")];

    private readonly PopupWindow _popup;
    private readonly AppSettings _settings = App.Services.Settings.Current;
    private readonly bool _ready;
    private bool _capturingHotkey;

    public SettingsView(PopupWindow popup)
    {
        _popup = popup;
        InitializeComponent();

        Fill(AutoLockCombo, AutoLockChoices, _settings.AutoLockMinutes);
        Fill(ClipboardCombo, ClipboardChoices, _settings.ClipboardClearSeconds);
        Fill(ThemeCombo, ThemeChoices, _settings.Theme);
        HotkeyText.Text = _settings.HotkeyText;
        AutoSubmitToggle.IsChecked = _settings.AutoSubmit;
        StartupToggle.IsChecked = App.Services.Startup.IsEnabled;
        AboutText.Text = "Cofre 1.0 · AES-256-GCM + Argon2id\nSeus dados ficam só neste computador.";

        _ready = true;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public void OnShown() { }

    private static void Fill<T>(ComboBox combo, (string Label, T Value)[] choices, T selected)
    {
        foreach (var (label, value) in choices)
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });

        var index = Array.FindIndex(choices, c => EqualityComparer<T>.Default.Equals(c.Value, selected));
        combo.SelectedIndex = index >= 0 ? index : 0;
    }

    private static T ValueOf<T>(ComboBox combo) => (T)((ComboBoxItem)combo.SelectedItem).Tag;

    private void Persist() => App.Services.Settings.Save();

    private void OnAutoLockChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _settings.AutoLockMinutes = ValueOf<int>(AutoLockCombo);
        Persist();
    }

    private void OnClipboardChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _settings.ClipboardClearSeconds = ValueOf<int>(ClipboardCombo);
        Persist();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _settings.Theme = ValueOf<string>(ThemeCombo);
        Persist();
        ThemeService.Apply(_settings.Theme, _popup);
    }

    private void OnAutoSubmitChanged(object sender, RoutedEventArgs e)
    {
        _settings.AutoSubmit = AutoSubmitToggle.IsChecked == true;
        Persist();
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        try
        {
            App.Services.Startup.SetEnabled(StartupToggle.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            StartupToggle.IsChecked = App.Services.Startup.IsEnabled;
            _popup.ShowToast("Não foi possível alterar a inicialização", isError: true);
        }
    }

    private void OnChangeHotkey(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        HotkeyText.Text = "Pressione a nova combinação… (Esc cancela)";
        Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (_capturingHotkey) EndHotkeyCapture();
            else OnBack(this, e);
            return;
        }

        if (!_capturingHotkey) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None)
            return; // wait for the actual key

        uint modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= NativeMethods.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= NativeMethods.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= NativeMethods.MOD_SHIFT;

        // Shift alone (or nothing) would hijack normal typing everywhere.
        if ((modifiers & (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT)) == 0)
        {
            _popup.ShowToast("Use Ctrl ou Alt na combinação", isError: true);
            return;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        var hotkeys = App.Services.Hotkeys;
        if (hotkeys.Register(modifiers, virtualKey))
        {
            _settings.HotkeyModifiers = modifiers;
            _settings.HotkeyVirtualKey = virtualKey;
            Persist();
            _popup.ShowToast("Atalho alterado");
        }
        else
        {
            hotkeys.Register(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey); // restore the previous one
            _popup.ShowToast("Esse atalho já está em uso", isError: true);
        }

        EndHotkeyCapture();
    }

    private void EndHotkeyCapture()
    {
        _capturingHotkey = false;
        HotkeyText.Text = _settings.HotkeyText;
    }

    private void OnChangeMasterPassword(object sender, RoutedEventArgs e) => _popup.Navigate(new ChangePasswordView(_popup));

    private void OnBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Salvar cópia do cofre",
            FileName = $"cofre-backup-{DateTime.Now:yyyyMMdd}.dat",
            Filter = "Cofre criptografado (*.dat)|*.dat|Todos os arquivos|*.*",
        };

        bool? chosen;
        using (_popup.SuppressAutoHide()) chosen = dialog.ShowDialog(_popup);
        if (chosen != true) return;

        try
        {
            App.Services.Vault.BackupTo(dialog.FileName);
            _popup.ShowToast("Backup salvo");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _popup.ShowToast("Falha no backup: " + ex.Message, isError: true);
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var path = App.Services.Vault.Path;
        var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
    }

    private void OnBack(object sender, RoutedEventArgs e) => _popup.Navigate(new VaultView(_popup));
}
