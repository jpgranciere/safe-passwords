using System.Windows.Controls;
using System.Windows.Media;
using Cofre.Core.Security;

namespace Cofre.App.Controls;

/// <summary>Five-segment strength meter with a short label.</summary>
internal partial class StrengthBar : UserControl
{
    private static readonly (string Label, Color Color)[] Levels =
    [
        ("Muito fraca", Color.FromRgb(0xB6, 0x62, 0x4B)),
        ("Fraca", Color.FromRgb(0xBB, 0x7D, 0x4F)),
        ("Razoável", Color.FromRgb(0xA7, 0x8B, 0x43)),
        ("Forte", Color.FromRgb(0x88, 0x9C, 0x67)),
        ("Excelente", Color.FromRgb(0x9C, 0xB5, 0x77)),
    ];

    private static readonly Brush Inactive = Frozen(Color.FromArgb(0x38, 0x80, 0x80, 0x80));

    private readonly Border[] _segments;

    public StrengthBar()
    {
        InitializeComponent();
        _segments = [Seg0, Seg1, Seg2, Seg3, Seg4];
        Update(null);
    }

    public void Update(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            foreach (var segment in _segments) segment.Background = Inactive;
            Label.Text = "Força da senha";
            return;
        }

        var result = PasswordStrength.Evaluate(password);
        var (label, color) = Levels[(int)result.Level];
        var brush = Frozen(color);

        for (var i = 0; i < _segments.Length; i++)
            _segments[i].Background = i <= (int)result.Level ? brush : Inactive;

        Label.Text = $"{label} · ~{(int)result.Bits} bits";
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
