using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cofre.Core.Security;

public static class BrowserProtocol
{
    public const string HostName = "com.cofre.passwords";
    public const int MaximumBytes = 65536;
    public static string PipeName => "Cofre.Browser." + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..20];

    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        var size = new byte[4];
        var first = await stream.ReadAsync(size.AsMemory(0, 1), ct);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(size.AsMemory(1), ct);
        var length = BinaryPrimitives.ReadInt32LittleEndian(size);
        if (length is <= 0 or > MaximumBytes) throw new InvalidDataException("Mensagem fora dos limites.");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        return body;
    }

    public static async Task WriteAsync(Stream stream, object value, CancellationToken ct = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value);
        await WriteBytesAsync(stream, body, ct);
    }

    public static async Task WriteBytesAsync(Stream stream, byte[] body, CancellationToken ct = default)
    {
        if (body.Length is <= 0 or > MaximumBytes) throw new InvalidDataException("Mensagem fora dos limites.");
        var size = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, body.Length);
        await stream.WriteAsync(size, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}
