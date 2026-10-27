using System.Text;
using Cofre.Core.Models;
using Cofre.Core.Security;
using Cofre.Core.Vault;

namespace Cofre.Core.Tests;

public class ExtendedVaultTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cofre-tests-" + Guid.NewGuid());
    private readonly VaultService _vault;
    public ExtendedVaultTests()
    {
        Directory.CreateDirectory(_dir);
        _vault = new VaultService(Path.Combine(_dir, "test.dat"), new KdfParameters(8192, 1, 1));
        _vault.Create("test-password-only");
    }

    [Fact]
    public void NewFieldsSurviveEncryptedRoundTripAndReadsAreIsolated()
    {
        var entry = _vault.Upsert(new VaultEntry { Title = "Example", Folder = "Pessoal", Tags = "banco, email", Totp = "JBSWY3DPEHPK3PXP" });
        _vault.Entries[0].Totp = "mutated";
        Assert.Equal(entry.Totp, _vault.Entries[0].Totp);
        Assert.DoesNotContain(entry.Totp, Encoding.UTF8.GetString(File.ReadAllBytes(_vault.Path)));
        _vault.Lock(); _vault.Unlock("test-password-only");
        var saved = Assert.Single(_vault.Entries);
        Assert.Equal("Pessoal", saved.Folder); Assert.Equal("banco, email", saved.Tags); Assert.Equal(entry.Totp, saved.Totp);
        Assert.True(EntryMatcher.Matches(saved, "pessoal banco"));
    }

    [Fact]
    public void FailedWriteDoesNotMutateTheUnlockedSession()
    {
        var entry = _vault.Upsert(new VaultEntry { Title = "Before" });
        Directory.CreateDirectory(_vault.Path + ".tmp");
        entry.Title = "After";
        Assert.ThrowsAny<Exception>(() => _vault.Upsert(entry));
        Assert.Equal("Before", Assert.Single(_vault.Entries).Title);
        Assert.ThrowsAny<Exception>(() => _vault.Delete(entry.Id));
        Assert.Single(_vault.Entries);
        Assert.ThrowsAny<Exception>(() => _vault.Upsert(new VaultEntry { Title = "New" }));
        Assert.Single(_vault.Entries);
    }

    [Fact]
    public void OlderRecordsKeepDefaultFields()
    {
        var entry = System.Text.Json.JsonSerializer.Deserialize<VaultEntry>("{\"Title\":\"Old\",\"Password\":\"secret\"}")!;
        Assert.Equal("", entry.Folder); Assert.Equal("", entry.Tags); Assert.Equal("", entry.Totp);
    }

    public void Dispose()
    {
        _vault.Dispose();
        // A GUID-created test directory, never any application or user-data directory.
        Directory.Delete(_dir, recursive: true);
    }
}
