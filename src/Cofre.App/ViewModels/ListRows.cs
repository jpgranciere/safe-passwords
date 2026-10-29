using System.Windows;
using System.Windows.Media;
using Cofre.Core.Models;
using Cofre.Core.Security;

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
        Tone("#586B44"),
        Tone("#7A624B"),
        Tone("#66705D"),
        Tone("#806F3E"),
    ];

    public EntryRow(VaultEntry entry)
    {
        Entry = entry;
        var title = entry.Title.Trim();
        Initial = title.Length > 0 ? char.ToUpperInvariant(title[0]).ToString() : "?";
        AvatarBrush = Avatars[Math.Abs(StableHash(title.ToLowerInvariant())) % Avatars.Length];
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public VaultEntry Entry { get; }
    public string Title => Entry.Title;
    public string Username => Entry.Username;
    public Guid Id => Entry.Id;
    // First metadata column: the site for a login, the kind of secret otherwise.
    public string SiteLabel => Entry.Kind.IsSecret() ? Entry.Kind.Label()
        : Uri.TryCreate(Entry.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? uri.IdnHost : "Site não informado";
    public string FolderLabel => string.IsNullOrWhiteSpace(Entry.Folder) ? "Sem pasta" : Entry.Folder;
    // Last metadata column: the update date for a login, the expiry for a secret.
    public string UpdatedLabel => Entry.Kind.IsSecret() ? SecretExpiry.Describe(Entry, Today)
        : Entry.UpdatedUtc.ToLocalTime().ToString("dd/MM/yyyy");
    // True when the expiry is near or past; the list paints that column red.
    public bool IsAlert => SecretExpiry.NeedsAttention(Entry, Today);
    public string Initial { get; }
    public Brush AvatarBrush { get; }
    public Visibility AlertVisibility => IsAlert ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FavoriteVisibility => Entry.Favorite ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UsernameVisibility => Entry.Username.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    // string.GetHashCode() is randomized per process; the avatar colour must not change between runs.
    private static int StableHash(string value)
    {
        var hash = 17;
        foreach (var ch in value) hash = unchecked(hash * 31 + ch);
        return hash == int.MinValue ? 0 : hash;
    }

    private static Brush Tone(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
