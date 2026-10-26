using System.Windows;
using System.Windows.Media;
using Cofre.Core.Models;

namespace Cofre.App.ViewModels;

/// <summary>Rows of the vault list: either a section header or an entry.</summary>
internal abstract class ListRow
{
    public virtual bool IsHeader => false;
}

internal sealed class HeaderRow(string text) : ListRow
{
    public string Text { get; } = text;
    public override bool IsHeader => true;
}

internal sealed class EntryRow : ListRow
{
    private static readonly Brush[] Avatars =
    [
        Gradient("#818CF8", "#4F46E5"), // indigo
        Gradient("#C084FC", "#7E22CE"), // purple
        Gradient("#F472B6", "#DB2777"), // pink
        Gradient("#FB7185", "#E11D48"), // rose
        Gradient("#FBBF24", "#D97706"), // amber
        Gradient("#34D399", "#059669"), // emerald
        Gradient("#2DD4BF", "#0D9488"), // teal
        Gradient("#38BDF8", "#0284C7"), // sky
    ];

    public EntryRow(VaultEntry entry)
    {
        Entry = entry;
        var title = entry.Title.Trim();
        Initial = title.Length > 0 ? char.ToUpperInvariant(title[0]).ToString() : "?";
        AvatarBrush = Avatars[Math.Abs(StableHash(title.ToLowerInvariant())) % Avatars.Length];
    }

    public VaultEntry Entry { get; }
    public string Title => Entry.Title;
    public string Username => Entry.Username;
    public string Initial { get; }
    public Brush AvatarBrush { get; }
    public Visibility FavoriteVisibility => Entry.Favorite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UsernameVisibility => Entry.Username.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    // string.GetHashCode() is randomized per process; the avatar colour must not change between runs.
    private static int StableHash(string value)
    {
        var hash = 17;
        foreach (var ch in value) hash = unchecked(hash * 31 + ch);
        return hash == int.MinValue ? 0 : hash;
    }

    private static Brush Gradient(string from, string to)
    {
        var brush = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(from),
            (Color)ColorConverter.ConvertFromString(to),
            new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }
}
