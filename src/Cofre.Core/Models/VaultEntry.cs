namespace Cofre.Core.Models;

/// <summary>
/// What an entry holds. <see cref="Login"/> must stay 0: vaults written before kinds existed deserialize to it.
/// </summary>
public enum EntryKind
{
    Login = 0,
    ApiToken = 1,
    SshKey = 2,
    RecoveryKey = 3,
    Other = 4,
}

public static class EntryKinds
{
    public static bool IsSecret(this EntryKind kind) => kind != EntryKind.Login;

    /// <summary>A multi-line value is a key: OpenSSH rejects one without the final newline, so keep exactly one.</summary>
    public static string NormalizeSecret(string raw)
    {
        var value = raw.Replace("\r\n", "\n").Trim();
        return value.Contains('\n') ? value + "\n" : value;
    }

    public static string Label(this EntryKind kind) => kind switch
    {
        EntryKind.ApiToken => "Token de API",
        EntryKind.SshKey => "Chave SSH",
        EntryKind.RecoveryKey => "Chave de recuperação",
        EntryKind.Other => "Outro segredo",
        _ => "Senha",
    };

    /// <summary>Caption for the value itself, as shown above the masked field.</summary>
    public static string ValueLabel(this EntryKind kind) => kind switch
    {
        EntryKind.ApiToken => "Token",
        EntryKind.SshKey => "Chave privada",
        EntryKind.RecoveryKey => "Chave de recuperação",
        _ => "Valor secreto",
    };
}

public sealed class VaultEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public EntryKind Kind { get; set; } = EntryKind.Login;
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    // Value of a non-login entry (token, private key, recovery key...). Kept apart from Password so that
    // nothing that releases passwords (browser extension, quick panel, auto-type) can ever hand it out.
    public string Secret { get; set; } = "";
    // Last day the secret is valid; null when it does not expire. Only meaningful for non-login entries.
    public DateOnly? Expires { get; set; }
    public string Url { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Favorite { get; set; }
    public string Folder { get; set; } = "";
    public string Tags { get; set; } = "";
    // A Base32 secret or an otpauth://totp URI; encrypted with the rest of the record.
    public string Totp { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public VaultEntry Clone() => (VaultEntry)MemberwiseClone();
}

public sealed class VaultData
{
    public int SchemaVersion { get; set; } = 2;
    public List<VaultEntry> Entries { get; set; } = new();
}
