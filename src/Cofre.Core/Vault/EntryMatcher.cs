using System.Globalization;
using Cofre.Core.Models;

namespace Cofre.Core.Vault;

public static class EntryMatcher
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private static readonly HashSet<string> GenericLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "com", "net", "org", "gov", "edu", "co", "br", "app", "web", "login", "mail", "accounts",
    };

    /// <summary>Every whitespace-separated term must appear in the title, username or URL (accent and case insensitive).</summary>
    public static bool Matches(VaultEntry entry, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        foreach (var term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Contains(entry.Title, term) && !Contains(entry.Username, term) && !Contains(entry.Url, term))
                return false;
        }

        return true;
    }

    /// <summary>Entries that look related to the title of the window the user was in (browser tab, app...).</summary>
    public static IReadOnlyList<VaultEntry> SuggestFor(IEnumerable<VaultEntry> entries, string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return [];

        return entries
            .Where(e => Keys(e).Any(key => Contains(windowTitle, key)))
            .ToList();
    }

    private static IEnumerable<string> Keys(VaultEntry entry)
    {
        if (entry.Title.Trim().Length >= 3) yield return entry.Title.Trim();

        var label = MainDomainLabel(entry.Url);
        if (label is not null) yield return label;
    }

    /// <summary>"https://mail.google.com/x" -> "google". Longest non-generic host label.</summary>
    internal static string? MainDomainLabel(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var candidate = url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;

        var label = uri.Host
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Length >= 3 && !GenericLabels.Contains(l))
            .OrderByDescending(l => l.Length)
            .FirstOrDefault();

        return label;
    }

    private static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) && Compare.IndexOf(haystack, needle, Loose) >= 0;
}
