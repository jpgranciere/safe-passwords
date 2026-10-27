using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cofre.Core.Security;

// stdout is exclusively length-prefixed native messaging, never diagnostic text.
using var input = Console.OpenStandardInput();
using var output = Console.OpenStandardOutput();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
try
{
    var manifestPath = Path.Combine(AppContext.BaseDirectory, BrowserProtocol.HostName + ".json");
    using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    var caller = args.FirstOrDefault() ?? "";
    if (!manifest.RootElement.GetProperty("allowed_origins").EnumerateArray().Any(x => x.GetString() == caller))
        throw new UnauthorizedAccessException();
    var bytes = await BrowserProtocol.ReadAsync(input, timeout.Token);
    if (bytes is null) return;
    var request = JsonNode.Parse(bytes) as JsonObject ?? throw new JsonException();
    request["extensionOrigin"] = caller;
    await using var pipe = new NamedPipeClientStream(".", BrowserProtocol.PipeName, PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.ConnectAsync(2500, timeout.Token);
    await BrowserProtocol.WriteAsync(pipe, request, timeout.Token);
    var response = await BrowserProtocol.ReadAsync(pipe, timeout.Token) ?? throw new IOException();
    await BrowserProtocol.WriteBytesAsync(output, response, timeout.Token);
}
catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or JsonException or OperationCanceledException
    or UnauthorizedAccessException or KeyNotFoundException)
{
    try { await BrowserProtocol.WriteAsync(output, new { ok = false, error = "Abra o Cofre, desbloqueie-o e verifique o registro da extensão." }); }
    catch (IOException) { }
}
