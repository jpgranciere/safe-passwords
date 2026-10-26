using System.Security.Cryptography;
using Cofre.Core.Models;
using Cofre.Core.Security;

namespace Cofre.Core.Tests;

public class VaultFileTests
{
    private static (byte[] Key, VaultFile.Header Header) NewKey(string password = "correct horse battery")
    {
        var salt = RandomNumberGenerator.GetBytes(KeyDerivation.SaltSize);
        return (KeyDerivation.Derive(password, salt, KdfParameters.Fast), new VaultFile.Header(KdfParameters.Fast, salt));
    }

    private static VaultData Sample() => new()
    {
        Entries = { new VaultEntry { Title = "Gmail", Username = "joao@example.com", Password = "s3nh@-Forte!", Notes = "çãõ 日本語" } },
    };

    [Fact]
    public void RoundTrip_PreservesEntries()
    {
        var (key, header) = NewKey();
        var file = VaultFile.Encrypt(Sample(), key, header);

        var data = VaultFile.Decrypt(file, key);

        var entry = Assert.Single(data.Entries);
        Assert.Equal("Gmail", entry.Title);
        Assert.Equal("s3nh@-Forte!", entry.Password);
        Assert.Equal("çãõ 日本語", entry.Notes);
    }

    [Fact]
    public void Ciphertext_DoesNotContainPlaintext()
    {
        var (key, header) = NewKey();
        var file = VaultFile.Encrypt(Sample(), key, header);

        var text = System.Text.Encoding.UTF8.GetString(file);
        Assert.DoesNotContain("Gmail", text);
        Assert.DoesNotContain("s3nh@-Forte!", text);
    }

    [Fact]
    public void EachEncryption_UsesAFreshNonce()
    {
        var (key, header) = NewKey();
        var a = VaultFile.Encrypt(Sample(), key, header);
        var b = VaultFile.Encrypt(Sample(), key, header);

        Assert.NotEqual(a.AsSpan(31, VaultFile.NonceSize).ToArray(), b.AsSpan(31, VaultFile.NonceSize).ToArray());
    }

    [Fact]
    public void WrongKey_ThrowsInvalidMasterPassword()
    {
        var (key, header) = NewKey();
        var (otherKey, _) = NewKey("another password entirely");
        var file = VaultFile.Encrypt(Sample(), key, header);

        Assert.Throws<InvalidMasterPasswordException>(() => VaultFile.Decrypt(file, otherKey));
    }

    [Theory]
    [InlineData(10)]   // KDF memory parameter inside the header
    [InlineData(20)]   // salt
    [InlineData(35)]   // nonce
    [InlineData(60)]   // ciphertext
    public void TamperedByte_IsDetected(int index)
    {
        var (key, header) = NewKey();
        var file = VaultFile.Encrypt(Sample(), key, header);
        file[index] ^= 0x01;

        Assert.ThrowsAny<Exception>(() => VaultFile.Decrypt(file, key));
    }

    [Fact]
    public void TamperedTag_IsDetected()
    {
        var (key, header) = NewKey();
        var file = VaultFile.Encrypt(Sample(), key, header);
        file[^1] ^= 0x01;

        Assert.Throws<InvalidMasterPasswordException>(() => VaultFile.Decrypt(file, key));
    }

    [Fact]
    public void ReadHeader_RejectsGarbage()
    {
        Assert.Throws<VaultFormatException>(() => VaultFile.ReadHeader(new byte[10]));
        Assert.Throws<VaultFormatException>(() => VaultFile.ReadHeader(new byte[200]));
    }

    [Fact]
    public void ReadHeader_RejectsAbsurdKdfParameters()
    {
        var (key, header) = NewKey();
        var file = VaultFile.Encrypt(Sample(), key, header);
        BitConverter.TryWriteBytes(file.AsSpan(6, 4), uint.MaxValue); // memory

        Assert.Throws<VaultFormatException>(() => VaultFile.ReadHeader(file));
    }

    [Fact]
    public void Derive_IsDeterministic_AndNormalizesUnicode()
    {
        var salt = RandomNumberGenerator.GetBytes(KeyDerivation.SaltSize);
        var composed = KeyDerivation.Derive("café-senha-longa", salt, KdfParameters.Fast);     // é
        var decomposed = KeyDerivation.Derive("café-senha-longa", salt, KdfParameters.Fast);  // e + acento

        Assert.Equal(composed, decomposed);
        Assert.NotEqual(composed, KeyDerivation.Derive("cafe-senha-longa", salt, KdfParameters.Fast));
    }
}
