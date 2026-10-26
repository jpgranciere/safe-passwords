using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Cofre.Core.Security;

/// <summary>Argon2id cost parameters. They are stored in the vault header so they can be raised later.</summary>
public readonly record struct KdfParameters(uint MemoryKiB, uint Iterations, byte Parallelism)
{
    /// <summary>64 MiB, 3 passes, 4 lanes: roughly half a second on a current desktop CPU.</summary>
    public static readonly KdfParameters Default = new(64 * 1024, 3, 4);

    /// <summary>Cheap parameters, only meant for unit tests.</summary>
    public static readonly KdfParameters Fast = new(256, 1, 1);

    internal bool IsWithinSafeBounds =>
        Parallelism is >= 1 and <= 16
        && Iterations is >= 1 and <= 32
        && MemoryKiB >= 8u * Parallelism && MemoryKiB <= 1024 * 1024;
}

public static class KeyDerivation
{
    public const int KeySize = 32;
    public const int SaltSize = 16;

    public static byte[] Derive(string masterPassword, byte[] salt, KdfParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);
        ArgumentNullException.ThrowIfNull(salt);

        // NFKC so the same visible password typed on different keyboards/IMEs yields the same key.
        var passwordBytes = Encoding.UTF8.GetBytes(masterPassword.Normalize(NormalizationForm.FormKC));
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = parameters.Parallelism,
                Iterations = (int)parameters.Iterations,
                MemorySize = (int)parameters.MemoryKiB,
            };
            return argon2.GetBytes(KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
