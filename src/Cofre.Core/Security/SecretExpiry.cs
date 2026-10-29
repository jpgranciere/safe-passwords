using Cofre.Core.Models;

namespace Cofre.Core.Security;

public enum ExpiryState { None, Valid, Soon, Expired }

/// <summary>Expiry of a non-login entry. "Soon" is what the UI paints red.</summary>
public static class SecretExpiry
{
    public const int WarningDays = 30;

    public static ExpiryState StateOf(VaultEntry entry, DateOnly today)
    {
        if (!entry.Kind.IsSecret() || entry.Expires is not { } date) return ExpiryState.None;
        var left = date.DayNumber - today.DayNumber;
        return left < 0 ? ExpiryState.Expired : left <= WarningDays ? ExpiryState.Soon : ExpiryState.Valid;
    }

    public static bool NeedsAttention(VaultEntry entry, DateOnly today) =>
        StateOf(entry, today) is ExpiryState.Soon or ExpiryState.Expired;

    public static string Describe(VaultEntry entry, DateOnly today)
    {
        if (!entry.Kind.IsSecret() || entry.Expires is not { } date) return "Sem validade";
        var left = date.DayNumber - today.DayNumber;
        return left switch
        {
            < -1 => $"Expirou há {-left} dias",
            -1 => "Expirou ontem",
            0 => "Expira hoje",
            1 => "Expira amanhã",
            <= WarningDays => $"Expira em {left} dias",
            _ => "Expira em " + date.ToString("dd/MM/yyyy"),
        };
    }
}
