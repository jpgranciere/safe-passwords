using System.Text;
using Cofre.Core.Security;

namespace Cofre.Core.Tests;

public class TotpTests
{
    // RFC 6238, Appendix B: all algorithms and timestamps, including counters beyond 2038.
    [Theory]
    [InlineData(59L, "94287082", "46119246", "90693936")]
    [InlineData(1111111109L, "07081804", "68084774", "25091201")]
    [InlineData(1111111111L, "14050471", "67062674", "99943326")]
    [InlineData(1234567890L, "89005924", "91819424", "93441116")]
    [InlineData(2000000000L, "69279037", "90698825", "38618901")]
    [InlineData(20000000000L, "65353130", "77737706", "47863826")]
    public void OfficialVectors(long timestamp, string sha1, string sha256, string sha512)
    {
        var seed = "1234567890123456789012345678901234567890123456789012345678901234";
        var now = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        Assert.Equal(sha1, Totp.Generate(new TotpSettings(Base32(Encoding.ASCII.GetBytes(seed[..20])), 8, 30, "SHA1"), now));
        Assert.Equal(sha256, Totp.Generate(new TotpSettings(Base32(Encoding.ASCII.GetBytes(seed[..32])), 8, 30, "SHA256"), now));
        Assert.Equal(sha512, Totp.Generate(new TotpSettings(Base32(Encoding.ASCII.GetBytes(seed)), 8, 30, "SHA512"), now));
    }

    [Fact]
    public void UriParametersAndPeriodBoundary()
    {
        var config = Totp.Parse("otpauth://totp/Example:user?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&algorithm=SHA1&digits=8&period=30");
        Assert.Equal("94287082", Totp.Generate(config, DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.Equal(1, Totp.RemainingSeconds(config, DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.Equal(30, Totp.RemainingSeconds(config, DateTimeOffset.FromUnixTimeSeconds(60)));
        Assert.Equal("287082", Totp.Generate(config with { Digits = 6 }, DateTimeOffset.FromUnixTimeSeconds(59)));
        Assert.Equal("jbsw y3dp ehpk 3pxp", Totp.Parse("jbsw y3dp ehpk 3pxp").Secret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567890123456")]
    [InlineData("otpauth://hotp/test?secret=JBSWY3DPEHPK3PXP")]
    [InlineData("otpauth://totp/test?secret=JBSWY3DPEHPK3PXP&digits=9")]
    [InlineData("otpauth://totp/test?secret=JBSWY3DPEHPK3PXP&period=0")]
    [InlineData("otpauth://totp/test?secret=JBSWY3DPEHPK3PXP&algorithm=MD5")]
    [InlineData("otpauth://totp/test?secret=JBSWY3DPEHPK3PXP&secret=JBSWY3DPEHPK3PXP")]
    [InlineData("otpauth://totp/test?secret=JBSWY3DPEHPK3PXP&period=no")]
    public void RejectsInvalidConfiguration(string value) => Assert.Throws<FormatException>(() => Totp.Parse(value));

    private static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var builder = new StringBuilder(); var buffer = 0; var bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { bits -= 5; builder.Append(alphabet[(buffer >> bits) & 31]); }
            buffer &= (1 << bits) - 1;
        }
        if (bits > 0) builder.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return builder.ToString();
    }
}
