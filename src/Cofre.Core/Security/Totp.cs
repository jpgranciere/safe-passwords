using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace Cofre.Core.Security;

/// <summary>RFC 6238. Secrets never leave the encrypted vault except during local calculation.</summary>
public sealed record TotpSettings(string Secret, int Digits = 6, int Period = 30, string Algorithm = "SHA1");

public static class Totp
{
    public static TotpSettings Parse(string value)
    {
        value = value.Trim();
        var settings = new TotpSettings(value);
        if (value.StartsWith("otpauth:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host != "totp")
                throw new FormatException("Use uma chave Base32 ou um endereço otpauth://totp válido.");
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2 || !parameters.TryAdd(Uri.UnescapeDataString(parts[0]), Uri.UnescapeDataString(parts[1])))
                    throw new FormatException("Parâmetros TOTP inválidos ou duplicados.");
            }
            if (!parameters.TryGetValue("secret", out var secret)) throw new FormatException("Chave TOTP ausente.");
            int Number(string name, int fallback) => parameters.TryGetValue(name, out var text)
                ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;
            settings = new TotpSettings(secret, Number("digits", 6), Number("period", 30),
                parameters.GetValueOrDefault("algorithm", "SHA1").ToUpperInvariant());
        }
        if (settings.Digits is not (6 or 8) || settings.Period is < 15 or > 120
            || settings.Algorithm is not ("SHA1" or "SHA256" or "SHA512"))
            throw new FormatException("TOTP aceita 6/8 dígitos, período de 15 a 120 segundos e SHA1/SHA256/SHA512.");
        var key = DecodeBase32(settings.Secret);
        CryptographicOperations.ZeroMemory(key);
        return settings;
    }

    public static string Generate(TotpSettings settings, DateTimeOffset now)
    {
        if (now.ToUnixTimeSeconds() < 0) throw new ArgumentOutOfRangeException(nameof(now));
        // Validate settings even when constructed directly instead of parsed from a URI.
        if (settings.Digits is not (6 or 8) || settings.Period is < 15 or > 120)
            throw new ArgumentOutOfRangeException(nameof(settings));
        var key = DecodeBase32(settings.Secret);
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / settings.Period);
        byte[]? hash = null;
        try
        {
            hash = settings.Algorithm switch
            {
                "SHA1" => HMACSHA1.HashData(key, counter),
                "SHA256" => HMACSHA256.HashData(key, counter),
                "SHA512" => HMACSHA512.HashData(key, counter),
                _ => throw new FormatException("Algoritmo TOTP não suportado.")
            };
            var offset = hash[^1] & 15;
            var code = BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
            var modulus = settings.Digits == 8 ? 100_000_000u : 1_000_000u;
            return (code % modulus).ToString(new string('0', settings.Digits), CultureInfo.InvariantCulture);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (hash is not null) CryptographicOperations.ZeroMemory(hash);
        }
    }

    public static int RemainingSeconds(TotpSettings settings, DateTimeOffset now) =>
        settings.Period - (int)(now.ToUnixTimeSeconds() % settings.Period);

    private static byte[] DecodeBase32(string value)
    {
        var text = string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        var unpadded = text.TrimEnd('=');
        if (unpadded.Length is < 16 or > 1024 || unpadded.Length % 8 is 1 or 3 or 6
            || (text.Contains('=') && (text.Length % 8 != 0 || text[..unpadded.Length].Contains('='))))
            throw new FormatException("Chave Base32 inválida (mínimo 16 caracteres).");
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var c in unpadded)
        {
            var digit = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c);
            if (digit < 0) throw new FormatException("A chave TOTP deve conter apenas A–Z e 2–7.");
            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
            }
            buffer &= (1 << bits) - 1;
        }
        if (buffer != 0) throw new FormatException("Bits finais da chave Base32 inválidos.");
        return bytes.ToArray();
    }
}
