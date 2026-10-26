using Cofre.Core.Models;
using Cofre.Core.Security;
using Cofre.Core.Vault;

namespace Cofre.Core.Tests;

public sealed class VaultServiceTests : IDisposable
{
    private const string Master = "uma-senha-mestra-bem-longa";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cofre-tests-" + Guid.NewGuid().ToString("N"));
    private string VaultPath => Path.Combine(_dir, "cofre.dat");

    private VaultService NewService() => new(VaultPath, KdfParameters.Fast);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Create_ThenReopen_KeepsEntries()
    {
        using (var vault = NewService())
        {
            vault.Create(Master);
            vault.Upsert(new VaultEntry { Title = "Banco", Username = "maria", Password = "x9!Zk2#pQ" });
        }

        using var reopened = NewService();
        Assert.True(reopened.Exists);
        Assert.False(reopened.IsUnlocked);
        reopened.Unlock(Master);

        var entry = Assert.Single(reopened.Entries);
        Assert.Equal("Banco", entry.Title);
        Assert.Equal("x9!Zk2#pQ", entry.Password);
    }

    [Fact]
    public void Create_RejectsShortMasterPassword_AndExistingVault()
    {
        using var vault = NewService();
        Assert.Throws<ArgumentException>(() => vault.Create("curta"));

        vault.Create(Master);
        using var second = NewService();
        Assert.Throws<InvalidOperationException>(() => second.Create(Master));
    }

    [Fact]
    public void Unlock_WithWrongPassword_Throws_AndStaysLocked()
    {
        using (var vault = NewService()) vault.Create(Master);

        using var again = NewService();
        Assert.Throws<InvalidMasterPasswordException>(() => again.Unlock("senha-errada-bem-longa"));
        Assert.False(again.IsUnlocked);
    }

    [Fact]
    public void Lock_BlocksAccess_AndRaisesEventOnce()
    {
        using var vault = NewService();
        vault.Create(Master);
        var raised = 0;
        vault.Locked += () => raised++;

        vault.Lock();
        vault.Lock();

        Assert.False(vault.IsUnlocked);
        Assert.Equal(1, raised);
        Assert.Throws<InvalidOperationException>(() => vault.Entries);
        Assert.Throws<InvalidOperationException>(() => vault.Upsert(new VaultEntry()));
    }

    [Fact]
    public void Unlocked_IsRaisedByCreateAndUnlock_ButNotByFailedAttempts()
    {
        using var vault = NewService();
        var raised = 0;
        vault.Unlocked += () => raised++;

        vault.Create(Master);
        Assert.Equal(1, raised);

        vault.Lock();
        Assert.Throws<InvalidMasterPasswordException>(() => vault.Unlock("senha-errada-bem-longa"));
        Assert.Equal(1, raised);

        vault.Unlock(Master);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void Upsert_UpdatesExisting_AndKeepsCreatedDate()
    {
        using var vault = NewService();
        vault.Create(Master);
        var created = vault.Upsert(new VaultEntry { Title = "A" });

        var edited = created.Clone();
        edited.Title = "B";
        var saved = vault.Upsert(edited);

        var entry = Assert.Single(vault.Entries);
        Assert.Equal("B", entry.Title);
        Assert.Equal(created.CreatedUtc, saved.CreatedUtc);
        Assert.True(saved.UpdatedUtc >= created.UpdatedUtc);
    }

    [Fact]
    public void Delete_RemovesAndPersists()
    {
        using var vault = NewService();
        vault.Create(Master);
        var entry = vault.Upsert(new VaultEntry { Title = "Apagar" });

        Assert.True(vault.Delete(entry.Id));
        Assert.False(vault.Delete(entry.Id));

        using var reopened = NewService();
        reopened.Unlock(Master);
        Assert.Empty(reopened.Entries);
    }

    [Fact]
    public void Entries_ReturnsSnapshot_NotLiveList()
    {
        using var vault = NewService();
        vault.Create(Master);
        var snapshot = vault.Entries;

        vault.Upsert(new VaultEntry { Title = "novo" });

        Assert.Empty(snapshot);
        Assert.Single(vault.Entries);
    }

    [Fact]
    public void ChangeMasterPassword_WorksAndOldPasswordStopsWorking()
    {
        const string next = "outra-senha-mestra-longa";
        using (var vault = NewService())
        {
            vault.Create(Master);
            vault.Upsert(new VaultEntry { Title = "Mantido" });
            Assert.Throws<InvalidMasterPasswordException>(() => vault.ChangeMasterPassword("errada-errada-errada", next));
            vault.ChangeMasterPassword(Master, next);
        }

        using var old = NewService();
        Assert.Throws<InvalidMasterPasswordException>(() => old.Unlock(Master));

        using var fresh = NewService();
        fresh.Unlock(next);
        Assert.Equal("Mantido", Assert.Single(fresh.Entries).Title);
    }

    [Fact]
    public void Save_KeepsBackupOfPreviousVersion_AndLeavesNoTempFile()
    {
        using var vault = NewService();
        vault.Create(Master);
        vault.Upsert(new VaultEntry { Title = "1" });
        vault.Upsert(new VaultEntry { Title = "2" });

        Assert.True(File.Exists(VaultPath + ".bak"));
        Assert.False(File.Exists(VaultPath + ".tmp"));
    }

    [Fact]
    public void BackupTo_CopiesEncryptedFile_WhileLocked()
    {
        using var vault = NewService();
        vault.Create(Master);
        vault.Lock();
        var backup = Path.Combine(_dir, "backup.dat");

        vault.BackupTo(backup);

        using var restored = new VaultService(backup, KdfParameters.Fast);
        restored.Unlock(Master);
        Assert.True(restored.IsUnlocked);
    }

    [Fact]
    public void Unlock_CorruptFile_ThrowsFormatException()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(VaultPath, "isto não é um cofre");

        using var vault = NewService();
        Assert.Throws<VaultFormatException>(() => vault.Unlock(Master));
    }
}
