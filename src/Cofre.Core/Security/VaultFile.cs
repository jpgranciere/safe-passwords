using System.Security.Cryptography;
using System.Text.Json;
using Cofre.Core.Models;

namespace Cofre.Core.Security;

public class VaultFormatException(string message) : Exception(message);

public sealed class InvalidMasterPasswordException()
    : Exception("Senha mestra incorreta (ou o arquivo do cofre foi alterado).");

/// <summary>
/// On-disk layout (little endian):
/// <code>
/// 0   4  magic "CFRE"
/// 4   1  format version (1)
/// 5   1  KDF id (1 = Argon2id)
/// 6   4  Argon2 memory (KiB)
/// 10  4  Argon2 iterations
/// 14  1  Argon2 parallelism
/// 15  16 salt
/// 31  12 AES-GCM nonce
/// 43  n  ciphertext (JSON)
/// ..  16 AES-GCM tag
/// </code>
/// The first 43 bytes are authenticated as AAD, so tampering with the KDF parameters or salt is detected.
/// </summary>
public static class VaultFile
{
    public const int HeaderSize = 43;
    public const int TagSize = 16;
    public const int NonceSize = 12;

    private const byte FormatVersion = 1;
    private const byte KdfArgon2id = 1;
    private static readonly byte[] Magic = [(byte)'C', (byte)'F', (byte)'R', (byte)'E'];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public readonly record struct Header(KdfParameters Kdf, byte[] Salt);

    public static Header ReadHeader(ReadOnlySpan<byte> file)
    {
        if (file.Length < HeaderSize + TagSize || !file[..4].SequenceEqual(Magic))
            throw new VaultFormatException("O arquivo não é um cofre válido.");
        if (file[4] != FormatVersion)
            throw new VaultFormatException($"Versão de cofre não suportada ({file[4]}).");
        if (file[5] != KdfArgon2id)
            throw new VaultFormatException("Algoritmo de derivação de chave desconhecido.");

        var kdf = new KdfParameters(
            BitConverter.ToUInt32(file.Slice(6, 4)),
            BitConverter.ToUInt32(file.Slice(10, 4)),
            file[14]);
        if (!kdf.IsWithinSafeBounds)
            throw new VaultFormatException("Parâmetros de criptografia fora dos limites aceitos.");

        return new Header(kdf, file.Slice(15, KeyDerivation.SaltSize).ToArray());
    }

    public static byte[] Encrypt(VaultData data, byte[] key, Header header)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);

        var file = new byte[HeaderSize + plaintext.Length + TagSize];
        var span = file.AsSpan();
        Magic.CopyTo(span);
        file[4] = FormatVersion;
        file[5] = KdfArgon2id;
        BitConverter.TryWriteBytes(span.Slice(6, 4), header.Kdf.MemoryKiB);
        BitConverter.TryWriteBytes(span.Slice(10, 4), header.Kdf.Iterations);
        file[14] = header.Kdf.Parallelism;
        header.Salt.CopyTo(span.Slice(15, KeyDerivation.SaltSize));
        nonce.CopyTo(span.Slice(31, NonceSize));

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(
                nonce,
                plaintext,
                span.Slice(HeaderSize, plaintext.Length),
                span.Slice(HeaderSize + plaintext.Length, TagSize),
                span[..HeaderSize]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        return file;
    }

    public static VaultData Decrypt(ReadOnlySpan<byte> file, byte[] key)
    {
        ReadHeader(file); // validates magic/version/bounds

        var cipherLength = file.Length - HeaderSize - TagSize;
        var plaintext = new byte[cipherLength];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                file.Slice(31, NonceSize),
                file.Slice(HeaderSize, cipherLength),
                file.Slice(HeaderSize + cipherLength, TagSize),
                plaintext,
                file[..HeaderSize]);

            return JsonSerializer.Deserialize<VaultData>(plaintext, JsonOptions)
                   ?? throw new VaultFormatException("Conteúdo do cofre vazio.");
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new InvalidMasterPasswordException();
        }
        catch (JsonException)
        {
            throw new VaultFormatException("Conteúdo do cofre corrompido.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
