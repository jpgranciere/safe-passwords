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
    public void FillRequiresConsentAndExactOrigin()
    {
        var count = 0;
        var access = new BrowserAccess(_vault, approval => { count++; Assert.Equal("https://example.com", approval.Origin); return true; });
        var result = Call(access, new { action = "fill", url = "https://example.com/login", id = _entry.Id });
        Assert.Equal(_entry.Password, result.GetProperty("password").GetString()); Assert.Equal(1, count);
        Assert.False(Call(access, new { action = "fill", url = "https://evil.test", id = _entry.Id }).GetProperty("ok").GetBoolean());
        Assert.Equal(1, count);
        var denied = new BrowserAccess(_vault, _ => false);
        Assert.DoesNotContain(_entry.Password, Call(denied, new { action = "fill", url = "https://example.com", id = _entry.Id }).ToString());
    }

    [Fact]
    public void LockingDuringConsentDoesNotReleaseTheSecret()
    {
        var access = new BrowserAccess(_vault, _ => { _vault.Lock(); return true; });
        var result = Call(access, new { action = "fill", url = "https://example.com", id = _entry.Id });
        Assert.False(result.GetProperty("ok").GetBoolean()); Assert.DoesNotContain(_entry.Password, result.ToString());
    }

    [Fact]
    public void EditingDuringConsentRequiresAnotherConfirmation()
    {
        var access = new BrowserAccess(_vault, _ => { var changed = _entry.Clone(); changed.Password = "replacement-secret"; _vault.Upsert(changed); return true; });
        var result = Call(access, new { action = "fill", url = "https://example.com", id = _entry.Id });
        Assert.False(result.GetProperty("ok").GetBoolean()); Assert.DoesNotContain("replacement-secret", result.ToString());
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

    public void Dispose() { _vault.Dispose(); Directory.Delete(_dir, recursive: true); }
}
