using Cofre.Core.Models;
using Cofre.Core.Security;

namespace Cofre.Core.Tests;

public class SecretDetectorTests
{
    private static VaultEntry Login(string title, string password, string totp = "", string tags = "") =>
        new() { Title = title, Password = password, Totp = totp, Tags = tags };

    [Theory]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("github_pat_11ABCDEFG0123456789_abcdefghijklmnop")]
    [InlineData("sk-proj-abcdefghijklmnopqrstuvwxyz012345")]
    [InlineData("sk_test_abcdefgh12345678")]
    [InlineData("xoxb-1234567890-abcdefghij")]
    [InlineData("AKIAIOSFODNN7EXAMPLE")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTYifQ.abc123def456")]
    public void RecognisesWellKnownTokenShapesAsConfident(string value)
    {
        var found = SecretDetector.Inspect(Login("Qualquer nome", value));
        Assert.NotNull(found);
        Assert.Equal(EntryKind.ApiToken, found!.Kind);
        Assert.True(found.Confident);
    }

    [Fact]
    public void PrivateKeysAreSshKeys()
    {
        var found = SecretDetector.Inspect(Login("Servidor", "-----BEGIN OPENSSH PRIVATE KEY-----\nabc\n-----END OPENSSH PRIVATE KEY-----"));
        Assert.Equal(EntryKind.SshKey, found!.Kind);
        Assert.True(found.Confident);
    }

    [Theory]
    [InlineData("Token do GitHub", EntryKind.ApiToken)]
    [InlineData("API key Stripe", EntryKind.ApiToken)]
    [InlineData("Chave SSH produção", EntryKind.SshKey)]
    [InlineData("Códigos de backup Google", EntryKind.RecoveryKey)]
    public void NamesSuggestACandidateButNeverAConfidentOne(string title, EntryKind kind)
    {
        var found = SecretDetector.Inspect(Login(title, "valor-qualquer"));
        Assert.Equal(kind, found!.Kind);
        Assert.False(found.Confident);
    }

    [Fact]
    public void OrdinaryLoginsAreLeftAlone()
    {
        Assert.Null(SecretDetector.Inspect(Login("Gmail", "T4!pG8#qV2$kR9&zW6")));
        Assert.Null(SecretDetector.Inspect(Login("Tokenizer Inc", "senha123")));
        Assert.Null(SecretDetector.Inspect(Login("Pemex", "senha123")));
        Assert.Null(SecretDetector.Inspect(Login("Token do GitHub", "")));
        Assert.Null(SecretDetector.Inspect(Login("Token do GitHub", "ghp_abcdefghijklmnopqrstuvwxyz0123456789", totp: "JBSWY3DPEHPK3PXP")));
        Assert.Null(SecretDetector.Inspect(new VaultEntry { Kind = EntryKind.ApiToken, Title = "Token", Secret = "x" }));
    }

    [Fact]
    public void ConvertingMovesThePasswordIntoTheSecretAndKeepsTheRest()
    {
        var original = new VaultEntry { Title = "GitHub", Username = "bot", Password = "ghp_x", Url = "https://github.com", Folder = "Dev", Tags = "ci", Notes = "n", Favorite = true };
        var moved = SecretDetector.ToSecret(original, EntryKind.ApiToken);
        Assert.Equal(original.Id, moved.Id);
        Assert.Equal(EntryKind.ApiToken, moved.Kind);
        Assert.Equal("ghp_x", moved.Secret); Assert.Equal("", moved.Password);
        Assert.Equal(("bot", "https://github.com", "Dev", "ci", "n", true), (moved.Username, moved.Url, moved.Folder, moved.Tags, moved.Notes, moved.Favorite));
        Assert.Equal("ghp_x", original.Password); // the source is not mutated
    }
}
