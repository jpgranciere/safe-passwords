using System.Text;
using System.Text.Json;
using Cofre.Core.Models;
using Cofre.Core.Security;
using Cofre.Core.Vault;

namespace Cofre.Core.Tests;

public class SecretEntryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cofre-secret-tests-" + Guid.NewGuid());
    private readonly VaultService _vault;
    private static readonly DateOnly Today = new(2026, 10, 29);

    public SecretEntryTests()
    {
        Directory.CreateDirectory(_dir);
        _vault = new VaultService(Path.Combine(_dir, "vault.dat"), new KdfParameters(8192, 1, 1));
        _vault.Create("test-password-only");
    }

    [Fact]
    public void SecretsSurviveEncryptedRoundTripAndStayOutOfThePlainFile()
    {
        _vault.Upsert(new VaultEntry
        {
            Kind = EntryKind.ApiToken, Title = "GitHub", Secret = "ghp_test_only_token", Expires = new DateOnly(2027, 1, 15),
        });
        Assert.DoesNotContain("ghp_test_only_token", Encoding.UTF8.GetString(File.ReadAllBytes(_vault.Path)));
        _vault.Lock(); _vault.Unlock("test-password-only");
        var saved = Assert.Single(_vault.Entries);
        Assert.Equal(EntryKind.ApiToken, saved.Kind);
        Assert.Equal("ghp_test_only_token", saved.Secret);
        Assert.Equal(new DateOnly(2027, 1, 15), saved.Expires);
    }

    [Fact]
    public void EntriesWrittenBeforeKindsExistedLoadAsLogins()
    {
        // A version 1 record has none of the new properties.
        var legacy = JsonSerializer.Deserialize<VaultData>(
            """{"SchemaVersion":1,"Entries":[{"Id":"7b1e6a52-4f0c-4a63-9d3a-0c5f6b3c1a11","Title":"Old","Password":"pw"}]}""")!;
        var entry = Assert.Single(legacy.Entries);
        Assert.Equal(EntryKind.Login, entry.Kind);
        Assert.Equal("pw", entry.Password);
        Assert.Equal("", entry.Secret);
        Assert.Null(entry.Expires);
    }

    [Fact]
    public void EachKindKeepsOnlyItsOwnFields()
    {
        var token = _vault.Upsert(new VaultEntry { Kind = EntryKind.SshKey, Title = "Servidor", Secret = "key", Password = "leftover", Totp = "JBSWY3DPEHPK3PXP" });
        Assert.Equal("", token.Password); Assert.Equal("", token.Totp);
        var login = _vault.Upsert(new VaultEntry { Title = "Site", Password = "pw", Secret = "leftover", Expires = Today });
        Assert.Equal("", login.Secret); Assert.Null(login.Expires);
        Assert.Throws<ArgumentException>(() => _vault.Upsert(new VaultEntry { Kind = (EntryKind)99, Title = "x" }));
    }

    [Fact]
    public void SecretsNeverReachTheBrowserAndAreNotAuditedAsPasswords()
    {
        _vault.Upsert(new VaultEntry { Kind = EntryKind.ApiToken, Title = "API", Secret = "tok-1", Url = "https://example.com" });
        _vault.Upsert(new VaultEntry { Title = "Login", Password = "correct-horse-battery-9", Url = "https://example.com" });
        var access = new BrowserAccess(_vault, _ => true);
        var list = JsonSerializer.SerializeToElement(access.Handle(JsonSerializer.SerializeToElement(new { action = "list", url = "https://example.com" })));
        Assert.Equal("Login", Assert.Single(list.GetProperty("entries").EnumerateArray()).GetProperty("title").GetString());
        var tokenId = _vault.Entries.Single(e => e.Kind == EntryKind.ApiToken).Id;
        var fill = JsonSerializer.SerializeToElement(access.Handle(JsonSerializer.SerializeToElement(new { action = "fill", url = "https://example.com", id = tokenId })));
        Assert.False(fill.GetProperty("ok").GetBoolean());
        Assert.DoesNotContain("tok-1", fill.ToString());
        Assert.Single(SecurityAudit.Analyze(_vault.Entries));
    }

    [Theory]
    [InlineData(-1, ExpiryState.Expired)]
    [InlineData(0, ExpiryState.Soon)]
    [InlineData(30, ExpiryState.Soon)]
    [InlineData(31, ExpiryState.Valid)]
    public void ExpiryStateFollowsTheWarningWindow(int daysFromToday, ExpiryState expected)
    {
        var entry = new VaultEntry { Kind = EntryKind.ApiToken, Expires = Today.AddDays(daysFromToday) };
        Assert.Equal(expected, SecretExpiry.StateOf(entry, Today));
        Assert.Equal(expected is ExpiryState.Soon or ExpiryState.Expired, SecretExpiry.NeedsAttention(entry, Today));
    }

    [Fact]
    public void ExpiryIsIgnoredWithoutADateOrForLogins()
    {
        Assert.Equal(ExpiryState.None, SecretExpiry.StateOf(new VaultEntry { Kind = EntryKind.SshKey }, Today));
        Assert.Equal(ExpiryState.None, SecretExpiry.StateOf(new VaultEntry { Expires = Today.AddDays(-5) }, Today));
        Assert.Equal("Sem validade", SecretExpiry.Describe(new VaultEntry { Kind = EntryKind.SshKey }, Today));
    }

    [Theory]
    [InlineData(-5, "Expirou há 5 dias")]
    [InlineData(-1, "Expirou ontem")]
    [InlineData(0, "Expira hoje")]
    [InlineData(1, "Expira amanhã")]
    [InlineData(12, "Expira em 12 dias")]
    [InlineData(90, "Expira em 27/01/2027")]
    public void ExpiryIsDescribedInPlainWords(int daysFromToday, string expected) =>
        Assert.Equal(expected, SecretExpiry.Describe(new VaultEntry { Kind = EntryKind.ApiToken, Expires = Today.AddDays(daysFromToday) }, Today));

    [Fact]
    public void SearchFindsSecretsByKindName()
    {
        var token = new VaultEntry { Kind = EntryKind.SshKey, Title = "Produção" };
        Assert.True(EntryMatcher.Matches(token, "ssh"));
        Assert.False(EntryMatcher.Matches(new VaultEntry { Title = "Produção" }, "ssh"));
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
}
