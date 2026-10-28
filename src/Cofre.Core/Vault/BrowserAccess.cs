using System.Text.Json;
using Cofre.Core.Models;
using Cofre.Core.Security;

namespace Cofre.Core.Vault;

public sealed record BrowserApproval(string Action, string Origin, string Title, string Username);

/// <summary>Exact-origin browser access. Vault writes require desktop approval.</summary>
public sealed class BrowserAccess(VaultService vault, Func<BrowserApproval, bool> approve)
{
    private static object Error(string message) => new { ok = false, error = message };
    public static string Read(JsonElement root, string name, int limit = 2048)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var field)
            || field.ValueKind != JsonValueKind.String) return "";
        var value = field.GetString() ?? "";
        return value.Length <= limit ? value : "";
    }

    public object Handle(JsonElement request, CancellationToken cancellationToken = default)
    {
        if (request.ValueKind != JsonValueKind.Object) return Error("Solicitação inválida.");
        if (!vault.IsUnlocked) return Error("Abra o Cofre e desbloqueie o cofre antes de continuar.");
        var origin = SiteOrigin.Normalize(Read(request, "url"));
        if (origin is null) return Error("A extensão aceita apenas páginas HTTPS.");
        var action = Read(request, "action", 20);
        if (action == "list")
        {
            // Bound even the escaped JSON response to the native framing limit. No secrets in list results.
            var matches = vault.Entries.Where(e => SiteOrigin.Matches(e.Url, origin)).Take(20)
                .Select(e => new { id = e.Id, title = new string(e.Title.Take(200).ToArray()), username = new string(e.Username.Take(200).ToArray()) }).ToArray();
            return new { ok = true, entries = matches };
        }
        if (action is not ("fill" or "save" or "update")) return Error("Ação não suportada.");
        VaultEntry? selected = null;
        var username = Read(request, "username", 1000);
        var password = Read(request, "password", 4096);
        var title = Read(request, "title", 200);
        if (action is "fill" or "update")
        {
            if (!Guid.TryParse(Read(request, "id", 50), out var id)) return Error("Entrada inválida.");
            selected = vault.Entries.FirstOrDefault(e => e.Id == id && SiteOrigin.Matches(e.Url, origin));
            if (selected is null) return Error("Entrada não corresponde à origem desta página.");
            title = selected.Title;
            if (action == "fill") username = selected.Username;
            if (action == "fill" && (selected.Password.Length > 4096 || selected.Username.Length > 1000))
                return Error("Esta entrada excede o limite da extensão. Use o aplicativo.");
        }
        if (action != "fill" && (string.IsNullOrWhiteSpace(title) || password.Length == 0)) return Error("Informe título e senha.");

        if (cancellationToken.IsCancellationRequested
            || (action != "fill" && !approve(new BrowserApproval(action, origin, title, username)))
            || cancellationToken.IsCancellationRequested || !vault.IsUnlocked)
            return Error("Solicitação cancelada ou cofre bloqueado.");
        // The approval UI can pump lock/edit messages. Check fresh state before releasing any credentials.
        if (action == "fill")
        {
            var current = vault.Entries.FirstOrDefault(e => e.Id == selected!.Id && SiteOrigin.Matches(e.Url, origin));
            if (current is null || current.Username != selected!.Username || current.Password != selected.Password)
                return Error("A entrada mudou. Solicite o preenchimento novamente.");
            return new { ok = true, username = current.Username, password = current.Password, origin };
        }
        try
        {
            if (action == "update")
            {
                var current = vault.Entries.FirstOrDefault(e => e.Id == selected!.Id && SiteOrigin.Matches(e.Url, origin));
                if (current is null || current.UpdatedUtc != selected!.UpdatedUtc || current.Password != selected.Password
                    || current.Username != selected.Username || current.Url != selected.Url)
                    return Error("A entrada mudou. Solicite a atualização novamente.");
                var changed = current.Clone();
                changed.Username = username;
                changed.Password = password;
                vault.Upsert(changed); // Keep TOTP, notes, folder, tags, title and URL.
            }
            else vault.Upsert(new VaultEntry { Title = title.Trim(), Username = username, Password = password, Url = origin });
            return new { ok = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return Error("Não foi possível gravar no cofre."); }
    }
}
