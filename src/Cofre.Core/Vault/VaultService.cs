using System.Security.Cryptography;
using Cofre.Core.Models;
using Cofre.Core.Security;

namespace Cofre.Core.Vault;

/// <summary>
/// Owns the encrypted vault file and the in-memory session. The derived key lives only while unlocked;
/// <see cref="Lock"/> zeroes it. Every mutation is persisted immediately with an atomic replace.
/// </summary>
public sealed class VaultService : IDisposable
{
    public const int MinMasterPasswordLength = 10;

    private readonly object _gate = new();
    private readonly KdfParameters _newVaultKdf;

    private byte[]? _key;
    private VaultFile.Header _header;
    private VaultData? _data;

    public VaultService(string path, KdfParameters? kdf = null)
    {
        Path = path;
        _newVaultKdf = kdf ?? KdfParameters.Default;
    }

    public string Path { get; }

    public bool Exists => File.Exists(Path);

    public bool IsUnlocked
    {
        get { lock (_gate) return _data is not null; }
    }

    /// <summary>Raised after the vault is unlocked or created. May come from a background thread.</summary>
    public event Action? Unlocked;

    /// <summary>Raised after the vault is locked (manually, by idle timeout or by the system).</summary>
    public event Action? Locked;
    public event Action? Changed;

    public IReadOnlyList<VaultEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                RequireUnlocked();
                return _data!.Entries.Select(e => e.Clone()).ToArray();
            }
        }
    }

    public void Create(string masterPassword)
    {
        ValidateMasterPassword(masterPassword);
        if (Exists) throw new InvalidOperationException("Já existe um cofre neste local.");

        var salt = RandomNumberGenerator.GetBytes(KeyDerivation.SaltSize);
        var key = KeyDerivation.Derive(masterPassword, salt, _newVaultKdf);

        lock (_gate)
        {
            _key = key;
            _header = new VaultFile.Header(_newVaultKdf, salt);
            _data = new VaultData();
            Persist();
        }

        Unlocked?.Invoke();
    }

    public void Unlock(string masterPassword)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);

        var file = File.ReadAllBytes(Path);
        var header = VaultFile.ReadHeader(file);
        var key = KeyDerivation.Derive(masterPassword, header.Salt, header.Kdf);

        VaultData data;
        try
        {
            data = VaultFile.Decrypt(file, key);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            throw;
        }

        lock (_gate)
        {
            ClearSession();
            _key = key;
            _header = header;
            _data = data;
        }

        Unlocked?.Invoke();
    }

    public void Lock()
    {
        bool wasUnlocked;
        lock (_gate)
        {
            wasUnlocked = _data is not null;
            ClearSession();
        }

        if (wasUnlocked) Locked?.Invoke();
    }

    public VaultEntry Upsert(VaultEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            RequireUnlocked();
            var copy = entry.Clone();
            if (!Enum.IsDefined(copy.Kind)) throw new ArgumentException("Tipo de entrada inválido.");
            // Each kind owns its own fields; the others are dropped so a secret never lingers in a password slot.
            if (copy.Kind.IsSecret()) { copy.Password = ""; copy.Totp = ""; }
            else { copy.Secret = ""; copy.Expires = null; }
            if (!string.IsNullOrWhiteSpace(copy.Totp)) Totp.Parse(copy.Totp);
            copy.UpdatedUtc = DateTime.UtcNow;

            var index = _data!.Entries.FindIndex(e => e.Id == copy.Id);
            var previous = index >= 0 ? _data.Entries[index] : null;
            if (index >= 0)
            {
                copy.CreatedUtc = _data.Entries[index].CreatedUtc;
                _data.Entries[index] = copy;
            }
            else
            {
                copy.CreatedUtc = copy.UpdatedUtc;
                _data.Entries.Add(copy);
            }

            try { Persist(); }
            catch
            {
                if (index >= 0) _data.Entries[index] = previous!;
                else _data.Entries.Remove(copy);
                throw;
            }
            Changed?.Invoke();
            return copy.Clone();
        }
    }

    public bool Delete(Guid id)
    {
        lock (_gate)
        {
            RequireUnlocked();
            var index = _data!.Entries.FindIndex(e => e.Id == id);
            if (index < 0) return false;
            var previous = _data.Entries[index];
            _data.Entries.RemoveAt(index);
            try { Persist(); }
            catch { _data.Entries.Insert(index, previous); throw; }
            Changed?.Invoke();
            return true;
        }
    }

    /// <summary>Re-encrypts the vault under a new master password (new salt, current Argon2 parameters).</summary>
    public void ChangeMasterPassword(string currentPassword, string newPassword)
    {
        ValidateMasterPassword(newPassword);

        VaultFile.Header header;
        lock (_gate)
        {
            RequireUnlocked();
            header = _header;
        }

        var check = KeyDerivation.Derive(currentPassword, header.Salt, header.Kdf);
        bool matches;
        lock (_gate)
        {
            RequireUnlocked();
            matches = CryptographicOperations.FixedTimeEquals(check, _key!);
        }
        CryptographicOperations.ZeroMemory(check);
        if (!matches) throw new InvalidMasterPasswordException();

        var salt = RandomNumberGenerator.GetBytes(KeyDerivation.SaltSize);
        var newKey = KeyDerivation.Derive(newPassword, salt, _newVaultKdf);

        lock (_gate)
        {
            RequireUnlocked();
            CryptographicOperations.ZeroMemory(_key);
            _key = newKey;
            _header = new VaultFile.Header(_newVaultKdf, salt);
            Persist();
        }
    }

    /// <summary>Copies the still-encrypted vault file. Safe to call while locked.</summary>
    public void BackupTo(string destination)
    {
        if (!Exists) throw new FileNotFoundException("O cofre ainda não foi criado.", Path);
        File.Copy(Path, destination, overwrite: true);
    }

    public void Dispose()
    {
        lock (_gate) ClearSession();
    }

    private static void ValidateMasterPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length < MinMasterPasswordLength)
            throw new ArgumentException($"A senha mestra precisa ter pelo menos {MinMasterPasswordLength} caracteres.");
    }

    private void RequireUnlocked()
    {
        if (_data is null || _key is null) throw new InvalidOperationException("O cofre está bloqueado.");
    }

    private void ClearSession()
    {
        if (_key is not null) CryptographicOperations.ZeroMemory(_key);
        _key = null;
        _data = null;
    }

    private void Persist()
    {
        var bytes = VaultFile.Encrypt(_data!, _key!, _header);

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        Directory.CreateDirectory(directory);

        var temp = Path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        if (File.Exists(Path))
            File.Replace(temp, Path, Path + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(temp, Path);
    }
}
