using System.Text.Json;
using Cofre.Core.Models;
using Cofre.Core.Security;
using Cofre.Core.Vault;

namespace Cofre.Core.Tests;

public class BrowserAccessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cofre-browser-tests-" + Guid.NewGuid());
    private readonly VaultService _vault;
    private readonly VaultEntry _entry;
    public BrowserAccessTests()
    {
        Directory.CreateDirectory(_dir);
        _vault = new VaultService(Path.Combine(_dir, "vault.dat"), new KdfParameters(8192, 1, 1));
        _vault.Create("test-only-password");
        _entry = _vault.Upsert(new VaultEntry { Title = "Example", Username = "demo", Password = "test-only-secret", Url = "https://example.com/login", Totp = "JBSWY3DPEHPK3PXP" });
    }
    private JsonElement Call(BrowserAccess access, object request) =>
        JsonSerializer.SerializeToElement(access.Handle(JsonSerializer.SerializeToElement(request)));

    [Fact]
    public void ListingContainsNoSecretsAndNeverCrossesOrigins()
    {
        var access = new BrowserAccess(_vault, _ => throw new Exception("List must not release secrets."));
        var result = Call(access, new { action = "list", url = "https://example.com" });
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Single(result.GetProperty("entries").EnumerateArray());
        Assert.DoesNotContain(_entry.Password, result.ToString()); Assert.DoesNotContain(_entry.Totp, result.ToString());
        Assert.Empty(Call(access, new { action = "list", url = "https://example.com.evil.test" }).GetProperty("entries").EnumerateArray());
        _vault.Lock(); Assert.False(Call(access, new { action = "list", url = "https://example.com" }).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void FillSkipsDesktopConsentButRequiresExactOrigin()
    {
        var access = new BrowserAccess(_vault, _ => throw new Exception("Fill must not prompt for desktop consent."));
        var result = Call(access, new { action = "fill", url = "https://example.com/login", id = _entry.Id });
        Assert.Equal(_entry.Password, result.GetProperty("password").GetString());
        foreach (var url in new[] { "https://evil.test", "https://example.com.evil.test", "https://sub.example.com", "https://example.com:444", "http://example.com" })
        {
            var rejected = Call(access, new { action = "fill", url, id = _entry.Id });
            Assert.False(rejected.GetProperty("ok").GetBoolean());
            Assert.DoesNotContain(_entry.Password, rejected.ToString());
        }
    }

    [Fact]
    public void LockedVaultDoesNotReleaseTheSecret()
    {
        var access = new BrowserAccess(_vault, _ => throw new Exception("Locked fill must not prompt."));
        _vault.Lock();
        var result = Call(access, new { action = "fill", url = "https://example.com", id = _entry.Id });
        Assert.False(result.GetProperty("ok").GetBoolean()); Assert.DoesNotContain(_entry.Password, result.ToString());
    }

    [Fact]
    public void ExpiredFillDoesNotReleaseTheSecret()
    {
        var access = new BrowserAccess(_vault, _ => throw new Exception("Fill must not prompt."));
        using var expiry = new CancellationTokenSource();
        expiry.Cancel();
        var request = JsonSerializer.SerializeToElement(new { action = "fill", url = "https://example.com", id = _entry.Id });
        var result = JsonSerializer.SerializeToElement(access.Handle(request, expiry.Token));
        Assert.False(result.GetProperty("ok").GetBoolean()); Assert.DoesNotContain(_entry.Password, result.ToString());
    }

    [Fact]
    public void SavingIsExplicitAndDoesNotOverwriteExistingEntries()
    {
        var request = new { action = "save", url = "https://example.com/new", title = "New", username = "demo", password = "new-secret" };
        Assert.False(Call(new BrowserAccess(_vault, _ => false), request).GetProperty("ok").GetBoolean());
        Assert.Single(_vault.Entries);
        Assert.True(Call(new BrowserAccess(_vault, _ => true), request).GetProperty("ok").GetBoolean());
        Assert.Equal(2, _vault.Entries.Count); Assert.Equal(_entry.Password, _vault.Entries.First(e => e.Id == _entry.Id).Password);
    }

    [Fact]
    public void SavingTokenRequiresApprovalAndNeverExposesItAsLogin()
    {
        var request = new { action = "save-token", url = "https://example.com/new", title = "API", username = "bot", password = "example-token" };
        Assert.False(Call(new BrowserAccess(_vault, _ => false), request).GetProperty("ok").GetBoolean());
        Assert.Single(_vault.Entries);
        var access = new BrowserAccess(_vault, approval => { Assert.Equal("save-token", approval.Action); return true; });
        Assert.True(Call(access, request).GetProperty("ok").GetBoolean());
        var token = _vault.Entries.Single(e => e.Kind == EntryKind.ApiToken);
        Assert.Equal("example-token", token.Secret);
        Assert.Equal("", token.Password);
        Assert.Equal("bot", token.Username);
        Assert.Single(Call(access, new { action = "list", url = "https://example.com" }).GetProperty("entries").EnumerateArray());
        Assert.False(Call(access, new { action = "fill", url = "https://example.com", id = token.Id }).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void ExpiredConsentCannotSave()
    {
        using var cancellation = new CancellationTokenSource();
        var access = new BrowserAccess(_vault, _ => { cancellation.Cancel(); return true; });
        var request = JsonSerializer.SerializeToElement(new { action = "save", url = "https://example.com", title = "New", password = "test-only" });
        var result = JsonSerializer.SerializeToElement(access.Handle(request, cancellation.Token));
        Assert.False(result.GetProperty("ok").GetBoolean()); Assert.Single(_vault.Entries);
    }

    [Fact]
    public void NativeListingStaysBelowTheFrameLimit()
    {
        for (var i = 0; i < 25; i++) _vault.Upsert(new VaultEntry { Title = new string('漢', 300), Username = new string('漢', 1000), Url = "https://example.com" });
        var result = Call(new BrowserAccess(_vault, _ => false), new { action = "list", url = "https://example.com" });
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(result).Length < BrowserProtocol.MaximumBytes);
    }

    [Fact]
    public void UpdateRequiresConsentAndPreservesOtherFields()
    {
        var original = _entry.Clone();
        original.Notes = "Keep notes"; original.Folder = "Personal"; original.Tags = "mail"; original.Favorite = true;
        original = _vault.Upsert(original);
        var request = new { action = "update", url = "https://example.com", id = original.Id, username = "new-user", password = "new-secret" };
        Assert.False(Call(new BrowserAccess(_vault, _ => false), request).GetProperty("ok").GetBoolean());
        Assert.Equal(original.Password, Assert.Single(_vault.Entries).Password);
        var access = new BrowserAccess(_vault, approval => {
            Assert.Equal("update", approval.Action); Assert.Equal(original.Title, approval.Title);
            return true;
        });
        Assert.True(Call(access, request).GetProperty("ok").GetBoolean());
        var updated = Assert.Single(_vault.Entries);
        Assert.Equal("new-secret", updated.Password); Assert.Equal("new-user", updated.Username);
        Assert.Equal(original.Id, updated.Id); Assert.Equal(original.CreatedUtc, updated.CreatedUtc);
        Assert.Equal(original.Title, updated.Title); Assert.Equal(original.Url, updated.Url);
        Assert.Equal(original.Totp, updated.Totp); Assert.Equal(original.Notes, updated.Notes);
        Assert.Equal(original.Folder, updated.Folder); Assert.Equal(original.Tags, updated.Tags); Assert.True(updated.Favorite);
    }

    [Fact]
    public void UpdateCannotCrossOriginsOrSaveAfterLockOrConcurrentEdit()
    {
        object Request(string url) => new { action = "update", url, id = _entry.Id, username = "new", password = "new-secret" };
        var neverApprove = new BrowserAccess(_vault, _ => throw new Exception("Must not ask consent for another site."));
        Assert.False(Call(neverApprove, Request("https://example.com.evil.test")).GetProperty("ok").GetBoolean());
        var concurrent = new BrowserAccess(_vault, _ => { var changed = _entry.Clone(); changed.Notes = "Concurrent edit"; _vault.Upsert(changed); return true; });
        Assert.False(Call(concurrent, Request("https://example.com")).GetProperty("ok").GetBoolean());
        Assert.Equal(_entry.Password, Assert.Single(_vault.Entries).Password);
        var locked = new BrowserAccess(_vault, _ => { _vault.Lock(); return true; });
        Assert.False(Call(locked, Request("https://example.com")).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void UpdateRejectsEmptyPasswordsAndExpiredConsent()
    {
        var access = new BrowserAccess(_vault, _ => throw new Exception("Invalid data must not prompt."));
        Assert.False(Call(access, new { action = "update", url = "https://example.com", id = _entry.Id, password = "" }).GetProperty("ok").GetBoolean());
        using var expiry = new CancellationTokenSource();
        access = new BrowserAccess(_vault, _ => { expiry.Cancel(); return true; });
        var request = JsonSerializer.SerializeToElement(new { action = "update", url = "https://example.com", id = _entry.Id, password = "new-secret" });
        var response = JsonSerializer.SerializeToElement(access.Handle(request, expiry.Token));
        Assert.False(response.GetProperty("ok").GetBoolean());
        Assert.Equal(_entry.Password, Assert.Single(_vault.Entries).Password);
    }

    public void Dispose() { _vault.Dispose(); Directory.Delete(_dir, recursive: true); }
}
