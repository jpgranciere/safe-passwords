using Cofre.Core.Models;
using Cofre.Core.Vault;

namespace Cofre.Core.Tests;

public class EntryMatcherTests
{
    private static VaultEntry E(string title, string user = "", string url = "") =>
        new() { Title = title, Username = user, Url = url };

    [Theory]
    [InlineData("gmail", true)]
    [InlineData("GMAIL", true)]
    [InlineData("joao", true)]
    [InlineData("google.com", true)]
    [InlineData("gmail joao", true)]
    [InlineData("gmail maria", false)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData(null, true)]
    public void Matches_SearchesTitleUserAndUrl(string? query, bool expected)
    {
        var entry = E("Gmail", "joao@exemplo.com", "https://mail.google.com");
        Assert.Equal(expected, EntryMatcher.Matches(entry, query));
    }

    [Fact]
    public void Matches_IgnoresAccents()
    {
        Assert.True(EntryMatcher.Matches(E("Cartão de Crédito"), "credito"));
        Assert.True(EntryMatcher.Matches(E("Credito"), "crédito"));
    }

    [Theory]
    [InlineData("https://mail.google.com/mail", "google")]
    [InlineData("www.example.com.br", "example")]
    [InlineData("login.microsoftonline.com", "microsoftonline")]
    [InlineData("http://localhost:8080", "localhost")]
    [InlineData("", null)]
    [InlineData("com", null)]
    public void MainDomainLabel_PicksTheMeaningfulLabel(string url, string? expected)
    {
        Assert.Equal(expected, EntryMatcher.MainDomainLabel(url));
    }

    [Fact]
    public void SuggestFor_MatchesByTitleOrDomain()
    {
        var gmail = E("Gmail");
        var github = E("Conta pessoal", url: "https://github.com/login");
        var bank = E("Banco XYZ", url: "https://www.bancoxyz.com.br");
        var all = new[] { gmail, github, bank };

        Assert.Equal([gmail], EntryMatcher.SuggestFor(all, "Caixa de entrada - Gmail - Google Chrome"));
        Assert.Equal([github], EntryMatcher.SuggestFor(all, "Sign in to GitHub · GitHub - Microsoft Edge"));
        Assert.Equal([bank], EntryMatcher.SuggestFor(all, "Internet Banking | bancoxyz"));
        Assert.Empty(EntryMatcher.SuggestFor(all, "Bloco de Notas"));
        Assert.Empty(EntryMatcher.SuggestFor(all, null));
    }
}
