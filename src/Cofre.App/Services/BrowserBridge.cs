using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using Cofre.Core.Vault;
using Cofre.Core.Security;

namespace Cofre.App.Services;

/// <summary>Local, current-user pipe; exact-origin fills and desktop approval for vault writes.</summary>
internal sealed class BrowserBridge : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    public void Start() => _worker = Task.Run(ListenAsync);

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(BrowserProtocol.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(80));
                var bytes = await BrowserProtocol.ReadAsync(pipe, timeout.Token);
                if (bytes is null) continue;
                using var request = JsonDocument.Parse(bytes);
                var pending = Application.Current.Dispatcher.InvokeAsync(() => HandleAsync(request.RootElement, timeout.Token));
                var response = await pending.Task.Unwrap();
                await BrowserProtocol.WriteAsync(pipe, response, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or OperationCanceledException or UnauthorizedAccessException)
            {
                // No payload logging: requests may contain credentials. Avoid a busy loop if a pipe is unavailable.
                if (!_stop.IsCancellationRequested) await Task.Delay(200);
            }
        }
    }

    private static object Error(string message) => new { ok = false, error = message };
    private static string Read(JsonElement root, string name, int limit = 2048)
    {
        if (!root.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String) return "";
        var value = field.GetString() ?? "";
        return value.Length <= limit ? value : "";
    }

    private static async Task<object> HandleAsync(JsonElement request, CancellationToken cancellationToken)
    {
        if (request.ValueKind != JsonValueKind.Object) return Error("Solicitação inválida.");
        var extension = Read(request, "extensionOrigin");
        try
        {
            var manifest = System.IO.Path.Combine(AppContext.BaseDirectory, "native", BrowserProtocol.HostName + ".json");
            using var config = JsonDocument.Parse(File.ReadAllText(manifest));
            if (!config.RootElement.GetProperty("allowed_origins").EnumerateArray().Any(x => x.GetString() == extension))
                return Error("Extensão não autorizada. Registre a integração nativa.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or KeyNotFoundException)
        { return Error("Integração não registrada. Execute tools/register-extension.ps1."); }

        if (cancellationToken.IsCancellationRequested) return Error("Solicitação expirada. Tente novamente.");
        if (!App.Services.Vault.IsUnlocked && Read(request, "action") is "list" or "fill")
        {
            if (SiteOrigin.Normalize(Read(request, "url")) is null) return Error("A extensão aceita apenas páginas HTTPS.");
            var unlocked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnUnlocked() => unlocked.TrySetResult(true);
            App.Services.Vault.Unlocked += OnUnlocked;
            try
            {
                if (!App.Services.Vault.IsUnlocked)
                {
                    ((App)Application.Current).OpenQuickPanel();
                    await unlocked.Task.WaitAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) { return Error("Tempo para desbloquear esgotado. Tente novamente."); }
            finally { App.Services.Vault.Unlocked -= OnUnlocked; }
        }
        var response = new BrowserAccess(App.Services.Vault, approval => Confirm(approval, cancellationToken)).Handle(request, cancellationToken);
        if (Read(request, "action") == "fill" && !cancellationToken.IsCancellationRequested && App.Services.Vault.IsUnlocked)
            App.Services.IdleLock.Touch();
        return response;
    }

    private static bool Confirm(BrowserApproval approval, CancellationToken cancellationToken)
    {
        ((App)Application.Current).OpenMainWindow();
        var owner = Application.Current.Windows.OfType<Views.MainWindow>().FirstOrDefault();
        var update = approval.Action == "update";
        var context = $"Site: {approval.Origin}\nConta: {SafeLabel(approval.Title)}\nUsuário: {SafeLabel(approval.Username)}";
        var approved = owner is not null && Views.AppDialogWindow.Confirm(owner,
            update ? "Atualizar esta conta?" : "Salvar esta conta?",
            update ? "O usuário e a senha desta conta serão substituídos pelos dados do formulário. Confira o domínio e a conta."
                : "Confira os dados abaixo. Uma nova conta será criada no seu cofre.",
            update ? "Atualizar conta" : "Salvar no cofre", context: context, cancellationToken: cancellationToken, bringToFront: true);
        if (approved) App.Services.IdleLock.Touch();
        return approved;
    }
    private static string SafeLabel(string text) => new(text.Where(c => !char.IsControl(c)
        && System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format).Take(150).ToArray());

    public void Dispose() => _stop.Cancel();
}
