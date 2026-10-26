namespace Cofre.Core.Models;

public sealed class VaultEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Url { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Favorite { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public VaultEntry Clone() => (VaultEntry)MemberwiseClone();
}

public sealed class VaultData
{
    public int SchemaVersion { get; set; } = 1;
    public List<VaultEntry> Entries { get; set; } = new();
}
