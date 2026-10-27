using System.Buffers.Binary;
using System.Text;
using Cofre.Core.Models;
using Cofre.Core.Security;

namespace Cofre.Core.Tests;

public class BrowserAndAuditTests
{
    [Theory]
    [InlineData("https://example.com/login", "https://example.com/account", true)]
    [InlineData("https://EXAMPLE.com:443/login", "https://example.com", true)]
    [InlineData("https://example.com", "https://example.com.evil.test", false)]
    [InlineData("https://example.com", "https://login.example.com", false)]
    [InlineData("https://example.com", "https://example.com:8443", false)]
    [InlineData("https://example.com", "http://example.com", false)]
    [InlineData("https://example.com", "https://example.com@evil.test", false)]
    [InlineData("https://example.com", "https://evil.test@example.com", false)]
    [InlineData("example.com", "https://example.com", false)]
    [InlineData("https://bücher.example", "https://xn--bcher-kva.example", true)]
    public void OriginsDoNotTrustTitlesSubdomainsOrDifferentPorts(string saved, string page, bool expected) =>
        Assert.Equal(expected, SiteOrigin.Matches(saved, page));

    [Fact]
    public void AuditFlagsReuseWithoutReturningSecrets()
    {
        var entries = new[] {
            new VaultEntry { Title = "A", Password = "senha123" },
            new VaultEntry { Title = "B", Password = "senha123", Totp = "JBSWY3DPEHPK3PXP" },
            new VaultEntry { Title = "C", Password = "zT9!V2@qH8#rN5$eL7&x" },
            new VaultEntry { Title = "D", Password = "" },
            new VaultEntry { Title = "E", Password = "" }
        };
        var findings = SecurityAudit.Analyze(entries);
        Assert.Equal(2, findings.Count(f => f.Reused));
        Assert.True(findings[0].Weak); Assert.True(findings[0].MissingTotp);
        Assert.False(findings[1].MissingTotp); Assert.False(findings[2].NeedsAttention);
        Assert.True(findings[3].Weak); Assert.False(findings[3].Reused);
        Assert.DoesNotContain("senha123", System.Text.Json.JsonSerializer.Serialize(findings));
    }

    [Fact]
    public async Task NativeFramesRoundTripAndDetectTruncation()
    {
        using var stream = new MemoryStream();
        await BrowserProtocol.WriteAsync(stream, new { action = "list", title = "Ação" });
        stream.Position = 0;
        var bytes = await BrowserProtocol.ReadAsync(stream);
        Assert.Contains("list", Encoding.UTF8.GetString(bytes!));
        Assert.Null(await BrowserProtocol.ReadAsync(stream));
        using var truncated = new MemoryStream(new byte[] { 5, 0, 0, 0, 123 });
        await Assert.ThrowsAsync<EndOfStreamException>(() => BrowserProtocol.ReadAsync(truncated));
    }

    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(65537)]
    public async Task NativeFramesRejectUntrustedLengths(int length)
    {
        var bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => BrowserProtocol.ReadAsync(stream));
    }
}
