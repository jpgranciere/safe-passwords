using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Cofre.Core.Security;
using Wpf.Ui.Controls;

namespace Cofre.App.Views;

internal partial class GeneratorView : UserControl, IPopupView
{
    private static readonly Brush DigitBrush = Frozen(Color.FromRgb(0x60, 0xA5, 0xFA));
    private static readonly Brush SymbolBrush = Frozen(Color.FromRgb(0xFB, 0x92, 0x3C));

    private readonly PopupWindow _popup;
    private readonly Action<string>? _onUse;
    private readonly Action _onBack;
    private readonly bool _ready;
    private string _current = "";

    /// <summary>Options of the last generation; the editor's quick-generate button reuses them.</summary>
    public static GeneratorOptions LastOptions { get; private set; } = new();

    /// <param name="onUse">When set the view is "picking" a password for the editor; otherwise it only offers copy.</param>
    public GeneratorView(PopupWindow popup, Action<string>? onUse, Action onBack)
    {
        _popup = popup;
        _onUse = onUse;
        _onBack = onBack;
        InitializeComponent();

        LengthSlider.Value = LastOptions.Length;
        UpperBox.IsChecked = LastOptions.Upper;
        LowerBox.IsChecked = LastOptions.Lower;
        DigitsBox.IsChecked = LastOptions.Digits;
        SymbolsBox.IsChecked = LastOptions.Symbols;
        AmbiguousBox.IsChecked = LastOptions.AvoidAmbiguous;
        UseButton.Visibility = onUse is null ? Visibility.Collapsed : Visibility.Visible;

        _ready = true;
        Regenerate();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            _onBack();
        };
    }

    public void OnShown() { }

    private void OnOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) Regenerate();
    }

    private void OnRegenerate(object sender, RoutedEventArgs e) => Regenerate();

    private void Regenerate()
    {
        var options = new GeneratorOptions(
            Length: (int)LengthSlider.Value,
            Upper: UpperBox.IsChecked == true,
            Lower: LowerBox.IsChecked == true,
            Digits: DigitsBox.IsChecked == true,
            Symbols: SymbolsBox.IsChecked == true,
            AvoidAmbiguous: AmbiguousBox.IsChecked == true);

        LengthText.Text = options.Length.ToString();

        // Never let the user switch off the last character class: keep at least one selected.
        if (!options.Upper && !options.Lower && !options.Digits && !options.Symbols)
        {
            LowerBox.IsChecked = true;
            options = options with { Lower = true };
        }

        LastOptions = options;
        _current = PasswordGenerator.Generate(options);
        PasswordText.Inlines.Clear();
        foreach (var ch in _current)
        {
            var run = new Run(ch.ToString());
            if (char.IsDigit(ch)) run.Foreground = DigitBrush;
            else if (!char.IsLetter(ch)) run.Foreground = SymbolBrush;
            PasswordText.Inlines.Add(run);
        }

        Strength.Update(_current);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var seconds = App.Services.Settings.Current.ClipboardClearSeconds;
        App.Services.Clipboard.CopySensitive(_current, seconds);
        _popup.ShowToast(seconds > 0 ? $"Copiada · limpa em {seconds} s" : "Senha copiada");
    }

    private void OnUse(object sender, RoutedEventArgs e) => _onUse?.Invoke(_current);

    private void OnBack(object sender, RoutedEventArgs e) => _onBack();

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
