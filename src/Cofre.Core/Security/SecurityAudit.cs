using Cofre.Core.Models;

namespace Cofre.Core.Security;

public sealed record SecurityFinding(Guid EntryId, string Title, bool Weak, bool Reused, bool MissingTotp)
{
    public bool NeedsAttention => Weak || Reused;
    public string Summary => string.Join(" · ", new[] { Weak ? "Senha fraca" : null, Reused ? "Senha repetida" : null }
        .Where(x => x is not null));
}

public static class SecurityAudit
{
    public static IReadOnlyList<SecurityFinding> Analyze(IEnumerable<VaultEntry> source)
    {
        var entries = source.ToArray();
        var reused = entries.Where(e => e.Password.Length > 0)
            .GroupBy(e => e.Password, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(e => e.Id)).ToHashSet();
        return entries.Select(e => new SecurityFinding(e.Id, e.Title,
            PasswordStrength.Evaluate(e.Password).Level <= StrengthLevel.Fair,
            reused.Contains(e.Id), string.IsNullOrWhiteSpace(e.Totp))).ToArray();
    }
}
