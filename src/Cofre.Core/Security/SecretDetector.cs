using System.Globalization;
using System.Text.RegularExpressions;
using Cofre.Core.Models;

namespace Cofre.Core.Security;

/// <summary>A login that looks like a token or key stored in the wrong place.</summary>
/// <param name="Confident">True when the value itself looks like a token; false when only the name suggests it.</param>
public sealed record SecretCandidate(VaultEntry Entry, EntryKind Kind, string Reason, bool Confident);

/// <summary>
/// Heuristics for finding tokens and keys that were saved as passwords before kinds existed. It only suggests:
/// the user reviews every candidate before anything is moved.
/// </summary>
public static partial class SecretDetector
{
    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    // Well-known credential shapes. Matching the value is a strong signal; random passwords do not look like this.
    [GeneratedRegex(@"^(?:(?:gh[pousr])_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|glpat-[A-Za-z0-9_\-]{16,}|sk-[A-Za-z0-9_\-]{20,}|[sr]k_(?:live|test)_[A-Za-z0-9]{8,}|xox[abprs]-[A-Za-z0-9\-]{10,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_\-]{30,}|npm_[A-Za-z0-9]{30,}|pypi-[A-Za-z0-9_\-]{30,}|hf_[A-Za-z0-9]{30,}|SG\.[A-Za-z0-9_\-]{16,}\.[A-Za-z0-9_\-]{16,}|eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]*)$")]
    private static partial Regex TokenShape();

    private static readonly string[] TokenWords = ["token", "tokens", "api key", "apikey", "chave de api", "chave api", "api secret", "bearer", "access key", "personal access"];
    private static readonly string[] SshWords = ["ssh", "chave privada", "private key", "pem"];
    private static readonly string[] RecoveryWords = ["recuperacao", "recuperação", "recovery", "backup code", "codigos de backup", "códigos de backup"];

    public static IReadOnlyList<SecretCandidate> Find(IEnumerable<VaultEntry> entries) =>
        entries.Select(Inspect).OfType<SecretCandidate>().ToArray();

    public static SecretCandidate? Inspect(VaultEntry entry)
    {
        // Entries with a TOTP seed are real accounts, and an empty password has nothing to move.
        if (entry.Kind != EntryKind.Login || entry.Password.Length == 0 || entry.Totp.Length > 0) return null;

        var value = entry.Password.Trim();
        if (value.Contains("PRIVATE KEY-----", StringComparison.Ordinal))
            return new SecretCandidate(entry, EntryKind.SshKey, "o valor é uma chave privada", true);
        if (!value.Any(char.IsWhiteSpace) && TokenShape().IsMatch(value))
            return new SecretCandidate(entry, EntryKind.ApiToken, "o valor tem o formato de um token", true);

        var name = entry.Title + " " + entry.Tags;
        if (HasWord(name, RecoveryWords)) return new SecretCandidate(entry, EntryKind.RecoveryKey, "o nome fala em recuperação", false);
        if (HasWord(name, SshWords)) return new SecretCandidate(entry, EntryKind.SshKey, "o nome fala em SSH", false);
        if (HasWord(name, TokenWords)) return new SecretCandidate(entry, EntryKind.ApiToken, "o nome fala em token", false);
        return null;
    }

    /// <summary>The entry as a secret of <paramref name="kind"/>: the password becomes the value; everything else is kept.</summary>
    public static VaultEntry ToSecret(VaultEntry entry, EntryKind kind)
    {
        var copy = entry.Clone();
        copy.Kind = kind;
        copy.Secret = EntryKinds.NormalizeSecret(entry.Password);
        copy.Password = "";
        return copy;
    }

    // Whole-word match, ignoring case and accents, so "Token" matches but "Tokenizer" and "pemex" do not.
    private static bool HasWord(string text, string[] words)
    {
        foreach (var word in words)
        {
            var from = 0;
            while (from < text.Length)
            {
                var at = Compare.IndexOf(text, word, from, Loose);
                if (at < 0) break;
                var end = at + word.Length;
                if ((at == 0 || !char.IsLetterOrDigit(text[at - 1])) && (end >= text.Length || !char.IsLetterOrDigit(text[end]))) return true;
                from = at + 1;
            }
        }
        return false;
    }
}
